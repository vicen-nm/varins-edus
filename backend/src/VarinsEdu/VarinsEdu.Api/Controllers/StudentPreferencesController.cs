using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Api.Errors;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Preferences;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/students/{personId:guid}/preferences")]
[Authorize(Policy = PermissionKeys.AccessibilityManage)]
public class StudentPreferencesController(PreferenceService preferences) : ControllerBase
{
    // For one student at a time: the preferences of several people are never listed.
    [HttpGet]
    public async Task<IActionResult> Get(Guid personId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        return this.ToPreferenceResult(await preferences.GetForStudentAsync(personId, ct));
    }

    [HttpPut]
    public async Task<IActionResult> Put(Guid personId, [FromBody] DisplayPreferences input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        return this.ToPreferenceResult(await preferences.SetForStudentAsync(personId, input, ct));
    }
}
