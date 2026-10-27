using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Api.Errors;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Access;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/access-points")]
public class AccessPointsController(AccessPointService points) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = PermissionKeys.AccessScan)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await points.ListAsync(ct));

    [HttpPost]
    [Authorize(Policy = PermissionKeys.AccessManage)]
    public async Task<IActionResult> Create([FromBody] AccessPointInput input, CancellationToken ct) =>
        this.ToResult(await points.CreateAsync(input, ct), StatusCodes.Status201Created);

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionKeys.AccessManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] AccessPointInput input, CancellationToken ct) =>
        this.ToResult(await points.UpdateAsync(id, input, ct));

    [HttpGet("{id:guid}/services")]
    [Authorize(Policy = PermissionKeys.AccessScan)]
    public async Task<IActionResult> ListServices(Guid id, CancellationToken ct) =>
        this.ToResult(await points.ListServicesAsync(id, ct));

    [HttpPost("{id:guid}/services")]
    [Authorize(Policy = PermissionKeys.AccessManage)]
    public async Task<IActionResult> CreateService(Guid id, [FromBody] AccessServiceInput input, CancellationToken ct) =>
        this.ToResult(await points.CreateServiceAsync(id, input, ct), StatusCodes.Status201Created);

    [HttpPut("{id:guid}/services/{serviceId:guid}")]
    [Authorize(Policy = PermissionKeys.AccessManage)]
    public async Task<IActionResult> UpdateService(
        Guid id,
        Guid serviceId,
        [FromBody] AccessServiceInput input,
        CancellationToken ct) =>
        this.ToResult(await points.UpdateServiceAsync(id, serviceId, input, ct));

    // The point's screen asks for this every few seconds. The token is a short-lived credential.
    [HttpGet("{id:guid}/qr")]
    [Authorize(Policy = PermissionKeys.AccessScan)]
    public async Task<IActionResult> Qr(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        return this.ToResult(await points.IssueQrAsync(id, ct));
    }
}
