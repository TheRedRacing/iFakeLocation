using iFakeLocation.Services.Devices;

namespace iFakeLocation.Services.Mount;

public enum DeveloperModeToggleState {
    NotApplicable,
    Visible,
    Hidden,
}

public interface IDeveloperModeService {
    /// <summary>Queries the AMFI developer-mode-toggle visibility state (iOS 16+ only; NotApplicable below that).</summary>
    Task<DeveloperModeToggleState> GetToggleStateAsync(DeviceRecord device, CancellationToken cancellationToken = default);

    /// <summary>Reveals the developer-mode toggle in Settings so the user can turn it on manually (iOS 16+ only).</summary>
    Task EnableToggleAsync(DeviceRecord device, CancellationToken cancellationToken = default);

    /// <summary>Mounts the developer disk image (or personalized image, iOS 17+) using the given resource file paths.</summary>
    Task MountAsync(DeviceRecord device, string[] resourcePaths, CancellationToken cancellationToken = default);
}
