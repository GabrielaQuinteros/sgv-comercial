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
    public async Task CreatesVerifiableSignatureWithUniqueDocumentId()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=SGV Signing Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
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
            var now = DateTimeOffset.UtcNow;
            var signer = new DocumentSigner(config, new FixedClock(now));
            var input = new DteInput(Guid.NewGuid(), 113m, "USD");
            var first = await signer.SignAsync(input);
            var second = await signer.SignAsync(input);
            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal(input.SaleId, first.SaleId);
            Assert.Equal(now, first.IssuedAt);
            Assert.True(rsa.VerifyData(Encoding.UTF8.GetBytes(first.Payload), Convert.FromBase64String(first.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            Assert.False(rsa.VerifyData(Encoding.UTF8.GetBytes(first.Payload + "changed"), Convert.FromBase64String(first.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RejectsInvalidDocumentsAndMissingCertificate()
    {
        var signer = new DocumentSigner(new ConfigurationBuilder().Build(), new FixedClock(DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<ArgumentException>(() => signer.SignAsync(new DteInput(Guid.Empty, 10m, "USD")));
        await Assert.ThrowsAsync<ArgumentException>(() => signer.SignAsync(new DteInput(Guid.NewGuid(), -1m, "USD")));
        await Assert.ThrowsAsync<ArgumentException>(() => signer.SignAsync(new DteInput(Guid.NewGuid(), 10m, "EUR")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => signer.SignAsync(new DteInput(Guid.NewGuid(), 10m, "USD")));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public Task<DateTimeOffset> UtcNowAsync(CancellationToken cancellation) => Task.FromResult(now);
    }
}
