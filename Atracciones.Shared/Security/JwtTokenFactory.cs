using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Atracciones.Shared.Settings;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Atracciones.Shared.Security;

public class JwtTokenFactory
{
    private readonly JwtSettings _settings;

    public JwtTokenFactory(IOptions<JwtSettings> settings)
    {
        _settings = settings.Value;
    }

    public (string Token, DateTime ExpirationUtc) Create(Guid subject, string userName, IEnumerable<string> roles)
    {
        var expiration = DateTime.UtcNow.AddMinutes(_settings.ExpirationMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, userName)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiration,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiration);
    }
}
