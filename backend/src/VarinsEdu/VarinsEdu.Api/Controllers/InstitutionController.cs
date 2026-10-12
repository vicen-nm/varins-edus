using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Api.Errors;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Institutions;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/institution")]
[Authorize]
public class InstitutionController(AppDbContext db, ICurrentTenant tenant, BrandingService brandingService)
    : ControllerBase
{
    // The caller's own institution.
    [HttpGet]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        if (tenant.InstitutionId is null)
        {
            return this.ApiProblem(
                StatusCodes.Status400BadRequest,
                "platform_user_has_no_institution",
                "Platform users do not belong to an institution.");
        }

        // No Where() here on purpose: the tenant filter leaves only the caller's institution visible.
        // If the filter ever failed, Single would throw instead of silently showing someone else's data.
        var institution = await db.Institutions
            .Include(i => i.Settings)
            .Include(i => i.Modules)
            .SingleOrDefaultAsync(ct);

        if (institution is null)
        {
            return this.ApiProblem(StatusCodes.Status404NotFound, "institution_not_found", "Institution not found.");
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

    // Replaces the institution's display name and colors. Null or blank values go back to the default.
    [HttpPut("branding")]
    [Authorize(Policy = PermissionKeys.BrandingManage)]
    public async Task<IActionResult> UpdateBranding(UpdateBrandingRequest request, CancellationToken ct)
    {
        var result = await brandingService.UpdateAsync(
            new UpdateBrandingCommand(request.DisplayName, request.PrimaryColor, request.AccentColor),
            ct);

        if (result.Succeeded)
        {
            return NoContent();
        }

        var (code, title) = result.Error switch
        {
            BrandingError.InvalidColor => ("invalid_color", "Colors must use the #RRGGBB format."),
            BrandingError.InvalidDisplayName => (
                "invalid_display_name",
                "The display name must have between 3 and 200 characters."),
            _ => ("platform_user_has_no_institution", "Platform users do not belong to an institution.")
        };

        return this.ApiProblem(StatusCodes.Status400BadRequest, code, title);
    }
}

public record UpdateBrandingRequest(string? DisplayName, string? PrimaryColor, string? AccentColor);
