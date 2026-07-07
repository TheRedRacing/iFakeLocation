namespace iFakeLocation.Contracts;

public enum RouteSimulationState {
    Running,
    Paused,
    Completed,
}

public sealed record RouteStatusResponse(
    RouteSimulationState State,
    RoutePointDto CurrentPosition,
    double ProgressPercent,
    double ElapsedSeconds,
    double TotalSeconds);
