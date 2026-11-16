using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Security;

public enum PairingError
{
    None,

    // Every problem looks the same from outside. The reason goes only to the audit log.
    Failed
}

public sealed record PairingResult(PairingError Error, AuthenticatedUser? User = null, IssuedSession? Session = null)
{
    public bool Succeeded => Error == PairingError.None;
}

// A display device (the cafeteria screen) has no password: the school gives it a one-time code,
// and the device exchanges it for its own long session.
public class DevicePairingService(
    DbContextOptions<AppDbContext> options,
    ResetCodeHasher codeHasher,
    RefreshTokenService sessions,
    ICurrentUser currentUser,
    TimeProvider time)
{
    public const int MaxFailedAttempts = 5;

    // Short on purpose: the code is only needed at the moment of pairing.
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromHours(1);

    private sealed record Found(Guid Id, Guid? InstitutionId, bool IsDevice, string Username);

    public async Task<PairingResult> PairAsync(string? username, string? code, CancellationToken ct = default)
    {
        var name = (username ?? string.Empty).Trim().ToLowerInvariant();

        if (name.Length == 0 || ActivationCodeGenerator.Normalize(code).Length == 0)
        {
            return await FailAsync(null, null, "missing_fields", ct);
        }

        // Step 1: who is it? Nothing is written with this context.
        Found? found;

        await using (var lookup = new AppDbContext(options, new NoTenant()))
        {
            found = await lookup.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(u => u.Username == name)
                .Select(u => new Found(u.Id, u.InstitutionId, u.IsDevice, u.Username))
                .FirstOrDefaultAsync(ct);
        }

        if (found is null)
        {
            return await FailAsync(null, null, "unknown_device", ct);
        }

        if (!found.IsDevice || found.InstitutionId is not { } institutionId)
        {
            return await FailAsync(null, found.Id, "not_device", ct);
        }

        // From here on, everything runs inside that institution.
        await using var scoped = new AppDbContext(options, new InstitutionTenant(institutionId));

        var device = await scoped.Users.FirstOrDefaultAsync(u => u.Id == found.Id && u.IsActive, ct);

        if (device is null)
        {
            return await FailAsync(institutionId, found.Id, "inactive_device", ct);
        }

        var row = await scoped.PasswordResetCodes.FirstOrDefaultAsync(c => c.UserId == device.Id, ct);

        if (row is null)
        {
            return await FailAsync(institutionId, device.Id, "no_code", ct);
        }

        if (row.UsedAt is not null)
        {
            return await FailAsync(institutionId, device.Id, "used", ct);
        }

        if (row.FailedAttempts >= MaxFailedAttempts)
        {
            return await FailAsync(institutionId, device.Id, "locked", ct);
        }

        if (!codeHasher.Verify(row.CodeHash, code ?? string.Empty))
        {
            row.FailedAttempts++;
            await scoped.SaveChangesAsync(ct);

            return await FailAsync(institutionId, device.Id, "wrong_code", ct);
        }

        var now = time.GetUtcNow();

        if (row.ExpiresAt <= now)
        {
            return await FailAsync(institutionId, device.Id, "expired", ct);
        }

        row.UsedAt = now;

        // Pairing again replaces the previous screen: its sessions end.
        await RefreshTokenService.RevokeAllAsync(scoped, device.Id, now, null, ct);

        scoped.AuditLogs.Add(new AuditLog
        {
            InstitutionId = institutionId,
            UserId = device.Id,
            Action = "auth.device.paired",
            EntityType = "User",
            EntityId = device.Id.ToString(),
            OccurredAt = now,
            IpAddress = currentUser.IpAddress
        });

        // One save, so one atomic transaction: the code is used up and the old sessions end together.
        await scoped.SaveChangesAsync(ct);

        var roleIds = scoped.UserRoles.Where(ur => ur.UserId == device.Id).Select(ur => ur.RoleId);

        var permissions = await scoped.RolePermissions
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.PermissionKey)
            .Distinct()
            .OrderBy(key => key)
            .ToListAsync(ct);

        var user = new AuthenticatedUser(device.Id, device.Username, institutionId, permissions, IsDevice: true);
        var session = await sessions.StartAsync(user, persistent: true, ct);

        return new PairingResult(PairingError.None, user, session);
    }

    // Records the exact reason, but never the username that was tried.
    private async Task<PairingResult> FailAsync(Guid? institutionId, Guid? userId, string reason, CancellationToken ct)
    {
        await using var auditDb = new AppDbContext(options, new NoTenant());

        auditDb.AuditLogs.Add(new AuditLog
        {
            InstitutionId = institutionId,
            UserId = null,
            Action = "auth.device.pair.failed",
            EntityType = "User",
            EntityId = userId?.ToString(),
            OccurredAt = time.GetUtcNow(),
            IpAddress = currentUser.IpAddress,
            Metadata = JsonSerializer.Serialize(new { reason })
        });

        await auditDb.SaveChangesAsync(ct);

        return new PairingResult(PairingError.Failed);
    }
}
