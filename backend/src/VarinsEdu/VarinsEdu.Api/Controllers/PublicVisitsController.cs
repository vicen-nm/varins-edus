using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VarinsEdu.Api.Errors;
using VarinsEdu.Api.Setup;
using VarinsEdu.Infrastructure.Visits;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/public/visits")]
public class PublicVisitsController(PublicVisitService visits) : ControllerBase
{
    // The visitor's form. Nobody is signed in. The body is small on purpose: a signature is a few KB.
    // The answer is the same for a new request and for a repeated one.
    [HttpPost("{slug}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSetup.PublicVisitPolicy)]
    [RequestSizeLimit(128 * 1024)]
    public async Task<IActionResult> Submit(string slug, [FromBody] VisitInput input, CancellationToken ct) =>
        this.ToVisitResult(await visits.SubmitAsync(slug, input, ct), StatusCodes.Status201Created);
}
