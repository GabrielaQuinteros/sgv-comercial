using System.Data;
using Microsoft.Data.SqlClient;
using Sgv.Api.Domain;
using Xunit;

namespace Sgv.Tests;

[Collection("SQL")]
public sealed class InventoryConcurrencyTests(SqlFixture database)
{
    [SqlFact]
    public async Task CP_INV_21_TwoConcurrentDiscountsNeverOversell()
    {
        var productId = await ProductAsync(10);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = 0;
        async Task<bool> DiscountAsync()
        {
            await using var connection = new SqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            if (Interlocked.Increment(ref ready) == 2) gate.SetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                await DiscountCommandAsync(connection, transaction, productId, 8);
                await transaction.CommitAsync();
                return true;
            }
            catch (SqlException exception) when (exception.Number == 50001)
            {
                return false;
            }
        }
        var results = await Task.WhenAll(DiscountAsync(), DiscountAsync());
        Assert.Single(results, succeeded => succeeded);
        Assert.Single(results, succeeded => !succeeded);
        Assert.Equal(2, await StockAsync(productId));
    }

    [SqlFact]
    public async Task FailureOnSecondProductRollsBackFirstDiscount()
    {
        var first = await ProductAsync(4);
        var second = await ProductAsync(1);
        await using (var connection = new SqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await DiscountCommandAsync(connection, transaction, first, 2);
            var exception = await Assert.ThrowsAsync<SqlException>(() => DiscountCommandAsync(connection, transaction, second, 2));
            Assert.Equal(50001, exception.Number);
        }
        Assert.Equal(4, await StockAsync(first));
        Assert.Equal(1, await StockAsync(second));
    }

    [SqlFact]
    public async Task RejectsImplicitTransactionsAndNonPositiveQuantities()
    {
        var productId = await ProductAsync(3);
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var missingTransaction = await Assert.ThrowsAsync<SqlException>(() => DiscountCommandAsync(connection, null, productId, 1));
        Assert.Equal(50002, missingTransaction.Number);
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            var invalid = await Assert.ThrowsAsync<SqlException>(() => DiscountCommandAsync(connection, transaction, productId, -1));
            Assert.Equal(50001, invalid.Number);
        }
        Assert.Equal(3, await StockAsync(productId));
    }

    private static async Task DiscountCommandAsync(SqlConnection connection, SqlTransaction? transaction, int productId, int quantity)
    {
        await using var command = new SqlCommand("dbo.usp_DescontarExistencias", connection, transaction) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@Quantity", quantity);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> ProductAsync(int stock)
    {
        var id = await database.Store.CreateProductAsync(new ProductInput("INV-" + Guid.NewGuid().ToString("N"), "Producto de concurrencia", 10m, 1), default);
        await database.Store.ReceiveStockAsync(new StockEntry(id, stock, "recepcion-qa"), "qa", default);
        return id;
    }

    private async Task<int> StockAsync(int productId) => (await database.Store.ProductsAsync(default)).Single(product => product.Id == productId).Stock;
}
