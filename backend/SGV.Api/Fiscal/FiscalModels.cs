using Sgv.Fiscal;

namespace Sgv.Api.Fiscal;

public sealed record FiscalStep(string Estado, DateTimeOffset Fecha, string Detalle);
public sealed record DteRecord(Guid Id, Guid VentaId, string TipoDte, string Estado, string NumeroControl, string CodigoGeneracion, string? SelloRecibido, string? Json, string? Jws, string? Respuesta, DateTimeOffset Fecha, DateTimeOffset Actualizado, DateTimeOffset? FechaRespuesta, long? TiempoEmisionMs, int Intentos, int Version, Guid? OriginalId, IReadOnlyList<FiscalStep> Timeline, string? MotivoInvalidacion, string? InvalidacionCodigo, string? InvalidacionJws, string? InvalidacionEstado, string? RespuestaInvalidacion);
public sealed record FiscalReservation(DteRecord Documento, FiscalSale Venta, bool Nuevo);
public sealed record PaidSale(Guid Id, string Referencia, string Estado, decimal Total, string Cliente, string TipoDte, string Escenario, Guid? DteId, string? EstadoDte);
public sealed record DteFilter(DateTimeOffset? Desde = null, DateTimeOffset? Hasta = null, string? Tipo = null, string? Estado = null);
public sealed record RejectionReason(string Motivo, int Cantidad);
public sealed record FiscalIndicators(int TotalEmitidos, int Procesados, int Rechazados, int EnContingencia, int Invalidados, decimal PorcentajeProcesados, decimal PorcentajeRechazados, double TiempoPromedioMs, IReadOnlyList<RejectionReason> MotivosFrecuentes);
public sealed record InvalidationInput(string Motivo);
