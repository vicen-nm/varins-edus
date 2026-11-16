using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VarinsEdu.Api.Errors;
using VarinsEdu.Api.Setup;
using VarinsEdu.Infrastructure.Vehicles;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/me/vehicles")]
[Authorize]
public class MyVehiclesController(VehicleService vehicles) : ControllerBase
{
    // Any signed-in user may ask, but only an active student has vehicles, and only their own.
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        this.ToVehicleResult(await vehicles.ListMineAsync(ct));

    [HttpPost]
    [EnableRateLimiting(RateLimitSetup.VehiclePolicy)]
    public async Task<IActionResult> Request([FromBody] VehicleInput input, CancellationToken ct) =>
        this.ToVehicleResult(await vehicles.RequestAsync(input, ct), StatusCodes.Status201Created);

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting(RateLimitSetup.VehiclePolicy)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct) =>
        this.ToVehicleNoContent(await vehicles.WithdrawAsync(id, ct));
}
