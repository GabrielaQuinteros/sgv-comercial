using System.Net;
using Microsoft.Extensions.Configuration;
using Sgv.Dte;
using Xunit;

namespace Sgv.Tests;

public sealed class DocumentDeliveryTests
{
    private static SignedDocument Document => new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "{\"total\":113}", "signature", "certificate");

    private static IConfiguration Configuration(string? address = "https://authority.example.invalid/dte", string? token = "test-credential") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DTE_AUTHORITY_URL"] = address, ["DTE_AUTHORITY_TOKEN"] = token }).Build();

    [Fact]
    public async Task SendsSignedDocumentAndReturnsActualGatewayReceipt()
    {
        string? received = null;
        string? authorization = null;
        string? idempotency = null;
        using var client = new HttpClient(new Handler(async request =>
        {
            received = await request.Content!.ReadAsStringAsync();
            authorization = request.Headers.Authorization?.ToString();
            idempotency = request.Headers.GetValues("Idempotency-Key").Single();
            return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{\"receipt\":\"received-001\"}") };
        }));
        var document = Document;
        var receipt = await new DocumentDelivery(client, Configuration()).SubmitAsync(document, default);
        Assert.Equal(202, receipt.StatusCode);
        Assert.Equal(document.Id, receipt.DocumentId);
        Assert.Equal("Bearer test-credential", authorization);
        Assert.Equal(document.Id.ToString(), idempotency);
        Assert.Contains(document.Id.ToString(), received);
        Assert.Contains("received-001", receipt.AuthorityResponse);
    }

    [Fact]
    public async Task RejectsUnsignedDocumentsAndMissingCredentials()
    {
        using var client = new HttpClient(new Handler(_ => throw new InvalidOperationException("No debe enviar la solicitud.")));
        await Assert.ThrowsAsync<ArgumentException>(() => new DocumentDelivery(client, Configuration()).SubmitAsync(Document with { Signature = "" }, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DocumentDelivery(client, Configuration("http://authority.example.invalid")).SubmitAsync(Document, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DocumentDelivery(client, Configuration(null)).SubmitAsync(Document, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DocumentDelivery(client, Configuration(token: null)).SubmitAsync(Document, default));
    }

    [Fact]
    public async Task DoesNotReportRejectedSubmissionAsSuccess()
    {
        using var client = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest))));
        await Assert.ThrowsAsync<HttpRequestException>(() => new DocumentDelivery(client, Configuration()).SubmitAsync(Document, default));
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
