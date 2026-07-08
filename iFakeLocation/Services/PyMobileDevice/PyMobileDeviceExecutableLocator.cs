using iFakeLocation.Options;

namespace iFakeLocation.Services.PyMobileDevice;

internal static class PyMobileDeviceExecutableLocator {
    public static string Resolve(PyMobileDeviceOptions options) {
        if (!string.IsNullOrWhiteSpace(options.ExecutablePathOverride))
            return options.ExecutablePathOverride;

        var fileName = OperatingSystem.IsWindows() ? "pmd3.exe" : "pmd3";
        return Path.Combine(AppContext.BaseDirectory, "pmd3", fileName);
    }
}
