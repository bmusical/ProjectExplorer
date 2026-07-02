using System.Runtime.InteropServices;

namespace ProjectExplorer.Shell.Interop;

/// <summary>
/// P/Invoke declarations for the Windows Shell thumbnail API
/// (IShellItemImageFactory), used to obtain true image/file thumbnails.
/// </summary>
internal static class ThumbnailNativeMethods
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    // IShellItemImageFactory IID: bcc18b79-ba16-442f-80c4-8a59c30c463b
    public static Guid IID_IShellItemImageFactory =
        new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
        public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
    }

    /// <summary>Flags controlling how the thumbnail is produced.</summary>
    [Flags]
    public enum SIIGBF
    {
        ResizeToFit = 0x00000000,
        BiggerSizeOk = 0x00000001,
        MemoryOnly = 0x00000002,
        IconOnly = 0x00000004,
        ThumbnailOnly = 0x00000008,
        InCacheOnly = 0x00000010,
    }
}

/// <summary>
/// COM interface used to request a thumbnail bitmap (HBITMAP) for a shell item.
/// </summary>
[ComImport]
[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    [PreserveSig]
    int GetImage(
        ThumbnailNativeMethods.SIZE size,
        ThumbnailNativeMethods.SIIGBF flags,
        out IntPtr phbm);
}
