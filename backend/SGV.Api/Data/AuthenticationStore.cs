using Microsoft.Data.SqlClient;
using Sgv.Api.Domain;
using Sgv.Api.Security;

namespace Sgv.Api.Data;

public sealed partial class CommercialStore
{
    public async Task<UserAccount?> AuthenticateAsync(string username, string password, CancellationToken cancellation)
    {
        if (username.Length > 100 || password.Length > 128) return null;
        await using var connection = await OpenAsync(cancellation);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellation);
        UserAccount? account = null;
        var failedAttempts = 0;
        DateTimeOffset? lockedUntil = null;
        await using (var query = new SqlCommand("SELECT Id,Username,PasswordHash,Role,Active,FailedAttempts,LockedUntil FROM dbo.Users WITH(UPDLOCK,HOLDLOCK) WHERE Username=@username", connection, transaction))
        {
            Parameter(query, "@username", username.Trim().ToLowerInvariant());
            await using var reader = await query.ExecuteReaderAsync(cancellation);
            if (await reader.ReadAsync(cancellation))
            {
                account = new UserAccount(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4));
                failedAttempts = reader.GetInt32(5);
                lockedUntil = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6);
            }
        }
        if (account is null || !account.Active || lockedUntil > DateTimeOffset.UtcNow) return null;
        if (lockedUntil is not null) failedAttempts = 0;
        var authenticated = PasswordHasher.Verify(password, account.PasswordHash);
        failedAttempts = authenticated ? 0 : failedAttempts + 1;
        lockedUntil = failedAttempts >= 5 ? DateTimeOffset.UtcNow.AddMinutes(15) : null;
        await using var update = new SqlCommand("UPDATE dbo.Users SET FailedAttempts=@attempts,LockedUntil=@until WHERE Id=@id", connection, transaction);
        Parameter(update, "@attempts", failedAttempts);
        Parameter(update, "@until", (object?)lockedUntil ?? DBNull.Value);
        Parameter(update, "@id", account.Id);
        await update.ExecuteNonQueryAsync(cancellation);
        await WriteAuditAsync(connection, transaction, account.Username, authenticated ? "Inicio de sesión" : "Acceso rechazado", account.Id.ToString(), authenticated ? "Autenticación correcta" : "Credenciales no válidas", cancellation);
        await transaction.CommitAsync(cancellation);
        return authenticated ? account : null;
    }
}
