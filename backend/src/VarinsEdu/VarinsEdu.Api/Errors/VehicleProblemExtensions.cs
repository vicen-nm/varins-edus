using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Infrastructure.Access;

namespace VarinsEdu.Api.Errors;

public static class VehicleProblemExtensions
{
    // Maps the stable error codes of the vehicle service to HTTP statuses.
    public static ObjectResult VehicleProblem(this ControllerBase controller, string code)
    {
        var status = code switch
        {
            "vehicle_not_found" or "student_not_found" => StatusCodes.Status404NotFound,
            "plate_taken" or "vehicle_limit_reached" or "invalid_status_change" => StatusCodes.Status409Conflict,
            "vehicle_student_unavailable" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };

        return controller.ApiProblem(status, code, "The request could not be completed.");
    }

    public static IActionResult ToVehicleResult<T>(
        this ControllerBase controller,
        Outcome<T> outcome,
        int status = StatusCodes.Status200OK) =>
        outcome.Error is { } code
            ? controller.VehicleProblem(code)
            : controller.StatusCode(status, outcome.Value);

    public static IActionResult ToVehicleNoContent(this ControllerBase controller, Outcome<bool> outcome) =>
        outcome.Error is { } code ? controller.VehicleProblem(code) : controller.NoContent();
}
