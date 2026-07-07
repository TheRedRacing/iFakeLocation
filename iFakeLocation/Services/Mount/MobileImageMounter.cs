using iFakeLocation.Services.Devices;

namespace iFakeLocation.Services.Mount;

internal abstract class MobileImageMounter(DeviceRecord device) {
    protected readonly DeviceRecord _device = device;

    public abstract void EnableDeveloperMode(string[] resourcePaths);
}
