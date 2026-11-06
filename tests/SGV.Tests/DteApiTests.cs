using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sgv.Dte;
using Xunit;

namespace Sgv.Tests;

public sealed class DteApiTests
{
    private static SignedDocument Document => new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "{\"total\":113}", "signed-document", "certificate");

    [Fact]
    public async Task RequiresServiceCredentialAndReturnsGatewayReceipt()
    {
        await using var factory = new DteFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/dte/submit", Document)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Service-Key", factory.ServiceKey);
        var document = Document;
        using var response = await client.PostAsJsonAsync("/api/dte/submit", document);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<DeliveryReceipt>();
        Assert.Equal(document.Id, receipt!.DocumentId);
        Assert.Equal(202, receipt.StatusCode);
        Assert.Contains("gateway-receipt", receipt.AuthorityResponse);
    }

    [Fact]
    public async Task ReturnsValidationConfigurationAndGatewayFailures()
    {
        await using var valid = new DteFactory();
        using var client = valid.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Key", valid.ServiceKey);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/dte/submit", Document with { Signature = "" })).StatusCode);
        await using var missing = new DteFactory(configured: false);
        using var missingClient = missing.CreateClient();
        missingClient.DefaultRequestHeaders.Add("X-Service-Key", missing.ServiceKey);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await missingClient.PostAsJsonAsync("/api/dte/submit", Document)).StatusCode);
        await using var rejected = new DteFactory(status: HttpStatusCode.BadRequest);
        using var rejectedClient = rejected.CreateClient();
        rejectedClient.DefaultRequestHeaders.Add("X-Service-Key", rejected.ServiceKey);
        Assert.Equal(HttpStatusCode.BadGateway, (await rejectedClient.PostAsJsonAsync("/api/dte/submit", Document)).StatusCode);
    }

    private sealed class DteFactory(bool configured = true, HttpStatusCode status = HttpStatusCode.Accepted) : WebApplicationFactory<DocumentDelivery>
    {
        public string ServiceKey { get; } = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("DTE_SERVICE_KEY", ServiceKey);
            builder.UseSetting("DTE_AUTHORITY_URL", "https://authority.example.invalid/dte");
            builder.UseSetting("DTE_AUTHORITY_TOKEN", configured ? "test-credential" : "");
            builder.ConfigureTestServices(services => services.AddHttpClient<DocumentDelivery>().ConfigurePrimaryHttpMessageHandler(() => new GatewayHandler(status)));
        }
    }

    private sealed class GatewayHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{\"receipt\":\"gateway-receipt\"}") });
    }
}
