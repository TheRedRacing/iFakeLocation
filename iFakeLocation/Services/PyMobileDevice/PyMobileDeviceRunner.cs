using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using iFakeLocation.Options;

namespace iFakeLocation.Services.PyMobileDevice;

public sealed class PyMobileDeviceRunner(IOptions<PyMobileDeviceOptions> options, ILogger<PyMobileDeviceRunner> logger)
    : IPyMobileDeviceRunner, IDisposable {
    // Tracks the currently-running "persistent" process per key (device UDID), if any -- see
    // StartPersistentAsync. Disposed/killed on replacement, explicit StopPersistent, or app
    // shutdown (Dispose), never left to leak as a zombie holding a device's GPS hostage.
    private readonly ConcurrentDictionary<string, Process> persistentProcesses = new();


    public async Task<PyMobileDeviceResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default) {
        using var process = CreateProcess(arguments);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        logger.LogDebug("Running pymobiledevice3 {Arguments}", string.Join(' ', arguments));
        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(options.Value.CommandTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            TryKill(process);
            throw new PyMobileDeviceException(
                $"pymobiledevice3 command timed out after {options.Value.CommandTimeout}: {string.Join(' ', arguments)}");
        }

        return new PyMobileDeviceResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    public async Task<bool> RunFireAndForgetAsync(IReadOnlyList<string> arguments, Func<string, bool> successMarker,
        TimeSpan timeout, CancellationToken cancellationToken = default) {
        using var process = CreateProcess(arguments);
        var successTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnLine(string? line) {
            if (line == null) return;
            if (successMarker(line))
                successTcs.TrySetResult(true);
        }

        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);
        process.Exited += (_, _) => successTcs.TrySetResult(process.ExitCode == 0);
        process.EnableRaisingEvents = true;

        logger.LogDebug("Running pymobiledevice3 (fire-and-forget) {Arguments}", string.Join(' ', arguments));
        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(timeout);
        await using var registration = timeoutCts.Token.Register(() => successTcs.TrySetResult(false));
        await using var callerRegistration = cancellationToken.Register(() => successTcs.TrySetCanceled(cancellationToken));

        bool succeeded;
        try {
            succeeded = await successTcs.Task.ConfigureAwait(false);
        }
        finally {
            TryKill(process);
        }

        return succeeded;
    }

    public async Task<bool> StartPersistentAsync(string key, IReadOnlyList<string> arguments, Func<string, bool> successMarker,
        TimeSpan startupTimeout, CancellationToken cancellationToken = default) {
        // Always replace: a stale session under this key would otherwise fight the new one, and
        // there's no in-place "update the location" protocol -- one process = one set location.
        StopPersistent(key);

        var process = CreateProcess(arguments);
        var successTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnLine(string? line) {
            if (line == null) return;
            if (successMarker(line))
                successTcs.TrySetResult(true);
        }

        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);
        process.Exited += (_, _) => successTcs.TrySetResult(process.ExitCode == 0);
        process.EnableRaisingEvents = true;

        logger.LogDebug("Starting persistent pymobiledevice3 session {Key} {Arguments}", key, string.Join(' ', arguments));
        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(startupTimeout);
        await using var timeoutRegistration = timeoutCts.Token.Register(() => successTcs.TrySetResult(false));
        await using var callerRegistration = cancellationToken.Register(() => successTcs.TrySetCanceled(cancellationToken));

        bool succeeded;
        try {
            succeeded = await successTcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            succeeded = false;
        }

        if (succeeded && !process.HasExited) {
            // Deliberately NOT killed: this process (and the channel it holds open) IS the
            // mechanism keeping the simulated location active. It's handed off to
            // persistentProcesses and only torn down by a future StopPersistent/replacement/
            // Dispose call.
            persistentProcesses[key] = process;
        }
        else {
            TryKill(process);
            process.Dispose();
        }

        return succeeded;
    }

    public void StopPersistent(string key) {
        if (persistentProcesses.TryRemove(key, out var process)) {
            TryKill(process);
            process.Dispose();
        }
    }

    public void Dispose() {
        foreach (var key in persistentProcesses.Keys) {
            StopPersistent(key);
        }
    }

    private Process CreateProcess(IReadOnlyList<string> arguments) {
        var executablePath = PyMobileDeviceExecutableLocator.Resolve(options.Value);

        var startInfo = new ProcessStartInfo(executablePath) {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Global verbose flag: debug logging goes to stderr, stdout stays clean JSON for commands
        // that return it (verified empirically -- see ARCHITECTURE.md).
        startInfo.ArgumentList.Add("-v");
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return new Process { StartInfo = startInfo };
    }

    private void TryKill(Process process) {
        try {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) {
            logger.LogWarning(ex, "Failed to terminate a pymobiledevice3 subprocess");
        }
    }
}
