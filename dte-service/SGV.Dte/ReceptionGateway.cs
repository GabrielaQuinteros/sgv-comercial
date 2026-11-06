using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Sgv.Fiscal;

namespace Sgv.Dte;

public sealed partial class ReceptionGateway(HttpClient client, IConfiguration configuration)
{
    [GeneratedRegex("^[A-Z0-9]{40}$", RegexOptions.CultureInvariant, 1000)] private static partial Regex SealPattern();

    public async Task<FiscalResponse> SendAsync(FiscalTransmission transmission, CancellationToken cancellation)
    {
        if (transmission.Operacion is not ("recepciondte" or "invalidacion")) throw new ArgumentException("Operación fiscal no válida.");
        var root = new Uri(configuration["Receiver:Url"] ?? "http://localhost:5082/api/receptor-pruebas/recepciondte");
        var address = root.AbsolutePath == "/" ? new Uri(root, "api/receptor-pruebas/" + transmission.Operacion) : transmission.Operacion == "recepciondte" ? root : new Uri(configuration["Receiver:InvalidationUrl"] ?? new Uri(root, "invalidacion").ToString());
        var seconds = configuration.GetValue<double?>("Receiver:TimeoutSeconds") ?? 8;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(seconds, 0.025, 8)));
        using var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = JsonContent.Create(transmission.Solicitud, options: FiscalJson.Options) };
        request.Headers.Add("X-Service-Key", configuration["DTE_SERVICE_KEY"]);
        var bearer = configuration["Receiver:Token"] ?? configuration["DTE_AUTHORITY_TOKEN"];
        if (!string.IsNullOrWhiteSpace(bearer)) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        if (transmission.Escenario == "timeout-once") request.Headers.Add("X-Prueba-Escenario", "timeout-once");
        try
        {
            using var response = await client.SendAsync(request, deadline.Token);
            var result = await response.Content.ReadFromJsonAsync<FiscalResponse>(FiscalJson.Options, deadline.Token);
            if (result is null || result.Ambiente != "00" || result.CodigoGeneracion != transmission.Solicitud.CodigoGeneracion || result.Estado is not ("PROCESADO" or "RECHAZADO") || result.Estado == "PROCESADO" && (!response.IsSuccessStatusCode || result.CodigoMsg != "001" || !SealPattern().IsMatch(result.SelloRecibido ?? "")))
                return FiscalResponse.Contingency(transmission.Solicitud.CodigoGeneracion, "RESPUESTA DEL RECEPTOR NO VÁLIDA");
            return result;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return FiscalResponse.Contingency(transmission.Solicitud.CodigoGeneracion, "RECEPTOR SIN RESPUESTA EN 8 SEGUNDOS");
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException)
        {
            return FiscalResponse.Contingency(transmission.Solicitud.CodigoGeneracion, "RECEPTOR NO DISPONIBLE O RESPUESTA ILEGIBLE");
        }
    }
}
