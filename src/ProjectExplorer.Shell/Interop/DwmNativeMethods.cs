using System.Runtime.InteropServices;

namespace ProjectExplorer.Shell.Interop;

/// <summary>
/// P/Invoke declarations for dwmapi.dll and uxtheme.dll, used to opt WinForms
/// windows and controls into Windows 11 Fluent Design visuals.
/// </summary>
internal static class DwmNativeMethods
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    public static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    // DWMWINDOWATTRIBUTE values (Windows 11)
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    // DWM_WINDOW_CORNER_PREFERENCE values
    public const int DWMWCP_ROUND = 2;
}
