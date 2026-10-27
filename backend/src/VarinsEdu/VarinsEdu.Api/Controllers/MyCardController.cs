using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Api.Errors;
using VarinsEdu.Infrastructure.Students;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/me/card")]
[Authorize]
public class MyCardController(StudentCardService cards) : ControllerBase
{
    // Any signed-in user may ask, but only an active student gets a card, and only their own.
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await cards.GetAsync(ct);

        if (result.Error is not null)
        {
            return this.ApiProblem(StatusCodes.Status403Forbidden, "card_unavailable", "No digital card is available for this account.");
        }

        // The token is a short-lived credential: nobody may keep a copy.
        Response.Headers.CacheControl = "no-store";

        var card = result.Card!;

        return Ok(new
        {
            card.FirstName,
            card.LastName,
            card.Group,
            token = card.Token.Token,
            expiresAt = card.Token.ExpiresAt
        });
    }
}
