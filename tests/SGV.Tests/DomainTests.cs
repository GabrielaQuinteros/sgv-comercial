using Sgv.Api.Domain;
using Sgv.Api.Security;
using Xunit;

namespace Sgv.Tests;

public sealed class DomainTests
{
    [Fact]
    public void CalculatesTaxWithoutFloatingPointDrift()
    {
        var result = SaleCalculator.Calculate([new SaleLine(1, "Producto", 3, 19.99m)]);
        Assert.Equal(59.97m, result.Subtotal);
        Assert.Equal(7.80m, result.Tax);
        Assert.Equal(67.77m, result.Total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_001)]
    public void RejectsInvalidQuantities(int quantity) =>
        Assert.Throws<BusinessException>(() => SaleCalculator.Calculate([new SaleLine(1, "Producto", quantity, 10m)]));

    [Fact]
    public void RejectsDuplicateProducts() =>
        Assert.Throws<BusinessException>(() => SaleCalculator.ValidateLines([new SaleLineInput(1, 1), new SaleLineInput(1, 2)]));

    [Fact]
    public void PasswordHashUsesUniqueSaltAndChecksCredentials()
    {
        var password = Guid.NewGuid().ToString("N");
        var first = PasswordHasher.Hash(password);
        Assert.NotEqual(first, PasswordHasher.Hash(password));
        Assert.True(PasswordHasher.Verify(password, first));
        Assert.False(PasswordHasher.Verify("incorrecta", first));
        Assert.False(PasswordHasher.Verify(password, "invalid"));
        Assert.False(PasswordHasher.Verify(password, "pbkdf2-sha256$600000$!$!"));
    }

    [Fact]
    public void PasswordPolicyRequiresFourteenCharacters() =>
        Assert.Throws<ArgumentException>(() => PasswordHasher.Hash("short"));
}
