using VarinsEdu.Infrastructure.Access;
using Xunit;

namespace VarinsEdu.Tests;

public class GeoDistanceTests
{
    [Fact]
    public void The_distance_to_the_same_point_is_zero()
    {
        Assert.Equal(0, GeoDistance.Meters(9.9333, -84.0833, 9.9333, -84.0833), 3);
    }

    [Fact]
    public void One_thousandth_of_a_degree_of_latitude_is_about_111_meters()
    {
        var meters = GeoDistance.Meters(9.9333, -84.0833, 9.9343, -84.0833);

        Assert.InRange(meters, 110, 112);
    }
}
