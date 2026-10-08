using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/institution")]
[Authorize]
public class InstitutionController(AppDbContext db, ICurrentTenant tenant) : ControllerBase
{
    // The caller's own institution.
    [HttpGet]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        if (tenant.InstitutionId is null)
        {
            return BadRequest(new { error = "Platform users do not belong to an institution." });
        }

        // No Where() here on purpose: the tenant filter leaves only the caller's institution visible.
        // If the filter ever failed, Single would throw instead of silently showing someone else's data.
        var institution = await db.Institutions
            .Include(i => i.Settings)
            .Include(i => i.Modules)
            .SingleOrDefaultAsync(ct);

        if (institution is null)
        {
            return NotFound();
        }

        return Ok(new
        {
            institution.Id,
            institution.Name,
            institution.Slug,
            institution.TimeZone,
            institution.Locale,
            Settings = institution.Settings is null
                ? null
                : new
                {
                    institution.Settings.DisplayName,
                    institution.Settings.LogoUrl,
                    institution.Settings.PrimaryColor,
                    institution.Settings.AccentColor
                },
            Modules = institution.Modules
                .OrderBy(m => m.ModuleKey)
                .Select(m => new { m.ModuleKey, m.IsEnabled })
        });
    }
}
