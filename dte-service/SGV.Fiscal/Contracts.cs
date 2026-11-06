using System.Text.Json;

namespace Sgv.Fiscal;

public sealed record FiscalCustomer(string Nombre, string Nit, string? Nrc = null, string CodActividad = "47119", string DescActividad = "Comercio", string Correo = "cliente@example.test", string Departamento = "06", string Municipio = "14", string Direccion = "Dirección de pruebas, San Salvador");
public sealed record FiscalLine(string Codigo, string Descripcion, decimal Cantidad, decimal PrecioBase, decimal Descuento = 0);
public sealed record FiscalSale(Guid Id, string Referencia, string Estado, decimal Total, FiscalCustomer Receptor, IReadOnlyList<FiscalLine> Lineas, string Escenario = "normal");
public sealed record FiscalIssuer(string Nombre = "Adventure Works", string Nit = "06140101900011", string Nrc = "1234567", string Establecimiento = "M001P001", string CodActividad = "47119", string DescActividad = "Comercio al por menor", string Departamento = "06", string Municipio = "14", string Direccion = "San Salvador, establecimiento de pruebas", string Correo = "facturacion@adventureworks.example", string Telefono = "22220000");
public sealed record FiscalPreparation(FiscalSale Venta, string TipoDte, string NumeroControl, string CodigoGeneracion, JsonElement? Original = null);
public sealed record PreparedFiscalDocument(string Json, string Jws, DateTimeOffset Generado, DateTimeOffset Firmado, string? Advertencia);
public sealed record ReceptionRequest(string Ambiente, int IdEnvio, int Version, string TipoDte, string Documento, string CodigoGeneracion);
public sealed record FiscalTransmission(ReceptionRequest Solicitud, string Escenario = "normal", string Operacion = "recepciondte");
public sealed record FiscalResponse(int Version, string Ambiente, string VersionApp, string Estado, string CodigoGeneracion, string? SelloRecibido, string FhProcesamiento, string ClasificaMsg, string CodigoMsg, string DescripcionMsg, IReadOnlyList<string> Observaciones)
{
    public static FiscalResponse Create(string code, IReadOnlyList<string> errors, string? seal = null) => new(1, "00", "SGV.ReceptorPruebas/1.0.0", errors.Count == 0 ? "PROCESADO" : "RECHAZADO", code, errors.Count == 0 ? seal : null, DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).ToString("dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), "10", errors.Count == 0 ? "001" : "012", errors.Count == 0 ? "RECIBIDO" : errors[0], errors);
    public static FiscalResponse Contingency(string code, string message) => new(1, "00", "SGV.Dte/1.0.0", "CONTINGENCIA", code, null, DateTimeOffset.UtcNow.ToString("O"), "10", "099", message, [message]);
}

public static class FiscalJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
