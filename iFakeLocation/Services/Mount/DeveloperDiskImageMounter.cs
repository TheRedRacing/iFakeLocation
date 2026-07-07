using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using iFakeLocation.Interop;
using iFakeLocation.Services.Devices;
using iMobileDevice;
using iMobileDevice.Afc;
using iMobileDevice.iDevice;
using iMobileDevice.Lockdown;
using iMobileDevice.MobileImageMounter;
using iMobileDevice.Plist;

namespace iFakeLocation.Services.Mount;

/// <summary>Mounts the classic (pre-iOS 17) DeveloperDiskImage.dmg + .signature pair.</summary>
internal sealed class DeveloperDiskImageMounter(DeviceRecord device) : MobileImageMounter(device) {
    private enum DiskImageUploadMode {
        AFC,
        UploadImage,
    }

    private static readonly MobileImageMounterUploadCallBack MounterUploadCallback = MounterReadCallback;

    private static int MounterReadCallback(nint buffer, uint size, nint userData) {
        var imageStream = (FileStream)GCHandle.FromIntPtr(userData).Target!;
        var buf = new byte[size];
        var rl = imageStream.Read(buf, 0, buf.Length);
        Marshal.Copy(buf, 0, buffer, buf.Length);
        return rl;
    }

    public override void EnableDeveloperMode(string[] resourcePaths) {
        EnableDeveloperMode(resourcePaths[0], resourcePaths[1]);
    }

    private void EnableDeveloperMode(string deviceImagePath, string deviceImageSignaturePath) {
        if (!File.Exists(deviceImagePath) || !File.Exists(deviceImageSignaturePath))
            throw new FileNotFoundException("The specified device image files do not exist.");

        iDeviceHandle? deviceHandle = null;
        LockdownClientHandle? lockdownHandle = null;
        LockdownServiceDescriptorHandle? serviceDescriptor = null;
        MobileImageMounterClientHandle? mounterHandle = null;
        AfcClientHandle? afcHandle = null;
        PlistHandle? plistHandle = null;
        FileStream? imageStream = null;

        // Use upload image for iOS 7 and above, otherwise use AFC
        var mode = _device.MajorIosVersion >= 7 ? DiskImageUploadMode.UploadImage : DiskImageUploadMode.AFC;

        var idevice = LibiMobileDevice.Instance.iDevice;
        var lockdown = LibiMobileDevice.Instance.Lockdown;
        var mounter = LibiMobileDevice.Instance.MobileImageMounter;
        var afc = LibiMobileDevice.Instance.Afc;

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

            if (mounter.mobile_image_mounter_new(deviceHandle, serviceDescriptor, out mounterHandle) !=
                MobileImageMounterError.Success)
                throw new Exception("Unable to create mobile image mounter instance.");

            serviceDescriptor.Close();
            serviceDescriptor = null;

            if (mode == DiskImageUploadMode.AFC) {
                if (lockdown.lockdownd_start_service(lockdownHandle, "com.apple.afc", out serviceDescriptor) !=
                    LockdownError.Success)
                    throw new Exception("Unable to start AFC service.");

                if (afc.afc_client_new(deviceHandle, serviceDescriptor, out afcHandle) != AfcError.Success)
                    throw new Exception("Unable to connect to AFC service.");

                serviceDescriptor.Close();
                serviceDescriptor = null;
            }

            lockdownHandle.Close();
            lockdownHandle = null;

            // Check if the developer image has already been mounted
            const string imageType = "Developer";
            if (mounter.mobile_image_mounter_lookup_image(mounterHandle, imageType, out plistHandle) ==
                MobileImageMounterError.Success) {
                var results = PlistHelper.ReadPlistDictFromNode(plistHandle, ["ImagePresent", "ImageSignature"]);

                // Some iOS use ImagePresent to verify presence, while others use ImageSignature instead.
                // Check the content of the ImageSignature value as iOS 14 returns a value even if empty.
                if ((results.TryGetValue("ImagePresent", out var imagePresent) && imagePresent is true) ||
                    (results.TryGetValue("ImageSignature", out var imageSignature) &&
                     imageSignature is string sigStr &&
                     sigStr.IndexOf("<data>", StringComparison.InvariantCulture) >= 0))
                    return;
            }

            plistHandle?.Close();
            plistHandle = null;

            const string pkgPath = "PublicStaging";
            const string pathPrefix = "/private/var/mobile/Media";

            var targetName = pkgPath + "/staging.dimage";
            var mountName = pathPrefix + "/" + targetName;

            imageStream = new FileStream(deviceImagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var sig = File.ReadAllBytes(deviceImageSignaturePath);

            switch (mode) {
                case DiskImageUploadMode.UploadImage:
                    var handle = GCHandle.Alloc(imageStream);
                    mounter.mobile_image_mounter_upload_image(mounterHandle, imageType, (uint)imageStream.Length,
                        sig, (ushort)sig.Length, MounterUploadCallback, GCHandle.ToIntPtr(handle));
                    handle.Free();
                    break;
                case DiskImageUploadMode.AFC:
                    ReadOnlyCollection<string> strs;
                    if (afc!.afc_get_file_info(afcHandle, pkgPath, out strs) != AfcError.Success ||
                        afc.afc_make_directory(afcHandle, pkgPath) != AfcError.Success)
                        throw new Exception("Unable to create directory '" + pkgPath + "' on the device.");

                    ulong af = 0;
                    if (afc.afc_file_open(afcHandle, targetName, AfcFileMode.FopenWronly, ref af) !=
                        AfcError.Success)
                        throw new Exception("Unable to create file '" + targetName + "'.");

                    uint amount;
                    var buf = new byte[8192];
                    do {
                        amount = (uint)imageStream.Read(buf, 0, buf.Length);
                        if (amount > 0) {
                            uint written = 0, total = 0;
                            while (total < amount) {
                                if (afc.afc_file_write(afcHandle, af, buf, amount, ref written) != AfcError.Success) {
                                    afc.afc_file_close(afcHandle, af);
                                    throw new Exception("An AFC write error occurred.");
                                }

                                total += written;
                            }

                            if (total != amount) {
                                afc.afc_file_close(afcHandle, af);
                                throw new Exception("The developer image was not written completely.");
                            }
                        }
                    } while (amount > 0);

                    afc.afc_file_close(afcHandle, af);
                    break;
            }

            if (mounter.mobile_image_mounter_mount_image(mounterHandle, mountName, sig, (ushort)sig.Length,
                    imageType, out plistHandle) != MobileImageMounterError.Success)
                throw new Exception("Unable to mount developer image.");

            var result = PlistHelper.ReadPlistDictFromNode(plistHandle);
            if (!result.TryGetValue("Status", out var status) || status as string != "Complete")
                throw new Exception("Mount failed with status: " +
                                    (result.GetValueOrDefault("Status") ?? "N/A") + " and error: " +
                                    (result.GetValueOrDefault("Error") ?? "N/A"));
        }
        finally {
            imageStream?.Close();
            plistHandle?.Close();
            afcHandle?.Close();
            mounterHandle?.Close();
            serviceDescriptor?.Close();
            lockdownHandle?.Close();
            deviceHandle?.Close();
        }
    }
}
