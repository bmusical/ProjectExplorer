using System.Drawing;

namespace ProjectExplorer.Shell;

/// <summary>
/// Service for retrieving shell thumbnails for files (e.g., image previews).
/// </summary>
public interface IShellThumbnailProvider
{
    /// <summary>
    /// Get a thumbnail bitmap for the given file path.
    /// </summary>
    Bitmap? GetThumbnail(string path, Size size);
}
