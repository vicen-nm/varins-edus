using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Parking;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Summary;

// The numbers of today for each module, in the institution's own time zone.
// The tenant filter limits every query to the caller's institution.
public class SummaryService(AppDbContext db, ICurrentTenant tenant, TimeProvider time)
{
    private const int WithoutExitDays = 30;

    private static readonly string[] WatchedActions =
        ["auth.login.failed", "auth.account.locked", "card.scan.reused", "auth.refresh.reuse"];

    public async Task<DailySummary> GetAsync(SummaryScope scope, CancellationToken ct = default)
    {
        if (tenant.InstitutionId is null)
        {
            throw new TenantViolationException("Only institution users have a daily summary.");
        }

        var zone = await LocalTime.ZoneAsync(db, ct);
        var local = TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime;
        var today = DateOnly.FromDateTime(local);
        var now = TimeOnly.FromDateTime(local);

        return new DailySummary(
            today,
            scope.Access ? await AccessAsync(today, ct) : null,
            scope.Parking ? await ParkingAsync(today, ct) : null,
            scope.Visits ? await VisitsAsync(today, ct) : null,
            scope.Exits ? await ExitsAsync(today, now, ct) : null,
            scope.Vehicles ? await VehiclesAsync(ct) : null,
            scope.Students ? await StudentsAsync(ct) : null,
            scope.Security ? await SecurityAsync(zone, today, ct) : null);
    }

    private async Task<AccessSummary> AccessAsync(DateOnly today, CancellationToken ct)
    {
        var rows = await db.AccessEvents
            .AsNoTracking()
            .Where(e => e.ServiceDate == today)
            .Select(e => new { Service = e.AccessService.Name, e.Method })
            .ToListAsync(ct);

        var byService = rows
            .GroupBy(r => r.Service)
            .Select(g => new ServiceCount(g.Key, g.Count()))
            .OrderByDescending(s => s.Total)
            .ThenBy(s => s.Service)
            .ToList();

        return new AccessSummary(
            rows.Count,
            rows.Count(r => r.Method == AccessMethods.Qr),
            rows.Count(r => r.Method == AccessMethods.Manual),
            byService);
    }

    private async Task<ParkingSummary> ParkingAsync(DateOnly today, CancellationToken ct)
    {
        var since = today.AddDays(-WithoutExitDays);

        return new ParkingSummary(
            await db.ParkingEvents.CountAsync(e => e.ServiceDate == today && e.ExitedAt == null && !e.AutoClosed, ct),
            await db.ParkingEvents.CountAsync(e => e.ServiceDate == today, ct),
            await db.ParkingEvents.CountAsync(e => e.ServiceDate == today && e.ExitedAt != null, ct),
            await db.ParkingEvents.CountAsync(e => e.ServiceDate >= since && e.ServiceDate < today && e.ExitedAt == null, ct));
    }

    private async Task<VisitsSummary> VisitsAsync(DateOnly today, CancellationToken ct)
    {
        var pending = VisitStatuses.Pending;
        var inside = VisitStatuses.Inside;

        return new VisitsSummary(
            await db.Visits.CountAsync(v => v.ServiceDate == today, ct),
            await db.Visits.CountAsync(v => v.ServiceDate == today && v.Status == pending, ct),
            await db.Visits.CountAsync(v => v.ServiceDate == today && v.Status == inside, ct));
    }

    private async Task<ExitsSummary> ExitsAsync(DateOnly today, TimeOnly now, CancellationToken ct)
    {
        var active = ExitPermitStatuses.Active;
        var used = ExitPermitStatuses.Used;

        return new ExitsSummary(
            await db.ExitPermits.CountAsync(p => p.ServiceDate == today, ct),
            await db.ExitPermits.CountAsync(p => p.ServiceDate == today && p.Status == active && p.ToTime > now, ct),
            await db.ExitPermits.CountAsync(p => p.ServiceDate == today && p.Status == used, ct));
    }

    private async Task<VehiclesSummary> VehiclesAsync(CancellationToken ct)
    {
        var pending = VehicleStatuses.Pending;
        var approved = VehicleStatuses.Approved;
        var suspended = VehicleStatuses.Suspended;

        return new VehiclesSummary(
            await db.Vehicles.CountAsync(v => v.Status == pending, ct),
            await db.Vehicles.CountAsync(v => v.Status == approved, ct),
            await db.Vehicles.CountAsync(v => v.Status == suspended, ct));
    }

    private async Task<StudentsSummary> StudentsAsync(CancellationToken ct)
    {
        var active = StudentStatuses.Active;

        var total = await db.StudentProfiles.CountAsync(s => s.Status == active && s.Person.IsActive, ct);

        var withAccount = await db.StudentProfiles.CountAsync(
            s => s.Status == active && s.Person.IsActive && db.Users.Any(u => u.PersonId == s.PersonId),
            ct);

        return new StudentsSummary(total, withAccount, total - withAccount);
    }

    // The day of the institution runs from its local midnight to the next one, turned into UTC.
    private async Task<SecuritySummary> SecurityAsync(TimeZoneInfo zone, DateOnly today, CancellationToken ct)
    {
        var start = LocalTime.StartOfDay(zone, today);
        var end = LocalTime.StartOfDay(zone, today.AddDays(1));

        var actions = await db.AuditLogs
            .AsNoTracking()
            .Where(a => a.OccurredAt >= start && a.OccurredAt < end && WatchedActions.Contains(a.Action))
            .Select(a => a.Action)
            .ToListAsync(ct);

        return new SecuritySummary(
            actions.Count(a => a == "auth.login.failed"),
            actions.Count(a => a == "auth.account.locked"),
            actions.Count(a => a == "card.scan.reused"),
            actions.Count(a => a == "auth.refresh.reuse"));
    }
}
