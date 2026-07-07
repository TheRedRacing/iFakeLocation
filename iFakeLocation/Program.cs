using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using iFakeLocation.Endpoints;
using iFakeLocation.Infrastructure;
using iFakeLocation.Options;
using iFakeLocation.Services.DeveloperImages;
using iFakeLocation.Services.Devices;
using iFakeLocation.Services.Location;
using iFakeLocation.Services.Mount;
using iFakeLocation.Services.RouteSimulation;

// Native libimobiledevice/libplist binaries must load before any device/plist P/Invoke is made.
if (!NativeLibraryBootstrap.TryLoad(out var loadError)) {
    Console.WriteLine("Failed to load necessary files to run iFakeLocation.");
    if (loadError != null)
        Console.WriteLine(loadError);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => {
    // Bind to an OS-assigned free port on loopback only, replacing the original app's manual
    // scan across the 49215-65535 range.
    options.Listen(System.Net.IPAddress.Loopback, 0);
});

builder.Services.Configure<DeveloperImageOptions>(builder.Configuration.GetSection(DeveloperImageOptions.SectionName));
builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.SectionName));

builder.Services.AddSingleton<IDeviceService, DeviceService>();
builder.Services.AddSingleton<IDeveloperModeService, DeveloperModeService>();
builder.Services.AddSingleton<ILocationSimulationService, LocationSimulationService>();
builder.Services.AddSingleton<DownloadStateStore>();
builder.Services.AddSingleton<IRouteSimulationService, RouteSimulationService>();

builder.Services.AddHttpClient<IDeveloperImageService, DeveloperImageService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
    });

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

// Serialize enums (e.g. RouteSimulationState) as camelCase strings ("running") rather than
// the default numeric value, so the frontend contract stays self-describing.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

var app = builder.Build();

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapSystemEndpoints();
app.MapDeviceEndpoints();
app.MapLocationEndpoints();
app.MapDependencyEndpoints();
app.MapRouteEndpoints();

await app.StartAsync();

var serverOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServerOptions>>().Value;
var addressesFeature = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
    .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
var url = addressesFeature?.Addresses.FirstOrDefault() ?? "http://localhost";

Console.WriteLine("iFakeLocation is now running at: " + url);
Console.WriteLine("\nPress Ctrl-C to quit (or click the close button).");

if (serverOptions.AutoLaunchBrowser) {
    try {
        BrowserLauncher.Open(url);
    }
    catch {
        Console.WriteLine("Unable to start iFakeLocation using default web browser.");
    }
}

await app.WaitForShutdownAsync();
