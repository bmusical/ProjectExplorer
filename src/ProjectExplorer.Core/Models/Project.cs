namespace ProjectExplorer.Core.Models;

/// <summary>
/// The top-level organizational container. Represents a body of work
/// (e.g., "NinjaTech Platform," "Client Website Redesign").
/// A Project can contain Collections and FolderReferences directly.
/// </summary>
public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The user-visible name of this Project.
    /// </summary>
    public string Name { get; set; } = "New Project";

    /// <summary>
    /// Optional description for this Project.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional color hex string for UI styling (e.g., "#3366FF").
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Optional icon identifier for future use.
    /// </summary>
    public string? IconKey { get; set; }

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime Modified { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The top-level children of this Project — can be Collections or FolderReferences.
    /// </summary>
    public List<ProjectChild> Children { get; set; } = new();

    /// <summary>
    /// Helper: find a Collection by Id anywhere in this Project's hierarchy.
    /// </summary>
    public Collection? FindCollection(Guid collectionId)
    {
        return FindCollectionIn(Children, collectionId);
    }

    private static Collection? FindCollectionIn(List<ProjectChild> children, Guid id)
    {
        foreach (var child in children)
        {
            if (child is Collection c)
            {
                if (c.Id == id) return c;
                var found = FindCollectionIn(c.Children, id);
                if (found != null) return found;
            }
        }
        return null;
    }

    /// <summary>
    /// Helper: find the parent list that contains a given child Id.
    /// Returns null if not found.
    /// </summary>
    public List<ProjectChild>? FindParentList(Guid childId)
    {
        if (Children.Any(c => c.Id == childId)) return Children;
        return FindParentListIn(Children, childId);
    }

    private static List<ProjectChild>? FindParentListIn(List<ProjectChild> children, Guid id)
    {
        foreach (var child in children)
        {
            if (child is Collection c)
            {
                if (c.Children.Any(cc => cc.Id == id)) return c.Children;
                var found = FindParentListIn(c.Children, id);
                if (found != null) return found;
            }
        }
        return null;
    }

    /// <summary>
    /// Validate that no circular references exist in the Collection hierarchy.
    /// </summary>
    public bool HasCircularReferences()
    {
        var visited = new HashSet<Guid>();
        return CheckCircular(Children, visited);
    }

    private static bool CheckCircular(List<ProjectChild> children, HashSet<Guid> visited)
    {
        foreach (var child in children)
        {
            if (child is Collection c)
            {
                if (!visited.Add(c.Id))
                    return true; // circular reference detected
                if (CheckCircular(c.Children, visited))
                    return true;
                visited.Remove(c.Id);
            }
        }
        return false;
    }
}
