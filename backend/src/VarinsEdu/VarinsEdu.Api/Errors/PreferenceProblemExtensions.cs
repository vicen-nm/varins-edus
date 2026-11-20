using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Infrastructure.Access;

namespace VarinsEdu.Api.Errors;

public static class PreferenceProblemExtensions
{
    // Maps the stable error codes of the preference service to HTTP statuses.
    public static IActionResult ToPreferenceResult<T>(this ControllerBase controller, Outcome<T> outcome)
    {
        if (outcome.Error is not { } code)
        {
            return controller.Ok(outcome.Value);
        }

        var status = code == "student_account_not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;

        return controller.ApiProblem(status, code, "The request could not be completed.");
    }
}
