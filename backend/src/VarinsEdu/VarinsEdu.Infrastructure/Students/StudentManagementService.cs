using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Imports;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;

namespace VarinsEdu.Infrastructure.Students;

public class StudentManagementService(
    AppDbContext db,
    ICurrentTenant tenant,
    ActivationCodeHasher codeHasher,
    TimeProvider timeProvider)
{
    public const string NoCode = "none";
    public const string Pending = "pending";
    public const string Expired = "expired";
    public const string Locked = "locked";
    public const string Used = "used";

    // The tenant filter limits every query to the caller's institution.
    public async Task<StudentPage> ListAsync(
        string? search,
        Guid? groupId,
        bool? hasAccount,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        RequireInstitution();

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(skip, 0);
        var now = timeProvider.GetUtcNow();

        var query = db.StudentProfiles.AsNoTracking().AsQueryable();

        if (groupId is { } gid)
        {
            query = query.Where(s => s.GroupId == gid);
        }

        if (hasAccount is true)
        {
            query = query.Where(s => db.Users.Any(u => u.PersonId == s.PersonId));
        }
        else if (hasAccount is false)
        {
            query = query.Where(s => !db.Users.Any(u => u.PersonId == s.PersonId));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            // Cédulas are stored without hyphens and in upper case.
            var digits = new string(search.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

            query = query.Where(s =>
                (s.Person.FirstName + " " + s.Person.LastName).ToLower().Contains(term) ||
                (digits.Length > 0 && s.Person.IdentificationNumber.Contains(digits)));
        }

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderBy(s => s.Person.LastName)
            .ThenBy(s => s.Person.FirstName)
            .ThenBy(s => s.PersonId)
            .Skip(skip)
            .Take(take)
            .Select(s => new
            {
                s.PersonId,
                s.Person.IdentificationNumber,
                s.Person.FirstName,
                s.Person.LastName,
                s.GroupId,
                GroupName = s.Group != null ? s.Group.Name : null,
                HasAccount = db.Users.Any(u => u.PersonId == s.PersonId),
                Code = db.ActivationCodes
                    .Where(c => c.PersonId == s.PersonId)
                    .OrderByDescending(c => c.CreatedAt)
                    .Select(c => new { c.ExpiresAt, c.UsedAt, c.FailedAttempts })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new StudentListItem(
                r.PersonId,
                r.IdentificationNumber,
                r.FirstName,
                r.LastName,
                r.GroupId,
                r.GroupName,
                r.HasAccount,
                r.Code is null ? NoCode : StatusOf(r.Code.ExpiresAt, r.Code.UsedAt, r.Code.FailedAttempts, now),
                r.Code?.ExpiresAt))
            .ToList();

        return new StudentPage(total, items);
    }

    public async Task<IReadOnlyList<GroupItem>> GroupsAsync(CancellationToken ct = default)
    {
        RequireInstitution();

        return await db.Groups
            .AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GroupItem(g.Id, g.Name))
            .ToListAsync(ct);
    }

    // Issues a new code and invalidates the old one. Only for students without an account.
    public async Task<CodeRegenerationResult> RegenerateCodeAsync(Guid personId, CancellationToken ct = default)
    {
        var institutionId = RequireInstitution();

        if (!await db.StudentProfiles.AnyAsync(s => s.PersonId == personId, ct))
        {
            return new CodeRegenerationResult(CodeRegenerationError.StudentNotFound, null, null);
        }

        if (await db.Users.AnyAsync(u => u.PersonId == personId, ct))
        {
            return new CodeRegenerationResult(CodeRegenerationError.HasAccount, null, null);
        }

        var now = timeProvider.GetUtcNow();
        var code = ActivationCodeGenerator.Generate();
        var hash = codeHasher.Hash(code);
        var expiresAt = now.Add(StudentImportCommitter.CodeLifetime);

        var codes = await db.ActivationCodes
            .Where(c => c.PersonId == personId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        var current = codes.FirstOrDefault();

        if (current is null)
        {
            db.ActivationCodes.Add(new ActivationCode
            {
                Id = Guid.NewGuid(),
                InstitutionId = institutionId,
                PersonId = personId,
                CodeHash = hash,
                ExpiresAt = expiresAt,
                CreatedAt = now
            });
        }
        else
        {
            // One code per person: reusing the row replaces the old fingerprint.
            current.CodeHash = hash;
            current.ExpiresAt = expiresAt;
            current.UsedAt = null;
            current.FailedAttempts = 0;
            current.CreatedAt = now;

            db.ActivationCodes.RemoveRange(codes.Skip(1));
        }

        await db.SaveChangesAsync(ct);

        return new CodeRegenerationResult(null, code, expiresAt);
    }

    private static string StatusOf(DateTimeOffset expiresAt, DateTimeOffset? usedAt, int failedAttempts, DateTimeOffset now)
    {
        if (usedAt is not null)
        {
            return Used;
        }

        if (failedAttempts >= StudentActivationService.MaxFailedAttempts)
        {
            return Locked;
        }

        return expiresAt <= now ? Expired : Pending;
    }

    private Guid RequireInstitution() =>
        tenant.InstitutionId ?? throw new TenantViolationException("Only institution users can manage students.");
}
