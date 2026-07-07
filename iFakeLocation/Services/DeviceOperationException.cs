namespace iFakeLocation.Services;

/// <summary>
/// Base type for the small set of expected, user-facing failure modes that occur while
/// operating on a device (as opposed to unexpected bugs, which surface as generic 500s).
/// </summary>
public abstract class DeviceOperationException(string message) : Exception(message);

public sealed class DeviceNotFoundException(string udid)
    : DeviceOperationException($"Unable to find the specified device '{udid}'. Are you sure it is connected?");

public sealed class DeveloperModeHiddenException()
    : DeviceOperationException("Please turn on Developer Mode first via Settings >> Privacy & Security on your device.");

public sealed class DeveloperImagesMissingException()
    : DeviceOperationException("The developer images for the specified device are missing.");

public sealed class UnsupportedIosVersionException()
    : DeviceOperationException("Your device's iOS version is not supported at this time.");
