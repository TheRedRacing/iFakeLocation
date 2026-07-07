using iFakeLocation.Services.Location;

namespace iFakeLocation.Services.RouteSimulation;

/// <summary>Pure geometry helpers for interpolating a position along a road-following polyline.</summary>
public static class RouteMath {
    private const double EarthRadiusMeters = 6371000.0;

    public static double HaversineMeters(PointLatLng a, PointLatLng b) {
        var lat1 = DegreesToRadians(a.Lat);
        var lat2 = DegreesToRadians(b.Lat);
        var deltaLat = DegreesToRadians(b.Lat - a.Lat);
        var deltaLng = DegreesToRadians(b.Lng - a.Lng);

        var h = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Sin(deltaLng / 2) * Math.Sin(deltaLng / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
        return EarthRadiusMeters * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    /// <summary>
    /// Cumulative distance (meters) from the first point up to and including each point, plus
    /// the total route length. cumulative[0] is always 0; cumulative[^1] equals the total.
    /// </summary>
    public static (double[] Cumulative, double Total) BuildCumulativeDistances(IReadOnlyList<PointLatLng> points) {
        var cumulative = new double[points.Count];
        double total = 0;
        for (var i = 1; i < points.Count; i++) {
            total += HaversineMeters(points[i - 1], points[i]);
            cumulative[i] = total;
        }
        return (cumulative, total);
    }

    /// <summary>
    /// Linearly interpolates the position at <paramref name="distanceTraveled"/> meters along
    /// the route. Clamps to the first/last point outside [0, total]. Uses a linear scan to find
    /// the containing segment (fine for the low hundreds of points a typical OSRM route has) and
    /// linear (not spherical) interpolation within that segment -- an accepted simplification for
    /// the short segment lengths road-network geometry produces.
    /// </summary>
    public static PointLatLng Interpolate(IReadOnlyList<PointLatLng> points, double[] cumulative, double total, double distanceTraveled) {
        if (points.Count == 1)
            return points[0];

        var d = Math.Clamp(distanceTraveled, 0, total);

        if (d <= 0) return points[0];
        if (d >= total) return points[^1];

        var segmentIndex = 0;
        for (var i = 1; i < cumulative.Length; i++) {
            if (cumulative[i] >= d) {
                segmentIndex = i - 1;
                break;
            }
        }

        var segmentStart = cumulative[segmentIndex];
        var segmentEnd = cumulative[segmentIndex + 1];
        var segmentLength = segmentEnd - segmentStart;

        var t = segmentLength <= 0 ? 0 : (d - segmentStart) / segmentLength;

        var a = points[segmentIndex];
        var b = points[segmentIndex + 1];
        return new PointLatLng(a.Lat + (b.Lat - a.Lat) * t, a.Lng + (b.Lng - a.Lng) * t);
    }
}
