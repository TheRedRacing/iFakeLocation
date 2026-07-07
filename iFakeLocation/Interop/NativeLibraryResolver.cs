using System.Reflection;
using System.Runtime.InteropServices;

namespace iFakeLocation.Interop;

/// <summary>
/// Resolves bare P/Invoke library names (e.g. "plist") declared in THIS assembly to the
/// version-suffixed native binaries bundled by the iMobileDevice-net NuGet package.
///
/// The original app relied on reflecting into an internal `iMobileDevice.LibraryResolver`
/// method to reuse the package's own resolver delegate. That internal method no longer exists
/// on the currently-referenced package version (verified against iMobileDevice-net 1.3.17: the
/// type now only exposes a public parameterless `EnsureRegistered()`, which only registers a
/// resolver for the package's OWN assembly, not for callers). This class replaces that fragile
/// reflection hack with an explicit, self-contained resolver for our own DllImport declarations.
/// </summary>
internal static class NativeLibraryResolver {
    private static readonly string[] LinuxCandidateSuffixes = ["-2.0", "-1.0", ""];
    private static readonly string[] MacCandidateSuffixes = ["-2.0", "-1.0", ""];

    public static void EnsureRegisteredFor(Assembly assembly) {
        NativeLibrary.SetDllImportResolver(assembly, Resolve);
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) {
        foreach (var candidatePath in EnumerateCandidatePaths(libraryName)) {
            if (NativeLibrary.TryLoad(candidatePath, out var handle))
                return handle;
        }

        // Fall back to default OS probing (covers Windows, where "plist.dll" already matches
        // the bare library name and no custom resolution is actually required).
        return NativeLibrary.TryLoad(libraryName, assembly, searchPath, out var defaultHandle)
            ? defaultHandle
            : 0;
    }

    private static IEnumerable<string> EnumerateCandidatePaths(string libraryName) {
        var searchDirectories = new[] {
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native"),
        };

        foreach (var directory in searchDirectories) {
            foreach (var fileName in EnumerateCandidateFileNames(libraryName)) {
                yield return Path.Combine(directory, fileName);
            }
        }
    }

    private static IEnumerable<string> EnumerateCandidateFileNames(string libraryName) {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            yield return libraryName + ".dll";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            foreach (var suffix in MacCandidateSuffixes)
                yield return $"lib{libraryName}{suffix}.dylib";
        }
        else {
            foreach (var suffix in LinuxCandidateSuffixes)
                yield return $"lib{libraryName}{suffix}.so";
        }
    }
}
