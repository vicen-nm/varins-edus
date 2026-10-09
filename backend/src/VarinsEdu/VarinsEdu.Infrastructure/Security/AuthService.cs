using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Security;

public class AuthService(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    // Returns null for any failure, so callers cannot tell why (prevents user enumeration).
    // The exact reason is only written to the audit log.
    public async Task<AuthenticatedUser?> ValidateCredentialsAsync(
        string username,
        string password,
        CancellationToken ct = default)
    {
        var normalized = username.Trim().ToLowerInvariant();

        // At login we do not know the institution yet, so this lookup deliberately skips the tenant filter.
        var user = await db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
            .FirstOrDefaultAsync(u => u.Username == normalized, ct);

        if (user is null)
        {
            await RecordAsync("auth.login.failed", null, normalized, "unknown_user", ct);
            return null;
        }

        if (!user.IsActive)
        {
            await RecordAsync("auth.login.failed", user, normalized, "inactive_user", ct);
            return null;
        }

        if (!passwordHasher.Verify(user.PasswordHash, password))
        {
            await RecordAsync("auth.login.failed", user, normalized, "wrong_password", ct);
            return null;
        }

        if (user.InstitutionId is { } institutionId)
        {
            var institutionIsActive = await db.Institutions
                .IgnoreQueryFilters()
                .AnyAsync(i => i.Id == institutionId && i.IsActive, ct);

            if (!institutionIsActive)
            {
                await RecordAsync("auth.login.failed", user, normalized, "inactive_institution", ct);
                return null;
            }
        }

        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.PermissionKey)
            .Distinct()
            .Order()
            .ToList();

        await RecordAsync("auth.login.succeeded", user, normalized, null, ct);

        return new AuthenticatedUser(user.Id, user.Username, user.InstitutionId, permissions);
    }

    private async Task RecordAsync(string action, User? user, string attemptedUsername, string? reason, CancellationToken ct)
    {
        // Never store the password. The attempted username is cut to a safe length.
        var shownUsername = attemptedUsername.Length > 100 ? attemptedUsername[..100] : attemptedUsername;

        db.AuditLogs.Add(new AuditLog
        {
            InstitutionId = user?.InstitutionId,
            UserId = user?.Id,
            Action = action,
            EntityType = "User",
            EntityId = user?.Id.ToString(),
            OccurredAt = timeProvider.GetUtcNow(),
            IpAddress = currentUser.IpAddress,
            Metadata = JsonSerializer.Serialize(new { username = shownUsername, reason })
        });

        await db.SaveChangesAsync(ct);
    }
}
