using System.Security.Cryptography;
using System.Text;
using Sgv.Fiscal;
using Sgv.ReceptorPruebas;

var builder=WebApplication.CreateBuilder(args);
var key=builder.Configuration["DTE_SERVICE_KEY"]??throw new InvalidOperationException("Configura DTE_SERVICE_KEY.");
if(key.Length<32)throw new InvalidOperationException("La credencial de servicio requiere al menos 32 caracteres.");
builder.Services.AddSingleton(new ReceiverLedger(builder.Configuration.GetConnectionString("Sgv")??throw new InvalidOperationException("Configura SQL Server.")));
builder.Services.AddSingleton<ReceiverService>();
builder.Services.AddProblemDetails();
builder.WebHost.ConfigureKestrel(options=>options.Limits.MaxRequestBodySize=1_100_000);
var app=builder.Build();
app.UseExceptionHandler();
app.Use(async(context,next)=>
{
    if(context.Request.Path.StartsWithSegments("/api")&&!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)),SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers["X-Service-Key"].ToString()))))
    {context.Response.StatusCode=401;return;}
    await next(context);
});
app.MapGet("/health",()=>Results.Ok(new {service="SGV.ReceptorPruebas",ambiente="00",status="ok"}));
app.MapPost("/api/receptor-pruebas/recepciondte",async(ReceptionRequest request,HttpContext context,ReceiverService receiver,CancellationToken ct)=>Results.Ok(await receiver.ReceiveAsync(request,"recepciondte",context.Request.Headers["X-Prueba-Escenario"].ToString(),ct)));
app.MapPost("/api/receptor-pruebas/invalidacion",async(ReceptionRequest request,ReceiverService receiver,CancellationToken ct)=>Results.Ok(await receiver.ReceiveAsync(request,"invalidacion","normal",ct)));
await app.RunAsync();
