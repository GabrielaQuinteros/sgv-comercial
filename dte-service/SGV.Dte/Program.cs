using Sgv.Dte;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<DocumentSigner>();
builder.Services.AddHttpClient<DocumentDelivery>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.MaxResponseContentBufferSize = 1_048_576;
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddProblemDetails();
var serviceKey = builder.Configuration["DTE_SERVICE_KEY"] ?? throw new InvalidOperationException("Configura DTE_SERVICE_KEY.");
if (serviceKey.Length < 32) throw new InvalidOperationException("DTE_SERVICE_KEY debe tener al menos 32 caracteres.");
var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers["X-Service-Key"].ToString())),
        SHA256.HashData(Encoding.UTF8.GetBytes(serviceKey))))
    {
        context.Response.StatusCode = 401;
        return;
    }
    await next(context);
});
app.MapGet("/health", () => Results.Ok(new { service = "SGV.Dte", status = "ok" }));
app.MapPost("/api/dte/sign", (DteInput input, DocumentSigner signer) =>
{
    try { return Results.Ok(signer.Sign(input)); }
    catch (ArgumentException exception) { return Results.Problem(statusCode: 400, detail: exception.Message); }
    catch (InvalidOperationException exception) { return Results.Problem(statusCode: 503, detail: exception.Message); }
});
app.MapPost("/api/dte/submit", async (SignedDocument document, DocumentDelivery delivery, CancellationToken cancellation) =>
{
    try { return Results.Ok(await delivery.SubmitAsync(document, cancellation)); }
    catch (ArgumentException exception) { return Results.Problem(statusCode: 400, detail: exception.Message); }
    catch (InvalidOperationException exception) { return Results.Problem(statusCode: 503, detail: exception.Message); }
    catch (HttpRequestException) { return Results.Problem(statusCode: 502, detail: "La pasarela DTE no confirmó la recepción del documento."); }
});
await app.RunAsync();
