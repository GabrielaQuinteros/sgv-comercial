using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sgv.Fiscal;

namespace Sgv.ReceptorPruebas;

public sealed class ReceiverService(ReceiverLedger ledger,IConfiguration configuration)
{
    public async Task<FiscalResponse> ReceiveAsync(ReceptionRequest request,string operation,string scenario,CancellationToken cancellation)
    {
        if(request.Ambiente!="00"||!FiscalValidator.IsUuid(request.CodigoGeneracion??""))return FiscalResponse.Create(request.CodigoGeneracion??"",["AMBIENTE O CÓDIGO DE GENERACIÓN NO VÁLIDO"]);
        JsonObject document;
        try
        {
            using var certificate=new X509Certificate2(configuration["DTE_PUBLIC_CERTIFICATE_PATH"]??throw new InvalidOperationException("Configura el certificado público de pruebas."));
            document=JwsDocuments.Verify(request.Documento??"",certificate);
            if(FiscalValidator.Text(document["identificacion"]?["codigoGeneracion"])!=request.CodigoGeneracion)throw new CryptographicException("Código diferente al payload firmado.");
        }
        catch(Exception exception)when(exception is CryptographicException or JsonException or InvalidOperationException or ArgumentException)
        {
            return FiscalResponse.Create(request.CodigoGeneracion??"",["FIRMA O ESTRUCTURA DEL DOCUMENTO NO VÁLIDA"]);
        }
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Documento??"")));
        var entry=await ledger.BeginAsync(request,hash,operation,cancellation);
        if(entry.Conflicto)return FiscalResponse.Create(request.CodigoGeneracion,["CÓDIGO DE GENERACIÓN UTILIZADO CON OTRO CONTENIDO"]);
        if(entry.Respuesta is not null)return JsonSerializer.Deserialize<FiscalResponse>(entry.Respuesta,FiscalJson.Options)!;
        var minimum=configuration.GetValue<int?>("Latency:MinimumMs")??300;
        var maximum=configuration.GetValue<int?>("Latency:MaximumMs")??900;
        var delay=scenario=="timeout-once"&&entry.Intentos==1&&configuration.GetValue<bool>("FaultInjection:Enabled")?9500:Random.Shared.Next(Math.Max(0,minimum),Math.Max(minimum,maximum)+1);
        await Task.Delay(delay,cancellation);
        var errors=new List<string>(); string? original=null;
        try
        {
            if(operation=="invalidacion")original=await ValidateInvalidationAsync(document,errors,cancellation);
            else
            {
                errors.AddRange(FiscalValidator.Validate(document));
                if(FiscalValidator.Text(document["identificacion"]?["tipoDte"])!=request.TipoDte||FiscalValidator.Number(document["identificacion"]?["version"])!=request.Version)errors.Add("TIPO O VERSIÓN DISTINTOS DEL DOCUMENTO FIRMADO");
                if(request.TipoDte=="05"&&errors.Count==0)
                {
                    var reference=FiscalValidator.Text(document["documentoRelacionado"]?[0]?["numeroDocumento"]);
                    if(!(await ledger.AcceptedAsync(reference,"03",cancellation)).Valido)errors.Add("CCF ORIGINAL NO PROCESADO O INVALIDADO");
                }
            }
        }
        catch(Exception exception)when(exception is InvalidOperationException or ArgumentException or OverflowException or JsonException)
        {
            errors.Add("ESTRUCTURA DEL DOCUMENTO NO VÁLIDA");
        }
        var seal=Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
        return await ledger.SaveAsync(FiscalResponse.Create(request.CodigoGeneracion,errors,seal),errors.Count==0?original:null,cancellation);
    }

    private async Task<string?> ValidateInvalidationAsync(JsonObject document,List<string> errors,CancellationToken cancellation)
    {
        if(FiscalValidator.Text(document["identificacion"]?["ambiente"])!="00")errors.Add("AMBIENTE NO VÁLIDO");
        var reason=FiscalValidator.Text(document["motivo"]?["motivoAnulacion"]);
        if(reason.Trim().Length is <5 or >250)errors.Add("MOTIVO DE INVALIDACIÓN OBLIGATORIO");
        var original=FiscalValidator.Text(document["documento"]?["codigoGeneracion"]);
        var accepted=await ledger.AcceptedAsync(original,null,cancellation);
        if(!accepted.Valido||accepted.Sello!=FiscalValidator.Text(document["documento"]?["selloRecibido"]))errors.Add("DOCUMENTO ORIGINAL O SELLO NO VÁLIDO");
        if(!FiscalValidator.IsNit(FiscalValidator.Text(document["emisor"]?["nit"])))errors.Add("NIT DEL EMISOR NO VÁLIDO");
        return original;
    }
}
