using Microsoft.AspNetCore.Mvc;

namespace VarinsEdu.Api.Errors;

public static class ControllerProblemExtensions
{
    // An error response in the standard ProblemDetails format, plus a stable machine-readable code.
    public static ObjectResult ApiProblem(this ControllerBase controller, int status, string code, string title)
    {
        var problem = controller.ProblemDetailsFactory.CreateProblemDetails(
            controller.HttpContext,
            statusCode: status,
            title: title);

        problem.Extensions["code"] = code;

        return new ObjectResult(problem) { StatusCode = status };
    }
}
