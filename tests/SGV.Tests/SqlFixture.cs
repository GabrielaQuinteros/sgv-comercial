using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Sgv.Api.Data;
using Xunit;

namespace Sgv.Tests;

public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SGV_TEST_SQL")))
            Skip = "Configura SGV_TEST_SQL para ejecutar las pruebas de integración con SQL Server.";
    }
}

public sealed class SqlFixture : IAsyncLifetime
{
    public string? ConnectionString { get; private set; }
    public string AdminPassword { get; } = Guid.NewGuid().ToString("N");
    public CommercialStore Store => new(ConnectionString ?? throw new InvalidOperationException("SQL Server no configurado."));

    public async Task InitializeAsync()
    {
        var source = Environment.GetEnvironmentVariable("SGV_TEST_SQL");
        if (string.IsNullOrEmpty(source)) return;
        var builder = new SqlConnectionStringBuilder(source) { InitialCatalog = "master" };
        await using var master = new SqlConnection(builder.ConnectionString);
        await master.OpenAsync();
        var database = "sgv_test_" + Guid.NewGuid().ToString("N");
        await using (var create = new SqlCommand($"CREATE DATABASE [{database}]", master))
            await create.ExecuteNonQueryAsync();
        builder.InitialCatalog = database;
        ConnectionString = builder.ConnectionString;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var file in new[] { "schema.sql", "catalog.sql", "fiscal.sql", "sales-seed.sql" })
        {
            var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, file));
            foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                await using var command = new SqlCommand(batch, connection);
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition("SQL")]
public sealed class SqlCollection : ICollectionFixture<SqlFixture>;
