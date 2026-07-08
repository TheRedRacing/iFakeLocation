using System.Text.Json;
using System.Text.Json.Serialization;
using iFakeLocation.Services.PyMobileDevice;

namespace iFakeLocation.Services.Devices;

public sealed class DeviceService(IPyMobileDeviceRunner runner, ILogger<DeviceService> logger) : IDeviceService {
    private sealed record UsbmuxListEntry(
        [property: JsonPropertyName("Identifier")] string Identifier,
        [property: JsonPropertyName("DeviceName")] string DeviceName,
        [property: JsonPropertyName("ProductType")] string ProductType,
        [property: JsonPropertyName("ProductVersion")] string ProductVersion,
        [property: JsonPropertyName("ConnectionType")] string ConnectionType);

    public async Task<IReadOnlyList<DeviceRecord>> GetDevicesAsync(CancellationToken cancellationToken = default) {
        var result = await runner.RunAsync(["usbmux", "list"], cancellationToken).ConfigureAwait(false);
        if (!result.Success) {
            logger.LogWarning("pymobiledevice3 usbmux list failed: {Error}", result.StandardError);
            return [];
        }

        List<UsbmuxListEntry>? entries;
        try {
            entries = JsonSerializer.Deserialize<List<UsbmuxListEntry>>(result.StandardOutput);
        }
        catch (JsonException ex) {
            logger.LogWarning(ex, "Failed to parse pymobiledevice3 usbmux list output");
            return [];
        }

        if (entries == null)
            return [];

        return entries.Select(e => new DeviceRecord(
            e.DeviceName,
            e.Identifier,
            string.Equals(e.ConnectionType, "Network", StringComparison.OrdinalIgnoreCase),
            new Dictionary<string, object?> {
                ["ProductType"] = e.ProductType,
                ["ProductVersion"] = e.ProductVersion,
            })).ToList();
    }

    public async Task<DeviceRecord> GetDeviceOrThrowAsync(string udid, CancellationToken cancellationToken = default) {
        var devices = await GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        return devices.FirstOrDefault(d => d.Udid == udid) ?? throw new DeviceNotFoundException(udid);
    }
}
