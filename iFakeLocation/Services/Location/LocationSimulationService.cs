using System.Collections.Concurrent;
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
    ILogger<LocationSimulationService> logger) : ILocationSimulationService, IDisposable {
    // Per-UDID serialization: pushes for the same device (a manual Set/Stop, and a
    // route-simulation tick) must never run concurrently. Overlapping DVT/userspace-tunnel setups
    // for the same device were confirmed live to fight each other -- ~2s calls stretching to
    // 25-57s -- rather than simply queuing, so this isn't optional belt-and-suspenders, it's
    // load-bearing (see ARCHITECTURE.md, "keep-alive re-push: tried and reverted").
    private readonly ConcurrentDictionary<string, SemaphoreSlim> deviceLocks = new();

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
        var deviceLock = deviceLocks.GetOrAdd(device.Udid, _ => new SemaphoreSlim(1, 1));
        await deviceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await PushLocationCoreAsync(device, target, cancellationToken).ConfigureAwait(false);
        }
        finally {
            deviceLock.Release();
        }
    }

    /// <summary>
    /// The actual set/clear dispatch, assumed to already be called under <see cref="deviceLocks"/>
    /// for this device.
    /// </summary>
    private async Task PushLocationCoreAsync(DeviceRecord device, PointLatLng? target, CancellationToken cancellationToken) {
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

            bool succeeded;
            if (useDvt) {
                // `dvt simulate-location set` deliberately never exits on its own once it has
                // succeeded -- confirmed both live (a "type:OK" response line follows the ~1-2s
                // tunnel/DVT handshake + dispatch) and in pymobiledevice3's own source
                // (`OSUTILS.wait_return()`, which blocks on `signal.sigwait([SIGINT, SIGTERM])`
                // forever). This isn't a quirk to route around by killing the process -- the open
                // DTX/Instruments channel IS what keeps the simulated location active, exactly
                // like Xcode's own "Simulate Location" only lasting as long as its debug session
                // is attached. Killing it early (as an earlier version of this code did) tears
                // the channel down and the device reverts to real GPS within moments: invisible
                // to a one-off read (Apple Maps shows the last cached fix) but very visible to
                // anything that polls continuously (see ARCHITECTURE.md). So: keep it running,
                // tracked per-UDID, and only tear it down on an explicit clear or a replacement
                // set for the same device.
                //
                // NOTE: a fixed Set can still slowly drift back to the real GPS fix after a
                // couple of minutes even with the channel held open -- see ARCHITECTURE.md's
                // "keep-alive re-push: tried and reverted" for why a periodic-refresh fix was
                // attempted and rolled back (it replaced a slow drift with a worse ~10s visible
                // flicker, since refreshing requires closing and reopening the channel). This is
                // a known, documented limitation, not something this code currently works around.
                succeeded = await runner.StartPersistentAsync(device.Udid, arguments, MakeSetSuccessMarker(),
                    options.Value.LocationPushTimeout, cancellationToken).ConfigureAwait(false);
            }
            else {
                // Classic (iOS<17) lockdown-based simulate-location: the CLI exits cleanly once
                // set, and the effect is a persistent server-side toggle on the device that
                // doesn't depend on any connection staying open -- fire-and-forget is correct here
                // (could not be verified against real pre-17 hardware; see ARCHITECTURE.md).
                succeeded = await runner.RunFireAndForgetAsync(arguments, MakeSetSuccessMarker(), options.Value.LocationPushTimeout,
                    cancellationToken).ConfigureAwait(false);
            }

            if (!succeeded)
                throw new PyMobileDeviceException("Failed to push the simulated location to the device.");
            return;
        }

        // Kill any persistent DVT `set` session for this device first -- otherwise its still-open
        // channel could keep the simulated location alive independently of the `clear` call below.
        runner.StopPersistent(device.Udid);

        var clearArguments = new List<string>(commandGroup) { "clear", "--udid", device.Udid };
        if (useDvt)
            clearArguments.Add("--userspace");

        var result = await runner.RunAsync(clearArguments, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            throw new PyMobileDeviceException("Failed to stop location simulation on the device.", result.StandardError);
    }

    public void Dispose() {
        foreach (var deviceLock in deviceLocks.Values)
            deviceLock.Dispose();
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
