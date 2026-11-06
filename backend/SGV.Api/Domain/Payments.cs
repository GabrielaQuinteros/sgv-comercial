namespace Sgv.Api.Domain;

public sealed record PaymentInput(decimal Amount, string Method, string Reference);
public sealed record PaymentReceipt(Guid Id, Guid SaleId, decimal Amount, string Method, string Reference, string Status);
