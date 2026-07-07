using iFakeLocation.Contracts;
using iFakeLocation.Services.Devices;
using iFakeLocation.Services.Location;

namespace iFakeLocation.Endpoints;

public static class LocationEndpoints {
    public static void MapLocationEndpoints(this IEndpointRouteBuilder app) {
        var group = app.MapGroup("/api/devices/{udid}/location");

        group.MapPost("/", async (string udid, SetLocationRequest request, IDeviceService deviceService,
            ILocationSimulationService locationSimulationService, CancellationToken cancellationToken) => {
            var device = await deviceService.GetDeviceOrThrowAsync(udid, cancellationToken).ConfigureAwait(false);
            await locationSimulationService.EnsureReadyAsync(device, cancellationToken).ConfigureAwait(false);
            await locationSimulationService.PushLocationAsync(device, new PointLatLng(request.Lat, request.Lng), cancellationToken)
                .ConfigureAwait(false);
            return Results.NoContent();
        });

        group.MapDelete("/", async (string udid, IDeviceService deviceService,
            ILocationSimulationService locationSimulationService, CancellationToken cancellationToken) => {
            var device = await deviceService.GetDeviceOrThrowAsync(udid, cancellationToken).ConfigureAwait(false);
            await locationSimulationService.EnsureReadyAsync(device, cancellationToken).ConfigureAwait(false);
            await locationSimulationService.PushLocationAsync(device, null, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        });
    }
}
