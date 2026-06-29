namespace ProjectExplorer.Core.Models;

/// <summary>
/// A reference to a real directory on disk. Folders can appear at the Project level,
/// inside any Collection, and can be referenced by multiple Projects or Collections
/// simultaneously (many-to-many).
/// </summary>
public class FolderReference : ProjectChild
{
    public override ChildType Type => ChildType.FolderReference;

    /// <summary>
    /// Absolute path to the real folder on disk.
    /// </summary>
    public string RealPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional description explaining the purpose or contents of this folder.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Returns the effective display name: DisplayName override if set,
    /// otherwise the folder name extracted from RealPath.
    /// Handles both Windows and POSIX path separators for cross-platform compatibility.
    /// </summary>
    public string EffectiveName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisplayName))
                return DisplayName;

            if (!string.IsNullOrWhiteSpace(RealPath))
            {
                // Normalize to forward slashes for consistent extraction,
                // then get the last segment (works for both C:\Dev\MyProject and /home/dev/myproject)
                var normalized = RealPath.Replace('\\', '/');
                var trimmed = normalized.TrimEnd('/');
                var lastSlash = trimmed.LastIndexOf('/');
                return lastSlash >= 0 ? trimmed.Substring(lastSlash + 1) : trimmed;
            }

            return "(unknown folder)";
        }
    }
}
