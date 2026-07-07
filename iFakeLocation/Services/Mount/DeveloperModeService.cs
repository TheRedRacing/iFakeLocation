using iFakeLocation.Interop;
using iFakeLocation.Services.Devices;
using iMobileDevice;
using iMobileDevice.iDevice;
using iMobileDevice.Lockdown;
using iMobileDevice.Plist;
using iMobileDevice.PropertyListService;

namespace iFakeLocation.Services.Mount;

public sealed class DeveloperModeService(ILogger<DeveloperModeService> logger) : IDeveloperModeService {
    public Task<DeveloperModeToggleState> GetToggleStateAsync(DeviceRecord device, CancellationToken cancellationToken = default) =>
        Task.Run(() => GetToggleState(device), cancellationToken);

    public Task EnableToggleAsync(DeviceRecord device, CancellationToken cancellationToken = default) =>
        Task.Run(() => EnableToggle(device), cancellationToken);

    public Task MountAsync(DeviceRecord device, string[] resourcePaths, CancellationToken cancellationToken = default) =>
        Task.Run(() => Mount(device, resourcePaths), cancellationToken);

    private static DeveloperModeToggleState GetToggleState(DeviceRecord device) {
        // Toggle only exists on iOS 16 onwards
        if (device.MajorIosVersion < 16)
            return DeveloperModeToggleState.NotApplicable;

        iDeviceHandle? deviceHandle = null;
        LockdownClientHandle? lockdownHandle = null;
        PlistHandle? plistHandle = null;

        var idevice = LibiMobileDevice.Instance.iDevice;
        var lockdown = LibiMobileDevice.Instance.Lockdown;
        var plist = LibiMobileDevice.Instance.Plist;

        try {
            if (idevice.idevice_new_with_options(out deviceHandle, device.Udid,
                    (int)(device.IsNetwork ? iDeviceOptions.LookupNetwork : iDeviceOptions.LookupUsbmux)) != iDeviceError.Success)
                throw new Exception("Unable to open device, is it connected?");

            if (lockdown.lockdownd_client_new_with_handshake(deviceHandle, out lockdownHandle, "iFakeLocation") !=
                LockdownError.Success)
                throw new Exception("Unable to connect to lockdownd.");

            if (lockdown.lockdownd_get_value(lockdownHandle, "com.apple.security.mac.amfi", "DeveloperModeStatus",
                    out plistHandle) != LockdownError.Success)
                throw new Exception("Unable to query com.apple.security.mac.amfi service.");

            char status = '\0';
            plist.plist_get_bool_val(plistHandle, ref status);
            return status > 0 ? DeveloperModeToggleState.Visible : DeveloperModeToggleState.Hidden;
        }
        finally {
            plistHandle?.Close();
            lockdownHandle?.Close();
            deviceHandle?.Close();
        }
    }

    private static void EnableToggle(DeviceRecord device) {
        if (device.MajorIosVersion < 16)
            return;

        iDeviceHandle? deviceHandle = null;
        LockdownClientHandle? lockdownHandle = null;
        LockdownServiceDescriptorHandle? serviceDescriptor = null;
        PropertyListServiceClientHandle? propertyListServiceClientHandle = null;
        PlistHandle? plistHandle = null;

        var idevice = LibiMobileDevice.Instance.iDevice;
        var lockdown = LibiMobileDevice.Instance.Lockdown;
        var plist = LibiMobileDevice.Instance.Plist;
        var propertyListService = LibiMobileDevice.Instance.PropertyListService;

        try {
            if (idevice.idevice_new_with_options(out deviceHandle, device.Udid,
                    (int)(device.IsNetwork ? iDeviceOptions.LookupNetwork : iDeviceOptions.LookupUsbmux)) != iDeviceError.Success)
                throw new Exception("Unable to open device, is it connected?");

            if (lockdown.lockdownd_client_new_with_handshake(deviceHandle, out lockdownHandle, "iFakeLocation") !=
                LockdownError.Success)
                throw new Exception("Unable to connect to lockdownd.");

            if (lockdown.lockdownd_start_service(lockdownHandle, "com.apple.amfi.lockdown", out serviceDescriptor) !=
                LockdownError.Success)
                throw new Exception("Unable to start the com.apple.amfi.lockdown service.");

            if (propertyListService.property_list_service_client_new(deviceHandle, serviceDescriptor,
                    out propertyListServiceClientHandle) != PropertyListServiceError.Success)
                throw new Exception("Unable to create property list service client.");

            plistHandle = plist.plist_new_dict();

            // 0 = reveal toggle in settings
            // 1 = enable developer mode (only if no passcode is set)
            // 2 = answers developer mode enable prompt post-restart?
            plist.plist_dict_set_item(plistHandle, "action", plist.plist_new_uint(0));

            if (propertyListService.property_list_service_send_xml_plist(propertyListServiceClientHandle, plistHandle) !=
                PropertyListServiceError.Success)
                throw new Exception("Failed to send request to enable developer mode toggle.");
            plistHandle.Close();
            plistHandle = null;

            if (propertyListService.property_list_service_receive_plist(propertyListServiceClientHandle, out plistHandle) !=
                PropertyListServiceError.Success)
                throw new Exception("Failed to retrieve response after attempting to enable developer mode toggle.");

            var dict = PlistHelper.ReadPlistDictFromNode(plistHandle);
            if (dict.TryGetValue("Error", out var error)) {
                throw new Exception("Failed to enable the developer mode toggle: " + error);
            }

            if (dict.TryGetValue("success", out var success)) {
                if (success is not true) {
                    throw new Exception("Failed to enable the developer mode toggle (unknown error)");
                }
            }
            else {
                throw new Exception("Failed to enable the developer mode toggle (unexpected response)");
            }
        }
        finally {
            plistHandle?.Close();
            propertyListServiceClientHandle?.Close();
            serviceDescriptor?.Close();
            lockdownHandle?.Close();
            deviceHandle?.Close();
        }
    }

    private void Mount(DeviceRecord device, string[] resourcePaths) {
        logger.LogInformation("Mounting developer image for device {Udid} (iOS {Version})", device.Udid, device.ProductVersion);

        // Use personalized image mounter for iOS 17 and above, otherwise use the standard mobile image mounter
        if (device.MajorIosVersion >= 17) {
            new PersonalizedImageMounter(device).EnableDeveloperMode(resourcePaths);
        }
        else {
            new DeveloperDiskImageMounter(device).EnableDeveloperMode(resourcePaths);
        }
    }
}
