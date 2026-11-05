using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Api.Errors;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Accounts;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = PermissionKeys.UsersManage)]
public class UsersController(UserManagementService users) : ControllerBase
{
    // kind: staff (default), student or all.
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? kind,
        [FromQuery] string? search,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default) =>
        Ok(await users.ListAsync(kind, search, skip, take, ct));

    [HttpGet("roles")]
    public async Task<IActionResult> Roles(CancellationToken ct) => Ok(await users.RolesAsync(ct));

    // The code is returned ONCE: only its fingerprint is stored.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStaffInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        return this.ToUserResult(await users.CreateStaffAsync(input, ct), StatusCodes.Status201Created);
    }

    [HttpPut("{id:guid}/roles")]
    public async Task<IActionResult> SetRoles(Guid id, [FromBody] RolesInput input, CancellationToken ct) =>
        this.ToNoContent(await users.SetRolesAsync(id, input.RoleKeys, ct));

    [HttpPut("{id:guid}/active")]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] ActiveInput input, CancellationToken ct) =>
        this.ToNoContent(await users.SetActiveAsync(id, input.IsActive, ct));

    [HttpPost("{id:guid}/reset-code")]
    public async Task<IActionResult> ResetCode(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        return this.ToUserResult(await users.IssueResetCodeAsync(id, ct));
    }
}
