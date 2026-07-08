using ProjectExplorer.Shell.Interop;

namespace ProjectExplorer.Shell.Services;

/// <summary>
/// Implementation of IShellPropertiesProvider using ShellExecuteEx's "properties" verb.
/// </summary>
public class ShellPropertiesProvider : IShellPropertiesProvider
{
    public void ShowPropertiesDialog(string path, IntPtr ownerHwnd)
    {
        var info = new ShellExecuteNativeMethods.SHELLEXECUTEINFO
        {
            lpVerb = "properties",
            lpFile = path,
            hwnd = ownerHwnd,
            nShow = ShellExecuteNativeMethods.SW_SHOWNORMAL,
            fMask = ShellExecuteNativeMethods.SEE_MASK_INVOKEIDLIST,
        };
        info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(info);

        ShellExecuteNativeMethods.ShellExecuteEx(ref info);
    }
}
