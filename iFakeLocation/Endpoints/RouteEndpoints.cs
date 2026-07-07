using iFakeLocation.Contracts;
using iFakeLocation.Services.Devices;
using iFakeLocation.Services.Location;
using iFakeLocation.Services.RouteSimulation;

namespace iFakeLocation.Endpoints;

public static class RouteEndpoints {
    public static void MapRouteEndpoints(this IEndpointRouteBuilder app) {
        var group = app.MapGroup("/api/devices/{udid}/route");

        group.MapPost("/start", async (string udid, RouteStartRequest request, IDeviceService deviceService,
            IRouteSimulationService routeSimulationService, CancellationToken cancellationToken) => {
            var device = await deviceService.GetDeviceOrThrowAsync(udid, cancellationToken).ConfigureAwait(false);
            var points = request.Points.Select(p => new PointLatLng(p.Lat, p.Lng)).ToList();
            var result = await routeSimulationService.StartAsync(device, points, request.SpeedKmh, request.Loop, cancellationToken)
                .ConfigureAwait(false);
            return Results.Accepted(value: ToResponse(result));
        });

        group.MapPost("/pause", (string udid, IRouteSimulationService routeSimulationService) =>
            Results.Ok(ToResponse(routeSimulationService.Pause(udid))));

        group.MapPost("/resume", (string udid, IRouteSimulationService routeSimulationService) =>
            Results.Ok(ToResponse(routeSimulationService.Resume(udid))));

        group.MapPost("/stop", (string udid, IRouteSimulationService routeSimulationService) => {
            routeSimulationService.Stop(udid);
            return Results.NoContent();
        });

        group.MapGet("/status", (string udid, IRouteSimulationService routeSimulationService) =>
            Results.Ok(ToResponse(routeSimulationService.GetStatus(udid))));
    }

    private static RouteStatusResponse ToResponse(RouteTickResult result) => new(
        result.State,
        new RoutePointDto(result.Position.Lat, result.Position.Lng),
        result.ProgressPercent,
        result.ElapsedSeconds,
        result.TotalSeconds);
}
