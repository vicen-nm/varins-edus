using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Access;

// Totals of the cafeteria over a date range, and the Excel export of the same data.
public class AccessReportService(
    AppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    TimeProvider time)
{
    public const int MaxDays = 92;
    public const int DefaultDays = 7;
    public const int MaxRows = 100_000;

    // The tenant filter limits every query to the caller's institution.
    // Without dates: the last 7 days, ending today in the institution's time zone.
    public async Task<Outcome<AccessReport>> BuildAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        Require();

        var zone = await ZoneAsync(ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);

        var end = to ?? today;
        var start = from ?? end.AddDays(-(DefaultDays - 1));

        if (start > end)
        {
            return Outcome<AccessReport>.Fail("invalid_date_range");
        }

        if (end.DayNumber - start.DayNumber + 1 > MaxDays)
        {
            return Outcome<AccessReport>.Fail("range_too_long");
        }

        var rows = await db.AccessEvents
            .AsNoTracking()
            .Where(e => e.ServiceDate >= start && e.ServiceDate <= end)
            .OrderBy(e => e.ServiceDate)
            .ThenBy(e => e.OccurredAt)
            .Select(e => new
            {
                e.ServiceDate,
                e.OccurredAt,
                e.Method,
                Point = e.AccessPoint.Name,
                Service = e.AccessService.Name,
                e.Person.IdentificationNumber,
                e.Person.FirstName,
                e.Person.LastName,
                Group = e.Person.StudentProfile != null && e.Person.StudentProfile.Group != null
                    ? e.Person.StudentProfile.Group.Name
                    : null
            })
            .Take(MaxRows + 1)
            .ToListAsync(ct);

        if (rows.Count > MaxRows)
        {
            return Outcome<AccessReport>.Fail("report_too_large");
        }

        var totals = new ReportTotals(
            rows.Count,
            rows.Count(r => r.Method == AccessMethods.Qr),
            rows.Count(r => r.Method == AccessMethods.Manual));

        var byDay = rows
            .GroupBy(r => new { r.ServiceDate, r.Point, r.Service })
            .OrderBy(g => g.Key.ServiceDate)
            .ThenBy(g => g.Key.Point)
            .ThenBy(g => g.Key.Service)
            .Select(g => new ReportDayRow(
                g.Key.ServiceDate,
                g.Key.Point,
                g.Key.Service,
                g.Count(),
                g.Count(r => r.Method == AccessMethods.Qr),
                g.Count(r => r.Method == AccessMethods.Manual)))
            .ToList();

        var byGroup = rows
            .GroupBy(r => r.Group)
            .Select(g => new ReportGroupRow(g.Key, g.Count()))
            .OrderByDescending(g => g.Total)
            .ThenBy(g => g.Group ?? string.Empty)
            .ToList();

        var details = rows
            .Select(r => new ReportDetailRow(
                r.ServiceDate,
                TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.OccurredAt, zone).DateTime),
                r.IdentificationNumber,
                r.FirstName,
                r.LastName,
                r.Group,
                r.Point,
                r.Service,
                r.Method))
            .ToList();

        return Outcome<AccessReport>.Ok(new AccessReport(start, end, totals, byDay, byGroup, details));
    }

    // The file has cédulas of minors, so every download is recorded (without any personal data).
    public async Task<Outcome<ReportFile>> ExportAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var institutionId = Require();

        var built = await BuildAsync(from, to, ct);

        if (built.Error is { } error)
        {
            return Outcome<ReportFile>.Fail(error);
        }

        var report = built.Value!;
        var bytes = AccessReportExcel.Build(report);

        db.AuditLogs.Add(new AuditLog
        {
            Action = "AccessReport.exported",
            EntityType = "AccessReport",
            InstitutionId = institutionId,
            UserId = currentUser.UserId,
            IpAddress = currentUser.IpAddress,
            OccurredAt = time.GetUtcNow(),
            Metadata = JsonSerializer.Serialize(new
            {
                from = report.From.ToString("yyyy-MM-dd"),
                to = report.To.ToString("yyyy-MM-dd"),
                rows = report.Totals.Total
            })
        });

        await db.SaveChangesAsync(ct);

        var fileName = $"comedor-{report.From:yyyy-MM-dd}-{report.To:yyyy-MM-dd}.xlsx";

        return Outcome<ReportFile>.Ok(new ReportFile(bytes, fileName));
    }

    private async Task<TimeZoneInfo> ZoneAsync(CancellationToken ct)
    {
        var zoneId = await db.Institutions.Select(i => i.TimeZone).FirstOrDefaultAsync(ct);

        return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(zoneId) ? "UTC" : zoneId);
    }

    private Guid Require() =>
        tenant.InstitutionId ?? throw new TenantViolationException("Only institution users can see access reports.");
}
