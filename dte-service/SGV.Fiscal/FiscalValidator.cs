using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sgv.Fiscal;

public static partial class FiscalValidator
{
    [GeneratedRegex(@"^(?:[0-9]{14}|[0-9]{9})$", RegexOptions.CultureInvariant, 1000)] private static partial Regex NitPattern();
    [GeneratedRegex(@"^[0-9]{1,8}$", RegexOptions.CultureInvariant, 1000)] private static partial Regex NrcPattern();
    [GeneratedRegex(@"^DTE-(01|03|05)-[A-Z0-9]{8}-[0-9]{15}$", RegexOptions.CultureInvariant, 1000)] private static partial Regex ControlPattern();
    public static bool IsNit(string value) => NitPattern().IsMatch(value);
    public static bool IsNrc(string value) => NrcPattern().IsMatch(value);
    public static bool IsUuid(string value) => Guid.TryParseExact(value, "D", out _) && !value.Any(char.IsAsciiLetterLower) && value[14] == '4' && "89AB".Contains(value[19], StringComparison.Ordinal);
    public static string Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var text) ? text : "";
    public static decimal Number(JsonNode? node) => decimal.TryParse(node?.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : decimal.MinValue;

    public static IReadOnlyList<string> Validate(JsonObject document)
    {
        var errors = new List<string>();
        ValidateIdentity(document["identificacion"], errors);
        ValidateParties(document, errors);
        ValidateTotals(document, errors);
        if (Text(document["identificacion"]?["tipoDte"]) == "05") ValidateReference(document, errors);
        return errors.Distinct().ToArray();
    }

    private static void ValidateIdentity(JsonNode? id, List<string> errors)
    {
        var type = Text(id?["tipoDte"]);
        if (type is not ("01" or "03" or "05") || Number(id?["version"]) != (type == "01" ? 1 : 3)) errors.Add("TIPO O VERSIÓN DEL DTE NO VÁLIDO");
        if (Text(id?["ambiente"]) != "00") errors.Add("SOLO SE ADMITE AMBIENTE DE PRUEBAS 00");
        if (!IsUuid(Text(id?["codigoGeneracion"]))) errors.Add("CÓDIGO DE GENERACIÓN NO VÁLIDO");
        var control = Text(id?["numeroControl"]);
        if (!ControlPattern().IsMatch(control) || !control.StartsWith("DTE-" + type + "-", StringComparison.Ordinal)) errors.Add("NÚMERO DE CONTROL NO VÁLIDO");
        if (Number(id?["tipoModelo"]) != 1 || Number(id?["tipoOperacion"]) != 1 || Text(id?["tipoMoneda"]) != "USD") errors.Add("MODELO, OPERACIÓN O MONEDA NO VÁLIDOS");
        if (!DateOnly.TryParseExact(Text(id?["fecEmi"]), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) || !TimeOnly.TryParseExact(Text(id?["horEmi"]), "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) errors.Add("FECHA U HORA DE EMISIÓN NO VÁLIDA");
    }

    private static void ValidateParties(JsonObject document, List<string> errors)
    {
        var issuer = document["emisor"]; var customer = document["receptor"];
        if (!IsNit(Text(issuer?["nit"])) || !IsNrc(Text(issuer?["nrc"]))) errors.Add("NIT O NRC DEL EMISOR NO VÁLIDO");
        if (string.IsNullOrWhiteSpace(Text(issuer?["nombre"])) || string.IsNullOrWhiteSpace(Text(issuer?["codActividad"])) || string.IsNullOrWhiteSpace(Text(issuer?["direccion"]?["complemento"]))) errors.Add("DATOS OBLIGATORIOS DEL EMISOR INCOMPLETOS");
        var invoice = Text(document["identificacion"]?["tipoDte"]) == "01";
        if (!IsNit(Text(customer?[invoice ? "numDocumento" : "nit"]))) errors.Add("NIT DEL RECEPTOR NO VÁLIDO");
        var nrc = Text(customer?["nrc"]);
        if ((!invoice || nrc.Length > 0) && !IsNrc(nrc)) errors.Add("NRC DEL RECEPTOR NO VÁLIDO");
        if (string.IsNullOrWhiteSpace(Text(customer?["nombre"])) || string.IsNullOrWhiteSpace(Text(customer?["direccion"]?["complemento"]))) errors.Add("DATOS OBLIGATORIOS DEL RECEPTOR INCOMPLETOS");
    }

    private static void ValidateTotals(JsonObject document, List<string> errors)
    {
        if (document["cuerpoDocumento"] is not JsonArray lines || lines.Count is 0 or > 200) { errors.Add("DETALLE OBLIGATORIO NO VÁLIDO"); return; }
        var invoice = Text(document["identificacion"]?["tipoDte"]) == "01";
        decimal taxable = 0, itemIva = 0;
        foreach (var line in lines)
        {
            var quantity = Number(line?["cantidad"]); var price = Number(line?["precioUni"]); var discount = Number(line?["montoDescu"]); var value = Number(line?["ventaGravada"]);
            if(value is <0 or >20_000_000_000_000m){errors.Add("IMPORTE DE LÍNEA NO VÁLIDO");continue;}
            if (quantity is <= 0 or > 100_000 || price is < 0 or > 1_000_000 || discount < 0 || discount > quantity * price) { errors.Add("CANTIDAD, PRECIO O DESCUENTO NO VÁLIDO"); continue; }
            if (value != FiscalDocumentFactory.Round(quantity * price - discount)) errors.Add("TOTAL DE LÍNEA NO COINCIDE CON CANTIDAD Y PRECIO");
            if (string.IsNullOrWhiteSpace(Text(line?["descripcion"])) || Number(line?["numItem"]) <= 0) errors.Add("DATOS OBLIGATORIOS DE LÍNEA INCOMPLETOS");
            taxable += value;
            if (invoice)
            {
                var iva = FiscalDocumentFactory.Round(value * 13m / 113m);
                if (Number(line?["ivaItem"]) != iva) errors.Add("IVA DE LÍNEA INCORRECTO");
                itemIva += iva;
            }
        }
        var summary = document["resumen"];
        var totalIva = invoice ? Number(summary?["totalIva"]) : Number(summary?["tributos"]?[0]?["valor"]);
        var expectedIva = invoice ? itemIva : FiscalDocumentFactory.Round(taxable * 0.13m);
        if (totalIva != expectedIva) errors.Add("IVA NO COINCIDE CON EL 13 POR CIENTO");
        var expected = invoice ? taxable : taxable + expectedIva;
        var credit=Text(document["identificacion"]?["tipoDte"])=="05";
        if (Number(summary?["totalGravada"]) != taxable || Number(summary?["subTotal"]) != taxable || Number(summary?["montoTotalOperacion"]) != expected || !credit && Number(summary?["totalPagar"]) != expected) errors.Add("TOTAL NO COINCIDE CON EL DETALLE");
        if (string.IsNullOrWhiteSpace(Text(summary?["totalLetras"])) || Number(summary?["condicionOperacion"]) is < 1 or > 3) errors.Add("RESUMEN OBLIGATORIO INCOMPLETO");
    }

    private static void ValidateReference(JsonObject document, List<string> errors)
    {
        if (document["documentoRelacionado"] is not JsonArray related || related.Count != 1 || Text(related[0]?["tipoDocumento"]) != "03" || !IsUuid(Text(related[0]?["numeroDocumento"]))) errors.Add("NOTA DE CRÉDITO SIN REFERENCIA ORIGINAL VÁLIDA");
    }
}
