using iFakeLocation.Services.Devices;

namespace iFakeLocation.Services.Location;

/// <summary>
/// Placeholder for a DVT-service-based location simulator for iOS 17+. Never actually invoked --
/// LocationSimulationService throws UnsupportedIosLocationException before reaching this class,
/// exactly as the original app's DeviceInformation.SetLocation did. Preserved as an explicit
/// marker of the known limitation rather than removed, matching the original file's intent.
/// </summary>
internal sealed class DvtSimulateLocation(DeviceRecord device) : LocationService(device) {
    public override void SetLocation(PointLatLng? target) {
    }
}
