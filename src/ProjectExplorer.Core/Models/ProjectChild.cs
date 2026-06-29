namespace ProjectExplorer.Core.Models;

/// <summary>
/// Base class for items that can be children of a Project or Collection.
/// </summary>
public abstract class ProjectChild
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ParentId { get; set; }
    public int SortOrder { get; set; }
    public abstract ChildType Type { get; }

    /// <summary>
    /// Optional display name override. If null, the default name is used.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Optional key-value metadata for future use (conditions, tags, etc.)
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}
