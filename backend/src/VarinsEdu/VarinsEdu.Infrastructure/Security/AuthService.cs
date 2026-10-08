using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Security;

public class AuthService(AppDbContext db, IPasswordHasher passwordHasher)
{
    // Returns null for any failure, so callers cannot tell why (prevents user enumeration).
    public async Task<AuthenticatedUser?> ValidateCredentialsAsync(
        string username,
        string password,
        CancellationToken ct = default)
    {
        var normalized = username.Trim().ToLowerInvariant();

        // At login we do not know the institution yet, so this lookup deliberately skips the tenant filter.
        var user = await db.Users
            .IgnoreQueryFilters()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
            .FirstOrDefaultAsync(u => u.Username == normalized, ct);

        if (user is null || !user.IsActive)
        {
            return null;
        }

        if (!passwordHasher.Verify(user.PasswordHash, password))
        {
            return null;
        }

        if (user.InstitutionId is { } institutionId)
        {
            var institutionIsActive = await db.Institutions
                .IgnoreQueryFilters()
                .AnyAsync(i => i.Id == institutionId && i.IsActive, ct);

            if (!institutionIsActive)
            {
                return null;
            }
        }

        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.PermissionKey)
            .Distinct()
            .Order()
            .ToList();

        return new AuthenticatedUser(user.Id, user.Username, user.InstitutionId, permissions);
    }
}
