using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using VarinsEdu.Api.Security;
using VarinsEdu.Domain.Constants;
using Xunit;

namespace VarinsEdu.Tests;

public class PermissionPolicyTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options => options.AddPermissionPolicies());
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal Principal(params string[] permissions)
    {
        var claims = permissions.Select(p => new Claim(AppClaims.Permission, p)).ToList();
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }

    [Fact]
    public async Task User_with_the_permission_is_authorized()
    {
        var auth = BuildProvider().GetRequiredService<IAuthorizationService>();

        var result = await auth.AuthorizeAsync(
            Principal(PermissionKeys.UsersManage), null, PermissionKeys.UsersManage);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task User_without_the_permission_is_denied()
    {
        var auth = BuildProvider().GetRequiredService<IAuthorizationService>();

        var result = await auth.AuthorizeAsync(
            Principal(PermissionKeys.StudentsView), null, PermissionKeys.UsersManage);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Anonymous_user_is_denied()
    {
        var auth = BuildProvider().GetRequiredService<IAuthorizationService>();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await auth.AuthorizeAsync(anonymous, null, PermissionKeys.UsersManage);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Every_permission_has_a_policy()
    {
        var policies = BuildProvider().GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (var key in PermissionKeys.All)
        {
            Assert.NotNull(await policies.GetPolicyAsync(key));
        }
    }
}
