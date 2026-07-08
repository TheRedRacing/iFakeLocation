using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using iFakeLocation.Options;

namespace iFakeLocation.Services.PyMobileDevice;

public sealed class PyMobileDeviceRunner(IOptions<PyMobileDeviceOptions> options, ILogger<PyMobileDeviceRunner> logger) : IPyMobileDeviceRunner {
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
