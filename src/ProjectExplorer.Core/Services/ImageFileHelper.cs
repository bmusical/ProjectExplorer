namespace ProjectExplorer.Core.Services;

/// <summary>
/// Small, dependency-free helpers for identifying image files by extension.
/// Kept in Core so both the WinForms ListView and the image viewer share one
/// source of truth (and it is trivially unit-testable).
/// </summary>
public static class ImageFileHelper
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".ico"
    };

    /// <summary>True when the extension (with or without a leading dot) is a supported image type.</summary>
    public static bool IsImageExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        var ext = extension.StartsWith('.') ? extension : "." + extension;
        return ImageExtensions.Contains(ext);
    }

    /// <summary>True when the given file path has a supported image extension.</summary>
    public static bool IsImageFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var dot = path.LastIndexOf('.');
        if (dot < 0)
            return false;

        return IsImageExtension(path.Substring(dot));
    }
}
