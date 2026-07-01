namespace ProjectExplorer.Core.Models;

/// <summary>
/// A reference to a single file on disk. Works like a WebResource but points at a
/// specific file rather than a URL. The file is opened with its associated
/// application (by file-type) via the shell when activated.
/// </summary>
public class FileReference : ProjectChild
{
    public override ChildType Type => ChildType.FileReference;

    /// <summary>
    /// Absolute path to the real file on disk.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Optional description explaining what this file is and when to use it.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The file extension (including the leading dot, lower-cased), e.g. ".pdf".
    /// Derived from <see cref="FilePath"/>. Empty when the path has no extension.
    /// </summary>
    public string Extension
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FilePath)) return string.Empty;
            var normalized = FilePath.Replace('\\', '/');
            var trimmed = normalized.TrimEnd('/');
            var lastSlash = trimmed.LastIndexOf('/');
            var fileName = lastSlash >= 0 ? trimmed.Substring(lastSlash + 1) : trimmed;
            var dot = fileName.LastIndexOf('.');
            return dot >= 0 ? fileName.Substring(dot).ToLowerInvariant() : string.Empty;
        }
    }

    /// <summary>
    /// Returns the effective display name: DisplayName override if set,
    /// otherwise the file name extracted from FilePath.
    /// Handles both Windows and POSIX path separators for cross-platform compatibility.
    /// </summary>
    public string EffectiveName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisplayName))
                return DisplayName;

            if (!string.IsNullOrWhiteSpace(FilePath))
            {
                var normalized = FilePath.Replace('\\', '/');
                var trimmed = normalized.TrimEnd('/');
                var lastSlash = trimmed.LastIndexOf('/');
                return lastSlash >= 0 ? trimmed.Substring(lastSlash + 1) : trimmed;
            }

            return "(unknown file)";
        }
    }
}
