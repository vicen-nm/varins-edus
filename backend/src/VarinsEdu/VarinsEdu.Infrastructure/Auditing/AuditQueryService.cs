using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Infrastructure.Parking;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Auditing;

// Search: text in the action or the entity type. From and To are days in the institution's time zone, both included.
public sealed record AuditFilter(string? EntityType, string? Search, DateOnly? From, DateOnly? To);

// The metadata is already redacted: personal data never reaches the log.
public sealed record AuditItem(
    long Id,
    string Action,
    string EntityType,
    string? EntityId,
    string? Username,
    DateTimeOffset OccurredAt,
    string? IpAddress,
    JsonElement? Metadata);

public sealed record AuditPage(int Total, IReadOnlyList<AuditItem> Items);

// Each institution sees only its own history (the tenant filter applies); the platform sees everything.
public class AuditQueryService(AppDbContext db, ICurrentTenant tenant)
{
    public async Task<AuditPage> SearchAsync(AuditFilter filter, int skip, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(skip, 0);

        var query = db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            var type = filter.EntityType.Trim();
            query = query.Where(a => a.EntityType == type);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            query = query.Where(a => a.Action.ToLower().Contains(term) || a.EntityType.ToLower().Contains(term));
        }

        if (filter.From is not null || filter.To is not null)
        {
            // The platform has no institution: its days are UTC days.
            var zone = tenant.InstitutionId is null ? TimeZoneInfo.Utc : await LocalTime.ZoneAsync(db, ct);

            if (filter.From is { } from)
            {
                var start = LocalTime.StartOfDay(zone, from);
                query = query.Where(a => a.OccurredAt >= start);
            }

            if (filter.To is { } to)
            {
                var end = LocalTime.StartOfDay(zone, to.AddDays(1));
                query = query.Where(a => a.OccurredAt < end);
            }
        }

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Skip(skip)
            .Take(take)
            .Select(a => new
            {
                a.Id,
                a.Action,
                a.EntityType,
                a.EntityId,
                a.OccurredAt,
                a.IpAddress,
                a.Metadata,
                Username = db.Users.Where(u => u.Id == a.UserId).Select(u => u.Username).FirstOrDefault()
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new AuditItem(
                r.Id,
                r.Action,
                r.EntityType,
                r.EntityId,
                r.Username,
                r.OccurredAt,
                r.IpAddress,
                r.Metadata is null ? null : JsonSerializer.Deserialize<JsonElement>(r.Metadata)))
            .ToList();

        return new AuditPage(total, items);
    }
}
