using System.Globalization;

namespace Sgv.Fiscal;

public static class MoneyWords
{
    private static readonly string[] Units = ["CERO", "UNO", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE", "DIEZ", "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE", "DIECISÉIS", "DIECISIETE", "DIECIOCHO", "DIECINUEVE", "VEINTE", "VEINTIUNO", "VEINTIDÓS", "VEINTITRÉS", "VEINTICUATRO", "VEINTICINCO", "VEINTISÉIS", "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE"];
    private static readonly string[] Tens = ["", "", "", "TREINTA", "CUARENTA", "CINCUENTA", "SESENTA", "SETENTA", "OCHENTA", "NOVENTA"];
    private static readonly string[] Hundreds = ["", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS", "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS"];
    public static string Format(decimal amount)
    {
        if (amount is < 0 or > 999_999_999.99m) throw new ArgumentOutOfRangeException(nameof(amount));
        amount = FiscalDocumentFactory.Round(amount);
        var integer = (int)amount;
        var cents = (int)((amount - integer) * 100);
        return $"{Words(integer)} {cents.ToString("D2", CultureInfo.InvariantCulture)}/100 DÓLARES";
    }
    private static string Words(int number)
    {
        if (number < 30) return Units[number];
        if (number < 100) return Tens[number / 10] + (number % 10 == 0 ? "" : " Y " + Words(number % 10));
        if (number == 100) return "CIEN";
        if (number < 1000) return Hundreds[number / 100] + (number % 100 == 0 ? "" : " " + Words(number % 100));
        if (number < 1_000_000) return (number / 1000 == 1 ? "MIL" : Words(number / 1000) + " MIL") + (number % 1000 == 0 ? "" : " " + Words(number % 1000));
        return (number / 1_000_000 == 1 ? "UN MILLÓN" : Words(number / 1_000_000) + " MILLONES") + (number % 1_000_000 == 0 ? "" : " " + Words(number % 1_000_000));
    }
}
