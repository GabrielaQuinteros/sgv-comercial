using System.Globalization;
using System.Security.Cryptography;

namespace Sgv.Api.Security;

public static class PasswordHasher
{
    private const int Iterations = 600_000;

    public static string Hash(string password)
    {
        if (password.Length is < 14 or > 128)
            throw new ArgumentException("La contraseña debe tener entre 14 y 128 caracteres.", nameof(password));
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        if (password.Length > 128) return false;
        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" ||
            !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var iterations) ||
            iterations is < 100_000 or > 1_000_000) return false;
        try
        {
            var expected = Convert.FromBase64String(parts[3]);
            if (expected.Length != 32) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[2]), iterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
