using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Policy = PermissionKeys.AuditView)]
public class AuditController(AppDbContext db) : ControllerBase
{
    // Each institution sees only its own history (the tenant filter applies); the platform sees everything.
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? entityType,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);

        var query = db.AuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(a => a.EntityType == entityType);
        }

        var rows = await query
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Take(take)
            .ToListAsync(ct);

        return Ok(rows.Select(a => new
        {
            a.Id,
            a.InstitutionId,
            a.UserId,
            a.Action,
            a.EntityType,
            a.EntityId,
            a.OccurredAt,
            a.IpAddress,
            Metadata = a.Metadata is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(a.Metadata)
        }));
    }
}
