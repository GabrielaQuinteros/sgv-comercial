using System.Data;
using Microsoft.Data.SqlClient;
using Sgv.Api.Domain;

namespace Sgv.Api.Data;

public sealed partial class CommercialStore
{
    private static readonly string[] PaymentMethods = ["Efectivo", "Tarjeta", "Transferencia"];
    public async Task<PaymentReceipt> RecordPaymentAsync(Guid saleId, PaymentInput input, string actor, CancellationToken cancellation)
    {
        if (input.Amount <= 0 || decimal.Round(input.Amount, 2) != input.Amount ||
            !PaymentMethods.Contains(input.Method) ||
            string.IsNullOrWhiteSpace(input.Reference) || input.Reference.Length > 100)
            throw new BusinessException("Importe, método o referencia de pago inválidos.");
        await using var connection = await OpenAsync(cancellation);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellation);
        try
        {
            decimal total;
            string status;
            PaymentReceipt? existing = null;
            await using (var query = new SqlCommand("SELECT s.Total,s.Status,p.Id,p.Amount,p.Method,p.Reference FROM dbo.Sales s WITH(UPDLOCK,HOLDLOCK) LEFT JOIN dbo.Payments p ON p.SaleId=s.Id WHERE s.Id=@id", connection, transaction))
            {
                Parameter(query, "@id", saleId);
                await using var reader = await query.ExecuteReaderAsync(cancellation);
                if (!await reader.ReadAsync(cancellation)) throw new BusinessException("Venta inexistente.", 404);
                total = reader.GetDecimal(0); status = reader.GetString(1);
                if (!reader.IsDBNull(2)) existing = new PaymentReceipt(reader.GetGuid(2), saleId, reader.GetDecimal(3), reader.GetString(4), reader.GetString(5), "Pagada");
            }
            if (existing is not null)
            {
                if (existing.Reference == input.Reference.Trim() && existing.Amount == input.Amount && existing.Method == input.Method) return existing;
                throw new BusinessException("La venta ya tiene un pago registrado.", 409);
            }
            if (status != "Pendiente") throw new BusinessException("La venta no admite pagos.", 409);
            if (input.Amount != total) throw new BusinessException("El pago debe coincidir con el total de la venta.");
            var items = new List<SaleLineInput>();
            await using (var query = new SqlCommand("SELECT ProductId,Quantity FROM dbo.SaleItems WHERE SaleId=@id ORDER BY ProductId", connection, transaction))
            {
                Parameter(query, "@id", saleId);
                await using var reader = await query.ExecuteReaderAsync(cancellation);
                while (await reader.ReadAsync(cancellation)) items.Add(new SaleLineInput(reader.GetInt32(0), reader.GetInt32(1)));
            }
            foreach (var line in items)
            {
                await using var discount = new SqlCommand("dbo.usp_DescontarExistencias", connection, transaction) { CommandType = CommandType.StoredProcedure };
                Parameter(discount, "@ProductId", line.ProductId); Parameter(discount, "@Quantity", line.Quantity);
                await discount.ExecuteNonQueryAsync(cancellation);
            }
            var receipt = new PaymentReceipt(Guid.NewGuid(), saleId, input.Amount, input.Method, input.Reference.Trim(), "Pagada");
            await using (var payment = new SqlCommand("INSERT dbo.Payments(Id,SaleId,Amount,Method,Reference) VALUES(@id,@sale,@amount,@method,@reference); UPDATE dbo.Sales SET Status='Pagada' WHERE Id=@sale", connection, transaction))
            {
                Parameter(payment, "@id", receipt.Id); Parameter(payment, "@sale", saleId); Parameter(payment, "@amount", input.Amount);
                Parameter(payment, "@method", input.Method); Parameter(payment, "@reference", receipt.Reference);
                await payment.ExecuteNonQueryAsync(cancellation);
            }
            await WriteAuditAsync(connection, transaction, actor, "Pago registrado", saleId.ToString(), receipt.Reference, cancellation);
            await transaction.CommitAsync(cancellation);
            return receipt;
        }
        catch (SqlException exception) when (exception.Number == 50001)
        {
            throw new BusinessException("Existencias insuficientes para completar la venta.", 409);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            throw new BusinessException("La referencia de pago ya está registrada.", 409);
        }
    }
}
