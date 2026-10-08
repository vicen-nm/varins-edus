using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Security;

namespace VarinsEdu.Api.Security;

public class TokenService(JwtOptions options, TimeProvider timeProvider)
{
    public (string Token, DateTimeOffset ExpiresAt) CreateToken(AuthenticatedUser user)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(options.ExpiresMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (user.InstitutionId is { } institutionId)
        {
            claims.Add(new Claim(AppClaims.InstitutionId, institutionId.ToString()));
        }
        else
        {
            claims.Add(new Claim(AppClaims.Scope, AppClaims.PlatformScope));
        }

        claims.AddRange(user.Permissions.Select(p => new Claim(AppClaims.Permission, p)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);

        return (token, expiresAt);
    }
}
