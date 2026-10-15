using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Security;

public enum ActivationError
{
    None,
    WeakPassword,

    // Every other failure looks the same from the outside. The exact reason goes only to the audit log.
    ActivationFailed
}

public sealed record ActivationCommand(string InstitutionSlug, string IdentificationNumber, string Code, string Password);

public sealed record ActivationResult(ActivationError Error, AuthenticatedUser? User = null)
{
    public bool Succeeded => Error == ActivationError.None;
}

// Lets a student create their account with the identification and activation code the school gave them.
public class StudentActivationService(
    DbContextOptions<AppDbContext> options,
    IPasswordHasher passwordHasher,
    ActivationCodeHasher codeHasher,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    public const int MaxFailedAttempts = 5;

    private const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 128;

    private static readonly string[] CommonPasswords =
        ["12345678", "123456789", "1234567890", "password", "contrasena", "qwertyui", "11111111"];

    public async Task<ActivationResult> ActivateAsync(ActivationCommand command, CancellationToken ct = default)
    {
        var slug = command.InstitutionSlug.Trim().ToLowerInvariant();
        var identification = IdentificationNumbers.Normalize(command.IdentificationNumber);

        // Checked first and cheap: a weak password never uses up an attempt.
        if (IsWeak(command.Password, identification))
        {
            return new ActivationResult(ActivationError.WeakPassword);
        }

        if (slug.Length == 0
            || identification.Length == 0
            || ActivationCodeGenerator.Normalize(command.Code).Length == 0)
        {
            return await FailAsync(null, null, "missing_fields", ct);
        }

        // Step 1: identify the institution. Nothing is written with this context.
        Guid? institutionId;

        await using (var lookup = new AppDbContext(options, new NoTenant()))
        {
            institutionId = await lookup.Institutions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(i => i.Slug == slug && i.IsActive)
                .Select(i => (Guid?)i.Id)
                .SingleOrDefaultAsync(ct);
        }

        if (institutionId is null)
        {
            return await FailAsync(null, null, "unknown_institution", ct);
        }

        // From here on, everything runs inside that institution: filters and write rules apply.
        await using var db = new AppDbContext(options, new InstitutionTenant(institutionId.Value));

        var matches = await db.People
            .Where(p => p.IdentificationNumber == identification && p.IsActive)
            .ToListAsync(ct);

        if (matches.Count != 1)
        {
            return await FailAsync(institutionId, null, "unknown_person", ct);
        }

        var person = matches[0];

        if (await db.Users.AnyAsync(u => u.PersonId == person.Id, ct))
        {
            return await FailAsync(institutionId, person.Id, "already_activated", ct);
        }

        var code = await db.ActivationCodes
            .Where(c => c.PersonId == person.Id && c.UsedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (code is null)
        {
            return await FailAsync(institutionId, person.Id, "no_active_code", ct);
        }

        if (code.FailedAttempts >= MaxFailedAttempts)
        {
            return await FailAsync(institutionId, person.Id, "locked", ct);
        }

        if (!codeHasher.Verify(code.CodeHash, command.Code))
        {
            code.FailedAttempts++;
            await db.SaveChangesAsync(ct);

            return await FailAsync(institutionId, person.Id, "wrong_code", ct);
        }

        var now = timeProvider.GetUtcNow();

        if (code.ExpiresAt <= now)
        {
            return await FailAsync(institutionId, person.Id, "expired", ct);
        }

        var role = await db.Roles.SingleOrDefaultAsync(r => r.Key == RoleKeys.Student, ct);

        if (role is null)
        {
            return await FailAsync(institutionId, person.Id, "student_role_missing", ct);
        }

        var username = StudentUsernames.Build(slug, identification);

        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Username == username, ct))
        {
            return await FailAsync(institutionId, person.Id, "username_taken", ct);
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            InstitutionId = institutionId,
            PersonId = person.Id,
            Username = username,
            PasswordHash = passwordHasher.Hash(command.Password),
            IsActive = true,
            CreatedAt = now
        };
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

        db.Users.Add(user);
        code.UsedAt = now;

        db.AuditLogs.Add(new AuditLog
        {
            InstitutionId = institutionId,
            UserId = user.Id,
            Action = "auth.activation.succeeded",
            EntityType = "User",
            EntityId = user.Id.ToString(),
            OccurredAt = now,
            IpAddress = currentUser.IpAddress
        });

        try
        {
            // One save, so one atomic transaction. The unique index on person_id stops two
            // simultaneous activations from creating two accounts.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return await FailAsync(institutionId, person.Id, "conflict", ct);
        }

        var permissions = await db.RolePermissions
            .Where(rp => rp.RoleId == role.Id)
            .Select(rp => rp.PermissionKey)
            .Distinct()
            .OrderBy(key => key)
            .ToListAsync(ct);

        return new ActivationResult(
            ActivationError.None,
            new AuthenticatedUser(user.Id, username, institutionId, permissions));
    }

    private static bool IsWeak(string password, string identification)
    {
        if (password.Length < MinPasswordLength || password.Length > MaxPasswordLength)
        {
            return true;
        }

        if (identification.Length > 0 && password.Contains(identification, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (CommonPasswords.Contains(password.ToLowerInvariant()))
        {
            return true;
        }

        return password.Distinct().Count() == 1;
    }

    // Records the exact reason, but never the identification number that was tried.
    private async Task<ActivationResult> FailAsync(Guid? institutionId, Guid? personId, string reason, CancellationToken ct)
    {
        await using var auditDb = new AppDbContext(options, new NoTenant());

        auditDb.AuditLogs.Add(new AuditLog
        {
            InstitutionId = institutionId,
            UserId = null,
            Action = "auth.activation.failed",
            EntityType = "Person",
            EntityId = personId?.ToString(),
            OccurredAt = timeProvider.GetUtcNow(),
            IpAddress = currentUser.IpAddress,
            Metadata = JsonSerializer.Serialize(new { reason })
        });

        await auditDb.SaveChangesAsync(ct);

        return new ActivationResult(ActivationError.ActivationFailed);
    }
}
