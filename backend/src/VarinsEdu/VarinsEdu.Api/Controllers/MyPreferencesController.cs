using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Api.Errors;
using VarinsEdu.Infrastructure.Preferences;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/me/preferences")]
[Authorize]
public class MyPreferencesController(PreferenceService preferences) : ControllerBase
{
    // Any signed-in user. It says how the person wants to see the screen: nobody may keep a copy.
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        return Ok(await preferences.GetMineAsync(ct));
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] DisplayPreferences input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        return this.ToPreferenceResult(await preferences.SetMineAsync(input, ct));
    }
}
