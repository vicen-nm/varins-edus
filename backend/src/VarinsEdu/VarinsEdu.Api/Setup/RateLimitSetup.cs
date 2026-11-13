using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace VarinsEdu.Api.Setup;

public static class RateLimitSetup
{
    public const string AnonymousAuthPolicy = "anonymous-auth";
    public const string CardPolicy = "card";
    public const string CheckInPolicy = "check-in";
    public const string RefreshPolicy = "refresh";
    public const string ScanPolicy = "scan";

    // Many students share the school's single public IP, so this only stops floods.
    // Guessing codes and passwords is stopped by per-code and per-account limits instead.
    private const int PermitsPerMinute = 60;

    // Per signed-in user: the card renews itself every 25 s, and nobody needs many check-in attempts.
    private const int CardPermitsPerMinute = 30;
    private const int CheckInPermitsPerMinute = 10;

    // Per IP, and high on purpose: a whole school behind one address renews its sessions every few minutes.
    private const int RefreshPermitsPerMinute = 600;

    // A gate guard at the busiest minute scans a few dozen cards: this only stops a script.
    private const int ScanPermitsPerMinute = 120;

    // Each person gets their own counter. Without a session the IP is used.
    public static string PartitionKey(string? userId, string? ipAddress) =>
        string.IsNullOrEmpty(userId) ? $"ip:{ipAddress ?? "unknown"}" : $"user:{userId}";

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(AnonymousAuthPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = PermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
            options.AddPolicy(RefreshPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = RefreshPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
            AddUserPolicy(options, CardPolicy, CardPermitsPerMinute);
            AddUserPolicy(options, CheckInPolicy, CheckInPermitsPerMinute);
            AddUserPolicy(options, ScanPolicy, ScanPermitsPerMinute);

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                }

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests. Try again in a moment."
                };
                problem.Extensions["code"] = "too_many_requests";

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                await context.HttpContext.Response.WriteAsJsonAsync(
                    problem,
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken: cancellationToken);
            };
        });

        return services;
    }



    // Needs the user to be known, so UseRateLimiter must run after UseAuthentication.
    private static void AddUserPolicy(RateLimiterOptions options, string name, int permitsPerMinute) =>
        options.AddPolicy(name, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(
                    httpContext.User.FindFirst("sub")?.Value,
                    httpContext.Connection.RemoteIpAddress?.ToString()),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
}
