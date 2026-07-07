using System.Runtime.InteropServices;

namespace iFakeLocation.Infrastructure;

internal static class NativeLibraryBootstrap {
    // iMobileDevice-net ships its native libimobiledevice/libplist binaries under
    // version-suffixed names (e.g. libimobiledevice-1.0.dylib). Its own internal native-library
    // resolver -- registered lazily, the first time any of its P/Invoke-declaring classes is
    // touched -- does not reliably resolve those version-suffixed names on macOS/Linux (verified:
    // it falls back to probing only the bare "libimobiledevice.dylib"/"imobiledevice.dylib"
    // names). We can't register our own competing resolver for that assembly to fix this
    // ourselves: .NET only allows one DllImportResolver per assembly, and the package's own
    // internal registration (which always runs, unconditionally) throws if one is already set.
    // So instead, before its resolver ever runs, we make sure an unversioned alias file exists
    // next to each versioned binary -- letting its (otherwise-correct) bare-name probing succeed.
    private static readonly string[] AliasBareNames = ["imobiledevice", "plist"];

    public static bool TryLoad(out Exception? error) {
        try {
            EnsureUnversionedAliasesExist();
            iMobileDevice.NativeLibraries.Load();
            error = null;
            return true;
        }
        catch (Exception ex) {
            error = ex;
            return false;
        }
    }

    private static void EnsureUnversionedAliasesExist() {
        // Windows assets already ship unversioned (imobiledevice.dll, plist.dll) -- nothing to do.
        if (OperatingSystem.IsWindows())
            return;

        var extension = OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        var directories = new[] {
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native"),
        };

        foreach (var directory in directories) {
            if (!Directory.Exists(directory))
                continue;

            foreach (var bareName in AliasBareNames) {
                var aliasPath = Path.Combine(directory, "lib" + bareName + extension);
                if (File.Exists(aliasPath))
                    continue;

                // Prefer the shortest matching versioned filename (e.g. libimobiledevice-1.0.dylib
                // over libimobiledevice-1.0.6.dylib) as the canonical build; skip the unrelated
                // "++" (C++ API) variant.
                var candidate = Directory.EnumerateFiles(directory, "lib" + bareName + "-*" + extension)
                    .Where(f => !Path.GetFileName(f).Contains("++"))
                    .OrderBy(f => f.Length)
                    .FirstOrDefault();

                if (candidate == null)
                    continue;

                try {
                    File.Copy(candidate, aliasPath);
                }
                catch {
                    // Best-effort: if this fails (read-only install dir, race with another
                    // instance, etc.) NativeLibraries.Load()/subsequent P/Invokes will surface
                    // whatever the underlying problem actually is.
                }
            }
        }
    }
}
