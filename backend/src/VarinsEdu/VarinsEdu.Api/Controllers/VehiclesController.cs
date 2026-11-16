using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Api.Errors;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Vehicles;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/vehicles")]
public class VehiclesController(VehicleService vehicles) : ControllerBase
{
    // status: pending, approved, rejected, suspended or withdrawn. Without it, all of them.
    [HttpGet]
    [Authorize(Policy = PermissionKeys.VehiclesView)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default) =>
        Ok(await vehicles.ListAsync(status, search, skip, take, ct));

    [HttpPost]
    [Authorize(Policy = PermissionKeys.VehiclesManage)]
    public async Task<IActionResult> Register([FromBody] StaffVehicleInput input, CancellationToken ct) =>
        this.ToVehicleResult(await vehicles.RegisterForStudentAsync(input, ct), StatusCodes.Status201Created);

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = PermissionKeys.VehiclesManage)]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] StatusInput input, CancellationToken ct) =>
        this.ToVehicleNoContent(await vehicles.SetStatusAsync(id, input, ct));
}
