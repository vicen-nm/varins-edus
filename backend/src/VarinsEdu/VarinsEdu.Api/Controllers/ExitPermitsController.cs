using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Api.Errors;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Exits;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/exit-permits")]
[Authorize(Policy = PermissionKeys.ExitsManage)]
public class ExitPermitsController(ExitPermitService exits) : ControllerBase
{
    // Without a date: today and the following days. status: active, used, cancelled or expired.
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? date,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default) =>
        Ok(await exits.ListAsync(date, status, search, skip, take, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ExitPermitInput input, CancellationToken ct) =>
        this.ToExitResult(await exits.CreateAsync(input, ct), StatusCodes.Status201Created);

    [HttpPut("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) =>
        this.ToExitNoContent(await exits.CancelAsync(id, ct));
}
