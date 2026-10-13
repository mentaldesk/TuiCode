using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Files;

/// <summary><c>-[NSFileManager trashItemAtURL:resultingItemURL:error:]</c>, which Finder's Put Back can restore from.</summary>
[SupportedOSPlatform("macos")]
public sealed class MacTrash : ITrash
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private static readonly Lazy<IntPtr> Foundation =
        new(() => NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation"));

    public string Name => "Trash";

    public void Move(string path)
    {
        _ = Foundation.Value;
        // A folder's URL ends in '/', which would make a symlink to one trash the folder it points to.
        var isDirectory = Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is null;
        var pool = objc_autoreleasePoolPush();
        var utf8 = Marshal.StringToCoTaskMemUTF8(path);
        try
        {
            var text = Send(Class("NSString"), Selector("stringWithUTF8String:"), utf8);
            var url = Send(Class("NSURL"), Selector("fileURLWithPath:isDirectory:"), text, isDirectory ? (byte)1 : (byte)0);
            var manager = Send(Class("NSFileManager"), Selector("defaultManager"));
            if (SendBool(manager, Selector("trashItemAtURL:resultingItemURL:error:"), url, IntPtr.Zero, out var error) != 0)
                return;

            var description = error == IntPtr.Zero ? IntPtr.Zero : Send(error, Selector("localizedDescription"));
            var reason = description == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(description, Selector("UTF8String")));
            throw new IOException(reason ?? "The Trash refused it.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
            objc_autoreleasePoolPop(pool);
        }
    }

    private static IntPtr Class(string name) => objc_getClass(name);

    private static IntPtr Selector(string name) => sel_registerName(name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg, byte flag);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern byte SendBool(IntPtr receiver, IntPtr selector, IntPtr url, IntPtr resulting, out IntPtr error);

    [DllImport(ObjC, CharSet = CharSet.Ansi)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC, CharSet = CharSet.Ansi)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC)]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(ObjC)]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);
}
