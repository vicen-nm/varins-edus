using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Seeding;

namespace VarinsEdu.Infrastructure.Security;

// IdleDays: days without use before a session ends. MaxDays: the absolute limit since the sign-in.
public sealed record SessionPolicy(int IdleDays = 7, int MaxDays = 30);

public sealed record IssuedSession(string Token, DateTimeOffset ExpiresAt, bool Persistent);

public enum RefreshStatus
{
    Ok,

    // Unknown, expired, revoked or reused: all look the same from outside.
    Invalid,

    // The token was used a moment ago (two requests crossed): nothing is revoked.
    Conflict
}

public sealed record RefreshResult(RefreshStatus Status, AuthenticatedUser? User = null, IssuedSession? Session = null);

// Renewable sessions: a long-lived token (kept in an HttpOnly cookie) that is replaced on every use.
public class RefreshTokenService(
    DbContextOptions<AppDbContext> options,
    RefreshTokenHasher hasher,
    SessionPolicy policy,
    ICurrentUser currentUser,
    AuditSuppression auditSuppression,
    TimeProvider time)
{
    // A token used again within this time is a race between two requests, not a theft.
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(10);

    private const int MaxTokenLength = 200;

    private sealed record Located(string Hash, ICurrentTenant Tenant);

    public async Task<IssuedSession> StartAsync(AuthenticatedUser user, bool persistent, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();

        await using var db = new AppDbContext(options, ScopeOf(user.InstitutionId));

        var session = Add(db, user.UserId, user.InstitutionId, Guid.NewGuid(), persistent, now, now);
        await SaveAsync(db, ct);

        return session;
    }

    // The user and the permissions are read again every time, so role changes reach active sessions.
    public async Task<RefreshResult> RefreshAsync(string? token, CancellationToken ct = default)
    {
        var invalid = new RefreshResult(RefreshStatus.Invalid);
        var located = await LocateAsync(token, ct);

        if (located is null)
        {
            return invalid;
        }

        await using var db = new AppDbContext(options, located.Tenant);

        var current = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == located.Hash, ct);

        if (current is null || current.RevokedAt is not null)
        {
            return invalid;
        }

        var now = time.GetUtcNow();

        if (current.UsedAt is { } usedAt)
        {
            if (now - usedAt <= Grace)
            {
                return new RefreshResult(RefreshStatus.Conflict);
            }

            // An old token came back: whoever has it may be a thief, so the whole session ends.
            await RevokeFamilyAsync(db, current, now, "auth.refresh.reuse", "reuse", ct);

            return invalid;
        }

        if (current.ExpiresAt <= now)
        {
            return invalid;
        }

        var user = await db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
            .FirstOrDefaultAsync(u => u.Id == current.UserId && u.IsActive, ct);

        if (user is null || !await InstitutionIsActiveAsync(db, user.InstitutionId, ct))
        {
            await RevokeFamilyAsync(db, current, now, null, null, ct);

            return invalid;
        }

        current.UsedAt = now;

        var session = Add(db, current.UserId, current.InstitutionId, current.FamilyId, current.Persistent, current.FamilyStartedAt, now);
        await SaveAsync(db, ct);

        return new RefreshResult(
            RefreshStatus.Ok,
            new AuthenticatedUser(user.Id, user.Username, user.InstitutionId, AuthService.PermissionsOf(user)),
            session);
    }

    // Ends the session of this browser. An unknown token does nothing.
    public async Task LogoutAsync(string? token, CancellationToken ct = default)
    {
        var located = await LocateAsync(token, ct);

        if (located is null)
        {
            return;
        }

        await using var db = new AppDbContext(options, located.Tenant);

        var current = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == located.Hash, ct);

        if (current is null)
        {
            return;
        }

        await RevokeFamilyAsync(db, current, time.GetUtcNow(), "auth.logout", null, ct);
    }

    // Closes every session of a user, except one family if given. It does not save: the caller does,
    // so the change is part of the same transaction as the password or the account change.
    public static async Task RevokeAllAsync(
        AppDbContext db,
        Guid userId,
        DateTimeOffset now,
        Guid? exceptFamilyId,
        CancellationToken ct = default)
    {
        var query = db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null);

        if (exceptFamilyId is { } keep)
        {
            query = query.Where(t => t.FamilyId != keep);
        }

        foreach (var token in await query.ToListAsync(ct))
        {
            token.RevokedAt = now;
        }
    }

    private IssuedSession Add(
        AppDbContext db,
        Guid userId,
        Guid? institutionId,
        Guid familyId,
        bool persistent,
        DateTimeOffset familyStartedAt,
        DateTimeOffset now)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        var idleLimit = now.AddDays(policy.IdleDays);
        var absoluteLimit = familyStartedAt.AddDays(policy.MaxDays);
        var expiresAt = idleLimit < absoluteLimit ? idleLimit : absoluteLimit;

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            InstitutionId = institutionId,
            UserId = userId,
            FamilyId = familyId,
            TokenHash = hasher.Hash(token),
            Persistent = persistent,
            CreatedAt = now,
            FamilyStartedAt = familyStartedAt,
            ExpiresAt = expiresAt
        });

        return new IssuedSession(token, expiresAt, persistent);
    }

    private async Task RevokeFamilyAsync(
        AppDbContext db,
        RefreshToken current,
        DateTimeOffset now,
        string? auditAction,
        string? reason,
        CancellationToken ct)
    {
        var family = await db.RefreshTokens
            .Where(t => t.FamilyId == current.FamilyId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in family)
        {
            token.RevokedAt = now;
        }

        if (auditAction is not null)
        {
            db.AuditLogs.Add(new AuditLog
            {
                InstitutionId = current.InstitutionId,
                UserId = current.UserId,
                Action = auditAction,
                EntityType = "User",
                EntityId = current.UserId.ToString(),
                OccurredAt = now,
                IpAddress = currentUser.IpAddress,
                Metadata = reason is null ? null : JsonSerializer.Serialize(new { reason })
            });
        }

        await SaveAsync(db, ct);
    }

    // The token itself is never written to the audit log nor stored: only its fingerprint.
    // Renewals are not audited one by one: there would be thousands of rows a day.
    private async Task SaveAsync(AppDbContext db, CancellationToken ct)
    {
        using (auditSuppression.Begin())
        {
            await db.SaveChangesAsync(ct);
        }
    }

    // Nobody is identified when a cookie arrives: the token says whose it is. The lookup skips the
    // tenant filter, and everything else runs inside that user's own scope.
    private async Task<Located?> LocateAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
        {
            return null;
        }

        var hash = hasher.Hash(token);

        await using var lookup = new AppDbContext(options, new NoTenant());

        var row = await lookup.RefreshTokens
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.TokenHash == hash)
            .Select(t => new { t.InstitutionId })
            .FirstOrDefaultAsync(ct);

        return row is null ? null : new Located(hash, ScopeOf(row.InstitutionId));
    }

    private static ICurrentTenant ScopeOf(Guid? institutionId) =>
        institutionId is { } id ? new InstitutionTenant(id) : new PlatformTenant();

    private static async Task<bool> InstitutionIsActiveAsync(AppDbContext db, Guid? institutionId, CancellationToken ct)
    {
        if (institutionId is not { } id)
        {
            return true;
        }

        return await db.Institutions.IgnoreQueryFilters().AnyAsync(i => i.Id == id && i.IsActive, ct);
    }
}
