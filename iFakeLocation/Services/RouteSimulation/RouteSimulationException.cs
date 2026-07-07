namespace iFakeLocation.Services.RouteSimulation;

public abstract class RouteSimulationException(string message) : Exception(message);

public sealed class RouteSimulationAlreadyRunningException(string udid)
    : RouteSimulationException($"A route simulation is already running for device '{udid}'.");

public sealed class RouteSimulationNotFoundException(string udid)
    : RouteSimulationException($"No route simulation session exists for device '{udid}'.");

public sealed class RouteSimulationInvalidException(string message) : RouteSimulationException(message);
