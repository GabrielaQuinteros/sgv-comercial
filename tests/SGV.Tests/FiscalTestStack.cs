using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Sgv.Api.Domain;
using Sgv.Api.Fiscal;
using Sgv.Dte;
using Sgv.Fiscal;
using Sgv.ReceptorPruebas;

namespace Sgv.Tests;

public sealed class FiscalTestCertificate : IDisposable
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "sgv-fiscal-" + Guid.NewGuid().ToString("N"));
    public string Password { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public string PrivatePath => Path.Combine(Folder, "firma.p12");
    public string PublicPath => Path.Combine(Folder, "public.cer");
    public FiscalTestCertificate()
    {
        Directory.CreateDirectory(Folder);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=SGV Fiscal Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
        File.WriteAllBytes(PrivatePath, certificate.Export(X509ContentType.Pkcs12, Password));
        File.WriteAllBytes(PublicPath, certificate.Export(X509ContentType.Cert));
    }
    public void Dispose() => Directory.Delete(Folder, true);
}

public sealed class FiscalTestStack : IAsyncDisposable
{
    private readonly FiscalTestCertificate certificate = new();
    private readonly WebApplicationFactory<ReceiverService> receiver;
    private readonly WebApplicationFactory<FiscalSigning> dte;
    private readonly WebApplicationFactory<Program> api;
    public HttpClient Client { get; }
    public HttpClient ReceiverClient { get; }
    public string ServiceKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public FiscalRepository Repository { get; }

    public FiscalTestStack(SqlFixture database)
    {
        Repository = new FiscalRepository(database.ConnectionString!);
        receiver = new WebApplicationFactory<ReceiverService>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sgv", database.ConnectionString!);
            builder.UseSetting("DTE_SERVICE_KEY", ServiceKey);
            builder.UseSetting("DTE_PUBLIC_CERTIFICATE_PATH", certificate.PublicPath);
            builder.UseSetting("Latency:MinimumMs", "0"); builder.UseSetting("Latency:MaximumMs", "0"); builder.UseSetting("FaultInjection:Enabled", "true");
        });
        ReceiverClient = receiver.CreateClient();
        dte = new WebApplicationFactory<FiscalSigning>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DTE_SERVICE_KEY", ServiceKey); builder.UseSetting("DTE_CERTIFICATE_PATH", certificate.PrivatePath); builder.UseSetting("DTE_CERTIFICATE_PASSWORD", certificate.Password);
            builder.UseSetting("Receiver:Url", "http://receiver.local/"); builder.UseSetting("Receiver:TimeoutSeconds", "0.5");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IClock>(new FixedFiscalClock());
                services.AddHttpClient<ReceptionGateway>().ConfigurePrimaryHttpMessageHandler(() => new Bridge(ReceiverClient));
            });
        });
        var dteClient = dte.CreateClient();
        api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sgv", database.ConnectionString!); builder.UseSetting("SGV_JWT_KEY", Convert.ToHexString(RandomNumberGenerator.GetBytes(48)));
            builder.UseSetting("SGV_ADMIN_USERNAME", "admin"); builder.UseSetting("SGV_ADMIN_PASSWORD", database.AdminPassword); builder.UseSetting("DTE_SERVICE_KEY", ServiceKey);
            builder.ConfigureTestServices(services => services.AddHttpClient<IFiscalClient, FiscalClient>().ConfigurePrimaryHttpMessageHandler(() => new Bridge(dteClient)));
        });
        Client = api.CreateClient();
    }
    public async Task LoginAsync(SqlFixture database)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new LoginInput("admin", database.AdminPassword)); response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
    }
    public async ValueTask DisposeAsync()
    {
        Client.Dispose(); ReceiverClient.Dispose(); await api.DisposeAsync(); await dte.DisposeAsync(); await receiver.DisposeAsync(); certificate.Dispose();
    }
    private sealed class FixedFiscalClock : IClock
    {
        public Task<DateTimeOffset> UtcNowAsync(CancellationToken cancellation) => Task.FromResult(DateTimeOffset.UtcNow);
    }
    private sealed class Bridge(HttpClient target) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            using var clone = new HttpRequestMessage(request.Method, new Uri(target.BaseAddress!, request.RequestUri!.PathAndQuery));
            foreach (var header in request.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (request.Content is not null)
            {
                clone.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(cancellation));
                foreach (var header in request.Content.Headers) clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return await target.SendAsync(clone, cancellation);
        }
    }
    public static async Task<Guid> PaidSaleAsync(SqlFixture database, FiscalCustomer? customer = null, string scenario = "normal", bool badTotal = false)
    {
        var id = await database.Store.CreateProductAsync(new ProductInput("DTE-" + Guid.NewGuid().ToString("N"), "Producto fiscal", 20m, 1), default);
        await database.Store.ReceiveStockAsync(new StockEntry(id, 100, "DTE-QA"), "qa", default);
        var sale = await database.Store.CreateSaleAsync(new SaleInput([new SaleLineInput(id, 2)]), "qa", default);
        await database.Store.RecordPaymentAsync(sale.Id, new PaymentInput(sale.Total, "Transferencia", Guid.NewGuid().ToString("N")), "qa", default);
        await using var connection = new SqlConnection(database.ConnectionString); await connection.OpenAsync();
        await using var update = new SqlCommand("UPDATE dbo.Sales SET ReceptorJson=@customer,PruebaEscenario=@scenario,Total=Total+@delta WHERE Id=@id", connection);
        update.Parameters.AddWithValue("@customer", FiscalJson.Serialize(customer ?? new FiscalCustomer("Cliente QA", "06140101900011")));
        update.Parameters.AddWithValue("@scenario", scenario); update.Parameters.AddWithValue("@delta", badTotal ? 1m : 0m); update.Parameters.AddWithValue("@id", sale.Id); await update.ExecuteNonQueryAsync();
        return sale.Id;
    }
}
