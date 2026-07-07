using System.Reflection;
using System.Runtime.InteropServices;
using iMobileDevice.Plist;

namespace iFakeLocation.Interop;

/// <summary>
/// P/Invoke declarations that aren't already exposed by the iMobileDevice-net package's own
/// wrapper classes (currently just plist_new_data, needed for the personalized-image/TSS flow).
/// </summary>
internal static class NativeMethods {
    static NativeMethods() {
        NativeLibraryResolver.EnsureRegisteredFor(Assembly.GetExecutingAssembly());
    }

    [DllImport(PlistNativeMethods.LibraryName, EntryPoint = "plist_new_data", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe PlistHandle plist_new_data(byte* val, ulong length);

    public static unsafe PlistHandle plist_new_data(byte[] val, int length) {
        fixed (byte* ptr = val)
            return plist_new_data(ptr, (ulong)length);
    }
}
