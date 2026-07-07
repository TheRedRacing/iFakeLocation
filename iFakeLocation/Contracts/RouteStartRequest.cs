namespace iFakeLocation.Contracts;

public sealed record RouteStartRequest(IReadOnlyList<RoutePointDto> Points, double SpeedKmh, bool Loop);
