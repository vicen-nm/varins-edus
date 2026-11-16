using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Security;

public enum PasswordError
{
    None,
    Weak,

    // Every problem with a reset code looks the same from outside. The reason goes only to the audit log.
    Failed,
    InvalidCurrentPassword
}

public sealed record PasswordResult(PasswordError Error)
{
    public bool Succeeded => Error == PasswordError.None;
}

public sealed record ResetPasswordCommand(string Username, string Code, string NewPassword);

// KeepSessionToken: the renewal cookie of this browser. Its session survives the change; the others end.
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword, string? KeepSessionToken = null);

// Choosing a new password: with a one-time code (forgotten password, new account) or knowing the current one.
public class PasswordService(
    DbContextOptions<AppDbContext> options,
    AppDbContext db,
    ICurrentUser currentUser,
    IPasswordHasher passwordHasher,
    ResetCodeHasher codeHasher,
    TimeProvider timeProvider,
    RefreshTokenHasher refreshHasher)
{
    public const int MaxFailedCodeAttempts = 5;
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromHours(24);

    private sealed record Found(Guid Id, Guid? InstitutionId, bool IsDevice);

    public async Task<PasswordResult> ResetAsync(ResetPasswordCommand command, CancellationToken ct = default)
    {
        var username = (command.Username ?? string.Empty).Trim().ToLowerInvariant();
        var newPassword = command.NewPassword ?? string.Empty;
        var localPart = username.Split('@')[0];

        // Checked first and cheap, with the minimum every account shares: a weak password never uses an
        // attempt and does not tell whether the username exists.
        if (PasswordPolicy.IsWeak(newPassword, PasswordPolicy.StudentMinLength, localPart))
        {
            return new PasswordResult(PasswordError.Weak);
        }

        if (username.Length == 0 || ActivationCodeGenerator.Normalize(command.Code).Length == 0)
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
                .Where(u => u.Username == username)
                .Select(u => new Found(u.Id, u.InstitutionId, u.IsDevice))
                .FirstOrDefaultAsync(ct);
        }

        if (found is null)
        {
            return await FailAsync(null, null, "unknown_user", ct);
        }

        // Devices have no password: they are paired, not reset.
        if (found.IsDevice)
        {
            return await FailAsync(null, found.Id, "device_account", ct);
        }

        if (found.InstitutionId is not { } institutionId)
        {
            return await FailAsync(null, found.Id, "platform_user", ct);
        }

        // From here on, everything runs inside that institution.
        await using var scoped = new AppDbContext(options, new InstitutionTenant(institutionId));

        var user = await scoped.Users.FirstOrDefaultAsync(u => u.Id == found.Id && u.IsActive, ct);

        if (user is null)
        {
            return await FailAsync(institutionId, found.Id, "inactive_user", ct);
        }

        var code = await scoped.PasswordResetCodes.FirstOrDefaultAsync(c => c.UserId == user.Id, ct);

        if (code is null)
        {
            return await FailAsync(institutionId, user.Id, "no_code", ct);
        }

        if (code.UsedAt is not null)
        {
            return await FailAsync(institutionId, user.Id, "used", ct);
        }

        if (code.FailedAttempts >= MaxFailedCodeAttempts)
        {
            return await FailAsync(institutionId, user.Id, "locked", ct);
        }

        if (!codeHasher.Verify(code.CodeHash, command.Code ?? string.Empty))
        {
            code.FailedAttempts++;
            await scoped.SaveChangesAsync(ct);

            return await FailAsync(institutionId, user.Id, "wrong_code", ct);
        }

        var now = timeProvider.GetUtcNow();

        if (code.ExpiresAt <= now)
        {
            return await FailAsync(institutionId, user.Id, "expired", ct);
        }

        // The person proved they hold a valid code, so the stricter minimum for staff can be told now.
        if (user.PersonId is null && PasswordPolicy.IsWeak(newPassword, PasswordPolicy.StaffMinLength, localPart))
        {
            return new PasswordResult(PasswordError.Weak);
        }

        user.PasswordHash = passwordHasher.Hash(newPassword);
        LoginPolicy.Clear(user);
        code.UsedAt = now;

        // Whoever knew the old password must not keep a session.
        await RefreshTokenService.RevokeAllAsync(scoped, user.Id, now, null, ct);

        scoped.AuditLogs.Add(new AuditLog
        {
            InstitutionId = institutionId,
            UserId = user.Id,
            Action = "auth.password.reset.succeeded",
            EntityType = "User",
            EntityId = user.Id.ToString(),
            OccurredAt = now,
            IpAddress = currentUser.IpAddress
        });

        // One save, so one atomic transaction: the code is used up and the password changes together.
        await scoped.SaveChangesAsync(ct);

        return new PasswordResult(PasswordError.None);
    }

    // For a signed-in user who knows their password. Wrong guesses count toward the same lock as the login,
    // so a stolen session cannot be used to guess the password here.
    public async Task<PasswordResult> ChangeAsync(ChangePasswordCommand command, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new InvalidOperationException("An authenticated user is required to change the password.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);

        if (user is null)
        {
            return new PasswordResult(PasswordError.Failed);
        }

        var now = timeProvider.GetUtcNow();
        var current = command.CurrentPassword ?? string.Empty;
        var newPassword = command.NewPassword ?? string.Empty;

        if (LoginPolicy.IsLocked(user, now))
        {
            return new PasswordResult(PasswordError.InvalidCurrentPassword);
        }

        if (!passwordHasher.Verify(user.PasswordHash, current))
        {
            LoginPolicy.RegisterFailure(user, now);
            await db.SaveChangesAsync(ct);

            return new PasswordResult(PasswordError.InvalidCurrentPassword);
        }

        var minLength = user.PersonId is null ? PasswordPolicy.StaffMinLength : PasswordPolicy.StudentMinLength;
        var localPart = user.Username.Split('@')[0];

        if (newPassword == current || PasswordPolicy.IsWeak(newPassword, minLength, localPart))
        {
            return new PasswordResult(PasswordError.Weak);
        }

        user.PasswordHash = passwordHasher.Hash(newPassword);
        LoginPolicy.Clear(user);

        // The other devices end their sessions; this browser keeps its own.
        Guid? keepFamily = null;

        if (!string.IsNullOrWhiteSpace(command.KeepSessionToken))
        {
            var keepHash = refreshHasher.Hash(command.KeepSessionToken);

            keepFamily = await db.RefreshTokens
                .Where(t => t.TokenHash == keepHash && t.UserId == user.Id)
                .Select(t => (Guid?)t.FamilyId)
                .FirstOrDefaultAsync(ct);
        }

        await RefreshTokenService.RevokeAllAsync(db, user.Id, now, keepFamily, ct);
        await db.SaveChangesAsync(ct);

        return new PasswordResult(PasswordError.None);
    }

    // Records the exact reason, but never the username that was tried.
    private async Task<PasswordResult> FailAsync(Guid? institutionId, Guid? userId, string reason, CancellationToken ct)
    {
        await using var auditDb = new AppDbContext(options, new NoTenant());

        auditDb.AuditLogs.Add(new AuditLog
        {
            InstitutionId = institutionId,
            UserId = null,
            Action = "auth.password.reset.failed",
            EntityType = "User",
            EntityId = userId?.ToString(),
            OccurredAt = timeProvider.GetUtcNow(),
            IpAddress = currentUser.IpAddress,
            Metadata = JsonSerializer.Serialize(new { reason })
        });

        await auditDb.SaveChangesAsync(ct);

        return new PasswordResult(PasswordError.Failed);
    }
}
