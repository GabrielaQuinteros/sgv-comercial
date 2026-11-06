using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Configuration;
using Sgv.Dte;
using Xunit;

namespace Sgv.Tests;

public sealed class DocumentSignerTests
{
    [Fact]
    public void CreatesVerifiableSignatureWithUniqueDocumentId()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=SGV Signing Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var path = Path.GetTempFileName();
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        try
        {
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, password));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DTE_CERTIFICATE_PATH"] = path, ["DTE_CERTIFICATE_PASSWORD"] = password
            }).Build();
            var signer = new DocumentSigner(config);
            var input = new DteInput(Guid.NewGuid(), 113m, "USD");
            var first = signer.Sign(input);
            var second = signer.Sign(input);
            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal(input.SaleId, first.SaleId);
            Assert.True(rsa.VerifyData(Encoding.UTF8.GetBytes(first.Payload), Convert.FromBase64String(first.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
            Assert.False(rsa.VerifyData(Encoding.UTF8.GetBytes(first.Payload + "changed"), Convert.FromBase64String(first.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsInvalidDocumentsAndMissingCertificate()
    {
        var signer = new DocumentSigner(new ConfigurationBuilder().Build());
        Assert.Throws<ArgumentException>(() => signer.Sign(new DteInput(Guid.Empty, 10m, "USD")));
        Assert.Throws<ArgumentException>(() => signer.Sign(new DteInput(Guid.NewGuid(), -1m, "USD")));
        Assert.Throws<ArgumentException>(() => signer.Sign(new DteInput(Guid.NewGuid(), 10m, "EUR")));
        Assert.Throws<InvalidOperationException>(() => signer.Sign(new DteInput(Guid.NewGuid(), 10m, "USD")));
    }
}
