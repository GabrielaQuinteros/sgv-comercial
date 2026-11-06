using System.Globalization;
using System.Text.Json.Nodes;

namespace Sgv.Fiscal;

public static class FiscalDocumentFactory
{
    public static string SelectType(FiscalCustomer customer) => string.IsNullOrWhiteSpace(customer.Nrc) ? "01" : "03";
    public static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string NormalizeId(string value)=>value.Replace("-","",StringComparison.Ordinal).Replace(" ","",StringComparison.Ordinal);

    // Antes de producción, validar estos contratos, catálogos, versiones y firma contra el esquema oficial vigente del MH.
    public static JsonObject Create(FiscalPreparation request, FiscalIssuer issuer, DateTimeOffset utc)
    {
        if (request.TipoDte is not ("01" or "03" or "05") || request.Venta.Lineas.Count is 0 or > 200)
            throw new ArgumentException("Tipo de DTE o detalle inválido.");
        var local = utc.ToOffset(TimeSpan.FromHours(-6));
        var invoice = request.TipoDte == "01";
        var body = new JsonArray();
        foreach (var line in request.Venta.Lineas)
        {
            if (line.Cantidad <= 0 || line.PrecioBase < 0 || line.Descuento < 0 || line.Descuento > line.Cantidad * line.PrecioBase)
                throw new ArgumentException("Cantidad, precio o descuento inválido.");
            var price = invoice ? decimal.Round(line.PrecioBase * 1.13m, 8) : line.PrecioBase;
            var discount = invoice ? Round(line.Descuento * 1.13m) : line.Descuento;
            var taxable = Round(line.Cantidad * price - discount);
            var item = new JsonObject
            {
                ["numItem"] = body.Count + 1, ["tipoItem"] = 1, ["numeroDocumento"] = null,
                ["cantidad"] = line.Cantidad, ["codigo"] = line.Codigo, ["codTributo"] = null,
                ["uniMedida"] = 59, ["descripcion"] = line.Descripcion, ["precioUni"] = price,
                ["montoDescu"] = discount, ["ventaNoSuj"] = 0, ["ventaExenta"] = 0,
                ["ventaGravada"] = taxable, ["tributos"] = invoice ? null : new JsonArray("20")
            };
            if (request.TipoDte != "05") { item["psv"] = 0; item["noGravado"] = 0; }
            if (invoice) item["ivaItem"] = Round(taxable * 13m / 113m);
            body.Add(item);
        }
        var totalGravada = body.Sum(item => item!["ventaGravada"]!.GetValue<decimal>());
        var iva = invoice ? body.Sum(item => item!["ivaItem"]!.GetValue<decimal>()) : Round(totalGravada * 0.13m);
        var computedTotal = invoice ? totalGravada : totalGravada + iva;
        var summary = new JsonObject
        {
            ["totalNoSuj"] = 0, ["totalExenta"] = 0, ["totalGravada"] = totalGravada,
            ["subTotalVentas"] = totalGravada, ["descuNoSuj"] = 0, ["descuExenta"] = 0,
            ["descuGravada"] = 0, ["porcentajeDescuento"] = 0, ["totalDescu"] = body.Sum(item=>item!["montoDescu"]!.GetValue<decimal>()),
            ["tributos"] = invoice ? null : new JsonArray(new JsonObject { ["codigo"] = "20", ["descripcion"] = "Impuesto al Valor Agregado 13%", ["valor"] = iva }),
            ["subTotal"] = totalGravada, ["ivaRete1"] = 0, ["reteRenta"] = 0,
            ["montoTotalOperacion"] = computedTotal,
            // Se conserva el importe pagado: el receptor detectará cualquier inconsistencia del origen.
            ["totalPagar"] = request.Venta.Total, ["totalLetras"] = MoneyWords.Format(request.Venta.Total),
            ["condicionOperacion"] = 1
        };
        if (invoice) summary["totalIva"] = iva;
        else summary["ivaPerci1"] = 0;
        if (request.TipoDte != "05")
        {
            summary["totalNoGravado"] = 0; summary["saldoFavor"] = 0;
            summary["pagos"] = new JsonArray(new JsonObject { ["codigo"] = "01", ["montoPago"] = request.Venta.Total, ["referencia"] = request.Venta.Referencia, ["plazo"] = null, ["periodo"] = null });
            summary["numPagoElectronico"] = null;
        }
        var identification = new JsonObject
        {
            ["version"] = invoice ? 1 : 3, ["ambiente"] = "00", ["tipoDte"] = request.TipoDte,
            ["numeroControl"] = request.NumeroControl, ["codigoGeneracion"] = request.CodigoGeneracion,
            ["tipoModelo"] = 1, ["tipoOperacion"] = 1, ["tipoContingencia"] = null,
            ["motivoContin"] = null, ["fecEmi"] = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["horEmi"] = local.ToString("HH:mm:ss", CultureInfo.InvariantCulture), ["tipoMoneda"] = "USD"
        };
        var result = new JsonObject
        {
            ["identificacion"] = identification, ["documentoRelacionado"] = Related(request),
            ["emisor"] = Issuer(issuer), ["receptor"] = Customer(request.Venta.Receptor, invoice),
            ["otrosDocumentos"] = null, ["ventaTercero"] = null, ["cuerpoDocumento"] = body,
            ["resumen"] = summary, ["extension"] = null, ["apendice"] = null
        };
        if(request.TipoDte=="05")
        {
            result.Remove("otrosDocumentos"); summary.Remove("porcentajeDescuento"); summary.Remove("totalPagar");
            var emitter=(JsonObject)result["emisor"]!;
            emitter.Remove("codEstableMH");emitter.Remove("codEstable");emitter.Remove("codPuntoVentaMH");emitter.Remove("codPuntoVenta");
        }
        return result;
    }

    private static JsonArray? Related(FiscalPreparation request)
    {
        if (request.TipoDte != "05") return null;
        if (request.Original is null) throw new ArgumentException("La nota de crédito requiere el documento original.");
        var id = request.Original.Value.GetProperty("identificacion");
        if (id.GetProperty("tipoDte").GetString() != "03") throw new ArgumentException("La nota de crédito referencia un CCF 03.");
        return new JsonArray(new JsonObject { ["tipoDocumento"] = "03", ["tipoGeneracion"] = 2, ["numeroDocumento"] = id.GetProperty("codigoGeneracion").GetString(), ["fechaEmision"] = id.GetProperty("fecEmi").GetString() });
    }

    private static JsonObject Address(string department, string municipality, string detail) => new() { ["departamento"] = department, ["municipio"] = municipality, ["complemento"] = detail };
    private static JsonObject Issuer(FiscalIssuer issuer) => new()
    {
        ["nit"] = NormalizeId(issuer.Nit), ["nrc"] = NormalizeId(issuer.Nrc), ["nombre"] = issuer.Nombre,
        ["codActividad"] = issuer.CodActividad, ["descActividad"] = issuer.DescActividad,
        ["nombreComercial"] = issuer.Nombre, ["tipoEstablecimiento"] = "02",
        ["direccion"] = Address(issuer.Departamento, issuer.Municipio, issuer.Direccion),
        ["telefono"] = issuer.Telefono, ["correo"] = issuer.Correo,
        ["codEstableMH"] = issuer.Establecimiento[..4], ["codEstable"] = issuer.Establecimiento[..4],
        ["codPuntoVentaMH"] = issuer.Establecimiento[4..], ["codPuntoVenta"] = issuer.Establecimiento[4..]
    };
    private static JsonObject Customer(FiscalCustomer customer, bool invoice)
    {
        var result = new JsonObject { ["nombre"] = customer.Nombre, ["nrc"] = customer.Nrc is null?null:NormalizeId(customer.Nrc), ["codActividad"] = customer.CodActividad, ["descActividad"] = customer.DescActividad, ["direccion"] = Address(customer.Departamento, customer.Municipio, customer.Direccion), ["telefono"] = null, ["correo"] = customer.Correo };
        if (invoice) { result["tipoDocumento"] = "36"; result["numDocumento"] = NormalizeId(customer.Nit); }
        else { result["nit"] = NormalizeId(customer.Nit); result["nombreComercial"] = customer.Nombre; }
        return result;
    }
}
