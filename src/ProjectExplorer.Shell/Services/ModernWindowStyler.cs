using ProjectExplorer.Shell.Interop;

namespace ProjectExplorer.Shell.Services;

/// <summary>
/// Applies Windows 11 Fluent Design touches to WinForms windows and controls via
/// DWM/UxTheme P/Invoke, without requiring any UI framework change. Every call is
/// a best-effort no-op on Windows versions or platforms that don't support it.
/// </summary>
public static class ModernWindowStyler
{
    /// <summary>
    /// Rounds the corners of a top-level window, matching Windows 11's default
    /// window chrome. No-op on Windows 10 and earlier.
    /// </summary>
    public static void ApplyRoundedCorners(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        try
        {
            int preference = DwmNativeMethods.DWMWCP_ROUND;
            DwmNativeMethods.DwmSetWindowAttribute(
                windowHandle, DwmNativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // Non-Windows or a stripped runtime without dwmapi.dll.
        }
        catch (EntryPointNotFoundException)
        {
            // Windows build predates this DWM attribute.
        }
    }

    /// <summary>
    /// Switches a TreeView/ListView to Explorer's visual style (alternating row
    /// hover/selection colors, no dotted focus rectangle) instead of the plain
    /// Win32 control look.
    /// </summary>
    public static void ApplyExplorerListStyle(IntPtr controlHandle)
    {
        if (controlHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            DwmNativeMethods.SetWindowTheme(controlHandle, "Explorer", null);
        }
        catch (DllNotFoundException)
        {
            // Non-Windows or a stripped runtime without uxtheme.dll.
        }
    }
}
