using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Domain.Exceptions;

namespace VarinsEdu.Api.Errors;

// Turns any unhandled exception into a standard error response.
// Internal details go to the logs, never to the client.
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        int status;
        string code;
        string title;

        if (exception is TenantViolationException)
        {
            status = StatusCodes.Status403Forbidden;
            code = "tenant_violation";
            title = "You are not allowed to access this data.";
            logger.LogWarning(exception, "Tenant violation blocked on {Path}", httpContext.Request.Path);
        }
        else
        {
            status = StatusCodes.Status500InternalServerError;
            code = "internal_error";
            title = "An unexpected error occurred.";
            logger.LogError(exception, "Unhandled exception on {Path}", httpContext.Request.Path);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Instance = httpContext.Request.Path.Value
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);

        return true;
    }
}
