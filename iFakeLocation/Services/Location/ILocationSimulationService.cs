using iFakeLocation.Services.Devices;

namespace iFakeLocation.Services.Location;

public interface ILocationSimulationService {
    /// <summary>
    /// Performs the (potentially expensive, native-mount-touching) one-time readiness check for
    /// a device: developer-mode-toggle visibility and developer-image mounting. Throws
    /// <see cref="DeveloperModeHiddenException"/> or <see cref="DeveloperImagesMissingException"/>
    /// if the device isn't ready yet. Call once before a burst of <see cref="PushLocationAsync"/>
    /// calls (e.g. once per manual set/stop request, or once at route-simulation start).
    /// </summary>
    Task EnsureReadyAsync(DeviceRecord device, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes a single location update (or, with <paramref name="target"/> null, a stop command)
    /// to an already-ready device. Cheap relative to <see cref="EnsureReadyAsync"/> -- safe to
    /// call repeatedly in a tight loop (e.g. once per route-simulation tick).
    /// </summary>
    Task PushLocationAsync(DeviceRecord device, PointLatLng? target, CancellationToken cancellationToken = default);
}
