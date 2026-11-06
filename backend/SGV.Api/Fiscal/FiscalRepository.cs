using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Sgv.Api.Domain;
using Sgv.Fiscal;

namespace Sgv.Api.Fiscal;

public sealed class FiscalRepository(string connectionString)
{
    private static async Task<SqlConnection> OpenAsync(string source, CancellationToken cancellation)
    {
        var connection = new SqlConnection(source);
        try { await connection.OpenAsync(cancellation); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
    private static SqlCommand Command(SqlConnection connection, SqlTransaction? transaction, string sql, params (string Key, object? Value)[] parameters)
    {
        var command = new SqlCommand(sql, connection, transaction);
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return command;
    }
    private static string? Optional(SqlDataReader reader, string key) => reader[key] is DBNull ? null : Convert.ToString(reader[key], CultureInfo.InvariantCulture);
    private static DteRecord Read(SqlDataReader r) => new(
        (Guid)r["Id"], (Guid)r["VentaId"], (string)r["TipoDte"], (string)r["Estado"], (string)r["NumeroControl"], (string)r["CodigoGeneracion"], Optional(r,"SelloRecibido"), Optional(r,"Json"), Optional(r,"Jws"), Optional(r,"Respuesta"), (DateTimeOffset)r["Fecha"], (DateTimeOffset)r["Actualizado"], r["FechaRespuesta"] is DBNull ? null : (DateTimeOffset)r["FechaRespuesta"], r["TiempoEmisionMs"] is DBNull ? null : (long)r["TiempoEmisionMs"], (int)r["Intentos"], (int)r["Version"], r["OriginalId"] is DBNull ? null : (Guid)r["OriginalId"], JsonSerializer.Deserialize<FiscalStep[]>((string)r["Timeline"], FiscalJson.Options)!, Optional(r,"MotivoInvalidacion"), Optional(r,"InvalidacionCodigo"), Optional(r,"InvalidacionJws"), Optional(r,"InvalidacionEstado"), Optional(r,"RespuestaInvalidacion"));

    public async Task<DteRecord?> FindAsync(Guid id, CancellationToken cancellation = default)
    {
        await using var connection = await OpenAsync(connectionString, cancellation);
        await using var command = Command(connection, null, "SELECT * FROM dbo.Dte WHERE Id=@id", ("@id",id));
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        return await reader.ReadAsync(cancellation) ? Read(reader) : null;
    }
    public async Task<DteRecord?> ForSaleAsync(Guid saleId, CancellationToken cancellation = default)
    {
        await using var connection = await OpenAsync(connectionString, cancellation);
        await using var command = Command(connection, null, "SELECT TOP(1) * FROM dbo.Dte WHERE VentaId=@id AND TipoDte<>'05' ORDER BY Fecha DESC", ("@id",saleId));
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        return await reader.ReadAsync(cancellation) ? Read(reader) : null;
    }

    private static async Task<FiscalSale> SaleAsync(SqlConnection connection, SqlTransaction? transaction, Guid id, bool locked, CancellationToken cancellation)
    {
        string status, reference, customerJson, scenario; decimal total;
        var sql = locked ? "SELECT * FROM dbo.Sales WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id" : "SELECT * FROM dbo.Sales WHERE Id=@id";
        await using (var command = Command(connection, transaction, sql, ("@id",id)))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellation);
            if (!await reader.ReadAsync(cancellation)) throw new BusinessException("Venta inexistente.",404);
            status=(string)reader["Status"]; total=(decimal)reader["Total"]; reference=Optional(reader,"Referencia") ?? id.ToString();
            customerJson=(string)reader["ReceptorJson"]; scenario=(string)reader["PruebaEscenario"];
        }
        var lines=new List<FiscalLine>();
        await using(var command=Command(connection,transaction,"SELECT p.Sku,p.Name,i.Quantity,i.UnitPrice FROM dbo.SaleItems i JOIN dbo.Products p ON p.Id=i.ProductId WHERE i.SaleId=@id ORDER BY i.ProductId",("@id",id)))
        {
            await using var reader=await command.ExecuteReaderAsync(cancellation);
            while(await reader.ReadAsync(cancellation)) lines.Add(new FiscalLine(reader.GetString(0),reader.GetString(1),reader.GetInt32(2),reader.GetDecimal(3)));
        }
        return new FiscalSale(id,reference,status,total,JsonSerializer.Deserialize<FiscalCustomer>(customerJson,FiscalJson.Options)!,lines,scenario);
    }
    public async Task<FiscalSale> SaleAsync(Guid id,CancellationToken cancellation=default)
    {
        await using var connection=await OpenAsync(connectionString,cancellation);
        return await SaleAsync(connection,null,id,false,cancellation);
    }

    public async Task<FiscalReservation> ReserveAsync(Guid saleId,string establishment,DateTimeOffset start,Guid? originalId,CancellationToken cancellation)
    {
        await using var connection=await OpenAsync(connectionString,cancellation);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,cancellation);
        var sale=await SaleAsync(connection,transaction,saleId,true,cancellation);
        var type=FiscalDocumentFactory.SelectType(sale.Receptor);
        if(originalId is null && sale.Estado is not ("Pagada" or "Facturada")) throw new BusinessException("Solo se pueden facturar ventas pagadas.",409);
        if(originalId is not null)
        {
            await using var original=Command(connection,transaction,"SELECT COUNT(*) FROM dbo.Dte WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id AND VentaId=@sale AND TipoDte='03' AND Estado='PROCESADO' AND (InvalidacionEstado IS NULL OR InvalidacionEstado='RECHAZADO')",("@id",originalId), ("@sale",saleId));
            if((int)(await original.ExecuteScalarAsync(cancellation))! != 1) throw new BusinessException("La nota de crédito requiere un CCF 03 procesado y vigente.",409);
            type="05";
        }
        DteRecord? existing;
        await using(var query=Command(connection,transaction,originalId is null ? "SELECT * FROM dbo.Dte WITH(UPDLOCK,HOLDLOCK) WHERE VentaId=@sale AND FacturaActiva=1" : "SELECT * FROM dbo.Dte WITH(UPDLOCK,HOLDLOCK) WHERE OriginalId=@original AND TipoDte='05'",("@sale",saleId),("@original",originalId)))
        {
            await using var reader=await query.ExecuteReaderAsync(cancellation);
            existing=await reader.ReadAsync(cancellation)?Read(reader):null;
        }
        if(existing is not null) { await transaction.CommitAsync(cancellation); return new FiscalReservation(existing,sale,false); }
        long number;
        await using(var sequence=Command(connection,transaction,"UPDATE dbo.DteCorrelativos WITH(UPDLOCK,HOLDLOCK) SET Correlativo=Correlativo+1 OUTPUT INSERTED.Correlativo WHERE Establecimiento=@est",("@est",establishment)))
        {
            var value=await sequence.ExecuteScalarAsync(cancellation);
            if(value is null)
            {
                await using var insert=Command(connection,transaction,"INSERT dbo.DteCorrelativos(Establecimiento,Correlativo) VALUES(@est,1)",("@est",establishment));
                await insert.ExecuteNonQueryAsync(cancellation); number=1;
            }
            else number=(long)value;
        }
        var id=Guid.NewGuid(); var code=Guid.NewGuid().ToString("D").ToUpperInvariant();
        var control=$"DTE-{type}-{establishment}-{number.ToString("D15",CultureInfo.InvariantCulture)}";
        await using(var insert=Command(connection,transaction,"INSERT dbo.Dte(Id,VentaId,TipoDte,Estado,FacturaActiva,OriginalId,NumeroControl,CodigoGeneracion,Fecha) VALUES(@id,@sale,@type,'GENERADO',@active,@original,@control,@code,@start)",("@id",id),("@sale",saleId),("@type",type),("@active",originalId is null),("@original",originalId),("@control",control),("@code",code),("@start",start)))
            await insert.ExecuteNonQueryAsync(cancellation);
        await transaction.CommitAsync(cancellation);
        return new FiscalReservation((await FindAsync(id,cancellation))!,sale,true);
    }

    public async Task SavePreparedAsync(Guid id,PreparedFiscalDocument prepared,CancellationToken cancellation)
    {
        var steps=new[] { new FiscalStep("GENERADO",prepared.Generado,"JSON, UUID y número de control asignados."),new FiscalStep("FIRMADO",prepared.Firmado,prepared.Advertencia ?? "JWS firmado con certificado .P12.") };
        await using var connection=await OpenAsync(connectionString,cancellation);
        await using var command=Command(connection,null,"UPDATE dbo.Dte SET Json=@json,Jws=@jws,Estado='FIRMADO',Timeline=@timeline,Actualizado=SYSDATETIMEOFFSET(),Version=Version+1 WHERE Id=@id",("@id",id),("@json",prepared.Json),("@jws",prepared.Jws),("@timeline",FiscalJson.Serialize(steps)));
        await command.ExecuteNonQueryAsync(cancellation);
    }
    public async Task SentAsync(Guid id,CancellationToken cancellation)
    {
        await using var connection=await OpenAsync(connectionString,cancellation);
        await using var command=Command(connection,null,"UPDATE dbo.Dte SET Estado='ENVIADO',Timeline=JSON_MODIFY(Timeline,'append $',JSON_QUERY(@step)),Actualizado=SYSDATETIMEOFFSET(),Version=Version+1 WHERE Id=@id",("@id",id),("@step",FiscalJson.Serialize(new FiscalStep("ENVIADO",DateTimeOffset.UtcNow,"Enviado al receptor de pruebas."))));
        await command.ExecuteNonQueryAsync(cancellation);
    }
    public async Task<bool> ClaimRetryAsync(Guid id,CancellationToken cancellation)
    {
        await using var connection=await OpenAsync(connectionString,cancellation);
        await using var command=Command(connection,null,"UPDATE dbo.Dte SET Estado='ENVIADO',Intentos=Intentos+1,Actualizado=SYSDATETIMEOFFSET(),Version=Version+1 WHERE Id=@id AND Estado='CONTINGENCIA'",("@id",id));
        return await command.ExecuteNonQueryAsync(cancellation)==1;
    }

    public async Task CompleteAsync(DteRecord document,FiscalResponse response,string actor,CancellationToken cancellation)
    {
        await using var connection=await OpenAsync(connectionString,cancellation);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(cancellation);
        var now=DateTimeOffset.UtcNow;
        var elapsed=Math.Max(0,(long)(now-document.Fecha).TotalMilliseconds);
        await using(var command=Command(connection,transaction,"UPDATE dbo.Dte SET Estado=@state,SelloRecibido=@seal,Respuesta=@response,FechaRespuesta=@date,TiempoEmisionMs=@duration,Timeline=JSON_MODIFY(Timeline,'append $',JSON_QUERY(@step)),Actualizado=SYSDATETIMEOFFSET(),Version=Version+1 WHERE Id=@id",("@id",document.Id),("@state",response.Estado),("@seal",response.SelloRecibido),("@response",FiscalJson.Serialize(response)),("@date",response.Estado=="CONTINGENCIA"?null:now),("@duration",elapsed),("@step",FiscalJson.Serialize(new FiscalStep(response.Estado,now,response.DescripcionMsg)))))
            await command.ExecuteNonQueryAsync(cancellation);
        if(response.Estado=="PROCESADO" && document.TipoDte!="05")
        {
            await using var sale=Command(connection,transaction,"UPDATE dbo.Sales SET Status='Facturada' WHERE Id=@id",("@id",document.VentaId));
            await sale.ExecuteNonQueryAsync(cancellation);
        }
        await AuditAsync(connection,transaction,actor,"DTE "+response.Estado,document.CodigoGeneracion,response.DescripcionMsg,cancellation);
        await transaction.CommitAsync(cancellation);
    }

    private static async Task AuditAsync(SqlConnection connection,SqlTransaction transaction,string actor,string action,string code,string result,CancellationToken cancellation)
    {
        await using var command=Command(connection,transaction,"INSERT dbo.AuditLog(Actor,Action,EntityId,Detail) VALUES(@actor,@action,@code,@result)",("@actor",actor),("@action",action),("@code",code),("@result",result.Length>1000?result[..1000]:result));
        await command.ExecuteNonQueryAsync(cancellation);
    }

    public async Task<IReadOnlyList<PaidSale>> PaidSalesAsync(CancellationToken cancellation=default)
    {
        var results=new List<PaidSale>();
        await using var connection=await OpenAsync(connectionString,cancellation);
        await using var command=Command(connection,null,"SELECT TOP(500) s.Id,COALESCE(s.Referencia,CONVERT(VARCHAR(36),s.Id)),s.Status,s.Total,JSON_VALUE(s.ReceptorJson,'$.nombre'),JSON_VALUE(s.ReceptorJson,'$.nrc'),s.PruebaEscenario,d.Id,d.Estado FROM dbo.Sales s OUTER APPLY(SELECT TOP(1) Id,Estado FROM dbo.Dte WHERE VentaId=s.Id AND FacturaActiva=1 ORDER BY Fecha DESC)d WHERE s.Status IN('Pagada','Facturada') ORDER BY s.CreatedAt,s.Referencia");
        await using var reader=await command.ExecuteReaderAsync(cancellation);
        while(await reader.ReadAsync(cancellation)) results.Add(new PaidSale(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetDecimal(3),reader.IsDBNull(4)?"Cliente":reader.GetString(4),reader.IsDBNull(5)||string.IsNullOrWhiteSpace(reader.GetString(5))?"01":"03",reader.GetString(6),reader.IsDBNull(7)?null:reader.GetGuid(7),reader.IsDBNull(8)?null:reader.GetString(8)));
        return results;
    }

    public async Task<IReadOnlyList<DteRecord>> SearchAsync(DteFilter filter,CancellationToken cancellation=default)
    {
        if(filter.Desde>filter.Hasta || (filter.Tipo is not null && filter.Tipo is not ("01" or "03" or "05"))) throw new BusinessException("Filtros de consulta inválidos.");
        var result=new List<DteRecord>();
        await using var connection=await OpenAsync(connectionString,cancellation);
        await using var command=Command(connection,null,"SELECT TOP(1001) * FROM dbo.Dte WHERE (@from IS NULL OR Fecha>=@from) AND (@to IS NULL OR Fecha<@to) AND (@type IS NULL OR TipoDte=@type) AND (@state IS NULL OR Estado=@state) ORDER BY Fecha DESC",("@from",filter.Desde),("@to",filter.Hasta),("@type",filter.Tipo),("@state",filter.Estado));
        await using var reader=await command.ExecuteReaderAsync(cancellation);
        while(await reader.ReadAsync(cancellation)) result.Add(Read(reader));
        if(result.Count>1000) throw new BusinessException("Reduce el rango de consulta: supera 1000 documentos.",413);
        return result;
    }

    public async Task<FiscalIndicators> IndicatorsAsync(DteFilter filter,CancellationToken cancellation=default)
    {
        var rows=await SearchAsync(filter,cancellation);
        var total=rows.Count; var processed=rows.Count(d=>d.Estado=="PROCESADO"); var rejected=rows.Count(d=>d.Estado=="RECHAZADO");
        var durations=rows.Where(d=>d.FechaRespuesta is not null && d.TiempoEmisionMs is not null).Select(d=>(double)d.TiempoEmisionMs!.Value).ToArray();
        var reasons=rows.Where(d=>d.Estado=="RECHAZADO"&&d.Respuesta is not null).Select(d=>JsonSerializer.Deserialize<FiscalResponse>(d.Respuesta!,FiscalJson.Options)!.DescripcionMsg).GroupBy(x=>x).OrderByDescending(x=>x.Count()).ThenBy(x=>x.Key,StringComparer.Ordinal).Take(5).Select(g=>new RejectionReason(g.Key,g.Count())).ToArray();
        return new FiscalIndicators(total,processed,rejected,rows.Count(d=>d.Estado=="CONTINGENCIA"),rows.Count(d=>d.Estado=="INVALIDADO"),total==0?0:Math.Round(processed*100m/total,2),total==0?0:Math.Round(rejected*100m/total,2),durations.Length==0?0:Math.Round(durations.Average(),0),reasons);
    }

    public async Task<bool> ClaimInvalidationAsync(Guid id,string reason,string code,string jws,CancellationToken cancellation)
    {
        await using var connection=await OpenAsync(connectionString,cancellation);
        await using var command=Command(connection,null,"UPDATE dbo.Dte SET MotivoInvalidacion=@reason,InvalidacionCodigo=@code,InvalidacionJws=@jws,InvalidacionEstado='ENVIADO',Actualizado=SYSDATETIMEOFFSET(),Version=Version+1 WHERE Id=@id AND Estado='PROCESADO' AND (InvalidacionEstado IS NULL OR InvalidacionEstado IN('RECHAZADO','CONTINGENCIA')) AND NOT EXISTS(SELECT 1 FROM dbo.Dte n WHERE n.OriginalId=@id)",("@id",id),("@reason",reason),("@code",code),("@jws",jws));
        return await command.ExecuteNonQueryAsync(cancellation)==1;
    }
    public async Task FinishInvalidationAsync(DteRecord document,FiscalResponse response,string actor,CancellationToken cancellation)
    {
        await using var connection=await OpenAsync(connectionString,cancellation);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(cancellation);
        var accepted=response.Estado=="PROCESADO";
        await using(var command=Command(connection,transaction,"UPDATE dbo.Dte SET Estado=CASE WHEN @accepted=1 THEN 'INVALIDADO' ELSE Estado END,FacturaActiva=CASE WHEN @accepted=1 THEN 0 ELSE FacturaActiva END,InvalidacionEstado=@state,RespuestaInvalidacion=@response,Timeline=JSON_MODIFY(Timeline,'append $',JSON_QUERY(@step)),Actualizado=SYSDATETIMEOFFSET(),Version=Version+1 WHERE Id=@id",("@id",document.Id),("@accepted",accepted),("@state",response.Estado),("@response",FiscalJson.Serialize(response)),("@step",FiscalJson.Serialize(new FiscalStep(accepted?"INVALIDADO":"INVALIDACION_"+response.Estado,DateTimeOffset.UtcNow,response.DescripcionMsg)))))
            await command.ExecuteNonQueryAsync(cancellation);
        if(accepted && document.TipoDte!="05")
        {
            await using var sale=Command(connection,transaction,"UPDATE dbo.Sales SET Status='Pagada' WHERE Id=@id",("@id",document.VentaId));
            await sale.ExecuteNonQueryAsync(cancellation);
        }
        await AuditAsync(connection,transaction,actor,"INVALIDACIÓN "+response.Estado,document.CodigoGeneracion,response.DescripcionMsg,cancellation);
        await transaction.CommitAsync(cancellation);
    }

    public async Task<object?> PublicReceiptAsync(string code,CancellationToken cancellation)
    {
        await using var connection=await OpenAsync(connectionString,cancellation);
        await using var command=Command(connection,null,"SELECT Estado,TipoDte,NumeroControl,CodigoGeneracion,SelloRecibido,Fecha,Json FROM dbo.Dte WHERE CodigoGeneracion=@code",("@code",code));
        await using var reader=await command.ExecuteReaderAsync(cancellation);
        if(!await reader.ReadAsync(cancellation)) return null;
        var summary=reader.IsDBNull(6)?default:JsonSerializer.Deserialize<JsonElement>(reader.GetString(6)).GetProperty("resumen");
        var total=summary.ValueKind==JsonValueKind.Undefined?0:summary.TryGetProperty("totalPagar",out var pay)?pay.GetDecimal():summary.GetProperty("montoTotalOperacion").GetDecimal();
        return new { ambiente="00",estado=reader.GetString(0),tipoDte=reader.GetString(1),numeroControl=reader.GetString(2),codigoGeneracion=reader.GetString(3),selloRecibido=Optional(reader,"SelloRecibido"),fecha=reader.GetFieldValue<DateTimeOffset>(5),emisor="Adventure Works",total };
    }
}
