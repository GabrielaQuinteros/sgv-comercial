using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Sgv.Fiscal;

namespace Sgv.Dte;

public static class FiscalEndpoints
{
    public static void MapFiscalEndpoints(this WebApplication app)
    {
        app.MapPost("/api/fiscal/preparar", async (FiscalPreparation request, FiscalSigning signing, CancellationToken ct) =>
        {
            try { return Results.Ok(await signing.PrepareAsync(request, ct)); }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, detail: e.Message); }
            catch (Exception e) when (e is InvalidOperationException or CryptographicException or IOException) { return Results.Problem(statusCode: 503, detail: "No fue posible preparar o firmar el DTE. Revisa la configuración del certificado."); }
        });
        app.MapPost("/api/fiscal/firmar-evento", async (JsonObject document, FiscalSigning signing, CancellationToken ct) => Results.Ok(await signing.SignEventAsync(document, ct)));
        app.MapPost("/api/fiscal/enviar", async (FiscalTransmission request, ReceptionGateway gateway, CancellationToken ct) => Results.Ok(await gateway.SendAsync(request, ct)));
    }
}
