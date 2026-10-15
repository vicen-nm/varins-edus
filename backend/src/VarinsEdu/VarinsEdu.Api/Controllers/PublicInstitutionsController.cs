using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Api.Errors;
using VarinsEdu.Api.Setup;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Api.Controllers;

// The only public view of an institution: just what the activation page needs to look like the school.
[ApiController]
[Route("api/public/institutions")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitSetup.AnonymousAuthPolicy)]
public class PublicInstitutionsController(AppDbContext db) : ControllerBase
{
    [HttpGet("{slug}")]
    public async Task<IActionResult> Get(string slug, CancellationToken ct)
    {
        var normalized = slug.Trim().ToLowerInvariant();

        // Nobody is logged in here, so the tenant filter is skipped on purpose.
        // Only these three harmless values are ever returned.
        var info = await db.Institutions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.Slug == normalized && i.IsActive)
            .Select(i => new
            {
                Name = i.Settings != null && i.Settings.DisplayName != null ? i.Settings.DisplayName : i.Name,
                PrimaryColor = i.Settings != null ? i.Settings.PrimaryColor : null,
                AccentColor = i.Settings != null ? i.Settings.AccentColor : null
            })
            .SingleOrDefaultAsync(ct);

        if (info is null)
        {
            return this.ApiProblem(StatusCodes.Status404NotFound, "institution_not_found", "Institution not found.");
        }

        return Ok(info);
    }
}
