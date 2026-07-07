using iFakeLocation.Contracts;
using iFakeLocation.Services.Devices;

namespace iFakeLocation.Services.DeveloperImages;

public interface IDeveloperImageService {
    /// <summary>Extracts the major.minor iOS version string used to key developer images (e.g. "16.4").</summary>
    string GetSoftwareVersion(DeviceRecord device);

    /// <summary>Checks whether the developer image files for this device already exist on disk.</summary>
    bool HasImageForDevice(DeviceRecord device, out string[]? paths);

    /// <summary>
    /// Checks dependencies for a device and, if missing, starts a background download (tracked
    /// in the injected <see cref="DownloadStateStore"/>, keyed by iOS version).
    /// </summary>
    Task<DependencyCheckResponse> CheckDependenciesAsync(DeviceRecord device, CancellationToken cancellationToken = default);

    /// <summary>Reads the current progress of a tracked download.</summary>
    DownloadProgressResponse GetDownloadProgress(string iosVersion);
}
