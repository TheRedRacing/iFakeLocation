namespace iFakeLocation.Services.PyMobileDevice;

public sealed record PyMobileDeviceResult(int ExitCode, string StandardOutput, string StandardError) {
    public bool Success => ExitCode == 0;
}
