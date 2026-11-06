using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Sgv.Api.Domain;
using Sgv.Api.Security;

namespace Sgv.Api.Data;

public sealed partial class CommercialStore(string connectionString)
{
    private async Task<SqlConnection> OpenAsync(CancellationToken cancellation)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellation);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static void Parameter(SqlCommand command, string name, object value) => command.Parameters.AddWithValue(name, value);

    public async Task EnsureAdminAsync(string username, string password, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);
        await using var count = new SqlCommand("SELECT COUNT(*) FROM dbo.Users WHERE Username=@username", connection);
        Parameter(count, "@username", username.Trim().ToLowerInvariant());
        if (Convert.ToInt32(await count.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture) > 0) return;
        await using var command = new SqlCommand("INSERT dbo.Users(Id, Username, PasswordHash, Role) VALUES(@id,@username,@hash,'Admin')", connection);
        Parameter(command, "@id", Guid.NewGuid());
        Parameter(command, "@username", username.Trim().ToLowerInvariant());
        Parameter(command, "@hash", PasswordHasher.Hash(password));
        await command.ExecuteNonQueryAsync(cancellation);
    }

    public async Task<UserAccount?> FindUserAsync(string username, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);
        await using var command = new SqlCommand("SELECT Id,Username,PasswordHash,Role,Active FROM dbo.Users WHERE Username=@username", connection);
        Parameter(command, "@username", username.Trim().ToLowerInvariant());
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        return await reader.ReadAsync(cancellation)
            ? new UserAccount(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4)) : null;
    }

    public async Task<Guid> CreateUserAsync(UserInput input, string actor, CancellationToken cancellation)
    {
        if (input.Username.Trim().Length is < 3 or > 100 || !new[] { "Admin", "Ventas", "Inventario", "Auditor" }.Contains(input.Role))
            throw new BusinessException("Usuario o rol inválido.");
        if (input.Password.Length is < 14 or > 128) throw new BusinessException("La contraseña debe tener entre 14 y 128 caracteres.");
        var id = Guid.NewGuid();
        await using var connection = await OpenAsync(cancellation);
        await using var command = new SqlCommand("INSERT dbo.Users(Id,Username,PasswordHash,Role) VALUES(@id,@username,@hash,@role)", connection);
        Parameter(command, "@id", id);
        Parameter(command, "@username", input.Username.Trim().ToLowerInvariant());
        Parameter(command, "@hash", PasswordHasher.Hash(input.Password));
        Parameter(command, "@role", input.Role);
        try { await command.ExecuteNonQueryAsync(cancellation); }
        catch (SqlException exception) when (exception.Number is 2601 or 2627) { throw new BusinessException("El usuario ya existe.", 409); }
        await AuditAsync(actor, "Usuario creado", id.ToString(), input.Role, cancellation);
        return id;
    }

    public async Task<IReadOnlyList<Product>> ProductsAsync(CancellationToken cancellation)
    {
        var products = new List<Product>();
        await using var connection = await OpenAsync(cancellation);
        await using var command = new SqlCommand("SELECT Id,Sku,Name,Price,Stock,MinimumStock FROM dbo.Products ORDER BY Name", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        while (await reader.ReadAsync(cancellation))
            products.Add(new Product(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3), reader.GetInt32(4), reader.GetInt32(5)));
        return products;
    }

    public async Task<int> CreateProductAsync(ProductInput input, CancellationToken cancellation)
    {
        if (input.Sku.Trim().Length is < 1 or > 50 || input.Name.Trim().Length is < 1 or > 200 ||
            input.Price is < 0 or > 1_000_000 || input.MinimumStock is < 0 or > 100_000)
            throw new BusinessException("Datos de producto inválidos.");
        await using var connection = await OpenAsync(cancellation);
        await using var command = new SqlCommand("INSERT dbo.Products(Sku,Name,Price,MinimumStock) OUTPUT INSERTED.Id VALUES(@sku,@name,@price,@minimum)", connection);
        Parameter(command, "@sku", input.Sku.Trim());
        Parameter(command, "@name", input.Name.Trim());
        Parameter(command, "@price", decimal.Round(input.Price, 2));
        Parameter(command, "@minimum", input.MinimumStock);
        try { return Convert.ToInt32(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture); }
        catch (SqlException exception) when (exception.Number is 2601 or 2627) { throw new BusinessException("El SKU ya existe.", 409); }
    }

    public async Task ReceiveStockAsync(StockEntry input, string actor, CancellationToken cancellation)
    {
        if (input.Quantity is <= 0 or > 100_000 || input.Reference.Trim().Length is < 1 or > 100)
            throw new BusinessException("Cantidad o referencia inválida.");
        await using var connection = await OpenAsync(cancellation);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellation);
        await using var command = new SqlCommand("UPDATE dbo.Products SET Stock=Stock+@quantity WHERE Id=@id AND Stock<=1000000000-@quantity", connection, transaction);
        Parameter(command, "@quantity", input.Quantity);
        Parameter(command, "@id", input.ProductId);
        if (await command.ExecuteNonQueryAsync(cancellation) != 1) throw new BusinessException("Producto inexistente o límite de existencias excedido.", 409);
        await WriteAuditAsync(connection, transaction, actor, "Entrada de inventario", input.ProductId.ToString(CultureInfo.InvariantCulture), input.Reference, cancellation);
        await transaction.CommitAsync(cancellation);
    }

    public async Task<Sale> CreateSaleAsync(SaleInput input, string actor, CancellationToken cancellation)
    {
        SaleCalculator.ValidateLines(input.Lines);
        var catalog = await ProductsAsync(cancellation);
        var lines = input.Lines.Select(item =>
        {
            var product = catalog.FirstOrDefault(product => product.Id == item.ProductId) ?? throw new BusinessException("Producto inexistente.", 404);
            return new SaleLine(product.Id, product.Name, item.Quantity, product.Price);
        }).ToList();
        var (subtotal, tax, total) = SaleCalculator.Calculate(lines);
        var sale = new Sale(Guid.NewGuid(), lines, subtotal, tax, total, "Pendiente");
        await using var connection = await OpenAsync(cancellation);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellation);
        await using var command = new SqlCommand("INSERT dbo.Sales(Id,Subtotal,Tax,Total,Status) VALUES(@id,@subtotal,@tax,@total,'Pendiente')", connection, transaction);
        Parameter(command, "@id", sale.Id); Parameter(command, "@subtotal", subtotal); Parameter(command, "@tax", tax); Parameter(command, "@total", total);
        await command.ExecuteNonQueryAsync(cancellation);
        foreach (var line in lines.OrderBy(line => line.ProductId))
        {
            await using var item = new SqlCommand("INSERT dbo.SaleItems(SaleId,ProductId,Quantity,UnitPrice) VALUES(@sale,@product,@quantity,@price)", connection, transaction);
            Parameter(item, "@sale", sale.Id); Parameter(item, "@product", line.ProductId); Parameter(item, "@quantity", line.Quantity); Parameter(item, "@price", line.UnitPrice);
            await item.ExecuteNonQueryAsync(cancellation);
        }
        await WriteAuditAsync(connection, transaction, actor, "Venta creada", sale.Id.ToString(), $"Total: {total.ToString(CultureInfo.InvariantCulture)} USD", cancellation);
        await transaction.CommitAsync(cancellation);
        return sale;
    }

    public async Task<Dashboard> DashboardAsync(CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);
        await using var command = new SqlCommand("SELECT (SELECT COUNT(*) FROM dbo.Products), (SELECT COUNT(*) FROM dbo.Products WHERE Stock<=MinimumStock), (SELECT COUNT(*) FROM dbo.Sales), (SELECT COALESCE(SUM(Total),0) FROM dbo.Sales WHERE Status='Pagada')", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        await reader.ReadAsync(cancellation);
        return new Dashboard(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetDecimal(3));
    }

    public async Task AuditAsync(string actor, string action, string entityId, string detail, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);
        await WriteAuditAsync(connection, null, actor, action, entityId, detail, cancellation);
    }

    private static async Task WriteAuditAsync(SqlConnection connection, SqlTransaction? transaction, string actor, string action, string entityId, string detail, CancellationToken cancellation)
    {
        await using var command = new SqlCommand("INSERT dbo.AuditLog(Actor,Action,EntityId,Detail) VALUES(@actor,@action,@entity,@detail)", connection, transaction);
        Parameter(command, "@actor", actor); Parameter(command, "@action", action); Parameter(command, "@entity", entityId); Parameter(command, "@detail", detail);
        await command.ExecuteNonQueryAsync(cancellation);
    }

    public async Task<IReadOnlyList<AuditEvent>> AuditEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        if (to <= from || to - from > TimeSpan.FromDays(31)) throw new BusinessException("Selecciona un rango mayor que cero y de hasta 31 días.");
        var events = new List<AuditEvent>();
        await using var connection = await OpenAsync(cancellation);
        await using var command = new SqlCommand("SELECT TOP(10000) Id,CreatedAt,Actor,Action,EntityId,Detail FROM dbo.AuditLog WHERE CreatedAt>=@from AND CreatedAt<@to ORDER BY CreatedAt,Id", connection);
        Parameter(command, "@from", from); Parameter(command, "@to", to);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        while (await reader.ReadAsync(cancellation))
            events.Add(new AuditEvent(reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        return events;
    }
}
