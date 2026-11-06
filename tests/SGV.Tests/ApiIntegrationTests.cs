using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Sgv.Api.Domain;
using Xunit;

namespace Sgv.Tests;

public sealed class ApiFactory(string connection, string password) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Sgv", connection);
        builder.UseSetting("SGV_JWT_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        builder.UseSetting("SGV_ADMIN_USERNAME", "admin");
        builder.UseSetting("SGV_ADMIN_PASSWORD", password);
        builder.UseSetting("DTE_SERVICE_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
    }
}

[Collection("SQL")]
public sealed class ApiIntegrationTests(SqlFixture database)
{
    [SqlFact]
    public async Task AuthenticatedWorkflowUsesRealDatabaseAndRoleChecks()
    {
        var password = database.AdminPassword;
        await database.Store.CreateUserAsync(new UserInput("existing-" + Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "Auditor"), "qa", default);
        await using var factory = new ApiFactory(database.ConnectionString!, password);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginInput("admin", "wrong"))).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginInput("admin", password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        var products = await client.GetFromJsonAsync<Product[]>("/api/products");
        Assert.NotEmpty(products!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dashboard")).StatusCode);
        var created = await client.PostAsJsonAsync("/api/products", new ProductInput("QA-" + Guid.NewGuid().ToString("N"), "Producto QA", 10m, 2));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/inventory/entries", new StockEntry(id, 5, "recepcion-qa"))).StatusCode);
        var sale = await client.PostAsJsonAsync("/api/sales", new SaleInput([new SaleLineInput(id, 2)]));
        Assert.Equal(HttpStatusCode.OK, sale.StatusCode);
        Assert.Equal(22.60m, (await sale.Content.ReadFromJsonAsync<Sale>())!.Total);
        var username = "ventas-" + Guid.NewGuid().ToString("N");
        var userPassword = Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/users", new UserInput(username, userPassword, "Ventas"))).StatusCode);
        var restrictedLogin = await client.PostAsJsonAsync("/api/auth/login", new LoginInput(username, userPassword));
        var restricted = await restrictedLogin.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", restricted.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/products", new ProductInput("DENIED", "No autorizado", 1m, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/audit?from=2026-11-01T00:00:00Z&to=2026-11-02T00:00:00Z")).StatusCode);
    }
}
