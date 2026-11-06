using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;

namespace Sgv.Fiscal;

public static class JwsDocuments
{
    private static readonly CryptoProviderFactory Factory = new() { CacheSignatureProviders = false };
    public static string Sign(string json, X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey || DateTime.UtcNow < certificate.NotBefore.ToUniversalTime() || DateTime.UtcNow > certificate.NotAfter.ToUniversalTime())
            throw new CryptographicException("El certificado de firma no está vigente o no tiene clave privada.");
        using var rsa = certificate.GetRSAPrivateKey() ?? throw new CryptographicException("El certificado debe ser RSA.");
        var header = Base64UrlEncoder.Encode(FiscalJson.Serialize(new { alg = "RS256", typ = "JWT", kid = certificate.Thumbprint }));
        var payload = Base64UrlEncoder.Encode(json);
        var input = header + "." + payload;
        var provider = Factory.CreateForSigning(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);
        try { return input + "." + Base64UrlEncoder.Encode(provider.Sign(Encoding.ASCII.GetBytes(input))); }
        finally { Factory.ReleaseSignatureProvider(provider); }
    }

    public static JsonObject Verify(string compact, X509Certificate2 certificate)
    {
        if (compact.Length > 1_048_576) throw new CryptographicException("Documento demasiado grande.");
        try
        {
            var parts = compact.Split('.');
            if (parts.Length != 3) throw new CryptographicException("JWS inválido.");
            var header = JsonNode.Parse(Base64UrlEncoder.Decode(parts[0]))!;
            if (header["alg"]?.GetValue<string>() != "RS256" || header["kid"]?.GetValue<string>() != certificate.Thumbprint)
                throw new CryptographicException("Algoritmo o certificado no permitido.");
            using var rsa = certificate.GetRSAPublicKey() ?? throw new CryptographicException("Certificado público inválido.");
            var provider = Factory.CreateForVerifying(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);
            try
            {
                if (!provider.Verify(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Base64UrlEncoder.DecodeBytes(parts[2])))
                    throw new CryptographicException("Firma no verificable.");
            }
            finally { Factory.ReleaseSignatureProvider(provider); }
            return JsonNode.Parse(Base64UrlEncoder.Decode(parts[1])) as JsonObject ?? throw new CryptographicException("El payload no es un objeto JSON.");
        }
        catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            throw new CryptographicException("JWS inválido.", exception);
        }
    }

    public static X509Certificate2 LoadPrivate(string path, string? password) => new(path, password, OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet);
}
