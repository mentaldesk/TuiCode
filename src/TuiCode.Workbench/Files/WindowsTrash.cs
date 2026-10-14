using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Files;

/// <summary><c>SHFileOperationW</c> with <c>FOF_ALLOWUNDO</c>: plain P/Invoke, where <c>IFileOperation</c> would need COM interop.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTrash : ITrash
{
    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x4;
    private const ushort FOF_NOCONFIRMATION = 0x10;
    private const ushort FOF_ALLOWUNDO = 0x40;
    private const ushort FOF_NOERRORUI = 0x400;
    // Without it, an item the Recycle Bin can't take is deleted for good without a word.
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    public string Name => "Recycle Bin";

    public void Move(string path)
    {
        var from = Marshal.StringToHGlobalUni(Path.GetFullPath(path) + '\0');
        try
        {
            var operation = new ShFileOpStruct
            {
                Func = FO_DELETE,
                From = from,
                Flags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING,
            };
            var result = SHFileOperationW(ref operation);
            if (result != 0)
                throw new IOException($"Windows couldn't move it to the Recycle Bin (error 0x{result:X}).");
            if (operation.AnyOperationsAborted != 0)
                throw new IOException("It wasn't moved to the Recycle Bin.");
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShFileOpStruct
    {
        public IntPtr Window;
        public uint Func;
        public IntPtr From;
        public IntPtr To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public IntPtr NameMappings;
        public IntPtr ProgressTitle;
    }

    [DllImport("shell32.dll")]
    private static extern int SHFileOperationW(ref ShFileOpStruct operation);
}
