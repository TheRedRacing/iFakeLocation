using System.Globalization;
using System.Reflection;
using iFakeLocation.Contracts;

namespace iFakeLocation.Endpoints;

public static class SystemEndpoints {
    public static void MapSystemEndpoints(this IEndpointRouteBuilder app) {
        var group = app.MapGroup("/api");

        group.MapGet("/version", () => {
            var version = Assembly.GetExecutingAssembly().GetName().Version!;
            return Results.Ok(new VersionResponse($"{version.Major}.{version.Minor}.{version.Build}"));
        });

        group.MapGet("/home-country", () =>
            Results.Ok(new HomeCountryResponse(RegionInfo.CurrentRegion.EnglishName)));

        group.MapPost("/exit", (IHostApplicationLifetime lifetime) => {
            lifetime.StopApplication();
            return Results.Accepted();
        });
    }
}
