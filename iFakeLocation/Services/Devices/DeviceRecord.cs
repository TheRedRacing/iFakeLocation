using System.Text;

namespace iFakeLocation.Services.Devices;

/// <summary>
/// Domain representation of a connected iDevice. Replaces the original DeviceInformation class,
/// stripped down to pure data -- device-mode/mount/location behavior now live in their own
/// services (DeveloperModeService, LocationSimulationService) that take a DeviceRecord as input.
/// </summary>
public sealed class DeviceRecord(string name, string udid, bool isNetwork, IReadOnlyDictionary<string, object?> properties) {
    public string Name { get; } = name;
    public string Udid { get; } = udid;
    public bool IsNetwork { get; } = isNetwork;
    public IReadOnlyDictionary<string, object?> Properties { get; } = properties;

    public string ProductVersion => (string)Properties["ProductVersion"]!;

    public int MajorIosVersion => int.Parse(ProductVersion.Split('.')[0]);

    public string DisplayName {
        get {
            var sb = new StringBuilder().Append(Name).Append(" (");

            if (Properties.TryGetValue("ProductType", out var productType) && productType is string productTypeStr) {
                sb.Append(ProductNameCatalog.RealProductName.TryGetValue(productTypeStr, out var friendly)
                    ? friendly
                    : productTypeStr);
            }

            if (Properties.TryGetValue("ProductVersion", out var version))
                sb.Append("; iOS ").Append(version);

            sb.Append(") [").Append(IsNetwork ? "Wi-Fi" : "USB").Append(']');
            return sb.ToString();
        }
    }
}
