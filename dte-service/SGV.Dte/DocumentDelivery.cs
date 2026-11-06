using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Sgv.Dte;

public sealed record DeliveryReceipt(Guid DocumentId, int StatusCode, string AuthorityResponse, DateTimeOffset SubmittedAt);

public sealed class DocumentDelivery(HttpClient client, IConfiguration configuration)
{
    public async Task<DeliveryReceipt> SubmitAsync(SignedDocument document, CancellationToken cancellation)
    {
        if (document.Id == Guid.Empty || document.SaleId == Guid.Empty || string.IsNullOrWhiteSpace(document.Signature) || string.IsNullOrWhiteSpace(document.Payload))
            throw new ArgumentException("El documento debe estar identificado y firmado.");
        if (!Uri.TryCreate(configuration["DTE_AUTHORITY_URL"], UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps || address.UserInfo.Length != 0)
            throw new InvalidOperationException("Configura DTE_AUTHORITY_URL con la dirección HTTPS de la pasarela autorizada.");
        var token = configuration["DTE_AUTHORITY_TOKEN"];
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Configura la credencial de la pasarela DTE.");
        using var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = JsonContent.Create(document) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", document.Id.ToString());
        using var response = await client.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        var receipt = await response.Content.ReadAsStringAsync(cancellation);
        return new DeliveryReceipt(document.Id, (int)response.StatusCode, receipt, DateTimeOffset.UtcNow);
    }
}
