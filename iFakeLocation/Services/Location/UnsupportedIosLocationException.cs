namespace iFakeLocation.Services.Location;

/// <summary>
/// iOS 17+ location simulation was never implemented in the original app either (the DVT
/// service dispatch is an empty stub) -- this rewrite preserves that limitation as-is and
/// just surfaces it as a clean typed 501 instead of an unhandled NotImplementedException.
/// </summary>
public sealed class UnsupportedIosLocationException()
    : Exception("Setting location is currently not supported for iOS 17 or newer.");
