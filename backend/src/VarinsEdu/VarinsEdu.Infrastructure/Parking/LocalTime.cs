using Microsoft.EntityFrameworkCore;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Parking;

internal static class LocalTime
{
    // The clock on the wall at the institution (the tenant filter limits this to the caller's own).
    public static async Task<DateTime> NowAsync(AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var zoneId = await db.Institutions.Select(i => i.TimeZone).FirstOrDefaultAsync(ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(zoneId) ? "UTC" : zoneId);

        return TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime;
    }
}
