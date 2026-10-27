using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Api.Errors;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Access;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/access")]
public class AccessController(AccessRegistrationService access) : ControllerBase
{
    public sealed record ManualRequest(Guid PointId, string? IdentificationNumber);

    // The student's phone sends the QR it scanned plus its location. Any signed-in user may try,
    // but only an active student gets registered, and only as herself.
    [HttpPost("checkin")]
    [Authorize]
    public async Task<IActionResult> CheckIn([FromBody] CheckInCommand command, CancellationToken ct) =>
        this.ToResult(await access.CheckInAsync(command, ct));

    [HttpPost("manual")]
    [Authorize(Policy = PermissionKeys.AccessScan)]
    public async Task<IActionResult> Manual([FromBody] ManualRequest request, CancellationToken ct) =>
        this.ToResult(await access.RegisterManuallyAsync(request.PointId, request.IdentificationNumber, ct));

    [HttpGet("events")]
    [Authorize(Policy = PermissionKeys.AccessView)]
    public async Task<IActionResult> Events(
        [FromQuery] Guid? pointId,
        [FromQuery] DateOnly? date,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default) =>
        Ok(await access.ListEventsAsync(pointId, date, skip, take, ct));
}
