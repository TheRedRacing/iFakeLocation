using iFakeLocation.Contracts;
using iFakeLocation.Services.Location;

namespace iFakeLocation.Services.RouteSimulation;

/// <summary>
/// Mutable state for a single device's active route simulation. One instance per UDID, held in
/// <see cref="RouteSimulationService"/>'s session dictionary. "Idle" (never started, or stopped)
/// is modeled as the absence of an entry rather than a state value.
/// </summary>
public sealed class RouteSimulationSession {
    private readonly object _lock = new();

    public string Udid { get; }
    public IReadOnlyList<PointLatLng> Points { get; }
    public bool Loop { get; }
    public double SpeedKmh { get; }
    public double[] Cumulative { get; }
    public double TotalDistanceMeters { get; }
    public double TotalDurationSeconds { get; }
    public CancellationTokenSource CancellationTokenSource { get; } = new();

    public RouteSimulationState State { get; private set; } = RouteSimulationState.Running;
    public double ElapsedSeconds { get; private set; }
    private DateTimeOffset? _lastTickTimestamp;

    public RouteSimulationSession(string udid, IReadOnlyList<PointLatLng> points, double speedKmh, bool loop) {
        if (points.Count < 2)
            throw new RouteSimulationInvalidException("A route simulation needs at least two points.");
        if (speedKmh <= 0)
            throw new RouteSimulationInvalidException("Speed must be greater than zero.");

        Udid = udid;
        Points = points;
        SpeedKmh = speedKmh;
        Loop = loop;

        var (cumulative, total) = RouteMath.BuildCumulativeDistances(points);
        Cumulative = cumulative;
        TotalDistanceMeters = total;

        var speedMetersPerSecond = speedKmh * 1000.0 / 3600.0;
        TotalDurationSeconds = total / speedMetersPerSecond;
    }

    /// <summary>
    /// Advances simulated time by <paramref name="deltaSeconds"/> and returns the resulting
    /// current position + progress snapshot. Pure with respect to wall-clock time (the caller
    /// supplies the delta), kept separate from the timer/loop plumbing so it's independently
    /// unit-testable.
    /// </summary>
    public RouteTickResult AdvanceTick(double deltaSeconds) {
        lock (_lock) {
            if (State == RouteSimulationState.Running) {
                ElapsedSeconds += deltaSeconds;

                if (ElapsedSeconds >= TotalDurationSeconds) {
                    if (Loop) {
                        // Basic loop behavior: restart from the beginning rather than reversing
                        // direction back to the start -- documented as a simplification.
                        ElapsedSeconds %= TotalDurationSeconds;
                    }
                    else {
                        ElapsedSeconds = TotalDurationSeconds;
                        State = RouteSimulationState.Completed;
                    }
                }
            }

            var position = RouteMath.Interpolate(Points, Cumulative, TotalDistanceMeters, ElapsedSeconds * SpeedMetersPerSecond());
            var progressPercent = TotalDurationSeconds <= 0 ? 100.0 : ElapsedSeconds / TotalDurationSeconds * 100.0;

            return new RouteTickResult(State, position, progressPercent, ElapsedSeconds, TotalDurationSeconds);
        }
    }

    public void Pause() {
        lock (_lock) {
            if (State == RouteSimulationState.Running)
                State = RouteSimulationState.Paused;
        }
    }

    public void Resume() {
        lock (_lock) {
            if (State == RouteSimulationState.Paused) {
                State = RouteSimulationState.Running;
                _lastTickTimestamp = null;
            }
        }
    }

    public RouteTickResult Snapshot() {
        lock (_lock) {
            var position = RouteMath.Interpolate(Points, Cumulative, TotalDistanceMeters, ElapsedSeconds * SpeedMetersPerSecond());
            var progressPercent = TotalDurationSeconds <= 0 ? 100.0 : ElapsedSeconds / TotalDurationSeconds * 100.0;
            return new RouteTickResult(State, position, progressPercent, ElapsedSeconds, TotalDurationSeconds);
        }
    }

    /// <summary>Computes the wall-clock delta since the previous tick (0 for the very first tick).</summary>
    public double ComputeDeltaSecondsSinceLastTick(DateTimeOffset now) {
        var delta = _lastTickTimestamp.HasValue ? (now - _lastTickTimestamp.Value).TotalSeconds : 0;
        _lastTickTimestamp = now;
        return delta;
    }

    private double SpeedMetersPerSecond() => SpeedKmh * 1000.0 / 3600.0;
}

public readonly record struct RouteTickResult(
    RouteSimulationState State,
    PointLatLng Position,
    double ProgressPercent,
    double ElapsedSeconds,
    double TotalSeconds);
