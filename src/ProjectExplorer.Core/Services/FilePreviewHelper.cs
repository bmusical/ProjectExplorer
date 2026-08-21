namespace ProjectExplorer.Core.Services;

/// <summary>
/// The kind of inline preview the WinForms file preview panel can render for a
/// given file. <see cref="None"/> means no in-app preview is available — the
/// UI still offers Open/Properties, just no rendered content.
/// </summary>
public enum FilePreviewKind
{
    None,
    Image,
    Text,
    Html,
    Markdown
}

/// <summary>
/// Classifies files by extension for the inline FileReference preview panel.
/// Kept in Core (alongside <see cref="ImageFileHelper"/>) so the classification
/// logic is shared and unit-testable independent of WinForms.
/// </summary>
public static class FilePreviewHelper
{
    /// <summary>
    /// Cap on how many bytes of a text file are read into the in-app preview.
    /// Larger files still open fine via the "Open" button; this just keeps the
    /// inline preview responsive.
    /// </summary>
    public const int MaxPreviewBytes = 256 * 1024;

    private static readonly HashSet<string> HtmlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm"
    };

    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".json", ".jsonc", ".xml", ".yml", ".yaml",
        ".csv", ".tsv", ".ini", ".cfg", ".config", ".conf", ".toml", ".env",
        ".cs", ".c", ".cpp", ".h", ".hpp", ".js", ".ts", ".jsx", ".tsx",
        ".py", ".rb", ".go", ".rs", ".java", ".kt", ".php",
        ".html", ".htm", ".css", ".scss", ".less", ".sql",
        ".ps1", ".psm1", ".sh", ".bat", ".cmd", ".editorconfig", ".gitignore"
    };

    /// <summary>True when the extension (with or without a leading dot) is a supported text type.</summary>
    public static bool IsTextExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        var ext = extension.StartsWith('.') ? extension : "." + extension;
        return TextExtensions.Contains(ext);
    }

    /// <summary>True when the given file path has a supported text extension.</summary>
    public static bool IsTextFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var dot = path.LastIndexOf('.');
        if (dot < 0)
            return false;

        return IsTextExtension(path.Substring(dot));
    }

    private static string? GetExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var dot = path.LastIndexOf('.');
        return dot < 0 ? null : path.Substring(dot);
    }

    /// <summary>Classifies a file path for inline preview purposes.</summary>
    public static FilePreviewKind GetPreviewKind(string? path)
    {
        if (ImageFileHelper.IsImageFile(path))
            return FilePreviewKind.Image;

        var ext = GetExtension(path);
        if (ext != null)
        {
            if (HtmlExtensions.Contains(ext))
                return FilePreviewKind.Html;
            if (MarkdownExtensions.Contains(ext))
                return FilePreviewKind.Markdown;
        }

        if (IsTextFile(path))
            return FilePreviewKind.Text;

        return FilePreviewKind.None;
    }
}
