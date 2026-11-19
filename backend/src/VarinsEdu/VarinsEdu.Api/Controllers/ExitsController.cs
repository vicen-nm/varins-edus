using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VarinsEdu.Api.Errors;
using VarinsEdu.Api.Setup;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Exits;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/exits")]
[Authorize(Policy = PermissionKeys.ExitsScan)]
public class ExitsController(ExitPermitService exits) : ControllerBase
{
    public sealed record PreviewRequest(string? Token);

    public sealed record RegisterRequest(string? Token, Guid PermitId);

    // Identifies the student and lists the permits valid now. The card code is NOT used up yet.
    // The answer is about a minor: nobody may keep a copy.
    [HttpPost("preview")]
    [EnableRateLimiting(RateLimitSetup.ScanPolicy)]
    public async Task<IActionResult> Preview([FromBody] PreviewRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        return this.ToExitResult(await exits.PreviewAsync(request.Token, ct));
    }

    // Registers the exit: the permit is used up and so is the card code.
    [HttpPost("register")]
    [EnableRateLimiting(RateLimitSetup.ScanPolicy)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        return this.ToExitResult(await exits.RegisterAsync(request.Token, request.PermitId, ct));
    }
}
