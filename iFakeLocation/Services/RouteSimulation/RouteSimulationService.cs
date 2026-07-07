using System.Collections.Concurrent;
using iFakeLocation.Contracts;
using iFakeLocation.Services.Devices;
using iFakeLocation.Services.Location;

namespace iFakeLocation.Services.RouteSimulation;

public sealed class RouteSimulationService(ILocationSimulationService locationSimulationService, ILogger<RouteSimulationService> logger)
    : IRouteSimulationService {
    private readonly ConcurrentDictionary<string, RouteSimulationSession> _sessions = new();

    public async Task<RouteTickResult> StartAsync(DeviceRecord device, IReadOnlyList<PointLatLng> points, double speedKmh, bool loop,
        CancellationToken cancellationToken = default) {
        if (_sessions.TryGetValue(device.Udid, out var existingSession) &&
            existingSession.State != RouteSimulationState.Completed)
            throw new RouteSimulationAlreadyRunningException(device.Udid);

        // One-time readiness check (developer-mode toggle + image mount), same call the manual
        // Set Fake Location endpoint makes -- reused here rather than duplicated.
        await locationSimulationService.EnsureReadyAsync(device, cancellationToken).ConfigureAwait(false);

        var session = new RouteSimulationSession(device.Udid, points, speedKmh, loop);
        _sessions[device.Udid] = session;

        logger.LogInformation("Starting route simulation for device {Udid}: {PointCount} points at {SpeedKmh} km/h (loop={Loop})",
            device.Udid, points.Count, speedKmh, loop);

        _ = RunLoopAsync(device, session);

        return session.Snapshot();
    }

    private async Task RunLoopAsync(DeviceRecord device, RouteSimulationSession session) {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var token = session.CancellationTokenSource.Token;

        try {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false)) {
                var delta = session.ComputeDeltaSecondsSinceLastTick(DateTimeOffset.UtcNow);
                var result = session.AdvanceTick(delta);

                try {
                    await locationSimulationService.PushLocationAsync(device, result.Position, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) {
                    logger.LogWarning(ex, "Failed to push a route-simulation location update for device {Udid}; continuing", device.Udid);
                }

                if (result.State == RouteSimulationState.Completed) {
                    logger.LogInformation("Route simulation for device {Udid} completed", device.Udid);
                    break;
                }
            }
        }
        catch (OperationCanceledException) {
            // Stop() was called -- expected.
        }
    }

    public RouteTickResult Pause(string udid) {
        var session = GetSessionOrThrow(udid);
        session.Pause();
        return session.Snapshot();
    }

    public RouteTickResult Resume(string udid) {
        var session = GetSessionOrThrow(udid);
        session.Resume();
        return session.Snapshot();
    }

    public void Stop(string udid) {
        if (!_sessions.TryRemove(udid, out var session))
            throw new RouteSimulationNotFoundException(udid);

        session.CancellationTokenSource.Cancel();
    }

    public RouteTickResult GetStatus(string udid) => GetSessionOrThrow(udid).Snapshot();

    private RouteSimulationSession GetSessionOrThrow(string udid) =>
        _sessions.TryGetValue(udid, out var session) ? session : throw new RouteSimulationNotFoundException(udid);
}
