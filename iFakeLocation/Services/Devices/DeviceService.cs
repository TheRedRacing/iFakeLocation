using System.Runtime.InteropServices;
using iFakeLocation.Interop;
using iMobileDevice;
using iMobileDevice.iDevice;
using iMobileDevice.Lockdown;
using iMobileDevice.Plist;

namespace iFakeLocation.Services.Devices;

public sealed class DeviceService(ILogger<DeviceService> logger) : IDeviceService {
    public Task<IReadOnlyList<DeviceRecord>> GetDevicesAsync(CancellationToken cancellationToken = default) {
        // libimobiledevice's device-enumeration APIs are synchronous/blocking native calls;
        // offload to the thread pool so we don't block a request thread.
        return Task.Run(() => EnumerateDevices(includeNetwork: true), cancellationToken);
    }

    public async Task<DeviceRecord> GetDeviceOrThrowAsync(string udid, CancellationToken cancellationToken = default) {
        var devices = await GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        return devices.FirstOrDefault(d => d.Udid == udid) ?? throw new DeviceNotFoundException(udid);
    }

    private IReadOnlyList<DeviceRecord> EnumerateDevices(bool includeNetwork) {
        var idevice = LibiMobileDevice.Instance.iDevice;
        var lockdown = LibiMobileDevice.Instance.Lockdown;
        var plist = LibiMobileDevice.Instance.Plist;

        var devices = new List<DeviceRecord>();

        nint devListPtr = 0;
        int count = 0;
        try {
            if (idevice.idevice_get_device_list_extended(ref devListPtr, ref count) != iDeviceError.Success) {
                logger.LogWarning("idevice_get_device_list_extended failed");
                return devices;
            }

            iDeviceHandle? deviceHandle = null;
            LockdownClientHandle? lockdownHandle = null;
            PlistHandle? plistHandle = null;

            nint devListCurPtr = devListPtr;
            while (devListCurPtr != 0 && Marshal.ReadIntPtr(devListCurPtr) != 0) {
                var info = (iDeviceInfo)Marshal.PtrToStructure(Marshal.ReadIntPtr(devListCurPtr), typeof(iDeviceInfo))!;
                devListCurPtr = nint.Add(devListCurPtr, nint.Size);

                bool isNetwork = info.conn_type == iDeviceConnectionType.Network;
                if (isNetwork && !includeNetwork)
                    continue;

                try {
                    var err = idevice.idevice_new_with_options(out deviceHandle, info.udidString,
                        (int)(isNetwork ? iDeviceOptions.LookupNetwork : iDeviceOptions.LookupUsbmux));
                    if (err != iDeviceError.Success)
                        continue;

                    if (lockdown.lockdownd_client_new_with_handshake(deviceHandle, out lockdownHandle, "iFakeLocation") !=
                        LockdownError.Success)
                        continue;

                    if (lockdown.lockdownd_get_device_name(lockdownHandle, out var name) != LockdownError.Success)
                        continue;

                    if (lockdown.lockdownd_get_value(lockdownHandle, null, null, out plistHandle) != LockdownError.Success ||
                        plist.plist_get_node_type(plistHandle) != PlistType.Dict)
                        continue;

                    var properties = PlistHelper.ReadPlistDictFromNode(plistHandle);

                    // Ensure device is attached
                    if (!properties.TryGetValue("HostAttached", out var hostAttached) || hostAttached is not false) {
                        devices.Add(new DeviceRecord(name, info.udidString, isNetwork, properties));
                    }
                }
                finally {
                    plistHandle?.Close();
                    lockdownHandle?.Close();
                    deviceHandle?.Close();
                    plistHandle = null;
                    lockdownHandle = null;
                    deviceHandle = null;
                }
            }
        }
        finally {
            if (devListPtr != 0)
                idevice.idevice_device_list_extended_free(devListPtr);
        }

        return devices;
    }
}
