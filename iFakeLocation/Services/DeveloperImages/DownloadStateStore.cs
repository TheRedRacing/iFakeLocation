using System.Collections.Concurrent;

namespace iFakeLocation.Services.DeveloperImages;

/// <summary>
/// Tracks in-progress developer-image downloads keyed by iOS version. Replaces the original
/// static `Dictionary&lt;string, DownloadState&gt; Downloads` field + bare `lock()` in Program.cs.
/// </summary>
public sealed class DownloadStateStore {
    private readonly ConcurrentDictionary<string, DownloadState> _downloads = new();

    /// <summary>
    /// Returns the existing download for <paramref name="iosVersion"/> if one is already
    /// tracked, otherwise creates, registers, and starts a new one. Idempotent: unlike the
    /// original app (which always called `Start()` on a freshly constructed state even when one
    /// already existed for that version, wastefully re-downloading in a race), a concurrent
    /// request for the same version reuses the same in-flight download.
    /// </summary>
    public DownloadState GetOrStart(string iosVersion, Func<DownloadState> factory) {
        return _downloads.GetOrAdd(iosVersion, _ => {
            var state = factory();
            state.Start();
            return state;
        });
    }

    public bool TryGet(string iosVersion, out DownloadState state) {
        return _downloads.TryGetValue(iosVersion, out state!);
    }
}
