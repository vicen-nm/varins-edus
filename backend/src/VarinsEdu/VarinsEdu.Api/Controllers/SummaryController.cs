using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Summary;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/summary")]
[Authorize]
public class SummaryController(SummaryService summary) : ControllerBase
{
    // Any signed-in user of an institution. Each section only appears if the caller may see that module.
    [HttpGet("today")]
    public async Task<IActionResult> Today(CancellationToken ct)
    {
        bool Has(string permission) => User.HasClaim(AppClaims.Permission, permission);

        var scope = new SummaryScope(
            Has(PermissionKeys.AccessView),
            Has(PermissionKeys.ParkingView),
            Has(PermissionKeys.VisitsView),
            Has(PermissionKeys.ExitsManage),
            Has(PermissionKeys.VehiclesView),
            Has(PermissionKeys.StudentsView),
            Has(PermissionKeys.AuditView));

        Response.Headers.CacheControl = "no-store";

        return Ok(await summary.GetAsync(scope, ct));
    }
}
