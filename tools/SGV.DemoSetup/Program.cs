using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

if (args.Length > 0 && args[0] == "certificates")
{
    var folder = args.Length > 1 ? args[1] : ".local/certificates";
    Directory.CreateDirectory(folder);
    var password = Environment.GetEnvironmentVariable("DTE_CERTIFICATE_PASSWORD") ?? throw new InvalidOperationException("Configura DTE_CERTIFICATE_PASSWORD.");
    var privatePath = Path.Combine(folder, "firma.p12");
    var publicPath = Path.Combine(folder, "public.cer");
    if (File.Exists(privatePath) && File.Exists(publicPath)) { Console.WriteLine("Certificados de pruebas disponibles."); return; }
    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest("CN=SGV DTE Ambiente de Pruebas", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(90));
    await File.WriteAllBytesAsync(privatePath, certificate.Export(X509ContentType.Pkcs12, password));
    await File.WriteAllBytesAsync(publicPath, certificate.Export(X509ContentType.Cert));
    Console.WriteLine("Certificado .P12 de pruebas y certificado público preparados.");
    return;
}
if (args.Length == 0 || args[0] != "migrate") throw new ArgumentException("Usa certificates <carpeta> o migrate.");
var source = Environment.GetEnvironmentVariable("ConnectionStrings__Sgv") ?? throw new InvalidOperationException("Configura ConnectionStrings__Sgv.");
var settings = new SqlConnectionStringBuilder(source);
if (settings.InitialCatalog != "SgvDemo") throw new InvalidOperationException("Este preparador solo modifica la base de demostración SgvDemo.");
var masterSettings = new SqlConnectionStringBuilder(source) { InitialCatalog = "master" };
await using (var master = new SqlConnection(masterSettings.ConnectionString))
{
    await master.OpenAsync();
    await using var command = new SqlCommand("IF DB_ID('SgvDemo') IS NULL CREATE DATABASE SgvDemo", master);
    await command.ExecuteNonQueryAsync();
}
await using var connection = new SqlConnection(source); await connection.OpenAsync();
foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "database"), "*.sql").OrderBy(p => p, StringComparer.Ordinal))
{
    var sql = await File.ReadAllTextAsync(file);
    foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
    {
        if (string.IsNullOrWhiteSpace(batch)) continue;
        await using var command = new SqlCommand(batch, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync();
    }
    Console.WriteLine("Aplicado: " + Path.GetFileName(file));
}
Console.WriteLine("Base SgvDemo preparada: cinco ventas principales y treinta adicionales.");
