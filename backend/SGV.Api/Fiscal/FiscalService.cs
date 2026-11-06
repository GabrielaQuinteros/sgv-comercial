using System.Text.Json;
using System.Text.Json.Nodes;
using Sgv.Api.Domain;
using Sgv.Fiscal;

namespace Sgv.Api.Fiscal;

public sealed class FiscalService(FiscalRepository repository,IFiscalClient client,IConfiguration configuration,ILogger<FiscalService> logger)
{
    public async Task<DteRecord> EmitAsync(Guid saleId,string actor,Guid? originalId=null,CancellationToken cancellation=default)
    {
        var establishment=configuration["Fiscal:Establecimiento"]??"M001P001";
        if(establishment.Length!=8||establishment.Any(c=>!char.IsAsciiLetterUpper(c)&&!char.IsAsciiDigit(c))) throw new BusinessException("Establecimiento fiscal inválido.");
        var reservation=await repository.ReserveAsync(saleId,establishment,DateTimeOffset.UtcNow,originalId,cancellation);
        if(!reservation.Nuevo) return reservation.Documento;
        return await ProcessAsync(reservation.Documento,reservation.Venta,actor,cancellation);
    }

    public async Task<DteRecord> RetryAsync(Guid id,string actor,CancellationToken cancellation=default)
    {
        var doc=await RequiredAsync(id,cancellation);
        if(!await repository.ClaimRetryAsync(id,cancellation)) throw new BusinessException("Solo se puede reenviar un DTE en contingencia.",409);
        doc=(await repository.FindAsync(id,cancellation))!;
        var sale=await repository.SaleAsync(doc.VentaId,cancellation);
        return await ProcessAsync(doc,sale,actor,cancellation);
    }

    private async Task<DteRecord> ProcessAsync(DteRecord doc,FiscalSale sale,string actor,CancellationToken cancellation)
    {
        FiscalResponse response;
        try
        {
            if(string.IsNullOrEmpty(doc.Jws))
            {
                JsonElement? original=null;
                if(doc.OriginalId is not null) original=JsonSerializer.Deserialize<JsonElement>((await RequiredAsync(doc.OriginalId.Value,cancellation)).Json!);
                var prepared=await client.PrepareAsync(new FiscalPreparation(sale,doc.TipoDte,doc.NumeroControl,doc.CodigoGeneracion,original),cancellation);
                await repository.SavePreparedAsync(doc.Id,prepared,cancellation);
                doc=(await repository.FindAsync(doc.Id,cancellation))!;
            }
            await repository.SentAsync(doc.Id,cancellation);
            var version=doc.TipoDte=="01"?1:3;
            response=await client.SendAsync(new FiscalTransmission(new ReceptionRequest("00",doc.Intentos,version,doc.TipoDte,doc.Jws!,doc.CodigoGeneracion),sale.Escenario),cancellation);
        }
        catch(Exception exception) when(exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            logger.LogWarning(exception,"No se completó la comunicación para DTE {Codigo}",doc.CodigoGeneracion);
            response=FiscalResponse.Contingency(doc.CodigoGeneracion,"SERVICIO FISCAL NO DISPONIBLE. REINTENTA EL ENVÍO.");
        }
        using var persist=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await repository.CompleteAsync(doc,response,actor,persist.Token);
        return (await repository.FindAsync(doc.Id,persist.Token))!;
    }

    public async Task<DteRecord> CreditNoteAsync(Guid originalId,string actor,CancellationToken cancellation=default)
    {
        var original=await RequiredAsync(originalId,cancellation);
        return await EmitAsync(original.VentaId,actor,originalId,cancellation);
    }

    public async Task<DteRecord> InvalidateAsync(Guid id,string reason,string actor,CancellationToken cancellation=default)
    {
        if(string.IsNullOrWhiteSpace(reason)||reason.Trim().Length is <5 or >250) throw new BusinessException("El motivo debe tener de 5 a 250 caracteres.");
        var doc=await RequiredAsync(id,cancellation);
        if(doc.Estado!="PROCESADO") throw new BusinessException("Solo se puede invalidar un DTE procesado.",409);
        if(doc.InvalidacionEstado=="CONTINGENCIA"&&doc.MotivoInvalidacion!=reason.Trim())throw new BusinessException("Reintenta la invalidación con el motivo original.",409);
        var code=doc.InvalidacionEstado=="CONTINGENCIA"?doc.InvalidacionCodigo!:Guid.NewGuid().ToString("D").ToUpperInvariant();
        var jws=doc.InvalidacionEstado=="CONTINGENCIA"?doc.InvalidacionJws!:null;
        if(jws is null)
        {
            var original=JsonNode.Parse(doc.Json!)!;
            // Antes de producción, validar el evento de invalidación contra el esquema oficial vigente del MH.
            var document=new JsonObject
            {
                ["identificacion"]=new JsonObject { ["version"]=2,["ambiente"]="00",["codigoGeneracion"]=code },
                ["emisor"]=original["emisor"]!.DeepClone(),
                ["documento"]=new JsonObject { ["tipoDte"]=doc.TipoDte,["numeroControl"]=doc.NumeroControl,["codigoGeneracion"]=doc.CodigoGeneracion,["selloRecibido"]=doc.SelloRecibido,["fecEmi"]=original["identificacion"]!["fecEmi"]!.DeepClone() },
                ["motivo"]=new JsonObject { ["tipoAnulacion"]=2,["motivoAnulacion"]=reason.Trim(),["nombreResponsable"]=actor,["tipDocResponsable"]="36",["numDocResponsable"]=original["emisor"]!["nit"]!.DeepClone() }
            };
            jws=(await client.SignEventAsync(document,cancellation)).Jws;
        }
        if(!await repository.ClaimInvalidationAsync(id,reason.Trim(),code,jws,cancellation)) throw new BusinessException("El DTE tiene una corrección activa o una nota de crédito.",409);
        FiscalResponse response;
        try { response=await client.SendAsync(new FiscalTransmission(new ReceptionRequest("00",1,2,doc.TipoDte,jws,code),Operacion:"invalidacion"),cancellation); }
        catch(Exception exception) when(exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            logger.LogWarning(exception,"Invalidación en contingencia para {Codigo}",doc.CodigoGeneracion);
            response=FiscalResponse.Contingency(code,"NO SE CONFIRMÓ LA INVALIDACIÓN. EL DOCUMENTO CONSERVA SU ESTADO.");
        }
        using var persist=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await repository.FinishInvalidationAsync(doc,response,actor,persist.Token);
        return (await repository.FindAsync(id,persist.Token))!;
    }

    public async Task<DteRecord> RequiredAsync(Guid id,CancellationToken cancellation=default)=>await repository.FindAsync(id,cancellation)??throw new BusinessException("DTE inexistente.",404);
}
