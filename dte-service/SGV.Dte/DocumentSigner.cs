using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Sgv.Dte;

public sealed record DteInput(Guid SaleId, decimal Total, string Currency);
public sealed record SignedDocument(Guid Id, Guid SaleId, DateTimeOffset IssuedAt, string Payload, string Signature, string CertificateThumbprint);

public sealed class DocumentSigner(IConfiguration configuration, IClock clock)
{
    public async Task<SignedDocument> SignAsync(DteInput input, CancellationToken cancellation = default)
    {
        if (input.SaleId == Guid.Empty || input.Total <= 0 || input.Currency != "USD")
            throw new ArgumentException("Documento inválido. Se requiere venta, importe positivo y moneda USD.");
        var path = configuration["DTE_CERTIFICATE_PATH"];
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Configura el certificado de firma DTE.");
        var flags = OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
        using var certificate = new X509Certificate2(path, configuration["DTE_CERTIFICATE_PASSWORD"], flags);
        var now = await clock.UtcNowAsync(cancellation);
        if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
            throw new InvalidOperationException("El certificado no está vigente.");
        using var rsa = certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException("El certificado debe contener una clave RSA privada.");
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { id, input.SaleId, input.Total, input.Currency, issuedAt = now });
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        return new SignedDocument(id, input.SaleId, now, payload, Convert.ToBase64String(signature), certificate.Thumbprint);
    }
}
