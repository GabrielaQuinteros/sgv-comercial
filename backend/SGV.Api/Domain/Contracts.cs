namespace Sgv.Api.Domain;

public sealed record Product(int Id, string Sku, string Name, decimal Price, int Stock, int MinimumStock);
public sealed record ProductInput(string Sku, string Name, decimal Price, int MinimumStock);
public sealed record StockEntry(int ProductId, int Quantity, string Reference);
public sealed record SaleLineInput(int ProductId, int Quantity);
public sealed record SaleInput(IReadOnlyList<SaleLineInput> Lines);
public sealed record SaleLine(int ProductId, string Name, int Quantity, decimal UnitPrice);
public sealed record Sale(Guid Id, IReadOnlyList<SaleLine> Lines, decimal Subtotal, decimal Tax, decimal Total, string Status);
public sealed record UserAccount(Guid Id, string Username, string PasswordHash, string Role, bool Active);
public sealed record LoginInput(string Username, string Password);
public sealed record UserInput(string Username, string Password, string Role);
public sealed record AuditEvent(long Id, DateTimeOffset CreatedAt, string Actor, string Action, string EntityId, string Detail);
public sealed record Dashboard(int Products, int LowStockProducts, int Sales, decimal Revenue);

public sealed class BusinessException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public static class SaleCalculator
{
    public const decimal TaxRate = 0.13m;

    public static (decimal Subtotal, decimal Tax, decimal Total) Calculate(IReadOnlyList<SaleLine> lines)
    {
        if (lines.Count is 0 or > 100)
            throw new BusinessException("La venta debe contener entre 1 y 100 productos.");
        if (lines.Any(line => line.Quantity is <= 0 or > 100_000 || line.UnitPrice is < 0 or > 1_000_000))
            throw new BusinessException("Cantidad o precio fuera del rango permitido.");
        var subtotal = decimal.Round(lines.Sum(line => line.Quantity * line.UnitPrice), 2, MidpointRounding.AwayFromZero);
        var tax = decimal.Round(subtotal * TaxRate, 2, MidpointRounding.AwayFromZero);
        return (subtotal, tax, subtotal + tax);
    }

    public static void ValidateLines(IReadOnlyList<SaleLineInput> lines)
    {
        if (lines.Count is 0 or > 100 || lines.Any(line => line.ProductId <= 0 || line.Quantity is <= 0 or > 100_000))
            throw new BusinessException("Productos y cantidades inválidos.");
        if (lines.Select(line => line.ProductId).Distinct().Count() != lines.Count)
            throw new BusinessException("Agrupa cada producto en una sola línea de la venta.");
    }
}
