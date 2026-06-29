using System.Drawing;

namespace ProjectExplorer.Shell;

/// <summary>
/// Service for displaying the native Windows shell context menu.
/// </summary>
public interface IShellContextMenuProvider
{
    /// <summary>
    /// Show the Windows shell context menu for the given file/folder paths
    /// at the specified screen position.
    /// </summary>
    /// <param name="ownerHwnd">Handle of the owning window</param>
    /// <param name="paths">Array of full paths to files/folders</param>
    /// <param name="screenPoint">Screen coordinates where the menu should appear</param>
    void ShowContextMenu(IntPtr ownerHwnd, string[] paths, Point screenPoint);
}
