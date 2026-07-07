namespace iFakeLocation.Services.DeveloperImages;

/// <summary>
/// Tracks the sequential download of one or more files for a single iOS version (e.g. a
/// DeveloperDiskImage.dmg + .dmg.signature pair, or a single .zip). Ported from the original
/// nested `DownloadState` class in Program.cs, rewritten with real async/await instead of
/// manually chained `.ContinueWith()` continuations.
/// </summary>
public sealed class DownloadState {
    private readonly HttpClient _httpClient;

    public IReadOnlyList<string> Links { get; }
    public IReadOnlyList<string> Paths { get; }
    public int CurrentIndex { get; private set; }
    public double Progress { get; private set; }
    public Exception? Error { get; private set; }
    public bool Done { get; private set; }

    public event EventHandler? DownloadCompleted;

    public DownloadState(HttpClient httpClient, string[] links, string[] paths) {
        if (links.Length != paths.Length)
            throw new ArgumentException("Links and paths must have the same length.");

        _httpClient = httpClient;
        Links = links;
        Paths = paths;
    }

    /// <summary>Kicks off the download in the background; does not need to be awaited by the caller.</summary>
    public void Start() {
        _ = RunAsync();
    }

    /// <summary>Internal (not private) purely so tests can await completion directly instead of polling after Start().</summary>
    internal async Task RunAsync() {
        try {
            for (CurrentIndex = 0; CurrentIndex < Links.Count; CurrentIndex++) {
                Progress = 0;
                var path = Paths[CurrentIndex];
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                await DownloadOneAsync(Links[CurrentIndex], path).ConfigureAwait(false);
            }

            Done = true;
            DownloadCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) {
            Error = ex;
        }
    }

    private async Task DownloadOneAsync(string link, string destinationPath) {
        var incompletePath = destinationPath + ".incomplete";

        using (var response = await _httpClient.GetAsync(link, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false)) {
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            await using var sourceStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            await using var destStream = File.OpenWrite(incompletePath);

            if (!contentLength.HasValue) {
                await sourceStream.CopyToAsync(destStream).ConfigureAwait(false);
            }
            else {
                var buffer = new byte[8192];
                long totalBytesRead = 0;
                int bytesRead;
                while ((bytesRead = await sourceStream.ReadAsync(buffer).ConfigureAwait(false)) != 0) {
                    await destStream.WriteAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
                    totalBytesRead += bytesRead;
                    Progress = (double)totalBytesRead / contentLength.Value * 100.0;
                }
            }
        }

        if (File.Exists(destinationPath))
            File.Delete(destinationPath);
        File.Move(incompletePath, destinationPath);
    }
}
