using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Access;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Parking;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Students;

namespace VarinsEdu.Infrastructure.Exits;

// Exit permits: the staff authorize, the guard registers the exit with the student's card.
// The card check and the permit share the request's database context, so the card is only used up
// if the permit is used in the same save. The tenant filter limits every query to the caller's institution.
public class ExitPermitService(
    AppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    CardScanService cards,
    AuditSuppression auditSuppression,
    TimeProvider time)
{
    public const int MaxReasonLength = 200;
    public const int MaxDaysAhead = 60;

    // ---- The staff ----

    public async Task<Outcome<CreatedExitPermit>> CreateAsync(ExitPermitInput input, CancellationToken ct = default)
    {
        var institutionId = Require();
        var staffId = RequireUser();

        var reason = input.Reason?.Trim() ?? string.Empty;

        if (reason.Length is 0 or > MaxReasonLength)
        {
            return Outcome<CreatedExitPermit>.Fail("invalid_exit_reason");
        }

        if (input.Date is not { } date)
        {
            return Outcome<CreatedExitPermit>.Fail("invalid_exit_date");
        }

        if (input.FromTime is not { } start || input.ToTime is not { } end || start >= end)
        {
            return Outcome<CreatedExitPermit>.Fail("invalid_exit_window");
        }

        var (today, now) = await LocalAsync(ct);

        if (date < today || date > today.AddDays(MaxDaysAhead))
        {
            return Outcome<CreatedExitPermit>.Fail("invalid_exit_date");
        }

        // A window that already ended today cannot be used.
        if (date == today && end <= now)
        {
            return Outcome<CreatedExitPermit>.Fail("invalid_exit_window");
        }

        var number = IdentificationNumbers.Normalize(input.IdentificationNumber);

        if (number.Length == 0)
        {
            return Outcome<CreatedExitPermit>.Fail("student_not_found");
        }

        var personId = await db.StudentProfiles
            .Where(s => s.Person.IdentificationNumber == number
                        && s.Status == StudentStatuses.Active
                        && s.Person.IsActive)
            .Select(s => (Guid?)s.PersonId)
            .FirstOrDefaultAsync(ct);

        if (personId is not { } pid)
        {
            return Outcome<CreatedExitPermit>.Fail("student_not_found");
        }

        var permit = new ExitPermit
        {
            Id = Guid.NewGuid(),
            InstitutionId = institutionId,
            PersonId = pid,
            ServiceDate = date,
            FromTime = start,
            ToTime = end,
            Reason = reason,
            Status = ExitPermitStatuses.Active,
            CreatedByUserId = staffId,
            CreatedAt = time.GetUtcNow()
        };

        db.ExitPermits.Add(permit);
        await db.SaveChangesAsync(ct);

        return Outcome<CreatedExitPermit>.Ok(new CreatedExitPermit(permit.Id));
    }

    // Without a date: today and the following days. status: active, used, cancelled or expired.
    public async Task<ExitPermitPage> ListAsync(
        DateOnly? date,
        string? status,
        string? search,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        Require();

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(skip, 0);

        var (today, now) = await LocalAsync(ct);
        var active = ExitPermitStatuses.Active;

        var query = db.ExitPermits.AsNoTracking().AsQueryable();

        query = date is { } day
            ? query.Where(p => p.ServiceDate == day)
            : query.Where(p => p.ServiceDate >= today);

        switch (status)
        {
            case ExitPermitStatuses.Used:
            case ExitPermitStatuses.Cancelled:
                query = query.Where(p => p.Status == status);
                break;
            case ExitPermitStatuses.Active:
                query = query.Where(p => p.Status == active
                                         && (p.ServiceDate > today || (p.ServiceDate == today && p.ToTime > now)));
                break;
            case ExitPermitStatuses.Expired:
                query = query.Where(p => p.Status == active
                                         && (p.ServiceDate < today || (p.ServiceDate == today && p.ToTime <= now)));
                break;
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();

            query = query.Where(p => (p.Person.FirstName + " " + p.Person.LastName).ToLower().Contains(term));
        }

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderBy(p => p.ServiceDate)
            .ThenBy(p => p.FromTime)
            .ThenBy(p => p.Id)
            .Skip(skip)
            .Take(take)
            .Select(p => new
            {
                p.Id,
                p.Person.FirstName,
                p.Person.LastName,
                Group = p.Person.StudentProfile != null && p.Person.StudentProfile.Group != null
                    ? p.Person.StudentProfile.Group.Name
                    : null,
                p.ServiceDate,
                p.FromTime,
                p.ToTime,
                p.Reason,
                p.Status,
                p.UsedAt,
                CreatedBy = db.Users.Where(u => u.Id == p.CreatedByUserId).Select(u => u.Username).FirstOrDefault(),
                UsedBy = db.Users.Where(u => u.Id == p.UsedByUserId).Select(u => u.Username).FirstOrDefault()
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new ExitPermitItem(
                r.Id,
                r.FirstName,
                r.LastName,
                r.Group,
                r.ServiceDate,
                r.FromTime,
                r.ToTime,
                r.Reason,
                StatusOf(r.Status, r.ServiceDate, r.ToTime, today, now),
                r.CreatedBy,
                r.UsedAt,
                r.UsedBy))
            .ToList();

        return new ExitPermitPage(total, items);
    }

    public async Task<Outcome<bool>> CancelAsync(Guid id, CancellationToken ct = default)
    {
        Require();

        var staffId = RequireUser();

        var permit = await db.ExitPermits.FirstOrDefaultAsync(p => p.Id == id, ct);

        if (permit is null)
        {
            return Outcome<bool>.Fail("exit_permit_not_found");
        }

        if (permit.Status != ExitPermitStatuses.Active)
        {
            return Outcome<bool>.Fail("exit_permit_not_cancellable");
        }

        permit.Status = ExitPermitStatuses.Cancelled;
        permit.CancelledByUserId = staffId;
        permit.CancelledAt = time.GetUtcNow();

        await db.SaveChangesAsync(ct);

        return Outcome<bool>.Ok(true);
    }

    // ---- The guard ----

    // Shows the student and the permits valid right now. The card is checked but NOT used up.
    public async Task<Outcome<ExitPreview>> PreviewAsync(string? token, CancellationToken ct = default)
    {
        Require();

        var verified = await cards.VerifyAsync(token, ct);

        if (verified.Error is { } error)
        {
            return Outcome<ExitPreview>.Fail(error);
        }

        var card = verified.Value!;
        var (today, now) = await LocalAsync(ct);
        var active = ExitPermitStatuses.Active;

        var permits = await db.ExitPermits
            .AsNoTracking()
            .Where(p => p.PersonId == card.PersonId
                        && p.Status == active
                        && p.ServiceDate == today
                        && p.FromTime <= now
                        && now < p.ToTime)
            .OrderBy(p => p.FromTime)
            .Select(p => new ExitOption(p.Id, p.FromTime, p.ToTime))
            .ToListAsync(ct);

        if (permits.Count == 0)
        {
            return Outcome<ExitPreview>.Fail("no_valid_permit");
        }

        return Outcome<ExitPreview>.Ok(new ExitPreview(card.Student, permits));
    }

    // Uses the permit up and the card with it. Every check comes before the card is used: a refusal
    // leaves the card usable.
    public async Task<Outcome<ExitResult>> RegisterAsync(string? token, Guid permitId, CancellationToken ct = default)
    {
        Require();

        var staffId = RequireUser();

        var verified = await cards.VerifyAsync(token, ct);

        if (verified.Error is { } error)
        {
            return Outcome<ExitResult>.Fail(error);
        }

        var card = verified.Value!;

        // A permit of another student looks just like one that does not exist.
        var permit = await db.ExitPermits.FirstOrDefaultAsync(p => p.Id == permitId && p.PersonId == card.PersonId, ct);

        if (permit is null)
        {
            return Outcome<ExitResult>.Fail("exit_permit_not_found");
        }

        var (today, now) = await LocalAsync(ct);

        if (!IsValidNow(permit, today, now))
        {
            return Outcome<ExitResult>.Fail("exit_permit_unavailable");
        }

        var at = time.GetUtcNow();

        permit.Status = ExitPermitStatuses.Used;
        permit.UsedAt = at;
        permit.UsedByUserId = staffId;

        cards.Stage(card);

        try
        {
            // The permit keeps who used it and when, and the card scan has its own table:
            // these rows are not audited one by one.
            using (auditSuppression.Begin())
            {
                await db.SaveChangesAsync(ct);
            }
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();

            // Two scans of the same code at once: the unique index let only one through.
            if (await cards.IsUsedAsync(card.PersonId, card.Nonce, ct))
            {
                return await cards.ReusedAsync<ExitResult>(card.PersonId, ct);
            }

            // Two guards using the same permit at once. Nothing was saved, so the card is still usable.
            return Outcome<ExitResult>.Fail("exit_permit_unavailable");
        }

        return Outcome<ExitResult>.Ok(new ExitResult(card.Student, at));
    }

    // ---- Shared ----

    private static bool IsValidNow(ExitPermit permit, DateOnly today, TimeOnly now) =>
        permit.Status == ExitPermitStatuses.Active
        && permit.ServiceDate == today
        && permit.FromTime <= now
        && now < permit.ToTime;

    private static string StatusOf(string status, DateOnly date, TimeOnly to, DateOnly today, TimeOnly now) =>
        status != ExitPermitStatuses.Active
            ? status
            : date < today || (date == today && to <= now)
                ? ExitPermitStatuses.Expired
                : ExitPermitStatuses.Active;

    private async Task<(DateOnly Day, TimeOnly Time)> LocalAsync(CancellationToken ct)
    {
        var local = await LocalTime.NowAsync(db, time, ct);

        return (DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));
    }

    private Guid Require() =>
        tenant.InstitutionId ?? throw new TenantViolationException("Only institution users can manage exit permits.");

    private Guid RequireUser() =>
        currentUser.UserId ?? throw new InvalidOperationException("An authenticated user is required.");
}
