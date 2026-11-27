using Microsoft.AspNetCore.HttpOverrides;

namespace VarinsEdu.Api.Setup;

public static class ForwardedHeadersSetup
{
    public const string NetworksKey = "ForwardedHeaders:KnownNetworks";

    public static string[] ReadNetworks(IConfiguration configuration) =>
        configuration.GetSection(NetworksKey).Get<string[]>() ?? [];

    // Only registers anything when trusted networks are configured (production behind Caddy).
    // Without them the headers are ignored, so nobody can fake their IP.
    public static IServiceCollection AddProxyHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var networks = ReadNetworks(configuration);
        if (networks.Length == 0)
        {
            return services;
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        return services;
    }
}
