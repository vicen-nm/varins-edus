using Microsoft.EntityFrameworkCore;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Parking;

internal static class LocalTime
{
    // The institution's time zone (the tenant filter limits this to the caller's own). UTC if it has none.
    public static async Task<TimeZoneInfo> ZoneAsync(AppDbContext db, CancellationToken ct)
    {
        var zoneId = await db.Institutions.Select(i => i.TimeZone).FirstOrDefaultAsync(ct);

        return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(zoneId) ? "UTC" : zoneId);
    }

    // The clock on the wall at the institution.
    public static async Task<DateTime> NowAsync(AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var zone = await ZoneAsync(db, ct);

        return TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime;
    }

    // The instant (UTC) at which a local day starts.
    public static DateTimeOffset StartOfDay(TimeZoneInfo zone, DateOnly day)
    {
        var local = DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);

        // A zone that skips midnight (a daylight saving change) starts its day an hour later.
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
}
