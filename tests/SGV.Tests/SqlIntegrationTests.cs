using Sgv.Api.Domain;
using Xunit;

namespace Sgv.Tests;

[Collection("SQL")]
public sealed class SqlIntegrationTests(SqlFixture database)
{
    [SqlFact]
    public async Task CreatesSaleUsingCatalogPricesAndAuditTrail()
    {
        var catalog = await database.Store.ProductsAsync(default);
        var product = catalog[0];
        var sale = await database.Store.CreateSaleAsync(new SaleInput([new SaleLineInput(product.Id, 2)]), "qa", default);
        Assert.Equal(product.Price * 2, sale.Subtotal);
        Assert.Equal("Pendiente", sale.Status);
        var audit = await database.Store.AuditEventsAsync(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1), default);
        Assert.Contains(audit, item => item.EntityId == sale.Id.ToString() && item.Action == "Venta creada");
    }

    [SqlFact]
    public async Task ReceivesStockAndRejectsInvalidQuantities()
    {
        var product = (await database.Store.ProductsAsync(default))[0];
        await database.Store.ReceiveStockAsync(new StockEntry(product.Id, 5, Guid.NewGuid().ToString()), "qa", default);
        var updated = (await database.Store.ProductsAsync(default)).Single(item => item.Id == product.Id);
        Assert.Equal(product.Stock + 5, updated.Stock);
        await Assert.ThrowsAsync<BusinessException>(() => database.Store.ReceiveStockAsync(new StockEntry(product.Id, -1, "invalid"), "qa", default));
    }
}
