namespace iFakeLocation.Options;

public sealed class PyMobileDeviceOptions {
    public const string SectionName = "PyMobileDevice";

    /// <summary>
    /// Full path to the pymobiledevice3-compatible executable to invoke. When unset, resolves to
    /// the bundled "pmd3"/"pmd3.exe" shipped next to this application (a PyInstaller-frozen
    /// build -- see ARCHITECTURE.md). Overridable for local development against a plain
    /// virtualenv install (e.g. "/path/to/venv/bin/pymobiledevice3") before the frozen executable
    /// exists.
    /// </summary>
    public string? ExecutablePathOverride { get; set; }

    /// <summary>Max time to wait for a command expected to exit on its own (queries, mount, clear).</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Max time to wait for a "fire and hold" command (simulate-location set, which does not
    /// exit on its own -- see ARCHITECTURE.md) to report success before giving up.
    /// </summary>
    public TimeSpan LocationPushTimeout { get; set; } = TimeSpan.FromSeconds(15);
}
