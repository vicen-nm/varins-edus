using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VarinsEdu.Api.Errors;
using VarinsEdu.Api.Setup;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Parking;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/parking")]
public class ParkingController(ParkingService parking) : ControllerBase
{
    public sealed record PreviewRequest(string? Token);

    public sealed record RegisterRequest(string? Token, Guid VehicleId);

    // Identifies the student and lists their approved vehicles. The card code is NOT used up yet.
    // The answer is about a minor: nobody may keep a copy.
    [HttpPost("preview")]
    [Authorize(Policy = PermissionKeys.ParkingScan)]
    [EnableRateLimiting(RateLimitSetup.ScanPolicy)]
    public async Task<IActionResult> Preview([FromBody] PreviewRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        return this.ToParkingResult(await parking.PreviewAsync(request.Token, ct));
    }

    // Registers the entry (or the exit, if the vehicle is inside) and uses the card code up.
    [HttpPost("events")]
    [Authorize(Policy = PermissionKeys.ParkingScan)]
    [EnableRateLimiting(RateLimitSetup.ScanPolicy)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        return this.ToParkingResult(await parking.RegisterAsync(request.Token, request.VehicleId, ct));
    }

    [HttpGet("events")]
    [Authorize(Policy = PermissionKeys.ParkingView)]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? date,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default) =>
        Ok(await parking.ListEventsAsync(date, skip, take, ct));
}
