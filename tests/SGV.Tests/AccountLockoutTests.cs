using Microsoft.Data.SqlClient;
using Sgv.Api.Domain;
using Xunit;

namespace Sgv.Tests;

[Collection("SQL")]
public sealed class AccountLockoutTests(SqlFixture database)
{
    [SqlFact]
    public async Task LocksAfterFiveFailuresAndReleasesAfterTimeout()
    {
        var username = "lock-" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var id = await database.Store.CreateUserAsync(new UserInput(username, password, "Ventas"), "qa", default);
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Null(await database.Store.AuthenticateAsync(username, "invalid", default));
        Assert.Null(await database.Store.AuthenticateAsync(username, password, default));
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var release = new SqlCommand("UPDATE dbo.Users SET LockedUntil=DATEADD(MINUTE,-1,SYSDATETIMEOFFSET()) WHERE Id=@id", connection))
        {
            release.Parameters.AddWithValue("@id", id);
            await release.ExecuteNonQueryAsync();
        }
        Assert.NotNull(await database.Store.AuthenticateAsync(username, password, default));
        await using var count = new SqlCommand("SELECT FailedAttempts FROM dbo.Users WHERE Id=@id", connection);
        count.Parameters.AddWithValue("@id", id);
        Assert.Equal(0, (int)(await count.ExecuteScalarAsync())!);
    }

    [SqlFact]
    public async Task DoesNotLoseConcurrentFailedAttemptsOrLockAnotherUser()
    {
        var username = "parallel-" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await database.Store.CreateUserAsync(new UserInput(username, password, "Auditor"), "qa", default);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => database.Store.AuthenticateAsync(username, "invalid", default)));
        Assert.All(results, result => Assert.Null(result));
        Assert.Null(await database.Store.AuthenticateAsync(username, password, default));
        Assert.Null(await database.Store.AuthenticateAsync("unknown-" + Guid.NewGuid().ToString("N"), password, default));
        Assert.Null(await database.Store.AuthenticateAsync(username, new string('x', 129), default));
        var audit = await database.Store.AuditEventsAsync(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1), default);
        Assert.Equal(5, audit.Count(item => item.Actor == username && item.Action == "Acceso rechazado"));
        Assert.DoesNotContain(audit, item => item.Detail.Contains(password, StringComparison.Ordinal));
    }
}
