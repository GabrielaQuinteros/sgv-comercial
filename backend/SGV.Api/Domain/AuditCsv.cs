using System.Globalization;
using System.Text;

namespace Sgv.Api.Domain;

public static class AuditCsv
{
    public static byte[] Export(IReadOnlyList<AuditEvent> events)
    {
        var csv = new StringBuilder("\uFEFFId,Fecha UTC,Usuario,Acción,Entidad,Detalle\r\n");
        foreach (var item in events)
        {
            var fields = new[] { item.Id.ToString(CultureInfo.InvariantCulture), item.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), item.Actor, item.Action, item.EntityId, item.Detail };
            csv.AppendJoin(',', fields.Select(Escape));
            csv.Append("\r\n");
        }
        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static string Escape(string value)
    {
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0], StringComparison.Ordinal)) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
