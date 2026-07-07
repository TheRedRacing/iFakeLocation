using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Options;
using iFakeLocation.Contracts;
using iFakeLocation.Options;
using iFakeLocation.Services.Devices;

namespace iFakeLocation.Services.DeveloperImages;

public sealed class DeveloperImageService : IDeveloperImageService {
    private static readonly string[] MobileImageFileList = ["DeveloperDiskImage.dmg", "DeveloperDiskImage.dmg.signature"];
    private static readonly string[] PersonalisedImageFileList = ["Image.dmg", "BuildManifest.plist", "Image.dmg.trustcache"];

    // "12.4" devices actually use the 12.3 image set -- preserved from the original app.
    private static readonly IReadOnlyDictionary<string, string> VersionMapping = new Dictionary<string, string> {
        {"12.4", "12.3"},
    };

    private readonly HttpClient _httpClient;
    private readonly DeveloperImageOptions _options;
    private readonly DownloadStateStore _downloadStateStore;
    private readonly ILogger<DeveloperImageService> _logger;

    private readonly ConcurrentDictionary<string, string> _legacyUrlCache = new();
    private readonly ConcurrentDictionary<string, string> _zipUrlCache = new();
    private readonly ConcurrentDictionary<string, string> _overrideUrlCache = new();
    private readonly SemaphoreSlim _remoteCatalogLock = new(1, 1);
    private bool _remoteCatalogPopulated;

    public DeveloperImageService(HttpClient httpClient, IOptions<DeveloperImageOptions> options,
        DownloadStateStore downloadStateStore, ILogger<DeveloperImageService> logger) {
        _httpClient = httpClient;
        _options = options.Value;
        _downloadStateStore = downloadStateStore;
        _logger = logger;
    }

    public string GetSoftwareVersion(DeviceRecord device) {
        var parts = device.ProductVersion.Split('.');
        var v = parts[0] + "." + parts[1];
        return VersionMapping.GetValueOrDefault(v, v);
    }

    public bool HasImageForDevice(DeviceRecord device, out string[]? paths) =>
        HasImageForDevice(GetSoftwareVersion(device), File.Exists, out paths);

    /// <summary>Overload used directly by unit tests to avoid touching the real filesystem.</summary>
    internal bool HasImageForDevice(string versionString, Func<string, bool> fileExists, out string[]? paths) {
        var expectedFiles = (int.Parse(versionString.Split('.')[0]) >= 17 ? PersonalisedImageFileList : MobileImageFileList)
            .Select(p => Path.Combine(_options.ImageBasePath, versionString, p))
            .ToArray();

        var s = Path.DirectorySeparatorChar;
        string[][] directoryPrefixes = [
            [],
            [$".{s}..{s}"],
            [$".{s}..{s}..{s}"],
        ];

        foreach (var prefixParts in directoryPrefixes) {
            var prefix = string.Concat(prefixParts);
            var candidates = expectedFiles.Select(f => prefix + f).ToArray();
            if (candidates.All(fileExists)) {
                paths = candidates;
                return true;
            }
        }

        paths = null;
        return false;
    }

    public async Task<DependencyCheckResponse> CheckDependenciesAsync(DeviceRecord device, CancellationToken cancellationToken = default) {
        var versionString = GetSoftwareVersion(device);
        var hasImages = HasImageForDevice(device, out _);

        if (!hasImages) {
            var links = await GetLinksForDeviceAsync(device, versionString, cancellationToken).ConfigureAwait(false);
            if (links == null)
                throw new UnsupportedIosVersionException();

            _downloadStateStore.GetOrStart(versionString, () => {
                var needsZipExtraction = links.Any(l => l.Item1.EndsWith(".zip", StringComparison.InvariantCultureIgnoreCase));
                var state = new DownloadState(_httpClient, links.Select(l => l.Item1).ToArray(), links.Select(l => l.Item2).ToArray());
                if (needsZipExtraction)
                    state.DownloadCompleted += (sender, _) => ExtractKnownFilesFromZips((DownloadState)sender!);
                return state;
            });
        }

        return new DependencyCheckResponse(hasImages, versionString);
    }

    public DownloadProgressResponse GetDownloadProgress(string iosVersion) {
        if (!_downloadStateStore.TryGet(iosVersion, out var state))
            throw new DownloadStateNotFoundException();

        if (state.Error != null)
            throw new InvalidOperationException(state.Error.ToString(), state.Error);

        return state.Done
            ? new DownloadProgressResponse(true, null, null)
            : new DownloadProgressResponse(false, Path.GetFileName(state.Paths[state.CurrentIndex]), state.Progress);
    }

    private static void ExtractKnownFilesFromZips(DownloadState state) {
        foreach (var file in state.Paths) {
            if (!file.EndsWith(".zip", StringComparison.InvariantCultureIgnoreCase))
                continue;

            using (var archive = ZipFile.OpenRead(file)) {
                foreach (var entry in archive.Entries) {
                    var entryFileName = entry.FullName.Replace('\\', '/').Split('/').Last();
                    if (string.IsNullOrEmpty(entryFileName) || !IsKnownImageFileName(entryFileName))
                        continue;

                    var destination = Path.Combine(Path.GetDirectoryName(file)!, entryFileName);
                    entry.ExtractToFile(destination, overwrite: true);
                }
            }

            File.Delete(file);
        }
    }

    private static bool IsKnownImageFileName(string fileName) =>
        MobileImageFileList.Contains(fileName) || PersonalisedImageFileList.Contains(fileName);

    private async Task<Tuple<string, string>[]?> GetLinksForDeviceAsync(DeviceRecord device, string versionString, CancellationToken cancellationToken) {
        await EnsureRemoteCatalogPopulatedAsync(cancellationToken).ConfigureAwait(false);

        if (!_overrideUrlCache.TryGetValue(versionString, out var url) &&
            !_zipUrlCache.TryGetValue(versionString, out url) &&
            !_legacyUrlCache.TryGetValue(versionString, out url))
            return null;

        if (url.EndsWith(".zip", StringComparison.InvariantCultureIgnoreCase)) {
            return [
                new Tuple<string, string>(url, Path.Combine(_options.ImageBasePath, versionString, versionString + ".zip")),
            ];
        }

        return [
            new Tuple<string, string>(url, Path.Combine(_options.ImageBasePath, versionString, "DeveloperDiskImage.dmg")),
            new Tuple<string, string>(url + ".signature", Path.Combine(_options.ImageBasePath, versionString, "DeveloperDiskImage.dmg.signature")),
        ];
    }

    private async Task EnsureRemoteCatalogPopulatedAsync(CancellationToken cancellationToken) {
        if (_remoteCatalogPopulated)
            return;

        await _remoteCatalogLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            if (_remoteCatalogPopulated)
                return;

            await PopulateLegacyCatalogAsync(cancellationToken).ConfigureAwait(false);
            await PopulateZipCatalogAsync(cancellationToken).ConfigureAwait(false);
            await PopulateOverrideCatalogAsync(cancellationToken).ConfigureAwait(false);

            _remoteCatalogPopulated = true;
        }
        finally {
            _remoteCatalogLock.Release();
        }
    }

    private async Task PopulateLegacyCatalogAsync(CancellationToken cancellationToken) {
        var treeList = _options.LegacyRepoDefaultTreeList;
        try {
            treeList = await ResolveTreeListIdAsync(_options.LegacyRepoFindUrl, treeList, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to resolve legacy developer-image repo tree list; using default");
        }

        try {
            using var request = new HttpRequestMessage(HttpMethod.Get, _options.LegacyRepoTreeListUrlPrefix + treeList);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var paths = body.Split('"').Where(s => s.EndsWith(".dmg", StringComparison.InvariantCultureIgnoreCase));
            foreach (var path in paths)
                _legacyUrlCache[path.Split('/')[1].Split(' ')[0]] = _options.LegacyRepoRawUrlPrefix + path;
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to populate legacy developer-image catalog");
        }
    }

    private async Task PopulateZipCatalogAsync(CancellationToken cancellationToken) {
        var treeList = _options.ZipRepoDefaultTreeList;
        try {
            treeList = await ResolveTreeListIdAsync(_options.ZipRepoFindUrl, treeList, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to resolve zip developer-image repo tree list; using default");
        }

        try {
            using var request = new HttpRequestMessage(HttpMethod.Get, _options.ZipRepoTreeListUrlPrefix + treeList);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var paths = body.Split('"')
                .Where(s => s.EndsWith(".zip", StringComparison.InvariantCultureIgnoreCase) &&
                            s.Contains("iPhoneOS", StringComparison.InvariantCulture));
            foreach (var path in paths)
                _zipUrlCache[Path.GetFileNameWithoutExtension(path.Split('/').Last())] = _options.ZipRepoRawUrlPrefix + path;
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to populate zip developer-image catalog");
        }
    }

    private async Task PopulateOverrideCatalogAsync(CancellationToken cancellationToken) {
        try {
            using var response = await _httpClient.GetAsync(_options.OverrideManifestUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("images", out var images)) {
                foreach (var property in images.EnumerateObject())
                    _overrideUrlCache[property.Name] = property.Value.GetString()!;
            }
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to populate override developer-image manifest");
        }
    }

    /// <summary>
    /// GitHub's "find file" endpoint returns an opaque tree-list id used by its companion
    /// tree-list endpoint; scrape it the same way the original app did. Falls back to the
    /// last-known-good id (baked into DeveloperImageOptions) if this ever breaks, since GitHub
    /// doesn't guarantee this undocumented endpoint's stability.
    /// </summary>
    private async Task<string> ResolveTreeListIdAsync(string findUrl, string fallback, CancellationToken cancellationToken) {
        using var request = new HttpRequestMessage(HttpMethod.Get, findUrl);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        const string marker = "/tree-list/";
        var idx = body.IndexOf(marker, StringComparison.InvariantCultureIgnoreCase);
        if (idx == -1)
            return fallback;

        var end = body.IndexOf('"', idx);
        return end == -1 ? fallback : body.Substring(idx + marker.Length, end - (idx + marker.Length));
    }
}
