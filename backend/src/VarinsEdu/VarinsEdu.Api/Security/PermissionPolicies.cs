using Microsoft.AspNetCore.Authorization;
using VarinsEdu.Domain.Constants;

namespace VarinsEdu.Api.Security;

public static class PermissionPolicies
{
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

        // Requires a platform-level token (no institution). Combine it with a permission policy.
        options.AddPolicy(PolicyNames.PlatformOnly, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(AppClaims.Scope, AppClaims.PlatformScope));

        return options;
    }
}
