using Microsoft.Data.SqlClient;
using Sgv.Api.Domain;
using Sgv.Api.Data;
using Xunit;

namespace Sgv.Tests;

[Collection("SQL")]
public sealed class PaymentTests(SqlFixture database)
{
    [SqlFact]
    public async Task PaymentIsAtomicAndRepeatedReferenceIsIdempotent()
    {
        var productId = await ProductAsync(5);
        var sale = await database.Store.CreateSaleAsync(new SaleInput([new SaleLineInput(productId, 2)]), "qa", default);
        var payment = new PaymentInput(sale.Total, "Tarjeta", Guid.NewGuid().ToString("N"));
        var receipt = await database.Store.RecordPaymentAsync(sale.Id, payment, "qa", default);
        var repeated = await database.Store.RecordPaymentAsync(sale.Id, payment, "qa", default);
        Assert.Equal(receipt.Id, repeated.Id);
        Assert.Equal("Pagada", receipt.Status);
        Assert.Equal(3, (await database.Store.ProductsAsync(default)).Single(product => product.Id == productId).Stock);
        var error = await Assert.ThrowsAsync<BusinessException>(() => database.Store.RecordPaymentAsync(sale.Id, payment with { Reference = "different" }, "qa", default));
        Assert.Equal(409, error.StatusCode);
    }

    [SqlFact]
    public async Task RejectsWrongAmountAndRollsBackInsufficientStock()
    {
        var productId = await ProductAsync(1);
        var sale = await database.Store.CreateSaleAsync(new SaleInput([new SaleLineInput(productId, 2)]), "qa", default);
        await Assert.ThrowsAsync<BusinessException>(() => database.Store.RecordPaymentAsync(sale.Id, new PaymentInput(1m, "Efectivo", "amount"), "qa", default));
        await Assert.ThrowsAsync<BusinessException>(() => database.Store.RecordPaymentAsync(sale.Id, new PaymentInput(sale.Total, "Efectivo", "stock"), "qa", default));
        Assert.Equal(1, (await database.Store.ProductsAsync(default)).Single(product => product.Id == productId).Stock);
        Assert.Equal("Pendiente", await StatusAsync(sale.Id));
        await Assert.ThrowsAsync<BusinessException>(() => database.Store.RecordPaymentAsync(Guid.NewGuid(), new PaymentInput(10m, "Efectivo", "unknown"), "qa", default));
    }

    [SqlFact]
    public async Task DuplicateReferenceRollsBackInventoryAndSaleStatus()
    {
        var productId = await ProductAsync(10);
        var input = new SaleInput([new SaleLineInput(productId, 2)]);
        var first = await database.Store.CreateSaleAsync(input, "qa", default);
        var second = await database.Store.CreateSaleAsync(input, "qa", default);
        var payment = new PaymentInput(first.Total, "Transferencia", Guid.NewGuid().ToString("N"));
        await database.Store.RecordPaymentAsync(first.Id, payment, "qa", default);
        await Assert.ThrowsAsync<BusinessException>(() => database.Store.RecordPaymentAsync(second.Id, payment, "qa", default));
        Assert.Equal(8, (await database.Store.ProductsAsync(default)).Single(product => product.Id == productId).Stock);
        Assert.Equal("Pendiente", await StatusAsync(second.Id));
    }

    [Fact]
    public async Task RejectsInvalidPaymentBeforeOpeningDatabase()
    {
        var store = new CommercialStore("not-a-connection");
        foreach (var payment in new[] { new PaymentInput(0, "Efectivo", "x"), new PaymentInput(1.001m, "Efectivo", "x"), new PaymentInput(1, "invalid", "x"), new PaymentInput(1, "Efectivo", " ") })
            await Assert.ThrowsAsync<BusinessException>(() => store.RecordPaymentAsync(Guid.NewGuid(), payment, "qa", default));
    }

    private async Task<int> ProductAsync(int stock)
    {
        var id = await database.Store.CreateProductAsync(new ProductInput("PAY-" + Guid.NewGuid().ToString("N"), "Producto de pago", 10m, 1), default);
        await database.Store.ReceiveStockAsync(new StockEntry(id, stock, "recepcion"), "qa", default);
        return id;
    }

    private async Task<string> StatusAsync(Guid id)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var query = new SqlCommand("SELECT Status FROM dbo.Sales WHERE Id=@id", connection);
        query.Parameters.AddWithValue("@id", id);
        return (string)(await query.ExecuteScalarAsync())!;
    }
}
