using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Sgv.Dte;
using Sgv.Fiscal;
using Xunit;

namespace Sgv.Tests;

public sealed class FiscalDocumentTests
{
    public static FiscalPreparation Preparation(string? nrc = null, decimal total = 113m) => new(new FiscalSale(Guid.NewGuid(), "TEST-001", "Pagada", total, new FiscalCustomer("Cliente", "0614-010190-001-1", nrc), [new FiscalLine("SKU", "Producto", 1, 100)]), nrc is null ? "01" : "03", "DTE-" + (nrc is null ? "01" : "03") + "-M001P001-000000000000001", Guid.NewGuid().ToString("D").ToUpperInvariant());
    [Fact]
    public void CP_DTE_03_JsonCumpleEstructura()
    {
        var document = FiscalDocumentFactory.Create(Preparation(), new FiscalIssuer(), new DateTimeOffset(2026, 11, 6, 15, 30, 0, TimeSpan.Zero));
        var required = "identificacion,documentoRelacionado,emisor,receptor,otrosDocumentos,ventaTercero,cuerpoDocumento,resumen,extension,apendice".Split(',');
        Assert.Equal(required.Order(StringComparer.Ordinal), document.Select(p => p.Key).Order(StringComparer.Ordinal));
        Assert.Equal("00", document["identificacion"]!["ambiente"]!.GetValue<string>());
        Assert.Equal("09:30:00", document["identificacion"]!["horEmi"]!.GetValue<string>());
        Assert.Equal("06140101900011", document["receptor"]!["numDocumento"]!.GetValue<string>());
        Assert.Equal(113m, FiscalValidator.Number(document["resumen"]!["totalGravada"]));
        Assert.Equal(13m, FiscalValidator.Number(document["resumen"]!["totalIva"]));
        Assert.Empty(FiscalValidator.Validate(document));
    }
    [Fact]
    public void CP_DTE_05_SeleccionTipoDte()
    {
        Assert.Equal("01", FiscalDocumentFactory.SelectType(new FiscalCustomer("Consumidor", "06140101900011")));
        Assert.Equal("03", FiscalDocumentFactory.SelectType(new FiscalCustomer("Contribuyente", "06140101900011", "1234567")));
        var document = FiscalDocumentFactory.Create(Preparation("1234567"), new FiscalIssuer(), DateTimeOffset.UtcNow);
        Assert.Equal(3m, FiscalValidator.Number(document["identificacion"]!["version"]));
        Assert.Equal(100m, FiscalValidator.Number(document["resumen"]!["totalGravada"]));
        Assert.Equal(13m, FiscalValidator.Number(document["resumen"]!["tributos"]![0]!["valor"]));
        Assert.Null(document["cuerpoDocumento"]![0]!["ivaItem"]);
        Assert.Empty(FiscalValidator.Validate(document));
    }
    [Fact]
    public void CP_DTE_09_FirmaVerificable()
    {
        using var keys = new FiscalTestCertificate();
        using var certificate = JwsDocuments.LoadPrivate(keys.PrivatePath, keys.Password);
        using var publicKey = new X509Certificate2(keys.PublicPath);
        var document = FiscalDocumentFactory.Create(Preparation(), new FiscalIssuer(), DateTimeOffset.UtcNow);
        var jws = JwsDocuments.Sign(document.ToJsonString(), certificate);
        Assert.Equal(3, jws.Split('.').Length);
        Assert.Equal("RS256", JsonNode.Parse(Base64UrlEncoder.Decode(jws.Split('.')[0]))!["alg"]!.GetValue<string>());
        Assert.Equal(document["identificacion"]!.ToJsonString(), JwsDocuments.Verify(jws, publicKey)["identificacion"]!.ToJsonString());
        var parts = jws.Split('.'); parts[1] = Base64UrlEncoder.Encode("{\"changed\":true}");
        Assert.Throws<CryptographicException>(() => JwsDocuments.Verify(string.Join('.', parts), publicKey));
        Assert.Throws<CryptographicException>(() => JwsDocuments.Verify("invalid", publicKey));
        parts = jws.Split('.'); parts[0] = Base64UrlEncoder.Encode("{\"alg\":\"none\"}");
        Assert.Throws<CryptographicException>(() => JwsDocuments.Verify(string.Join('.', parts), publicKey));
        Assert.Throws<CryptographicException>(() => JwsDocuments.Sign("{}", publicKey));
    }
    [Theory]
    [InlineData("nit")]
    [InlineData("total")]
    [InlineData("iva")]
    [InlineData("control")]
    [InlineData("date")]
    [InlineData("currency")]
    [InlineData("detail")]
    [InlineData("quantity")]
    [InlineData("version")]
    public void RejectsMalformedFiscalFields(string field)
    {
        var document = FiscalDocumentFactory.Create(Preparation(), new FiscalIssuer(), DateTimeOffset.UtcNow);
        switch (field)
        {
            case "nit": document["receptor"]!["numDocumento"] = "INVALIDO"; break;
            case "total": document["resumen"]!["totalPagar"] = 114; break;
            case "iva": document["resumen"]!["totalIva"] = 12; break;
            case "control": document["identificacion"]!["numeroControl"] = "INVALIDO"; break;
            case "date": document["identificacion"]!["fecEmi"] = "invalid"; break;
            case "currency": document["identificacion"]!["tipoMoneda"] = "EUR"; break;
            case "detail": document["cuerpoDocumento"] = new JsonArray(); break;
            case "quantity": document["cuerpoDocumento"]![0]!["cantidad"] = -1; break;
            case "version": document["identificacion"]!["version"] = 2; break;
        }
        Assert.NotEmpty(FiscalValidator.Validate(document));
    }
    [Fact]
    public void CreditNoteUsesItsOwnSchemaAndOriginalReference()
    {
        var original = FiscalDocumentFactory.Create(Preparation("1234567"), new FiscalIssuer(), DateTimeOffset.UtcNow);
        var request = Preparation("1234567") with { TipoDte = "05", NumeroControl = "DTE-05-M001P001-000000000000002", Original = JsonSerializer.Deserialize<JsonElement>(original.ToJsonString()) };
        var note = FiscalDocumentFactory.Create(request, new FiscalIssuer(), DateTimeOffset.UtcNow);
        Assert.Null(note["otrosDocumentos"]); Assert.False(note.ContainsKey("otrosDocumentos"));
        Assert.False(((JsonObject)note["resumen"]!).ContainsKey("totalPagar"));
        Assert.False(((JsonObject)note["emisor"]!).ContainsKey("codEstableMH"));
        Assert.Equal(original["identificacion"]!["codigoGeneracion"]!.ToString(), note["documentoRelacionado"]![0]!["numeroDocumento"]!.ToString());
        Assert.Empty(FiscalValidator.Validate(note));
        Assert.Throws<ArgumentException>(() => FiscalDocumentFactory.Create(request with { Original = null }, new FiscalIssuer(), DateTimeOffset.UtcNow));
    }
    [Fact]
    public async Task NtpFailureUsesLocalTimeAndKeepsTheSignatureVerifiable()
    {
        using var keys = new FiscalTestCertificate();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DTE_CERTIFICATE_PATH"] = keys.PrivatePath, ["DTE_CERTIFICATE_PASSWORD"] = keys.Password }).Build();
        var service = new FiscalSigning(configuration, new UnavailableClock(), NullLogger<FiscalSigning>.Instance);
        var result = await service.PrepareAsync(Preparation(), default);
        Assert.Contains("NTP", result.Advertencia);
        using var certificate = new X509Certificate2(keys.PublicPath);
        Assert.NotNull(JwsDocuments.Verify(result.Jws, certificate)["identificacion"]);
        Assert.InRange((DateTimeOffset.UtcNow - result.Generado).TotalSeconds, 0, 5);
    }
    [Theory]
    [InlineData(0, "CERO")]
    [InlineData(21.25, "VEINTIUNO")]
    [InlineData(100, "CIEN")]
    [InlineData(300, "TRESCIENTOS")]
    [InlineData(1000, "MIL")]
    [InlineData(1200, "MIL DOSCIENTOS")]
    [InlineData(1000000, "UN MILLÓN")]
    [InlineData(2000001, "DOS MILLONES")]
    public void AmountInWordsIncludesCents(decimal amount, string expected) => Assert.Contains(expected, MoneyWords.Format(amount));
    private sealed class UnavailableClock : IClock
    {
        public Task<DateTimeOffset> UtcNowAsync(CancellationToken cancellation) => Task.FromException<DateTimeOffset>(new InvalidOperationException("NTP no disponible"));
    }
}
