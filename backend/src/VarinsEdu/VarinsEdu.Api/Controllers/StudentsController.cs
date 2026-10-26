using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Api.Errors;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Students;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/students")]
public class StudentsController(StudentManagementService students) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = PermissionKeys.StudentsView)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] Guid? groupId,
        [FromQuery] bool? hasAccount,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default) =>
        Ok(await students.ListAsync(search, groupId, hasAccount, skip, take, ct));

    [HttpGet("groups")]
    [Authorize(Policy = PermissionKeys.StudentsView)]
    public async Task<IActionResult> Groups(CancellationToken ct) =>
        Ok(await students.GroupsAsync(ct));

    // The new code is returned ONCE: only its fingerprint is stored.
    [HttpPost("{personId:guid}/activation-code")]
    [Authorize(Policy = PermissionKeys.StudentsImport)]
    public async Task<IActionResult> RegenerateCode(Guid personId, CancellationToken ct)
    {
        var result = await students.RegenerateCodeAsync(personId, ct);

        if (result.Error == CodeRegenerationError.StudentNotFound)
        {
            return this.ApiProblem(StatusCodes.Status404NotFound, "student_not_found", "Student not found.");
        }

        if (result.Error == CodeRegenerationError.HasAccount)
        {
            return this.ApiProblem(StatusCodes.Status409Conflict, "student_has_account", "The student already has an account.");
        }

        Response.Headers.CacheControl = "no-store";

        return Ok(new { code = result.Code, expiresAt = result.ExpiresAt });
    }
}
