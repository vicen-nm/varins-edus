using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VarinsEdu.Api.Setup;
using Xunit;

namespace VarinsEdu.Tests;

public class CorsSetupTests
{
    private static ServiceCollection ServicesWith(params string[] origins)
    {
        var settings = origins
            .Select((origin, index) => new KeyValuePair<string, string?>($"Cors:AllowedOrigins:{index}", origin))
            .ToList();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFrontendCors(configuration);

        return services;
    }

    private static async Task<CorsPolicy> GetPolicyAsync(ServiceCollection services)
    {
        var provider = services.BuildServiceProvider().GetRequiredService<ICorsPolicyProvider>();
        var policy = await provider.GetPolicyAsync(new DefaultHttpContext(), CorsSetup.PolicyName);

        return Assert.IsType<CorsPolicy>(policy);
    }

    [Fact]
    public async Task Configured_origins_are_allowed_and_trailing_slashes_are_ignored()
    {
        var policy = await GetPolicyAsync(ServicesWith("http://localhost:5173/"));

        Assert.Contains("http://localhost:5173", policy.Origins);
        Assert.False(policy.AllowAnyOrigin);
        Assert.False(policy.AllowAnyHeader);
        Assert.Contains("Authorization", policy.Headers);
    }

    [Fact]
    public async Task No_configured_origins_allows_none()
    {
        var policy = await GetPolicyAsync(ServicesWith());

        Assert.Empty(policy.Origins);
        Assert.False(policy.AllowAnyOrigin);
    }

    [Fact]
    public void Wildcard_origin_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => ServicesWith("*"));
    }
}
