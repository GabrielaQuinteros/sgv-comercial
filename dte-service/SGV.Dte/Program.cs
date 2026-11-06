using Sgv.Dte;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<DocumentSigner>();
builder.Services.AddSingleton<IClock, NtpClock>();
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
app.MapPost("/api/dte/sign", async (DteInput input, DocumentSigner signer, CancellationToken cancellation) =>
{
    try { return Results.Ok(await signer.SignAsync(input, cancellation)); }
    catch (ArgumentException exception) { return Results.Problem(statusCode: 400, detail: exception.Message); }
    catch (InvalidOperationException exception) { return Results.Problem(statusCode: 503, detail: exception.Message); }
});
await app.RunAsync();
