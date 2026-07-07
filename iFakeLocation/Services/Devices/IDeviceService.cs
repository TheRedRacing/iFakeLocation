namespace iFakeLocation.Services.Devices;

public interface IDeviceService {
    /// <summary>Enumerates currently connected (USB + optionally Wi-Fi) iDevices.</summary>
    Task<IReadOnlyList<DeviceRecord>> GetDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-enumerates devices fresh and returns the one matching <paramref name="udid"/>, or
    /// throws <see cref="DeviceNotFoundException"/>. Always re-enumerates rather than relying on
    /// a cached list from a previous call, unlike the original app (there, the static device
    /// list was only ever populated by a prior /get_devices call, so an action performed before
    /// the first refresh would incorrectly report "device not found" even when connected).
    /// </summary>
    Task<DeviceRecord> GetDeviceOrThrowAsync(string udid, CancellationToken cancellationToken = default);
}
