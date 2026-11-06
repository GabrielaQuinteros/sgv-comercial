using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Sgv.Api.Domain;
using Xunit;

namespace Sgv.Tests;

[Collection("SQL")]
public sealed class AuditExportTests(SqlFixture database)
{
    [Fact]
    public void PreservesQuotesUnicodeAndLineBreaksAndEscapesSpreadsheetFormulas()
    {
        var rows = new[]
        {
            new AuditEvent(1, DateTimeOffset.Parse("2026-11-06T10:00:00-06:00", System.Globalization.CultureInfo.InvariantCulture), "=formula", "Exportación", "sale", "Texto, con \"comillas\"\ny otra línea"),
            new AuditEvent(2, DateTimeOffset.UtcNow, "  +formula", "@evento", "-id", "normal")
        };
        var result = Encoding.UTF8.GetString(AuditCsv.Export(rows));
        Assert.StartsWith("\uFEFFId,Fecha UTC", result);
        Assert.Contains("\"'=formula\"", result);
        Assert.Contains("\"'  +formula\"", result);
        Assert.Contains("\"'@evento\"", result);
        Assert.Contains("\"'-id\"", result);
        Assert.Contains("\"Texto, con \"\"comillas\"\"\ny otra línea\"", result);
        Assert.Contains("2026-11-06T16:00:00", result);
        Assert.EndsWith("\r\n", result);
        Assert.Contains("Id,Fecha UTC", Encoding.UTF8.GetString(AuditCsv.Export([])));
    }

    [SqlFact]
    public async Task ExportsAuthorizedRangeAndRejectsInvalidRanges()
    {
        await database.Store.AuditAsync("qa-export", "Exportación", "qa", "evento exportable", default);
        await using var factory = new ApiFactory(database.ConnectionString!, database.AdminPassword);
        using var client = factory.CreateClient();
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"));
        var route = $"/api/audit/export?from={from}&to={to}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginInput("admin", database.AdminPassword));
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("evento exportable", await response.Content.ReadAsStringAsync());
        Assert.Contains("bitacora-", response.Content.Headers.ContentDisposition?.FileName);
        var invalid = await client.GetAsync($"/api/audit/export?from={to}&to={from}");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
