using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Sgv.Api.Domain;

namespace Sgv.Api.Security;

public sealed class TokenService(string key)
{
    public const string Issuer = "sgv-comercial";
    public const string Audience = "sgv-web";

    public object Create(UserAccount account)
    {
        var expires = DateTime.UtcNow.AddMinutes(15);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, account.Username),
            new Claim(ClaimTypes.Role, account.Role)
        };
        var jwt = new JwtSecurityToken(Issuer, Audience, claims, expires: expires,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        return new { accessToken = new JwtSecurityTokenHandler().WriteToken(jwt), expiresAt = expires, username = account.Username, role = account.Role };
    }
}
