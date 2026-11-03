using VarinsEdu.Api.Setup;
using Xunit;

namespace VarinsEdu.Tests;

public class RateLimitPartitionTests
{
    [Fact]
    public void A_signed_in_user_gets_a_counter_of_their_own()
    {
        var a = RateLimitSetup.PartitionKey("user-a", "203.0.113.7");
        var b = RateLimitSetup.PartitionKey("user-b", "203.0.113.7");

        Assert.Equal("user:user-a", a);
        Assert.NotEqual(a, b);
        Assert.Equal(a, RateLimitSetup.PartitionKey("user-a", "198.51.100.1"));
    }

    [Fact]
    public void Without_a_session_the_ip_address_is_used()
    {
        Assert.Equal("ip:203.0.113.7", RateLimitSetup.PartitionKey(null, "203.0.113.7"));
        Assert.Equal("ip:203.0.113.7", RateLimitSetup.PartitionKey("", "203.0.113.7"));
    }

    [Fact]
    public void Without_session_or_ip_a_fixed_key_is_used()
    {
        Assert.Equal("ip:unknown", RateLimitSetup.PartitionKey(null, null));
    }
}
