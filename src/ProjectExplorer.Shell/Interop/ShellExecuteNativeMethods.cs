using System.Runtime.InteropServices;

namespace ProjectExplorer.Shell.Interop;

/// <summary>
/// P/Invoke declarations for ShellExecuteEx, used to invoke native shell verbs
/// (e.g. the "properties" verb to show the Properties dialog) on a real path.
/// </summary>
internal static class ShellExecuteNativeMethods
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    // Shows the Properties dialog's UI even when invoked outside Explorer.
    public const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;

    public const int SW_SHOWNORMAL = 1;
}
