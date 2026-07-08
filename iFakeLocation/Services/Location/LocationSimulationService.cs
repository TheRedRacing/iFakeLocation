using System.Globalization;
using Microsoft.Extensions.Options;
using iFakeLocation.Options;
using iFakeLocation.Services.DeveloperMode;
using iFakeLocation.Services.Devices;
using iFakeLocation.Services.PyMobileDevice;

namespace iFakeLocation.Services.Location;

public sealed class LocationSimulationService(
    IDeveloperModeService developerModeService,
    IPyMobileDeviceRunner runner,
    IOptions<PyMobileDeviceOptions> options,
    ILogger<LocationSimulationService> logger) : ILocationSimulationService {
    public async Task EnsureReadyAsync(DeviceRecord device, CancellationToken cancellationToken = default) {
        // Check if developer mode toggle is visible (on >= iOS 16); if so it must be turned on
        // manually by the user first, we can only reveal the toggle, not flip it ourselves.
        var toggleState = await developerModeService.GetToggleStateAsync(device, cancellationToken).ConfigureAwait(false);
        if (toggleState == DeveloperModeToggleState.Hidden) {
            await developerModeService.EnableToggleAsync(device, cancellationToken).ConfigureAwait(false);
            throw new DeveloperModeHiddenException();
        }

        await developerModeService.EnsureMountedAsync(device, cancellationToken).ConfigureAwait(false);
    }

    public async Task PushLocationAsync(DeviceRecord device, PointLatLng? target, CancellationToken cancellationToken = default) {
        // iOS 17+ requires the DVT/DTX path over a RemoteXPC tunnel; below that, the classic
        // "com.apple.dt.simulatelocation" lockdown service is used directly. Both are handled by
        // pymobiledevice3 -- unlike the original app (and this rewrite's first iMobileDevice-net
        // based pass), iOS 17+ is genuinely supported now, not a documented limitation.
        var useDvt = device.MajorIosVersion >= 17;
        var commandGroup = useDvt ? new[] { "developer", "dvt", "simulate-location" } : ["developer", "simulate-location"];

        if (target.HasValue) {
            var arguments = new List<string>(commandGroup) { "set", "--udid", device.Udid };
            if (useDvt)
                arguments.Add("--userspace");
            arguments.Add("--");
            arguments.Add(target.Value.Lat.ToString(CultureInfo.InvariantCulture));
            arguments.Add(target.Value.Lng.ToString(CultureInfo.InvariantCulture));

            logger.LogInformation("Pushing location {Lat},{Lng} to device {Udid}", target.Value.Lat, target.Value.Lng, device.Udid);

            // `simulate-location set` does not reliably exit on its own once it has succeeded --
            // confirmed live against a real iOS 17+ device (see ARCHITECTURE.md): the tunnel/DVT
            // handshake and the simulateLocationWithLatitude:longitude: call both complete within
            // ~1-2s, evidenced by a "type:OK" response line following the dispatch, but the CLI
            // process then idles indefinitely rather than exiting -- so success is detected from
            // that marker (or a clean exit, for the non-DVT iOS<17 path, which could not be
            // verified against real hardware) rather than by waiting for the process to end.
            var succeeded = await runner.RunFireAndForgetAsync(arguments, MakeSetSuccessMarker(), options.Value.LocationPushTimeout,
                cancellationToken).ConfigureAwait(false);
            if (!succeeded)
                throw new PyMobileDeviceException("Failed to push the simulated location to the device.");
        }
        else {
            var arguments = new List<string>(commandGroup) { "clear", "--udid", device.Udid };
            if (useDvt)
                arguments.Add("--userspace");

            var result = await runner.RunAsync(arguments, cancellationToken).ConfigureAwait(false);
            if (!result.Success)
                throw new PyMobileDeviceException("Failed to stop location simulation on the device.", result.StandardError);
        }
    }

    /// <summary>
    /// Matches the DVT response line confirming `simulateLocationWithLatitude:longitude:`
    /// succeeded. Stateful per call (tracks whether the dispatch line has been seen yet) since the
    /// handshake produces other unrelated "type:OK" lines beforehand (e.g. channel setup).
    /// </summary>
    private static Func<string, bool> MakeSetSuccessMarker() {
        var sawDispatch = false;
        return line => {
            if (!sawDispatch) {
                if (line.Contains("simulateLocationWithLatitude", StringComparison.Ordinal))
                    sawDispatch = true;
                return false;
            }

            return line.Contains("type:OK", StringComparison.Ordinal);
        };
    }
}
