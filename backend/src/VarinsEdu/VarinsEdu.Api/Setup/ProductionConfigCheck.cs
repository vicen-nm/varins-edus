namespace VarinsEdu.Api.Setup;

public static class ProductionConfigCheck
{
    // Returns what is wrong, never secret values. Jwt:Key is already checked in Program.cs.
    public static IReadOnlyList<string> FindProblems(IConfiguration configuration)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Default")))
        {
            problems.Add("ConnectionStrings:Default is missing.");
        }

        var networks = ForwardedHeadersSetup.ReadNetworks(configuration);
        if (networks.Length == 0)
        {
            problems.Add("ForwardedHeaders:KnownNetworks is missing: list the proxy network in CIDR form.");
        }

        foreach (var network in networks)
        {
            if (!System.Net.IPNetwork.TryParse(network, out _))
            {
                problems.Add($"ForwardedHeaders:KnownNetworks has an invalid entry: {network}");
            }
        }

        return problems;
    }
}
