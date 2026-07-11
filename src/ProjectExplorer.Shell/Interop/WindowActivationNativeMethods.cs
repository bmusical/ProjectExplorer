using System.Runtime.InteropServices;

namespace ProjectExplorer.Shell.Interop;

/// <summary>
/// P/Invoke declarations for user32.dll window-activation APIs, used to reliably steal
/// foreground focus for the "Prevent multiple copies" Focus on Run setting — plain
/// Form.Activate() is often silently ignored by Windows' foreground-lock timeout when the
/// request comes from a different process than the one currently in the foreground.
/// </summary>
internal static class WindowActivationNativeMethods
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    public const int SW_RESTORE = 9;
}
