namespace VarinsEdu.Api.Setup;

public static class CorsSetup
{
    public const string PolicyName = "Frontend";

    // Allowed origins come from configuration (Cors:AllowedOrigins). An empty list allows nothing.
    public static IServiceCollection AddFrontendCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = (configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .Select(o => o.Trim().TrimEnd('/'))
            .Where(o => o.Length > 0)
            .ToArray();

        if (origins.Contains("*"))
        {
            throw new InvalidOperationException(
                "Cors:AllowedOrigins cannot contain '*'. List each allowed origin explicitly.");
        }

        services.AddCors(options => options.AddPolicy(PolicyName, policy => policy
            .WithOrigins(origins)
            .WithHeaders("Authorization", "Content-Type")
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")));

        return services;
    }
}
