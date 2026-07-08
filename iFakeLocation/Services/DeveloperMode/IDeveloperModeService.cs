using iFakeLocation.Services.Devices;

namespace iFakeLocation.Services.DeveloperMode;

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

    /// <summary>
    /// Ensures the developer disk image (or personalized image, iOS 17+) is downloaded and
    /// mounted, via pymobiledevice3's own `mounter auto-mount` -- which resolves/downloads/mounts
    /// the correct image internally, including the personalized-image/TSS flow for iOS 17+.
    /// </summary>
    Task EnsureMountedAsync(DeviceRecord device, CancellationToken cancellationToken = default);
}
