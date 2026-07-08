using iFakeLocation.Services;
using iFakeLocation.Services.Devices;
using iFakeLocation.Services.PyMobileDevice;

namespace iFakeLocation.Services.DeveloperMode;

public sealed class DeveloperModeService(IPyMobileDeviceRunner runner, ILogger<DeveloperModeService> logger) : IDeveloperModeService {
    public async Task<DeveloperModeToggleState> GetToggleStateAsync(DeviceRecord device, CancellationToken cancellationToken = default) {
        // Toggle only exists on iOS 16 onwards
        if (device.MajorIosVersion < 16)
            return DeveloperModeToggleState.NotApplicable;

        var result = await runner.RunAsync(["amfi", "developer-mode-status", "--udid", device.Udid], cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
            throw new PyMobileDeviceException("Unable to query developer mode status.", result.StandardError);

        var enabled = result.StandardOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        return enabled ? DeveloperModeToggleState.Visible : DeveloperModeToggleState.Hidden;
    }

    public async Task EnableToggleAsync(DeviceRecord device, CancellationToken cancellationToken = default) {
        if (device.MajorIosVersion < 16)
            return;

        var result = await runner.RunAsync(["amfi", "reveal-developer-mode", "--udid", device.Udid], cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
            throw new PyMobileDeviceException("Failed to reveal the developer mode toggle.", result.StandardError);
    }

    public async Task EnsureMountedAsync(DeviceRecord device, CancellationToken cancellationToken = default) {
        logger.LogInformation("Ensuring developer image is mounted for device {Udid} (iOS {Version})", device.Udid, device.ProductVersion);

        var arguments = new List<string> { "mounter", "auto-mount", "--udid", device.Udid };
        // iOS 17+ needs a RemoteXPC tunnel; --userspace avoids requiring admin/sudo privileges
        // (see ARCHITECTURE.md) at the cost of slower throughput for this one mount transfer only.
        if (device.MajorIosVersion >= 17)
            arguments.Add("--userspace");

        var result = await runner.RunAsync(arguments, cancellationToken).ConfigureAwait(false);
        if (result.Success)
            return;

        if (result.StandardError.Contains("Developer Mode", StringComparison.OrdinalIgnoreCase) &&
            result.StandardError.Contains("not enabled", StringComparison.OrdinalIgnoreCase)) {
            throw new DeveloperModeHiddenException();
        }

        throw new DeveloperImagesMissingException();
    }
}
