namespace iFakeLocation.Options;

public sealed class ServerOptions {
    public const string SectionName = "Server";

    /// <summary>
    /// Whether to automatically open the system default browser pointing at the bound
    /// address when the server starts (matches the original app's first-run behavior).
    /// </summary>
    public bool AutoLaunchBrowser { get; set; } = true;
}
