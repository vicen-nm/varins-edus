using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Infrastructure.Access;

namespace VarinsEdu.Api.Errors;

public static class VisitProblemExtensions
{
    // Maps the stable error codes of the visit services to HTTP statuses.
    public static ObjectResult VisitProblem(this ControllerBase controller, string code)
    {
        var status = code switch
        {
            "visit_not_found" or "institution_not_found" => StatusCodes.Status404NotFound,
            "invalid_visit_status" => StatusCodes.Status409Conflict,
            "visit_limit_reached" => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status400BadRequest
        };

        return controller.ApiProblem(status, code, "The request could not be completed.");
    }

    public static IActionResult ToVisitResult<T>(
        this ControllerBase controller,
        Outcome<T> outcome,
        int status = StatusCodes.Status200OK) =>
        outcome.Error is { } code
            ? controller.VisitProblem(code)
            : controller.StatusCode(status, outcome.Value);

    public static IActionResult ToVisitNoContent(this ControllerBase controller, Outcome<bool> outcome) =>
        outcome.Error is { } code ? controller.VisitProblem(code) : controller.NoContent();
}
