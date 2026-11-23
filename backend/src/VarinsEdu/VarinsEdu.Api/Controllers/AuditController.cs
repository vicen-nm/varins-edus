using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Auditing;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Policy = PermissionKeys.AuditView)]
public class AuditController(AuditQueryService audit) : ControllerBase
{
    // Each institution sees only its own history; the platform sees everything. Newest first.
    // search: text in the action or the entity type. from and to: days (yyyy-MM-dd), both included.
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? entityType,
        [FromQuery] string? search,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default) =>
        Ok(await audit.SearchAsync(new AuditFilter(entityType, search, from, to), skip, take, ct));
}
