using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Seeding;

namespace VarinsEdu.Infrastructure.Security;

public class AuthService(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    DbContextOptions<AppDbContext> options,
    AuditSuppression auditSuppression)
{
    // The permissions of a user, from all their roles.
    public static List<string> PermissionsOf(User user) => user.UserRoles
        .SelectMany(ur => ur.Role.RolePermissions)
        .Select(rp => rp.PermissionKey)
        .Distinct()
        .Order()
        .ToList();

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

        var now = timeProvider.GetUtcNow();

        // A locked account refuses even the right password, and new attempts do not extend the lock.
        if (LoginPolicy.IsLocked(user, now))
        {
            await RecordAsync("auth.login.failed", user, normalized, "locked", ct);
            return null;
        }

        if (!passwordHasher.Verify(user.PasswordHash, password))
        {
            var updated = await UpdateUserAsync(user, u => LoginPolicy.RegisterFailure(u, now), ct);

            await RecordAsync("auth.login.failed", user, normalized, "wrong_password", ct);

            if (updated is not null && LoginPolicy.IsLocked(updated, now))
            {
                await RecordAsync("auth.account.locked", user, normalized, "too_many_failures", ct);
            }

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

        var permissions = PermissionsOf(user);

        await UpdateUserAsync(
            user,
            u =>
            {
                LoginPolicy.Clear(u);
                u.LastLoginAt = now;
            },
            ct);

        await RecordAsync("auth.login.succeeded", user, normalized, null, ct);

        return new AuthenticatedUser(user.Id, user.Username, user.InstitutionId, permissions);
    }

    // Nobody is identified yet at login, so the write runs in the user's own scope: their institution,
    // or platform scope (only for a platform user's own row). The audit log is not flooded with one
    // entry per counted attempt: the failures themselves are recorded by RecordAsync.
    private async Task<User?> UpdateUserAsync(User user, Action<User> change, CancellationToken ct)
    {
        ICurrentTenant tenant = user.InstitutionId is { } id ? new InstitutionTenant(id) : new PlatformTenant();

        await using var write = new AppDbContext(options, tenant);

        var tracked = await write.Users.FirstOrDefaultAsync(u => u.Id == user.Id, ct);

        if (tracked is null)
        {
            return null;
        }

        change(tracked);

        using (auditSuppression.Begin())
        {
            await write.SaveChangesAsync(ct);
        }

        return tracked;
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
