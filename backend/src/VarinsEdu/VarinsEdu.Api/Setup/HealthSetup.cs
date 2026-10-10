using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Api.Setup;

public static class HealthSetup
{
    public static IServiceCollection AddAppHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database");

        return services;
    }

    public static IEndpointRouteBuilder MapAppHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        // Liveness: the process is running. Checks nothing else.
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteReport
        });

        // Readiness: the app can do its job (the database answers).
        endpoints.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteReport
        });

        return endpoints;
    }

    // Only names and statuses are exposed, never exception details.
    private static Task WriteReport(HttpContext context, HealthReport report)
    {
        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() })
        };

        return context.Response.WriteAsJsonAsync(body);
    }
}
