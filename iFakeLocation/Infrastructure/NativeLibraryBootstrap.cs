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
        var ridPrefix = OperatingSystem.IsMacOS() ? "osx" : OperatingSystem.IsLinux() ? "ubuntu" : "linux";

        // dyld/the runtime's bare-name native-library probing only ever checks
        // AppContext.BaseDirectory (confirmed empirically) -- the alias must always be written
        // there, never into a "runtimes/<rid>/native" subfolder, even though that's often where
        // the *source* versioned file actually lives.
        var runtimesRoot = Path.Combine(AppContext.BaseDirectory, "runtimes");
        // A self-contained per-RID publish flattens assets straight into the base directory. A
        // framework-dependent build (e.g. `dotnet run`, no -r pinned) instead keeps every RID the
        // package ships as a sibling folder under "runtimes/" regardless of the host machine's
        // actual RID -- e.g. on Apple Silicon, iMobileDevice-net has no "osx-arm64" native build
        // at all, only "osx-x64", so an exact-RID-match search would find nothing there. Search
        // every OS-appropriate sibling folder for a source file instead of assuming an exact match.
        var sourceDirectories = new List<string> { AppContext.BaseDirectory };
        if (Directory.Exists(runtimesRoot)) {
            sourceDirectories.AddRange(
                Directory.EnumerateDirectories(runtimesRoot, ridPrefix + "*")
                    .Select(dir => Path.Combine(dir, "native")));
        }

        foreach (var bareName in AliasBareNames) {
            var aliasPath = Path.Combine(AppContext.BaseDirectory, "lib" + bareName + extension);
            if (File.Exists(aliasPath))
                continue;

            var candidate = sourceDirectories
                .Where(Directory.Exists)
                // Prefer the shortest matching versioned filename (e.g. libimobiledevice-1.0.dylib
                // over libimobiledevice-1.0.6.dylib) as the canonical build; skip the unrelated
                // "++" (C++ API) variant.
                .SelectMany(dir => Directory.EnumerateFiles(dir, "lib" + bareName + "-*" + extension))
                .Where(f => !Path.GetFileName(f).Contains("++"))
                .OrderBy(f => f.Length)
                .FirstOrDefault();

            if (candidate == null)
                continue;

            try {
                File.Copy(candidate, aliasPath);
            }
            catch {
                // Best-effort: if this fails (read-only install dir, race with another instance,
                // etc.) NativeLibraries.Load()/subsequent P/Invokes will surface whatever the
                // underlying problem actually is.
            }
        }
    }
}
