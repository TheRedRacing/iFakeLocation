namespace iFakeLocation.Services.PyMobileDevice;

public interface IPyMobileDeviceRunner {
    /// <summary>
    /// Runs a pymobiledevice3 command that is expected to exit on its own (device
    /// queries, mounting, clearing a simulated location -- everything except
    /// <see cref="RunFireAndForgetAsync"/>'s use case). Always always passes the global
    /// `-v` flag (verbose logging goes to stderr; stdout stays clean for commands that return
    /// JSON) so callers can inspect diagnostic detail on failure.
    /// </summary>
    Task<PyMobileDeviceResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a pymobiledevice3 command that does NOT reliably exit on its own once it has
    /// succeeded (confirmed for `developer dvt simulate-location set` -- see ARCHITECTURE.md).
    /// Streams combined stdout+stderr line by line: if <paramref name="successMarker"/> matches a
    /// line, the process is killed and this returns true. If the process exits with code 0 first,
    /// this returns true. If it exits with a non-zero code, or neither happens before
    /// <paramref name="timeout"/> elapses, the process is killed and this returns false.
    /// </summary>
    Task<bool> RunFireAndForgetAsync(IReadOnlyList<string> arguments, Func<string, bool> successMarker,
        TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a pymobiledevice3 command whose effect on the device only lasts as long as the
    /// process (and the DTX/Instruments channel it opens) stays alive -- confirmed for
    /// `developer dvt simulate-location set`: the CLI deliberately blocks forever
    /// (`signal.sigwait([SIGINT, SIGTERM])` in pymobiledevice3's own source) after applying the
    /// simulated location, exactly mirroring how Xcode's own "Simulate Location" only stays in
    /// effect while its debug session is attached. Killing the process (as
    /// <see cref="RunFireAndForgetAsync"/> always does) tears down that channel and the device
    /// reverts to real GPS -- fine for a one-off read (Apple Maps shows the last cached fix) but
    /// broken for anything that polls continuously (see ARCHITECTURE.md). Waits for
    /// <paramref name="successMarker"/> to match a line (or the process to exit) before returning,
    /// but -- unlike <see cref="RunFireAndForgetAsync"/> -- leaves the process running afterward on
    /// success, tracked under <paramref name="key"/> (typically the device UDID). Any previously
    /// tracked process under the same key is killed first, whether or not this call itself
    /// succeeds.
    /// </summary>
    Task<bool> StartPersistentAsync(string key, IReadOnlyList<string> arguments, Func<string, bool> successMarker,
        TimeSpan startupTimeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kills the persistent process tracked under <paramref name="key"/> (see
    /// <see cref="StartPersistentAsync"/>), if any. Safe to call when none is tracked.
    /// </summary>
    void StopPersistent(string key);
}
