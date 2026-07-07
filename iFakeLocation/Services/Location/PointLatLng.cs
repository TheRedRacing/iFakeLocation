namespace iFakeLocation.Services.Location;

public readonly struct PointLatLng(double lat, double lng) {
    public double Lat { get; } = lat;
    public double Lng { get; } = lng;
}
