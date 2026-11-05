using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Infrastructure.Access;

namespace VarinsEdu.Api.Errors;

public static class UserProblemExtensions
{
    // Maps the stable error codes of the account services to HTTP statuses.
    public static ObjectResult UserProblem(this ControllerBase controller, string code)
    {
        var status = code switch
        {
            "user_not_found" => StatusCodes.Status404NotFound,
            "username_taken" or "last_admin" => StatusCodes.Status409Conflict,
            "cannot_modify_self" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };

        return controller.ApiProblem(status, code, "The request could not be completed.");
    }

    public static IActionResult ToUserResult<T>(
        this ControllerBase controller,
        Outcome<T> outcome,
        int status = StatusCodes.Status200OK) =>
        outcome.Error is { } code
            ? controller.UserProblem(code)
            : controller.StatusCode(status, outcome.Value);

    public static IActionResult ToNoContent(this ControllerBase controller, Outcome<bool> outcome) =>
        outcome.Error is { } code ? controller.UserProblem(code) : controller.NoContent();
}
