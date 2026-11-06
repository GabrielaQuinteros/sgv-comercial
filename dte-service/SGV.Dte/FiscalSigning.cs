using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Sgv.Fiscal;

namespace Sgv.Dte;

public sealed class FiscalSigning(IConfiguration configuration, IClock clock, ILogger<FiscalSigning> logger)
{
    public async Task<PreparedFiscalDocument> PrepareAsync(FiscalPreparation request, CancellationToken cancellation)
    {
        var (now, warning) = await TimeAsync(cancellation);
        var issuer = configuration.GetSection("Fiscal:Emisor").Get<FiscalIssuer>() ?? new FiscalIssuer();
        var json = FiscalDocumentFactory.Create(request, issuer, now).ToJsonString(FiscalJson.Options);
        return Sign(json, now, warning);
    }

    public async Task<PreparedFiscalDocument> SignEventAsync(JsonObject document, CancellationToken cancellation)
    {
        var (now, warning) = await TimeAsync(cancellation);
        var local = now.ToOffset(TimeSpan.FromHours(-6));
        document["identificacion"]!["fecAnula"] = local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        document["identificacion"]!["horAnula"] = local.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        return Sign(document.ToJsonString(FiscalJson.Options), now, warning);
    }
    private PreparedFiscalDocument Sign(string json, DateTimeOffset generated, string? warning)
    {
        var path = configuration["DTE_CERTIFICATE_PATH"] ?? throw new InvalidOperationException("Configura el certificado .P12 de pruebas.");
        using var certificate = JwsDocuments.LoadPrivate(path, configuration["DTE_CERTIFICATE_PASSWORD"]);
        var compact = JwsDocuments.Sign(json, certificate);
        return new PreparedFiscalDocument(json, compact, generated, DateTimeOffset.UtcNow, warning);
    }
    private async Task<(DateTimeOffset Time, string? Warning)> TimeAsync(CancellationToken cancellation)
    {
        try { return (await clock.UtcNowAsync(cancellation), null); }
        catch (Exception exception) when (exception is InvalidOperationException or System.Net.Sockets.SocketException or TimeoutException || exception is OperationCanceledException && !cancellation.IsCancellationRequested)
        {
            logger.LogWarning(exception, "NTP no disponible. Se utiliza hora local UTC para la emisión DTE.");
            return (DateTimeOffset.UtcNow, "NTP no disponible; se utilizó la hora local del servidor.");
        }
    }
}
