using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Infrastructure.Access;

namespace VarinsEdu.Api.Errors;

public static class ExitProblemExtensions
{
    // Maps the stable error codes of exit permits and card scanning to HTTP statuses.
    public static ObjectResult ExitProblem(this ControllerBase controller, string code)
    {
        var status = code switch
        {
            "exit_permit_not_found" or "student_not_found" => StatusCodes.Status404NotFound,
            "exit_permit_unavailable" or "exit_permit_not_cancellable" or "card_already_used"
                => StatusCodes.Status409Conflict,
            "card_student_unavailable" or "no_valid_permit" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };

        return controller.ApiProblem(status, code, "The request could not be completed.");
    }

    public static IActionResult ToExitResult<T>(
        this ControllerBase controller,
        Outcome<T> outcome,
        int status = StatusCodes.Status200OK) =>
        outcome.Error is { } code
            ? controller.ExitProblem(code)
            : controller.StatusCode(status, outcome.Value);

    public static IActionResult ToExitNoContent(this ControllerBase controller, Outcome<bool> outcome) =>
        outcome.Error is { } code ? controller.ExitProblem(code) : controller.NoContent();
}
