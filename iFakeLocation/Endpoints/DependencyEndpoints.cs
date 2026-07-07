using iFakeLocation.Services.DeveloperImages;
using iFakeLocation.Services.Devices;

namespace iFakeLocation.Endpoints;

public static class DependencyEndpoints {
    public static void MapDependencyEndpoints(this IEndpointRouteBuilder app) {
        var group = app.MapGroup("/api");

        group.MapPost("/devices/{udid}/dependencies/check",
            async (string udid, IDeviceService deviceService, IDeveloperImageService developerImageService,
                CancellationToken cancellationToken) => {
                var device = await deviceService.GetDeviceOrThrowAsync(udid, cancellationToken).ConfigureAwait(false);
                var result = await developerImageService.CheckDependenciesAsync(device, cancellationToken).ConfigureAwait(false);
                return Results.Ok(result);
            });

        group.MapGet("/downloads/{iosVersion}/progress", (string iosVersion, IDeveloperImageService developerImageService) =>
            Results.Ok(developerImageService.GetDownloadProgress(iosVersion)));
    }
}
