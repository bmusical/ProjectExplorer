using ProjectExplorer.Shell.Interop;

namespace ProjectExplorer.Shell.Services;

/// <summary>
/// Forces a window to the foreground even when the request comes from a different process
/// than the one Windows currently considers foreground — needed because plain
/// SetForegroundWindow (and Form.Activate()) is silently ignored by the foreground-lock
/// timeout in that situation. The standard workaround is to briefly attach this thread's
/// input queue to the current foreground window's thread, which Windows treats as
/// permission to switch focus, then detach again immediately.
/// </summary>
public static class WindowActivator
{
    public static void ForceToForeground(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return;

        try
        {
            WindowActivationNativeMethods.ShowWindow(windowHandle, WindowActivationNativeMethods.SW_RESTORE);

            var foregroundWindow = WindowActivationNativeMethods.GetForegroundWindow();
            if (foregroundWindow == windowHandle)
                return;

            var foregroundThreadId = WindowActivationNativeMethods.GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
            var currentThreadId = WindowActivationNativeMethods.GetCurrentThreadId();

            var attached = foregroundThreadId != currentThreadId
                && WindowActivationNativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);

            try
            {
                WindowActivationNativeMethods.SetForegroundWindow(windowHandle);
            }
            finally
            {
                if (attached)
                    WindowActivationNativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
        }
        catch (DllNotFoundException)
        {
            // Non-Windows or a stripped runtime without user32.dll — best-effort only.
        }
    }
}
