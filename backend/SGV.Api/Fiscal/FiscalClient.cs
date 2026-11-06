using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Sgv.Fiscal;

namespace Sgv.Api.Fiscal;

public interface IFiscalClient
{
    Task<PreparedFiscalDocument> PrepareAsync(FiscalPreparation request,CancellationToken cancellation);
    Task<PreparedFiscalDocument> SignEventAsync(JsonObject document,CancellationToken cancellation);
    Task<FiscalResponse> SendAsync(FiscalTransmission request,CancellationToken cancellation);
}

public sealed class FiscalClient(HttpClient client):IFiscalClient
{
    public async Task<PreparedFiscalDocument> PrepareAsync(FiscalPreparation request,CancellationToken cancellation)=>await PostAsync<FiscalPreparation,PreparedFiscalDocument>("api/fiscal/preparar",request,cancellation);
    public async Task<PreparedFiscalDocument> SignEventAsync(JsonObject document,CancellationToken cancellation)=>await PostAsync<JsonObject,PreparedFiscalDocument>("api/fiscal/firmar-evento",document,cancellation);
    public async Task<FiscalResponse> SendAsync(FiscalTransmission request,CancellationToken cancellation)=>await PostAsync<FiscalTransmission,FiscalResponse>("api/fiscal/enviar",request,cancellation);
    private async Task<TResult> PostAsync<TInput,TResult>(string path,TInput input,CancellationToken cancellation)
    {
        using var response=await client.PostAsJsonAsync(path,input,FiscalJson.Options,cancellation);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResult>(FiscalJson.Options,cancellation)??throw new HttpRequestException("El servicio fiscal devolvió una respuesta vacía.");
    }
}
