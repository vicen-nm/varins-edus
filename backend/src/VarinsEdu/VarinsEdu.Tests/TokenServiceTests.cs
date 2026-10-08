using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VarinsEdu.Api.Security;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Security;
using Xunit;

namespace VarinsEdu.Tests;

public class TokenServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        Key = new string('k', 48),
        ExpiresMinutes = 30
    };

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static TokenValidationParameters ValidationParameters(string key) => new()
    {
        ValidIssuer = Options.Issuer,
        ValidAudience = Options.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        ClockSkew = TimeSpan.Zero
    };

    [Fact]
    public void Token_contains_identity_institution_and_permissions()
    {
        var user = new AuthenticatedUser(
            Guid.NewGuid(),
            "ana",
            Guid.NewGuid(),
            [PermissionKeys.StudentsView, PermissionKeys.UsersManage]);

        var (token, expiresAt) = new TokenService(Options, TimeProvider.System).CreateToken(user);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);

        Assert.Equal(user.UserId.ToString(), jwt.Subject);
        Assert.Equal(user.InstitutionId.ToString(), jwt.Claims.Single(c => c.Type == AppClaims.InstitutionId).Value);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == AppClaims.Scope);

        var permissions = jwt.Claims
            .Where(c => c.Type == AppClaims.Permission)
            .Select(c => c.Value)
            .Order()
            .ToList();
        Assert.Equal(new[] { PermissionKeys.StudentsView, PermissionKeys.UsersManage }.Order(), permissions);

        Assert.True(expiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Platform_user_gets_platform_scope_and_no_institution()
    {
        var user = new AuthenticatedUser(Guid.NewGuid(), "root", null, [PermissionKeys.UsersManage]);

        var (token, _) = new TokenService(Options, TimeProvider.System).CreateToken(user);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);

        Assert.DoesNotContain(jwt.Claims, c => c.Type == AppClaims.InstitutionId);
        Assert.Contains(jwt.Claims, c => c.Type == AppClaims.Scope && c.Value == AppClaims.PlatformScope);
    }

    [Fact]
    public async Task Token_is_valid_with_the_right_key_and_rejected_with_another()
    {
        var user = new AuthenticatedUser(Guid.NewGuid(), "ana", Guid.NewGuid(), []);
        var (token, _) = new TokenService(Options, TimeProvider.System).CreateToken(user);
        var handler = new JsonWebTokenHandler();

        var withRightKey = await handler.ValidateTokenAsync(token, ValidationParameters(Options.Key));
        var withWrongKey = await handler.ValidateTokenAsync(token, ValidationParameters(new string('x', 48)));

        Assert.True(withRightKey.IsValid);
        Assert.False(withWrongKey.IsValid);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var user = new AuthenticatedUser(Guid.NewGuid(), "ana", Guid.NewGuid(), []);
        var twoHoursAgo = new FixedTime(DateTimeOffset.UtcNow.AddHours(-2));

        var (token, _) = new TokenService(Options, twoHoursAgo).CreateToken(user);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, ValidationParameters(Options.Key));

        Assert.False(result.IsValid);
    }
}
