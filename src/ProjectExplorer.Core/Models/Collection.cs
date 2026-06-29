namespace ProjectExplorer.Core.Models;

/// <summary>
/// A free-form sub-grouping within a Project. Collections can nest
/// (a Collection can contain other Collections) and can also contain
/// Folder references. They are purely organizational — virtual containers,
/// not real directories.
/// </summary>
public class Collection : ProjectChild
{
    public override ChildType Type => ChildType.Collection;

    /// <summary>
    /// The user-visible name of this Collection.
    /// </summary>
    public string Name { get; set; } = "New Collection";

    /// <summary>
    /// Optional description for this Collection.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional color hex string for UI styling (e.g., "#FF5733").
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// The children of this Collection — can be other Collections or FolderReferences.
    /// </summary>
    public List<ProjectChild> Children { get; set; } = new();
}
