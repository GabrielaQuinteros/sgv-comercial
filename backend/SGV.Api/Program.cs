using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Sgv.Api.Data;
using Sgv.Api.Domain;
using Sgv.Api.Security;

var builder = WebApplication.CreateBuilder(args);
var signingKey = builder.Configuration["SGV_JWT_KEY"] ?? throw new InvalidOperationException("Configura SGV_JWT_KEY.");
if (Encoding.UTF8.GetByteCount(signingKey) < 32) throw new InvalidOperationException("SGV_JWT_KEY debe tener al menos 32 bytes.");
var connectionString = builder.Configuration.GetConnectionString("Sgv") ?? throw new InvalidOperationException("Configura ConnectionStrings__Sgv.");
builder.Services.AddSingleton(new CommercialStore(connectionString));
builder.Services.AddSingleton(new TokenService(signingKey));
builder.Services.AddHttpClient("dte", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:DteUrl"] ?? "http://localhost:5081");
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Add("X-Service-Key", builder.Configuration["DTE_SERVICE_KEY"]);
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = TokenService.Issuer,
        ValidateAudience = true, ValidAudience = TokenService.Audience,
        ValidateLifetime = true, ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Ventas", policy => policy.RequireRole("Admin", "Ventas"))
    .AddPolicy("Inventario", policy => policy.RequireRole("Admin", "Inventario"))
    .AddPolicy("Auditoria", policy => policy.RequireRole("Admin", "Auditor"))
    .AddPolicy("Admin", policy => policy.RequireRole("Admin"));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(builder.Configuration["Cors:AllowedOrigin"] ?? "http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddProblemDetails();
var app = builder.Build();
app.UseExceptionHandler(error => error.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var status = exception is BusinessException business ? business.StatusCode : 500;
    var detail = exception is BusinessException ? exception.Message : "No fue posible completar la operación. Contacta al administrador.";
    await Results.Problem(statusCode: status, detail: detail).ExecuteAsync(context);
}));
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next(context);
});
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { service = "SGV.Api", status = "ok" }));

app.MapPost("/api/auth/login", async (LoginInput input, CommercialStore store, TokenService tokens, CancellationToken cancellation) =>
{
    if (input.Username.Length > 100 || input.Password.Length > 128) return Results.Unauthorized();
    var account = await store.AuthenticateAsync(input.Username, input.Password, cancellation);
    if (account is null) return Results.Unauthorized();
    return Results.Ok(tokens.Create(account));
}).RequireRateLimiting("login");

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/products", (CommercialStore store, CancellationToken cancellation) => store.ProductsAsync(cancellation));
api.MapGet("/dashboard", (CommercialStore store, CancellationToken cancellation) => store.DashboardAsync(cancellation));
api.MapPost("/products", async (ProductInput input, CommercialStore store, CancellationToken cancellation) => Results.Created("/api/products", new { id = await store.CreateProductAsync(input, cancellation) })).RequireAuthorization("Inventario");
api.MapPost("/inventory/entries", async (StockEntry input, ClaimsPrincipal user, CommercialStore store, CancellationToken cancellation) =>
{
    await store.ReceiveStockAsync(input, user.Identity!.Name!, cancellation);
    return Results.NoContent();
}).RequireAuthorization("Inventario");
api.MapPost("/sales", async (SaleInput input, ClaimsPrincipal user, CommercialStore store, CancellationToken cancellation) => Results.Ok(await store.CreateSaleAsync(input, user.Identity!.Name!, cancellation))).RequireAuthorization("Ventas");
api.MapPost("/sales/{id:guid}/payments", async (Guid id, PaymentInput input, ClaimsPrincipal user, CommercialStore store, CancellationToken cancellation) => Results.Ok(await store.RecordPaymentAsync(id, input, user.Identity!.Name!, cancellation))).RequireAuthorization("Ventas");
api.MapGet("/audit", (DateTimeOffset from, DateTimeOffset to, CommercialStore store, CancellationToken cancellation) => store.AuditEventsAsync(from, to, cancellation)).RequireAuthorization("Auditoria");
api.MapGet("/audit/export", async (DateTimeOffset from, DateTimeOffset to, CommercialStore store, CancellationToken cancellation) =>
{
    var events = await store.AuditEventsAsync(from, to, cancellation);
    return Results.File(AuditCsv.Export(events), "text/csv; charset=utf-8", $"bitacora-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
}).RequireAuthorization("Auditoria");
api.MapPost("/users", async (UserInput input, ClaimsPrincipal user, CommercialStore store, CancellationToken cancellation) => Results.Created("/api/users", new { id = await store.CreateUserAsync(input, user.Identity!.Name!, cancellation) })).RequireAuthorization("Admin");
api.MapPost("/dte/sign", async (DteSignInput input, IHttpClientFactory clients, CancellationToken cancellation) =>
{
    using var response = await clients.CreateClient("dte").PostAsJsonAsync("/api/dte/sign", input, cancellation);
    var document = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellation);
    return Results.Json(document, statusCode: (int)response.StatusCode);
}).RequireAuthorization("Ventas");
api.MapPost("/dte/submit", async (System.Text.Json.JsonElement document, ClaimsPrincipal user, CommercialStore store, IHttpClientFactory clients, CancellationToken cancellation) =>
{
    using var response = await clients.CreateClient("dte").PostAsJsonAsync("/api/dte/submit", document, cancellation);
    var receipt = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellation);
    if (response.IsSuccessStatusCode)
        await store.AuditAsync(user.Identity!.Name!, "DTE enviado", document.GetProperty("id").GetString() ?? "", "Recepción confirmada por la pasarela configurada", cancellation);
    return Results.Json(receipt, statusCode: (int)response.StatusCode);
}).RequireAuthorization("Ventas");

await app.Services.GetRequiredService<CommercialStore>().EnsureAdminAsync(
    builder.Configuration["SGV_ADMIN_USERNAME"] ?? "admin",
    builder.Configuration["SGV_ADMIN_PASSWORD"] ?? throw new InvalidOperationException("Configura SGV_ADMIN_PASSWORD para el alta inicial."),
    app.Lifetime.ApplicationStopping);
await app.RunAsync();

public partial class Program;
