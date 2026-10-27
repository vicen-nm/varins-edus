using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Infrastructure.Access;

namespace VarinsEdu.Api.Errors;

public static class AccessProblemExtensions
{
    // Maps the stable error codes of the access services to HTTP statuses.
    public static ObjectResult AccessProblem(this ControllerBase controller, string code)
    {
        var status = code switch
        {
            "access_point_not_found" or "access_service_not_found" or "student_not_found"
                => StatusCodes.Status404NotFound,
            "already_registered" or "access_point_name_taken" or "access_service_name_taken"
                => StatusCodes.Status409Conflict,
            "student_unavailable" or "too_far" or "location_inaccurate"
                => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };

        return controller.ApiProblem(status, code, "The access request could not be completed.");
    }

    public static IActionResult ToResult<T>(
        this ControllerBase controller,
        Outcome<T> outcome,
        int status = StatusCodes.Status200OK) =>
        outcome.Error is { } code
            ? controller.AccessProblem(code)
            : controller.StatusCode(status, outcome.Value);
}
