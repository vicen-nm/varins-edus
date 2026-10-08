using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Api.Security;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Institutions;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Api.Controllers;

// Both policies must pass: a platform-level token AND the institutions.manage permission.
[ApiController]
[Route("api/platform/institutions")]
[Authorize(Policy = PolicyNames.PlatformOnly)]
[Authorize(Policy = PermissionKeys.InstitutionsManage)]
public class PlatformInstitutionsController(InstitutionService institutionService, AppDbContext db)
    : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var institutions = await db.Institutions
            .OrderBy(i => i.Name)
            .Select(i => new InstitutionSummary(i.Id, i.Name, i.Slug, i.IsActive, i.CreatedAt))
            .ToListAsync(ct);

        return Ok(institutions);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateInstitutionRequest request, CancellationToken ct)
    {
        var result = await institutionService.CreateAsync(
            new CreateInstitutionCommand(request.Name, request.Slug, request.AdminUsername, request.AdminPassword),
            ct);

        if (result.Succeeded)
        {
            return StatusCode(
                StatusCodes.Status201Created,
                new { institutionId = result.InstitutionId, adminUserId = result.AdminUserId });
        }

        var body = new { error = result.Error.ToString() };

        IActionResult response = result.Error switch
        {
            InstitutionCreateError.SlugTaken or InstitutionCreateError.UsernameTaken => Conflict(body),
            _ => BadRequest(body)
        };

        return response;
    }
}

public record CreateInstitutionRequest(string Name, string Slug, string AdminUsername, string AdminPassword);

public record InstitutionSummary(Guid Id, string Name, string Slug, bool IsActive, DateTimeOffset CreatedAt);
