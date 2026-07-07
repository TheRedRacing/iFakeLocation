using iFakeLocation.Services.Devices;
using iFakeLocation.Services.Location;

namespace iFakeLocation.Services.RouteSimulation;

public interface IRouteSimulationService {
    /// <summary>
    /// Starts a new route simulation for a device: performs the one-time readiness check, then
    /// begins periodically pushing interpolated locations along the route. Throws
    /// <see cref="RouteSimulationAlreadyRunningException"/> if one is already active for this
    /// device.
    /// </summary>
    Task<RouteTickResult> StartAsync(DeviceRecord device, IReadOnlyList<PointLatLng> points, double speedKmh, bool loop,
        CancellationToken cancellationToken = default);

    RouteTickResult Pause(string udid);
    RouteTickResult Resume(string udid);
    void Stop(string udid);
    RouteTickResult GetStatus(string udid);
}
