using iFakeLocation.Services.Location;
using iFakeLocation.Services.RouteSimulation;

namespace iFakeLocation.Tests.Services.RouteSimulation;

public class RouteMathTests {
    [Fact]
    public void HaversineMeters_KnownDistance_IsApproximatelyCorrect() {
        // Paris (48.8566, 2.3522) to London (51.5074, -0.1278) is ~344 km.
        var paris = new PointLatLng(48.8566, 2.3522);
        var london = new PointLatLng(51.5074, -0.1278);

        var distance = RouteMath.HaversineMeters(paris, london);

        Assert.InRange(distance, 340_000, 348_000);
    }

    [Fact]
    public void HaversineMeters_SamePoint_IsZero() {
        var point = new PointLatLng(10, 20);
        Assert.Equal(0, RouteMath.HaversineMeters(point, point), precision: 6);
    }

    [Fact]
    public void BuildCumulativeDistances_StraightLine_AccumulatesCorrectly() {
        var points = new[] {
            new PointLatLng(0, 0),
            new PointLatLng(0, 1),
            new PointLatLng(0, 2),
        };

        var (cumulative, total) = RouteMath.BuildCumulativeDistances(points);

        Assert.Equal(0, cumulative[0]);
        Assert.True(cumulative[1] > 0);
        Assert.Equal(total, cumulative[^1]);
        // Two equal-length segments -> midpoint cumulative is half the total.
        Assert.Equal(total / 2, cumulative[1], precision: 3);
    }

    [Fact]
    public void Interpolate_AtStart_ReturnsFirstPoint() {
        var points = new[] { new PointLatLng(0, 0), new PointLatLng(0, 2) };
        var (cumulative, total) = RouteMath.BuildCumulativeDistances(points);

        var result = RouteMath.Interpolate(points, cumulative, total, 0);

        Assert.Equal(points[0].Lat, result.Lat, precision: 6);
        Assert.Equal(points[0].Lng, result.Lng, precision: 6);
    }

    [Fact]
    public void Interpolate_AtEnd_ReturnsLastPoint() {
        var points = new[] { new PointLatLng(0, 0), new PointLatLng(0, 2) };
        var (cumulative, total) = RouteMath.BuildCumulativeDistances(points);

        var result = RouteMath.Interpolate(points, cumulative, total, total);

        Assert.Equal(points[^1].Lat, result.Lat, precision: 6);
        Assert.Equal(points[^1].Lng, result.Lng, precision: 6);
    }

    [Fact]
    public void Interpolate_AtMidpoint_ReturnsMidpoint() {
        var points = new[] { new PointLatLng(0, 0), new PointLatLng(0, 2) };
        var (cumulative, total) = RouteMath.BuildCumulativeDistances(points);

        var result = RouteMath.Interpolate(points, cumulative, total, total / 2);

        Assert.Equal(1.0, result.Lng, precision: 3);
    }

    [Fact]
    public void Interpolate_BeyondTotalDistance_ClampsToLastPoint() {
        var points = new[] { new PointLatLng(0, 0), new PointLatLng(0, 2) };
        var (cumulative, total) = RouteMath.BuildCumulativeDistances(points);

        var result = RouteMath.Interpolate(points, cumulative, total, total * 10);

        Assert.Equal(points[^1].Lat, result.Lat, precision: 6);
        Assert.Equal(points[^1].Lng, result.Lng, precision: 6);
    }

    [Fact]
    public void Interpolate_NegativeDistance_ClampsToFirstPoint() {
        var points = new[] { new PointLatLng(0, 0), new PointLatLng(0, 2) };
        var (cumulative, total) = RouteMath.BuildCumulativeDistances(points);

        var result = RouteMath.Interpolate(points, cumulative, total, -100);

        Assert.Equal(points[0].Lat, result.Lat, precision: 6);
        Assert.Equal(points[0].Lng, result.Lng, precision: 6);
    }

    [Fact]
    public void Interpolate_DuplicateConsecutivePoints_DoesNotThrow() {
        var points = new[] { new PointLatLng(0, 0), new PointLatLng(0, 0), new PointLatLng(0, 2) };
        var (cumulative, total) = RouteMath.BuildCumulativeDistances(points);

        var result = RouteMath.Interpolate(points, cumulative, total, 0);

        Assert.Equal(0, result.Lat, precision: 6);
        Assert.Equal(0, result.Lng, precision: 6);
    }

    [Fact]
    public void Interpolate_SinglePoint_ReturnsThatPoint() {
        var points = new[] { new PointLatLng(5, 5) };
        var (cumulative, total) = RouteMath.BuildCumulativeDistances(points);

        var result = RouteMath.Interpolate(points, cumulative, total, 0);

        Assert.Equal(5, result.Lat, precision: 6);
        Assert.Equal(5, result.Lng, precision: 6);
    }
}
