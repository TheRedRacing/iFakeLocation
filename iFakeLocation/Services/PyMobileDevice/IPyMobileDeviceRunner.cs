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
}
