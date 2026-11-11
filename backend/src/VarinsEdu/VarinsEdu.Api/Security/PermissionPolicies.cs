using Microsoft.AspNetCore.Authorization;
using VarinsEdu.Domain.Constants;

namespace VarinsEdu.Api.Security;

public static class PermissionPolicies
{
    // Reading the access points is needed to register manually (access.scan) and to show the screen (access.display).
    public const string AccessPointsRead = "access.points.read";

    // One policy per permission, named exactly like the permission key.
    // Usage: [Authorize(Policy = PermissionKeys.UsersManage)]
    public static AuthorizationOptions AddPermissionPolicies(this AuthorizationOptions options)
    {
        foreach (var key in PermissionKeys.All)
        {
            options.AddPolicy(key, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(AppClaims.Permission, key));
        }

        options.AddPolicy(AccessPointsRead, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(context =>
                context.User.HasClaim(AppClaims.Permission, PermissionKeys.AccessScan)
                || context.User.HasClaim(AppClaims.Permission, PermissionKeys.AccessDisplay)));

        // Requires a platform-level token (no institution). Combine it with a permission policy.
        options.AddPolicy(PolicyNames.PlatformOnly, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(AppClaims.Scope, AppClaims.PlatformScope));

        return options;
    }
}
