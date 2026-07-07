using iFakeLocation.Services.Devices;

namespace iFakeLocation.Services.Location;

internal abstract class LocationService(DeviceRecord device) {
    protected readonly DeviceRecord _device = device;

    public abstract void SetLocation(PointLatLng? target);
}
