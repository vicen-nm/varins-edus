using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VarinsEdu.Api.Errors;
using VarinsEdu.Api.Setup;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Students;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/card-scans")]
[Authorize(Policy = PermissionKeys.CardsScan)]
public class CardScansController(CardScanService scans) : ControllerBase
{
    public sealed record ScanRequest(string? Token);

    // Identifies the student and uses the token up. The answer is about a minor: nobody may keep a copy.
    [HttpPost]
    [EnableRateLimiting(RateLimitSetup.ScanPolicy)]
    public async Task<IActionResult> Scan([FromBody] ScanRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        return this.ToResult(await scans.ScanAsync(request.Token, ct));
    }
}
