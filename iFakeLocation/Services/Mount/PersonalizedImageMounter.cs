using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using iFakeLocation.Interop;
using iFakeLocation.Services.Devices;
using iFakeLocation.Services.Restore;
using iMobileDevice;
using iMobileDevice.iDevice;
using iMobileDevice.Lockdown;
using iMobileDevice.Plist;
using iMobileDevice.PropertyListService;
using iMobileDevice.Service;

namespace iFakeLocation.Services.Mount;

/// <summary>Mounts a TSS-personalized (IMG4) developer image, required from iOS 17 onwards.</summary>
internal sealed class PersonalizedImageMounter(DeviceRecord device) : MobileImageMounter(device) {
    public override void EnableDeveloperMode(string[] resourcePaths) {
        EnableDeveloperMode(resourcePaths[0], resourcePaths[1], resourcePaths[2]);
    }

    private static Dictionary<string, object?> SendRecvPlist(PropertyListServiceClientHandle propListServiceHandle,
        PlistHandle plist, bool isXml = true) {
        var propListService = LibiMobileDevice.Instance.PropertyListService;

        PlistHandle? plistOutHandle = null;

        try {
            if ((isXml
                    ? propListService.property_list_service_send_xml_plist(propListServiceHandle, plist)
                    : propListService.property_list_service_send_binary_plist(propListServiceHandle, plist)) !=
                PropertyListServiceError.Success)
                throw new Exception("Failed to send the plist to the specified service.");

            if (propListService.property_list_service_receive_plist(propListServiceHandle, out plistOutHandle) !=
                PropertyListServiceError.Success)
                throw new Exception("Failed to receive the plist from the specified service.");

            return PlistHelper.ReadPlistDictFromNode(plistOutHandle);
        }
        finally {
            plistOutHandle?.Close();
        }
    }

    private static Dictionary<string, object?> SendDataRecvPlist(PropertyListServiceClientHandle propListServiceHandle, byte[] data) {
        var propListService = LibiMobileDevice.Instance.PropertyListService;
        var service = LibiMobileDevice.Instance.Service;

        PlistHandle? plistOutHandle = null;
        ServiceClientHandle? serviceClientHandle = null;

        try {
            // Extract the service client from the property_list_service_t->parent value (warning: hacky)
            // struct property_list_service_client_private {
            //      service_client_t parent;
            // };
            serviceClientHandle = ServiceClientHandle.DangerousCreate(Marshal.ReadIntPtr(propListServiceHandle.DangerousGetHandle()));

            uint sent = 0;
            if (service.service_send(serviceClientHandle, data, (uint)data.Length, ref sent) != ServiceError.Success ||
                sent != data.Length) {
                throw new Exception("Failed to send the data to the specified service.");
            }

            if (propListService.property_list_service_receive_plist(propListServiceHandle, out plistOutHandle) !=
                PropertyListServiceError.Success)
                throw new Exception("Failed to receive the plist from the specified service.");

            return PlistHelper.ReadPlistDictFromNode(plistOutHandle);
        }
        finally {
            // Ensure CLR does not attempt to close the handle during destruction of the SafeFileHandle
            // since we extracted this handle manually (we will get an exception otherwise during garbage collection)
            serviceClientHandle?.SetHandleAsInvalid();
            plistOutHandle?.Close();
        }
    }

    private Dictionary<string, object?> QueryPersonalizationIdentifiers(PropertyListServiceClientHandle propListServiceHandle) {
        var plist = LibiMobileDevice.Instance.Plist;
        var plistHandle = plist.plist_new_dict();

        try {
            plist.plist_dict_set_item(plistHandle, "Command", plist.plist_new_string("QueryPersonalizationIdentifiers"));
            return SendRecvPlist(propListServiceHandle, plistHandle);
        }
        finally {
            plistHandle?.Close();
        }
    }

    private byte[] QueryNonce(PropertyListServiceClientHandle propListServiceHandle, string? personalizedImageType = null) {
        var plist = LibiMobileDevice.Instance.Plist;
        var plistHandle = plist.plist_new_dict();

        try {
            plist.plist_dict_set_item(plistHandle, "Command", plist.plist_new_string("QueryNonce"));
            if (personalizedImageType != null) {
                plist.plist_dict_set_item(plistHandle, "PersonalizedImageType", plist.plist_new_string(personalizedImageType));
            }

            var result = SendRecvPlist(propListServiceHandle, plistHandle);
            if (!result.TryGetValue("PersonalizationNonce", out var nonce) || nonce is not byte[] nonceBytes)
                throw new Exception("Unable to locate personalization nonce in response.");

            return nonceBytes;
        }
        finally {
            plistHandle?.Close();
        }
    }

    private byte[] GetManifestFromTSS(PropertyListServiceClientHandle propListServiceHandle, Dictionary<string, object?> buildManifest) {
        var identifiers = QueryPersonalizationIdentifiers(propListServiceHandle);
        if (!identifiers.TryGetValue("PersonalizationIdentifiers", out var identifiersObj) ||
            identifiersObj is not Dictionary<string, object?> personalizationIdentifiers)
            throw new Exception("Failed to extract personalization identifiers from the plist response.");

        var request = new TSSRequest();

        foreach (var kvp in personalizationIdentifiers) {
            if (kvp.Key.StartsWith("Ap,"))
                request.Update(kvp.Key, kvp.Value);
        }

        var boardId = int.Parse(personalizationIdentifiers["BoardId"]!.ToString()!);
        var chipId = int.Parse(personalizationIdentifiers["ChipID"]!.ToString()!);
        Dictionary<string, object?>? buildIdentity = null;
        foreach (Dictionary<string, object?> identity in (object[])buildManifest["BuildIdentities"]!) {
            var curBoardId = identity.TryGetValue("ApBoardID", out var apBoardId)
                ? int.Parse(((string)apBoardId!).Replace("0x", ""), NumberStyles.HexNumber)
                : 0;
            var curChipId = identity.TryGetValue("ApChipID", out var apChipId)
                ? int.Parse(((string)apChipId!).Replace("0x", ""), NumberStyles.HexNumber)
                : 0;
            if (curBoardId == boardId && curChipId == chipId) {
                buildIdentity = identity;
                break;
            }
        }

        if (buildIdentity == null)
            throw new Exception("Unable to find a build identity matching the current device in the build manifest.");

        request.Update(new Dictionary<string, object> {
            {"@ApImg4Ticket", true},
            {"@BBTicket", true},
            {"ApBoardID", boardId},
            {"ApChipID", chipId},
            {"ApECID", _device.Properties["UniqueChipID"]!},
            {"ApNonce", QueryNonce(propListServiceHandle, "DeveloperDiskImage")},
            {"ApProductionMode", true},
            {"ApSecurityDomain", 1},
            {"ApSecurityMode", true},
            {"SepNonce", Encoding.ASCII.GetBytes("\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00")},
            {"UID_MODE", false},
        });

        var parameters = new Dictionary<string, object?> {
            {"ApProductionMode", true},
            {"ApSecurityDomain", 1},
            {"ApSecurityMode", true},
            {"ApSupportsImg4", true},
        };

        var manifest = (Dictionary<string, object?>)buildIdentity["Manifest"]!;
        foreach (var kvp in manifest) {
            var manifestEntry = (Dictionary<string, object?>)kvp.Value!;

            // Only permit trusted items
            if (!manifestEntry.TryGetValue("Info", out var info) || info == null)
                continue;
            if (!manifestEntry.TryGetValue("Trusted", out var trusted) || trusted is not true)
                continue;

            var tssEntry = new Dictionary<string, object?>(manifestEntry);
            tssEntry.Remove("Info");

            // Apply the restore request rules
            var loadableTrustCache = (Dictionary<string, object?>)manifest["LoadableTrustCache"]!;
            var loadableTrustCacheInfo = (Dictionary<string, object?>)loadableTrustCache["Info"]!;
            if (loadableTrustCacheInfo.ContainsKey("RestoreRequestRules")) {
                var rules = (object[])loadableTrustCacheInfo["RestoreRequestRules"]!;
                if (rules.Length > 0) {
                    request.ApplyRestoreRequestRules(tssEntry, parameters,
                        rules.Select(s => (Dictionary<string, object?>)s));
                }
            }

            // Ensure a digest always exists
            if (!manifestEntry.TryGetValue("Digest", out var digest) || digest == null) {
                tssEntry["Digest"] = Array.Empty<byte>();
            }

            request.Update(kvp.Key, tssEntry);
        }

        var tssResponse = request.SendAndReceive();
        if (!tssResponse.TryGetValue("ApImg4Ticket", out var ticket) || ticket is not byte[] ticketBytes)
            throw new Exception("TSS response did not contain the expected ticket.");

        return ticketBytes;
    }

    private byte[] QueryPersonalizationManifest(PropertyListServiceClientHandle propListServiceHandle, string imageType, byte[] signature) {
        var plist = LibiMobileDevice.Instance.Plist;
        var plistHandle = plist.plist_new_dict();

        try {
            plist.plist_dict_set_item(plistHandle, "Command", plist.plist_new_string("QueryPersonalizationManifest"));
            plist.plist_dict_set_item(plistHandle, "PersonalizedImageType", plist.plist_new_string(imageType));
            plist.plist_dict_set_item(plistHandle, "ImageType", plist.plist_new_string(imageType));
            plist.plist_dict_set_item(plistHandle, "ImageSignature", NativeMethods.plist_new_data(signature, signature.Length));

            var result = SendRecvPlist(propListServiceHandle, plistHandle);
            if (!result.TryGetValue("ImageSignature", out var sig) || sig is not byte[] sigBytes)
                throw new KeyNotFoundException("Unable to locate image signature in response.");

            return sigBytes;
        }
        finally {
            plistHandle?.Close();
        }
    }

    private void UploadPersonalizedImage(PropertyListServiceClientHandle propListServiceHandle, string imageType,
        byte[] image, byte[] signature) {
        var plist = LibiMobileDevice.Instance.Plist;
        var plistHandle = plist.plist_new_dict();

        try {
            plist.plist_dict_set_item(plistHandle, "Command", plist.plist_new_string("ReceiveBytes"));
            plist.plist_dict_set_item(plistHandle, "ImageType", plist.plist_new_string(imageType));
            plist.plist_dict_set_item(plistHandle, "ImageSize", plist.plist_new_uint((uint)image.Length));
            plist.plist_dict_set_item(plistHandle, "ImageSignature", NativeMethods.plist_new_data(signature, signature.Length));

            var result = SendRecvPlist(propListServiceHandle, plistHandle);
            if (!result.TryGetValue("Status", out var status) || (string?)status != "ReceiveBytesAck")
                throw new Exception("Failed to upload the image to the device: " + (result.GetValueOrDefault("Error") ?? "Unknown error"));

            result = SendDataRecvPlist(propListServiceHandle, image);
            if (!result.TryGetValue("Status", out var status2) || (string?)status2 != "Complete")
                throw new Exception("Failed to validate that the image upload successfully.");
        }
        finally {
            plistHandle?.Close();
        }
    }

    private void MountPersonalizedImage(PropertyListServiceClientHandle propListServiceHandle, string imageType, byte[] signature,
        Action<IPlistApi, PlistHandle>? extraPropsAction = null) {
        var plist = LibiMobileDevice.Instance.Plist;
        var plistHandle = plist.plist_new_dict();

        try {
            plist.plist_dict_set_item(plistHandle, "Command", plist.plist_new_string("MountImage"));
            plist.plist_dict_set_item(plistHandle, "ImageType", plist.plist_new_string(imageType));
            plist.plist_dict_set_item(plistHandle, "ImageSignature", NativeMethods.plist_new_data(signature, signature.Length));

            extraPropsAction?.Invoke(plist, plistHandle);

            var result = SendRecvPlist(propListServiceHandle, plistHandle);

            if (result.TryGetValue("DetailedError", out var detailedError) &&
                detailedError is string detailedErrorStr) {
                if (detailedErrorStr.Contains("Developer mode is not enabled"))
                    throw new Exception("Developer mode is not enabled on the device.");
                if (detailedErrorStr.Contains("is already mounted"))
                    return;
            }

            if (!result.TryGetValue("Status", out var status) || (string?)status != "Complete")
                throw new Exception("Failed to mount the personalized image.");
        }
        finally {
            plistHandle?.Close();
        }
    }

    private static bool IsPersonalizedImageMounted(PropertyListServiceClientHandle propListServiceHandle, string imageType) {
        var plist = LibiMobileDevice.Instance.Plist;
        var plistHandle = plist.plist_new_dict();

        try {
            plist.plist_dict_set_item(plistHandle, "Command", plist.plist_new_string("LookupImage"));
            plist.plist_dict_set_item(plistHandle, "ImageType", plist.plist_new_string(imageType));

            var result = SendRecvPlist(propListServiceHandle, plistHandle);
            return (result.TryGetValue("ImagePresent", out var imagePresent) && imagePresent is true) ||
                   (result.TryGetValue("ImageSignature", out var imageSignature) &&
                    ((imageSignature is object[] sigArray && sigArray.Length > 0) ||
                     (imageSignature is not object[] && imageSignature != null)));
        }
        finally {
            plistHandle?.Close();
        }
    }

    private void EnableDeveloperMode(string imagePath, string buildManifestPath, string trustCachePath, bool useExistingManifest = true) {
        if (!File.Exists(imagePath) || !File.Exists(buildManifestPath) || !File.Exists(trustCachePath))
            throw new FileNotFoundException("The specified device image files do not exist.");

        iDeviceHandle? deviceHandle = null;
        LockdownClientHandle? lockdownHandle = null;
        LockdownServiceDescriptorHandle? serviceDescriptor = null;
        PropertyListServiceClientHandle? propListServiceHandle = null;

        void CloseAllHandles() {
            propListServiceHandle?.Close();
            serviceDescriptor?.Close();
            lockdownHandle?.Close();
            deviceHandle?.Close();

            propListServiceHandle = null;
            serviceDescriptor = null;
            lockdownHandle = null;
            deviceHandle = null;
        }

        var idevice = LibiMobileDevice.Instance.iDevice;
        var lockdown = LibiMobileDevice.Instance.Lockdown;
        var propListService = LibiMobileDevice.Instance.PropertyListService;

        try {
            if (idevice.idevice_new_with_options(out deviceHandle, _device.Udid,
                    (int)(_device.IsNetwork ? iDeviceOptions.LookupNetwork : iDeviceOptions.LookupUsbmux)) != iDeviceError.Success)
                throw new Exception("Unable to open device, is it connected?");

            if (lockdown.lockdownd_client_new_with_handshake(deviceHandle, out lockdownHandle, "iFakeLocation") !=
                LockdownError.Success)
                throw new Exception("Unable to connect to lockdownd.");

            if (lockdown.lockdownd_start_service(lockdownHandle, "com.apple.mobile.mobile_image_mounter",
                    out serviceDescriptor) != LockdownError.Success)
                throw new Exception("Unable to start the mobile image mounter service.");

            if (propListService.property_list_service_client_new(deviceHandle, serviceDescriptor, out propListServiceHandle) !=
                PropertyListServiceError.Success)
                throw new Exception("Failed to obtain a property list service handle.");

            if (IsPersonalizedImageMounted(propListServiceHandle, "Personalized"))
                return;

            byte[] manifest;

            if (useExistingManifest) {
                try {
                    using var imageStream = File.OpenRead(imagePath);
                    manifest = QueryPersonalizationManifest(propListServiceHandle, "DeveloperDiskImage",
                        SHA384.HashData(imageStream));
                }
                catch (KeyNotFoundException) {
                    // Need to run this function again (without querying for manifest) as service connection will be dead now
                    CloseAllHandles();
                    EnableDeveloperMode(imagePath, buildManifestPath, trustCachePath, false);
                    return;
                }
            }
            else {
                using var manifestStream = File.OpenRead(buildManifestPath);
                manifest = GetManifestFromTSS(propListServiceHandle, PlistHelper.ReadPlistDictFromStream(manifestStream));
            }

            UploadPersonalizedImage(propListServiceHandle, "Personalized", File.ReadAllBytes(imagePath), manifest);

            MountPersonalizedImage(propListServiceHandle, "Personalized", manifest, (plist, plistHandleInner) => {
                var trustCache = File.ReadAllBytes(trustCachePath);
                plist.plist_dict_set_item(plistHandleInner, "ImageTrustCache",
                    NativeMethods.plist_new_data(trustCache, trustCache.Length));
            });
        }
        finally {
            CloseAllHandles();
        }
    }
}
