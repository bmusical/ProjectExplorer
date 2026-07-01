namespace ProjectExplorer.Core.Models;

/// <summary>
/// Discriminator for the type of a child node within a Project or Collection.
/// </summary>
public enum ChildType
{
    Collection,
    FolderReference,
    WebResource,
    FileReference
}
