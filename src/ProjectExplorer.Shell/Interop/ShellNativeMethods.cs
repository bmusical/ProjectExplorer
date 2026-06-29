using System.Runtime.InteropServices;

namespace ProjectExplorer.Shell.Interop;

/// <summary>
/// P/Invoke declarations for SHGetFileInfo and related Shell32 functions.
/// </summary>
internal static class ShellNativeMethods
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbSizeFileInfo,
        uint uFlags);

    [DllImport("shell32.dll")]
    public static extern void SHGetFileInfo(
        IntPtr pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbSizeFileInfo,
        uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        IntPtr psfi,
        uint cbSizeFileInfo,
        uint uFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    // SHGFI flags
    public const uint SHGFI_ICON = 0x000000100;
    public const uint SHGFI_DISPLAYNAME = 0x000000200;
    public const uint SHGFI_TYPENAME = 0x000000400;
    public const uint SHGFI_ATTRIBUTES = 0x000000800;
    public const uint SHGFI_ICONLOCATION = 0x000001000;
    public const uint SHGFI_EXETYPE = 0x000002000;
    public const uint SHGFI_SYSICONINDEX = 0x000004000;
    public const uint SHGFI_LINKOVERLAY = 0x000008000;
    public const uint SHGFI_SELECTED = 0x000010000;
    public const uint SHGFI_ATTR_SPECIFIED = 0x000020000;
    public const uint SHGFI_LARGEICON = 0x000000000;
    public const uint SHGFI_SMALLICON = 0x000000001;
    public const uint SHGFI_OPENICON = 0x000000002;
    public const uint SHGFI_SHELLICONSIZE = 0x000000004;
    public const uint SHGFI_PIDL = 0x000000008;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

    // File attribute constants for SHGetFileInfo
    public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    // SHGetImageList flags
    public const int SHIL_LARGE = 0x0;   // 32x32
    public const int SHIL_SMALL = 0x1;   // 16x16
    public const int SHIL_EXTRALARGE = 0x2; // 48x48
    public const int SHIL_JUMBO = 0x4;   // 256x256

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern int SHGetImageList(
        int iImageList,
        ref Guid riid,
        out IntPtr pvImageList);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint ExtractIconEx(
        string szFileName,
        int nIconIndex,
        IntPtr[]? phiconLarge,
        IntPtr[]? phiconSmall,
        uint nIcons);
}
