using System.Drawing;

namespace ProjectExplorer.Shell;

/// <summary>
/// Service for retrieving file and folder icons from the Windows Shell API.
/// </summary>
public interface IShellIconProvider
{
    /// <summary>
    /// Get the icon for a specific file or folder path as Windows would display it.
    /// </summary>
    Icon GetFileIcon(string path, IconSize size, bool isFolder = false);

    /// <summary>
    /// Get the icon for a folder. Optionally get the "open" variant.
    /// </summary>
    Icon GetFolderIcon(IconSize size, bool open = false);

    /// <summary>
    /// Get the icon for a file type by extension (e.g., ".txt", ".cs").
    /// </summary>
    Icon GetIconByExtension(string extension, IconSize size);

    /// <summary>
    /// Convert an Icon to an ImageList-compatible bitmap with transparent background.
    /// </summary>
    Bitmap IconToBitmap(Icon icon, int size);
}
