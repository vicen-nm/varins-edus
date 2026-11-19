using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Api.Errors;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Visits;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/visits")]
public class VisitsController(VisitService visits) : ControllerBase
{
    // Without a date: today. status: pending, inside, exited, rejected, expired or no_exit.
    [HttpGet]
    [Authorize(Policy = PermissionKeys.VisitsView)]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? date,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default) =>
        Ok(await visits.ListAsync(date, status, search, skip, take, ct));

    [HttpPut("{id:guid}/admit")]
    [Authorize(Policy = PermissionKeys.VisitsManage)]
    public async Task<IActionResult> Admit(Guid id, CancellationToken ct) =>
        this.ToVisitNoContent(await visits.AdmitAsync(id, ct));

    [HttpPut("{id:guid}/reject")]
    [Authorize(Policy = PermissionKeys.VisitsManage)]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct) =>
        this.ToVisitNoContent(await visits.RejectAsync(id, ct));

    [HttpPut("{id:guid}/exit")]
    [Authorize(Policy = PermissionKeys.VisitsManage)]
    public async Task<IActionResult> Exit(Guid id, CancellationToken ct) =>
        this.ToVisitNoContent(await visits.ExitAsync(id, ct));

    // The signature is personal data: nobody may keep a copy.
    [HttpGet("{id:guid}/signature")]
    [Authorize(Policy = PermissionKeys.VisitsView)]
    public async Task<IActionResult> Signature(Guid id, CancellationToken ct)
    {
        var outcome = await visits.SignatureAsync(id, ct);

        if (outcome.Error is { } code)
        {
            return this.VisitProblem(code);
        }

        Response.Headers.CacheControl = "no-store";

        return File(outcome.Value!, "image/png");
    }
}
