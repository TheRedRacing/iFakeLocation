namespace iFakeLocation.Services.PyMobileDevice;

/// <summary>An unexpected (not specifically typed/handled) pymobiledevice3 command failure.</summary>
public sealed class PyMobileDeviceException(string message, string? stderr = null) : Exception(message) {
    public string? Stderr { get; } = stderr;
}
