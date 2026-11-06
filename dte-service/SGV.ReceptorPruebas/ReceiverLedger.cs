using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Sgv.Fiscal;

namespace Sgv.ReceptorPruebas;

public sealed record ReceptionEntry(int Intentos,string? Respuesta,bool Conflicto);
public sealed record AcceptedDocument(bool Valido,string? Sello);

public sealed class ReceiverLedger(string connectionString)
{
    private static SqlCommand Query(SqlConnection connection,SqlTransaction? tx,string sql,params(string Name,object? Value)[] args)
    {
        var command=new SqlCommand(sql,connection,tx);
        foreach(var(name,value)in args)command.Parameters.AddWithValue(name,value??DBNull.Value);
        return command;
    }

    public async Task<ReceptionEntry> BeginAsync(ReceptionRequest request,string hash,string operation,CancellationToken cancellation)
    {
        await using var connection=new SqlConnection(connectionString); await connection.OpenAsync(cancellation);
        await using var tx=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,cancellation);
        int attempts=1; string? response=null; bool exists=false,conflict=false;
        await using(var query=Query(connection,tx,"SELECT HashJws,Respuesta,Intentos,Operacion,TipoDte FROM dbo.ReceptorDte WITH(UPDLOCK,HOLDLOCK) WHERE CodigoGeneracion=@code",("@code",request.CodigoGeneracion)))
        {
            await using var reader=await query.ExecuteReaderAsync(cancellation);
            if(await reader.ReadAsync(cancellation))
            {
                exists=true; conflict=reader.GetString(0)!=hash||reader.GetString(3)!=operation||reader.GetString(4)!=request.TipoDte;
                response=reader.IsDBNull(1)?null:reader.GetString(1); attempts=reader.GetInt32(2)+1;
            }
        }
        if(!exists)
        {
            await using var insert=Query(connection,tx,"INSERT dbo.ReceptorDte(CodigoGeneracion,HashJws,TipoDte,Operacion) VALUES(@code,@hash,@type,@operation)",("@code",request.CodigoGeneracion),("@hash",hash),("@type",request.TipoDte),("@operation",operation));
            await insert.ExecuteNonQueryAsync(cancellation);
        }
        else if(!conflict&&response is null)
        {
            await using var update=Query(connection,tx,"UPDATE dbo.ReceptorDte SET Intentos=Intentos+1 WHERE CodigoGeneracion=@code",("@code",request.CodigoGeneracion)); await update.ExecuteNonQueryAsync(cancellation);
        }
        await tx.CommitAsync(cancellation);
        return new ReceptionEntry(attempts,response,conflict);
    }

    public async Task<AcceptedDocument> AcceptedAsync(string code,string? type,CancellationToken cancellation)
    {
        await using var connection=new SqlConnection(connectionString); await connection.OpenAsync(cancellation);
        await using var query=Query(connection,null,"SELECT Respuesta,Invalidado FROM dbo.ReceptorDte WHERE CodigoGeneracion=@code AND Operacion='recepciondte' AND (@type IS NULL OR TipoDte=@type)",("@code",code),("@type",type));
        await using var reader=await query.ExecuteReaderAsync(cancellation);
        if(!await reader.ReadAsync(cancellation)||reader.IsDBNull(0)||reader.GetBoolean(1))return new AcceptedDocument(false,null);
        var response=JsonSerializer.Deserialize<FiscalResponse>(reader.GetString(0),FiscalJson.Options)!;
        return new AcceptedDocument(response.Estado=="PROCESADO",response.SelloRecibido);
    }

    public async Task<FiscalResponse> SaveAsync(FiscalResponse response,string? invalidateOriginal,CancellationToken cancellation)
    {
        await using var connection=new SqlConnection(connectionString); await connection.OpenAsync(cancellation);
        await using var tx=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,cancellation);
        await using(var query=Query(connection,tx,"SELECT Respuesta FROM dbo.ReceptorDte WITH(UPDLOCK,HOLDLOCK) WHERE CodigoGeneracion=@code",("@code",response.CodigoGeneracion)))
        {
            var existing=await query.ExecuteScalarAsync(cancellation);
            if(existing is string json){await tx.CommitAsync(cancellation);return JsonSerializer.Deserialize<FiscalResponse>(json,FiscalJson.Options)!;}
        }
        if(invalidateOriginal is not null && response.Estado=="PROCESADO")
        {
            await using var update=Query(connection,tx,"UPDATE dbo.ReceptorDte SET Invalidado=1 WHERE CodigoGeneracion=@code AND Invalidado=0 AND Respuesta IS NOT NULL",("@code",invalidateOriginal));
            if(await update.ExecuteNonQueryAsync(cancellation)!=1)response=FiscalResponse.Create(response.CodigoGeneracion,["DOCUMENTO ORIGINAL YA INVALIDADO O NO RECIBIDO"]);
        }
        await using(var update=Query(connection,tx,"UPDATE dbo.ReceptorDte SET Respuesta=@json WHERE CodigoGeneracion=@code",("@code",response.CodigoGeneracion),("@json",FiscalJson.Serialize(response))))await update.ExecuteNonQueryAsync(cancellation);
        await tx.CommitAsync(cancellation);
        return response;
    }
}
