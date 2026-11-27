using Microsoft.Extensions.Configuration;
using VarinsEdu.Api.Setup;

namespace VarinsEdu.Tests;

public class ProductionConfigCheckTests
{
    private static IConfiguration Config(string? connection, params string[] networks)
    {
        var values = new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection };
        for (var i = 0; i < networks.Length; i++)
        {
            values[$"ForwardedHeaders:KnownNetworks:{i}"] = networks[i];
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Complete_configuration_has_no_problems()
    {
        Assert.Empty(ProductionConfigCheck.FindProblems(Config("Host=db", "172.28.0.0/16")));
    }

    [Fact]
    public void Missing_connection_string_is_reported()
    {
        var problems = ProductionConfigCheck.FindProblems(Config(null, "172.28.0.0/16"));

        Assert.Single(problems);
        Assert.Contains("ConnectionStrings:Default", problems[0]);
    }

    [Fact]
    public void Missing_networks_are_reported()
    {
        var problems = ProductionConfigCheck.FindProblems(Config("Host=db"));

        Assert.Single(problems);
        Assert.Contains("ForwardedHeaders:KnownNetworks", problems[0]);
    }

    [Fact]
    public void Invalid_network_is_reported()
    {
        var problems = ProductionConfigCheck.FindProblems(Config("Host=db", "not-a-network"));

        Assert.Single(problems);
        Assert.Contains("invalid entry", problems[0]);
    }
}
