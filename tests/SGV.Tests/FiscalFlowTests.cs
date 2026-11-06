using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Sgv.Api.Fiscal;
using Sgv.Fiscal;
using Xunit;

namespace Sgv.Tests;

[Collection("SQL")]
public sealed class FiscalFlowTests(SqlFixture database)
{
    [SqlFact]
    public async Task CP_DTE_14_CodigoGeneracionUnico()
    {
        var store = new FiscalRepository(database.ConnectionString!);
        var sales = new List<Guid>();
        for (var n = 0; n < 8; n++) sales.Add(await FiscalTestStack.PaidSaleAsync(database));
        var rows = await Task.WhenAll(sales.Select(id => store.ReserveAsync(id, "M001P001", DateTimeOffset.UtcNow, null, default)));
        Assert.Equal(8, rows.Select(x => x.Documento.CodigoGeneracion).Distinct().Count());
        Assert.Equal(8, rows.Select(x => x.Documento.NumeroControl).Distinct().Count());
        Assert.All(rows, x => Assert.True(FiscalValidator.IsUuid(x.Documento.CodigoGeneracion)));
        var repeated = await store.ReserveAsync(sales[0], "M001P001", DateTimeOffset.UtcNow, null, default);
        Assert.False(repeated.Nuevo); Assert.Equal(rows[0].Documento.Id, repeated.Documento.Id);
    }
    [SqlFact]
    public async Task CP_DTE_18_ReceptorInvalidoEsRechazado()
    {
        await using var stack = new FiscalTestStack(database); await stack.LoginAsync(database);
        var sale = await FiscalTestStack.PaidSaleAsync(database, new FiscalCustomer("Receptor inválido", "INVALIDO"));
        var doc = await EmitAsync(stack, sale);
        Assert.Equal("RECHAZADO", doc.Estado); Assert.Null(doc.SelloRecibido); Assert.NotNull(doc.Jws);
        Assert.Contains("NIT DEL RECEPTOR NO VÁLIDO", JsonSerializer.Deserialize<FiscalResponse>(doc.Respuesta!, FiscalJson.Options)!.Observaciones);
        Assert.Equal("Pagada", (await stack.Repository.SaleAsync(sale)).Estado);
        Assert.Equal("RECHAZADO", (await stack.Repository.FindAsync(doc.Id))!.Estado);
        var audit = await database.Store.AuditEventsAsync(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1), default);
        Assert.Contains(audit, e => e.EntityId == doc.CodigoGeneracion && e.Action == "DTE RECHAZADO");
    }
    [SqlFact]
    public async Task CP_DTE_23_ReceptorSinRespuestaQuedaEnContingencia()
    {
        await using var stack = new FiscalTestStack(database); await stack.LoginAsync(database);
        var sale = await FiscalTestStack.PaidSaleAsync(database, scenario: "timeout-once");
        var doc = await EmitAsync(stack, sale);
        Assert.Equal("CONTINGENCIA", doc.Estado); Assert.Null(doc.SelloRecibido);
        Assert.Equal("Pagada", (await stack.Repository.SaleAsync(sale)).Estado);
        Assert.Equal("CONTINGENCIA", (await stack.Repository.FindAsync(doc.Id))!.Estado);
        var retried = await PostAsync(stack, $"/api/dte/{doc.Id}/reenviar", new { });
        Assert.Equal("PROCESADO", retried.Estado); Assert.Equal(doc.CodigoGeneracion, retried.CodigoGeneracion); Assert.Equal(doc.NumeroControl, retried.NumeroControl); Assert.Equal(doc.Jws, retried.Jws);
        Assert.Equal(2, retried.Intentos); Assert.Equal(40, retried.SelloRecibido!.Length);
        Assert.Equal("Facturada", (await stack.Repository.SaleAsync(sale)).Estado);
    }
    [SqlFact]
    public async Task CP_DTE_27_InvalidacionDte()
    {
        await using var stack = new FiscalTestStack(database); await stack.LoginAsync(database);
        var sale = await FiscalTestStack.PaidSaleAsync(database);
        var original = await EmitAsync(stack, sale); Assert.Equal("PROCESADO", original.Estado);
        var invalid = await PostAsync(stack, $"/api/dte/{original.Id}/invalidacion", new { motivo = "Corrección de los datos del receptor" });
        Assert.Equal("INVALIDADO", invalid.Estado); Assert.Equal("PROCESADO", invalid.InvalidacionEstado); Assert.Equal(original.Jws, invalid.Jws); Assert.Equal(original.SelloRecibido, invalid.SelloRecibido);
        Assert.Equal("Pagada", (await stack.Repository.SaleAsync(sale)).Estado);
        Assert.Contains("RECIBIDO", invalid.RespuestaInvalidacion);
        var replacement = await EmitAsync(stack, sale); Assert.NotEqual(original.CodigoGeneracion, replacement.CodigoGeneracion); Assert.Equal("PROCESADO", replacement.Estado);
        var shortReason = await stack.Client.PostAsJsonAsync($"/api/dte/{replacement.Id}/invalidacion", new { motivo = "x" }); Assert.Equal(HttpStatusCode.BadRequest, shortReason.StatusCode);
    }
    [SqlFact]
    public async Task CP_DTE_28_NotaCreditoReferenciaOriginal()
    {
        await using var stack = new FiscalTestStack(database); await stack.LoginAsync(database);
        var sale = await FiscalTestStack.PaidSaleAsync(database, new FiscalCustomer("Contribuyente", "06140101900011", "1234567"));
        var original = await EmitAsync(stack, sale); Assert.Equal("03", original.TipoDte); Assert.Equal("PROCESADO", original.Estado);
        var note = await PostAsync(stack, $"/api/dte/{original.Id}/nota-credito", new { });
        Assert.Equal("05", note.TipoDte); Assert.Equal("PROCESADO", note.Estado); Assert.Equal(original.Id, note.OriginalId);
        var json = JsonSerializer.Deserialize<JsonElement>(note.Json!);
        Assert.Equal(original.CodigoGeneracion, json.GetProperty("documentoRelacionado")[0].GetProperty("numeroDocumento").GetString());
        Assert.False(json.GetProperty("resumen").TryGetProperty("totalPagar", out _));
        var duplicate = await PostAsync(stack, $"/api/dte/{original.Id}/nota-credito", new { }); Assert.Equal(note.Id, duplicate.Id);
        var invalid = await stack.Client.PostAsJsonAsync($"/api/dte/{original.Id}/invalidacion", new { motivo = "No se permite con nota activa" }); Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
    }
    [SqlFact]
    public async Task RejectsInconsistentTotalsAndExportsFilteredIndicators()
    {
        await using var stack = new FiscalTestStack(database); await stack.LoginAsync(database);
        var sale = await FiscalTestStack.PaidSaleAsync(database, badTotal: true);
        var doc = await EmitAsync(stack, sale); Assert.Equal("RECHAZADO", doc.Estado); Assert.Contains("TOTAL NO COINCIDE CON EL DETALLE", doc.Respuesta);
        var rows = await stack.Client.GetFromJsonAsync<DteRecord[]>("/api/dte?tipo=01&estado=RECHAZADO"); Assert.Contains(rows!, d => d.Id == doc.Id); Assert.All(rows!, d => Assert.Equal("RECHAZADO", d.Estado));
        var indicators = await stack.Client.GetFromJsonAsync<FiscalIndicators>("/api/facturacion/indicadores?tipo=01&estado=RECHAZADO");
        Assert.Equal(rows!.Length, indicators!.TotalEmitidos); Assert.Equal(100m, indicators.PorcentajeRechazados); Assert.Contains(indicators.MotivosFrecuentes, r => r.Motivo.Contains("TOTAL NO COINCIDE", StringComparison.Ordinal));
        using var export = await stack.Client.GetAsync("/api/dte/exportar?tipo=01&estado=RECHAZADO"); Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType); Assert.Contains(doc.CodigoGeneracion, await export.Content.ReadAsStringAsync());
        var consult = await stack.Client.GetFromJsonAsync<JsonElement>("/api/consulta/dte/" + doc.CodigoGeneracion); Assert.Equal("RECHAZADO", consult.GetProperty("estado").GetString());
        var notFound = await stack.Client.GetAsync("/api/dte/" + Guid.NewGuid()); Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        var retryRejected = await stack.Client.PostAsJsonAsync($"/api/dte/{doc.Id}/reenviar", new { }); Assert.Equal(HttpStatusCode.Conflict, retryRejected.StatusCode);
    }
    private static Task<DteRecord> EmitAsync(FiscalTestStack stack, Guid sale) => PostAsync(stack, $"/api/ventas/{sale}/dte", new { });
    private static async Task<DteRecord> PostAsync<T>(FiscalTestStack stack, string path, T body)
    {
        using var response = await stack.Client.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<DteRecord>())!;
    }
}
