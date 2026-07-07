using Microsoft.Extensions.Logging.Abstractions;
using iFakeLocation.Options;
using iFakeLocation.Services.DeveloperImages;
using iFakeLocation.Services.Devices;

namespace iFakeLocation.Tests.Services.DeveloperImages;

public class DeveloperImageServiceTests {
    private static DeveloperImageService CreateService() {
        var httpClient = new HttpClient();
        return new DeveloperImageService(httpClient, Microsoft.Extensions.Options.Options.Create(new DeveloperImageOptions()),
            new DownloadStateStore(), NullLogger<DeveloperImageService>.Instance);
    }

    private static DeviceRecord CreateDevice(string productVersion) =>
        new("Test Device", "udid-1", false, new Dictionary<string, object?> { ["ProductVersion"] = productVersion });

    [Theory]
    [InlineData("16.4.1", "16.4")]
    [InlineData("17.0", "17.0")]
    [InlineData("15.7.2", "15.7")]
    public void GetSoftwareVersion_ExtractsMajorMinor(string productVersion, string expected) {
        var service = CreateService();
        var device = CreateDevice(productVersion);

        Assert.Equal(expected, service.GetSoftwareVersion(device));
    }

    [Fact]
    public void GetSoftwareVersion_AppliesLegacyVersionMapping() {
        var service = CreateService();
        var device = CreateDevice("12.4.1");

        // 12.4 devices use the 12.3 image set -- preserved quirk from the original app.
        Assert.Equal("12.3", service.GetSoftwareVersion(device));
    }

    [Fact]
    public void HasImageForDevice_AllFilesPresentInCurrentDirectory_ReturnsTrue() {
        var service = CreateService();
        var existing = new HashSet<string>();

        var found = service.HasImageForDevice("16.4", path => existing.Contains(path), out var paths);
        // Nothing exists yet -- expect false first.
        Assert.False(found);

        var probedPaths = ProbeExpectedPaths("16.4");
        foreach (var p in probedPaths) existing.Add(p);

        found = service.HasImageForDevice("16.4", path => existing.Contains(path), out paths);
        Assert.True(found);
        Assert.NotNull(paths);
        Assert.Equal(2, paths!.Length); // DeveloperDiskImage.dmg + .signature for iOS < 17
    }

    [Fact]
    public void HasImageForDevice_Ios17Plus_UsesPersonalizedFileList() {
        var service = CreateService();
        var probedPaths = ProbeExpectedPaths("17.0");
        var existing = new HashSet<string>(probedPaths);

        var found = service.HasImageForDevice("17.0", path => existing.Contains(path), out var paths);

        Assert.True(found);
        Assert.Equal(3, paths!.Length); // Image.dmg + BuildManifest.plist + Image.dmg.trustcache
    }

    [Fact]
    public void HasImageForDevice_MissingFiles_ReturnsFalse() {
        var service = CreateService();

        var found = service.HasImageForDevice("16.4", _ => false, out var paths);

        Assert.False(found);
        Assert.Null(paths);
    }

    private static string[] ProbeExpectedPaths(string version) {
        // Mirrors the first (current-directory) path pattern the service checks.
        var service = CreateService();
        service.HasImageForDevice(version, _ => false, out _);
        var basePath = Path.Combine("DeveloperImages", version);
        return int.Parse(version.Split('.')[0]) >= 17
            ? [Path.Combine(basePath, "Image.dmg"), Path.Combine(basePath, "BuildManifest.plist"), Path.Combine(basePath, "Image.dmg.trustcache")]
            : [Path.Combine(basePath, "DeveloperDiskImage.dmg"), Path.Combine(basePath, "DeveloperDiskImage.dmg.signature")];
    }
}
