using System.Diagnostics;
using System.Runtime.InteropServices;

namespace iFakeLocation.Infrastructure;

internal static class BrowserLauncher {
    public static void Open(string url) {
        try {
            Process.Start(url);
        }
        catch {
            // hack because of this: https://github.com/dotnet/corefx/issues/10361
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                var escapedUrl = url.Replace("&", "^&");
                Process.Start(new ProcessStartInfo("cmd", $"/c start {escapedUrl}") { CreateNoWindow = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                Process.Start("open", url);
            }
            else {
                throw;
            }
        }
    }
}
