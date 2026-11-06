using System.Security.Claims;
using System.Text;
using Sgv.Api.Domain;

namespace Sgv.Api.Fiscal;

public static class FiscalApi
{
    public static void MapBilling(this WebApplication app)
    {
        var routes=app.MapGroup("/api").RequireAuthorization("Ventas");
        routes.MapGet("/facturacion/ventas",(FiscalRepository store,CancellationToken ct)=>store.PaidSalesAsync(ct));
        routes.MapPost("/ventas/{id:guid}/dte",(Guid id,ClaimsPrincipal user,FiscalService service,CancellationToken ct)=>service.EmitAsync(id,user.Identity!.Name!,cancellation:ct));
        routes.MapGet("/ventas/{id:guid}/dte",(Guid id,FiscalRepository store,CancellationToken ct)=>store.ForSaleAsync(id,ct));
        routes.MapGet("/dte/{id:guid}",(Guid id,FiscalService service,CancellationToken ct)=>service.RequiredAsync(id,ct));
        routes.MapPost("/dte/{id:guid}/reenviar",(Guid id,ClaimsPrincipal user,FiscalService service,CancellationToken ct)=>service.RetryAsync(id,user.Identity!.Name!,ct));
        routes.MapPost("/dte/{id:guid}/invalidacion",(Guid id,InvalidationInput input,ClaimsPrincipal user,FiscalService service,CancellationToken ct)=>service.InvalidateAsync(id,input.Motivo,user.Identity!.Name!,ct));
        routes.MapPost("/dte/{id:guid}/nota-credito",(Guid id,ClaimsPrincipal user,FiscalService service,CancellationToken ct)=>service.CreditNoteAsync(id,user.Identity!.Name!,ct));
        routes.MapGet("/dte",(DateTimeOffset? desde,DateTimeOffset? hasta,string? tipo,string? estado,FiscalRepository store,CancellationToken ct)=>store.SearchAsync(new DteFilter(desde,hasta,tipo,estado),ct));
        routes.MapGet("/facturacion/indicadores",(DateTimeOffset? desde,DateTimeOffset? hasta,string? tipo,string? estado,FiscalRepository store,CancellationToken ct)=>store.IndicatorsAsync(new DteFilter(desde,hasta,tipo,estado),ct));
        routes.MapGet("/dte/exportar",async(DateTimeOffset? desde,DateTimeOffset? hasta,string? tipo,string? estado,FiscalRepository store,CancellationToken ct)=>
        {
            var rows=await store.SearchAsync(new DteFilter(desde,hasta,tipo,estado),ct);
            var csv=new StringBuilder("\uFEFFFecha,Tipo,Estado,NumeroControl,CodigoGeneracion,SelloRecibido\r\n");
            foreach(var row in rows)csv.AppendLine(string.Join(',',new[]{row.Fecha.ToString("O"),row.TipoDte,row.Estado,row.NumeroControl,row.CodigoGeneracion,row.SelloRecibido??""}.Select(AuditCsv.Escape)));
            return Results.File(Encoding.UTF8.GetBytes(csv.ToString()),"text/csv; charset=utf-8","documentos-electronicos.csv");
        });
        app.MapGet("/api/consulta/dte/{codigo}",async(string codigo,FiscalRepository store,CancellationToken ct)=>
        {
            var receipt=await store.PublicReceiptAsync(codigo,ct);
            return receipt is null?Results.NotFound():Results.Ok(receipt);
        });
        if(app.Configuration.GetValue<bool>("Demo:Enabled"))
            routes.MapPost("/facturacion/preparar-demostracion",async(ClaimsPrincipal user,FiscalRepository store,FiscalService service,CancellationToken ct)=>
            {
                var sales=(await store.PaidSalesAsync(ct)).Where(s=>s.Referencia.StartsWith("ADIC-",StringComparison.Ordinal)).OrderBy(s=>s.Referencia,StringComparer.Ordinal).Take(24).ToArray();
                await Parallel.ForEachAsync(sales,new ParallelOptions {MaxDegreeOfParallelism=4,CancellationToken=ct},async(sale,token)=>{await service.EmitAsync(sale.Id,user.Identity!.Name!,cancellation:token);});
                return Results.Ok(await store.IndicatorsAsync(new DteFilter(),ct));
            }).RequireAuthorization("Admin");
    }
}
