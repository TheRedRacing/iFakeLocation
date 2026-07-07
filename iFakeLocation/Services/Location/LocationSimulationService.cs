using iFakeLocation.Services.DeveloperImages;
using iFakeLocation.Services.Devices;
using iFakeLocation.Services.Mount;

namespace iFakeLocation.Services.Location;

public sealed class LocationSimulationService(
    IDeveloperModeService developerModeService,
    IDeveloperImageService developerImageService,
    ILogger<LocationSimulationService> logger) : ILocationSimulationService {
    public async Task EnsureReadyAsync(DeviceRecord device, CancellationToken cancellationToken = default) {
        // Check if developer mode toggle is visible (on >= iOS 16); if so it must be turned on
        // manually by the user first, we can only reveal the toggle, not flip it ourselves.
        var toggleState = await developerModeService.GetToggleStateAsync(device, cancellationToken).ConfigureAwait(false);
        if (toggleState == DeveloperModeToggleState.Hidden) {
            await developerModeService.EnableToggleAsync(device, cancellationToken).ConfigureAwait(false);
            throw new DeveloperModeHiddenException();
        }

        if (!developerImageService.HasImageForDevice(device, out var paths))
            throw new DeveloperImagesMissingException();

        await developerModeService.MountAsync(device, paths!, cancellationToken).ConfigureAwait(false);
    }

    public Task PushLocationAsync(DeviceRecord device, PointLatLng? target, CancellationToken cancellationToken = default) {
        // Use DVT for iOS 17 and above, otherwise use the standard DT service. DVT support was
        // never implemented in the original app either -- preserved as a known limitation.
        if (device.MajorIosVersion >= 17) {
            throw new UnsupportedIosLocationException();
        }

        logger.LogInformation("Pushing location {Target} to device {Udid}", target, device.Udid);
        return Task.Run(() => new DtSimulateLocation(device).SetLocation(target), cancellationToken);
    }
}
