namespace ProjectExplorer.Shell;

/// <summary>
/// Service for showing the native Windows file/folder Properties dialog.
/// </summary>
public interface IShellPropertiesProvider
{
    /// <summary>
    /// Show the Windows Properties dialog for the given path.
    /// </summary>
    void ShowPropertiesDialog(string path, IntPtr ownerHwnd);
}
