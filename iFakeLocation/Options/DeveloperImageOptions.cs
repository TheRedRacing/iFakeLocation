namespace iFakeLocation.Options;

/// <summary>
/// Configurable sources/paths for developer disk image resolution. Defaults match the
/// hardcoded values used by the original implementation.
/// </summary>
public sealed class DeveloperImageOptions {
    public const string SectionName = "DeveloperImages";

    public string ImageBasePath { get; set; } = "DeveloperImages";

    public string OverrideManifestUrl { get; set; } =
        "https://raw.githubusercontent.com/master131/iFakeLocation/master/updates.json";

    public string ZipRepoFindUrl { get; set; } =
        "https://github.com/haikieu/xcode-developer-disk-image-all-platforms/find/master?_pjax=%23js-repo-pjax-container";

    public string ZipRepoTreeListUrlPrefix { get; set; } =
        "https://github.com/haikieu/xcode-developer-disk-image-all-platforms/tree-list/";

    public string ZipRepoRawUrlPrefix { get; set; } =
        "https://github.com/haikieu/xcode-developer-disk-image-all-platforms/raw/master/";

    public string ZipRepoDefaultTreeList { get; set; } = "89cdf804bd416d0d6ba3f958b5c6d086cb914fa1";

    public string LegacyRepoFindUrl { get; set; } =
        "https://github.com/xushuduo/Xcode-iOS-Developer-Disk-Image/find/master?_pjax=%23js-repo-pjax-container";

    public string LegacyRepoTreeListUrlPrefix { get; set; } =
        "https://github.com/xushuduo/Xcode-iOS-Developer-Disk-Image/tree-list/";

    public string LegacyRepoRawUrlPrefix { get; set; } =
        "https://github.com/xushuduo/Xcode-iOS-Developer-Disk-Image/raw/master/";

    public string LegacyRepoDefaultTreeList { get; set; } = "795fc91f28cb3884edc45b876482911c797de85c";
}
