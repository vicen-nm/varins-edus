using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Infrastructure.Access;

namespace VarinsEdu.Api.Errors;

public static class ParkingProblemExtensions
{
    // Maps the stable error codes of parking and card scanning to HTTP statuses.
    public static ObjectResult ParkingProblem(this ControllerBase controller, string code)
    {
        var status = code switch
        {
            "vehicle_not_found" => StatusCodes.Status404NotFound,
            "card_already_used" or "parking_conflict" => StatusCodes.Status409Conflict,
            "card_student_unavailable" or "no_approved_vehicle" or "vehicle_not_approved" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };

        return controller.ApiProblem(status, code, "The request could not be completed.");
    }

    public static IActionResult ToParkingResult<T>(
        this ControllerBase controller,
        Outcome<T> outcome,
        int status = StatusCodes.Status200OK) =>
        outcome.Error is { } code
            ? controller.ParkingProblem(code)
            : controller.StatusCode(status, outcome.Value);
}
