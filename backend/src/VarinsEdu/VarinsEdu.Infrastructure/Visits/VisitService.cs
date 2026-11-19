using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Access;
using VarinsEdu.Infrastructure.Parking;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Visits;

// The guard's side: see today's visits, admit or reject them, and register the exit.
// The tenant filter limits every query to the caller's institution.
public class VisitService(AppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser, TimeProvider time)
{
    // Without a date: today. status: pending, inside, exited, rejected, expired or no_exit.
    public async Task<VisitPage> ListAsync(
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

        var today = await TodayAsync(ct);
        var day = date ?? today;
        var pending = VisitStatuses.Pending;
        var inside = VisitStatuses.Inside;

        var insideNow = await db.Visits.CountAsync(v => v.ServiceDate == today && v.Status == inside, ct);
        var pendingNow = await db.Visits.CountAsync(v => v.ServiceDate == today && v.Status == pending, ct);

        var query = db.Visits.AsNoTracking().Where(v => v.ServiceDate == day);

        // A pending or inside visit of a previous day is "expired" or "no_exit", so those filters only mean
        // something for past days, and "pending" and "inside" only for today.
        switch (status)
        {
            case VisitStatuses.Pending or VisitStatuses.Inside when day >= today:
            case VisitStatuses.Exited or VisitStatuses.Rejected:
                query = query.Where(v => v.Status == status);
                break;
            case VisitStatuses.Expired when day < today:
                query = query.Where(v => v.Status == pending);
                break;
            case VisitStatuses.NoExit when day < today:
                query = query.Where(v => v.Status == inside);
                break;
            case VisitStatuses.Pending or VisitStatuses.Inside or VisitStatuses.Expired or VisitStatuses.NoExit:
                query = query.Where(_ => false);
                break;
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            var idTerm = IdentificationNumbers.Normalize(search);

            query = query.Where(v =>
                v.FullName.ToLower().Contains(term) ||
                (idTerm.Length > 0 && v.IdentificationNumber.Contains(idTerm)));
        }

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(v => v.CreatedAt)
            .ThenBy(v => v.Id)
            .Skip(skip)
            .Take(take)
            .Select(v => new
            {
                v.Id,
                v.FullName,
                v.IdentificationNumber,
                v.Phone,
                v.Host,
                v.Reason,
                v.Status,
                v.ServiceDate,
                v.CreatedAt,
                v.EnteredAt,
                v.ExitedAt,
                HandledBy = db.Users.Where(u => u.Id == v.HandledByUserId).Select(u => u.Username).FirstOrDefault(),
                ExitedBy = db.Users.Where(u => u.Id == v.ExitedByUserId).Select(u => u.Username).FirstOrDefault()
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new VisitItem(
                r.Id,
                r.FullName,
                r.IdentificationNumber,
                r.Phone,
                r.Host,
                r.Reason,
                StatusOf(r.Status, r.ServiceDate, today),
                r.CreatedAt,
                r.EnteredAt,
                r.HandledBy,
                r.ExitedAt,
                r.ExitedBy))
            .ToList();

        return new VisitPage(total, insideNow, pendingNow, items);
    }

    public Task<Outcome<bool>> AdmitAsync(Guid id, CancellationToken ct = default) =>
        ChangeAsync(id, VisitStatuses.Pending, VisitStatuses.Inside, ct);

    public Task<Outcome<bool>> RejectAsync(Guid id, CancellationToken ct = default) =>
        ChangeAsync(id, VisitStatuses.Pending, VisitStatuses.Rejected, ct);

    public Task<Outcome<bool>> ExitAsync(Guid id, CancellationToken ct = default) =>
        ChangeAsync(id, VisitStatuses.Inside, VisitStatuses.Exited, ct);

    // The signature is personal data: it is not in the list, only here, for staff who may see it.
    public async Task<Outcome<byte[]>> SignatureAsync(Guid id, CancellationToken ct = default)
    {
        Require();

        var bytes = await db.Visits
            .AsNoTracking()
            .Where(v => v.Id == id)
            .Select(v => v.SignaturePng)
            .FirstOrDefaultAsync(ct);

        return bytes is null ? Outcome<byte[]>.Fail("visit_not_found") : Outcome<byte[]>.Ok(bytes);
    }

    // Only today's visits move: an older one is expired or without an exit, and it stays that way.
    private async Task<Outcome<bool>> ChangeAsync(Guid id, string from, string to, CancellationToken ct)
    {
        Require();

        var staffId = currentUser.UserId
            ?? throw new InvalidOperationException("An authenticated user is required.");

        var visit = await db.Visits.FirstOrDefaultAsync(v => v.Id == id, ct);

        if (visit is null)
        {
            return Outcome<bool>.Fail("visit_not_found");
        }

        var today = await TodayAsync(ct);

        if (visit.Status != from || visit.ServiceDate != today)
        {
            return Outcome<bool>.Fail("invalid_visit_status");
        }

        var now = time.GetUtcNow();

        visit.Status = to;

        if (to == VisitStatuses.Inside)
        {
            visit.EnteredAt = now;
            visit.HandledByUserId = staffId;
        }
        else if (to == VisitStatuses.Rejected)
        {
            visit.HandledByUserId = staffId;
        }
        else
        {
            visit.ExitedAt = now;
            visit.ExitedByUserId = staffId;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two guards at once: the other one got there first.
            return Outcome<bool>.Fail("invalid_visit_status");
        }

        return Outcome<bool>.Ok(true);
    }

    private static string StatusOf(string status, DateOnly date, DateOnly today) =>
        date >= today
            ? status
            : status switch
            {
                VisitStatuses.Pending => VisitStatuses.Expired,
                VisitStatuses.Inside => VisitStatuses.NoExit,
                _ => status
            };

    private async Task<DateOnly> TodayAsync(CancellationToken ct) =>
        DateOnly.FromDateTime(await LocalTime.NowAsync(db, time, ct));

    private Guid Require() =>
        tenant.InstitutionId ?? throw new TenantViolationException("Only institution users can manage visits.");
}
