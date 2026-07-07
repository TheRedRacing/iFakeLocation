using iFakeLocation.Contracts;
using iFakeLocation.Services.Devices;

namespace iFakeLocation.Endpoints;

public static class DeviceEndpoints {
    public static void MapDeviceEndpoints(this IEndpointRouteBuilder app) {
        var group = app.MapGroup("/api");

        group.MapGet("/devices", async (IDeviceService deviceService, CancellationToken cancellationToken) => {
            var devices = await deviceService.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
            var dtos = devices.Select(d => new DeviceDto(d.Name, d.DisplayName, d.Udid, d.IsNetwork)).ToList();
            return Results.Ok(new DevicesResponse(dtos));
        });
    }
}
