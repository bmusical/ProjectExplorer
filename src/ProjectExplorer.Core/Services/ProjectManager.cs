using ProjectExplorer.Core.Interfaces;
using ProjectExplorer.Core.Models;

namespace ProjectExplorer.Core.Services;

/// <summary>
/// High-level service for managing Projects, Collections, and FolderReferences.
/// Coordinates persistence through IProjectRepository.
/// All operations persist immediately (awaited, not fire-and-forget).
/// </summary>
public class ProjectManager
{
    private readonly IProjectRepository _repository;
    private List<Project> _projects = new();

    public ProjectManager(IProjectRepository repository)
    {
        _repository = repository;
    }

    public IReadOnlyList<Project> Projects => _projects.AsReadOnly();

    /// <summary>
    /// Load all projects from storage. Must be called before other operations.
    /// </summary>
    public async Task InitializeAsync()
    {
        _projects = await _repository.LoadAllAsync();
    }

    // ── Project CRUD ──

    public async Task<Project> CreateProjectAsync(string name, string? description = null)
    {
        var project = new Project
        {
            Name = name,
            Description = description,
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow
        };
        _projects.Add(project);
        await _repository.SaveProjectAsync(project);
        return project;
    }

    public async Task RenameProjectAsync(Guid projectId, string newName)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");
        project.Name = newName;
        project.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(project);
    }

    public async Task DeleteProjectAsync(Guid projectId)
    {
        _projects.RemoveAll(p => p.Id == projectId);
        await _repository.DeleteProjectAsync(projectId);
    }

    public Project? GetProject(Guid projectId)
    {
        return _projects.FirstOrDefault(p => p.Id == projectId);
    }

    // ── Collection CRUD ──

    public async Task<Collection> CreateCollectionAsync(Guid projectId, string name, Guid? parentCollectionId = null)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");

        var collection = new Collection
        {
            Name = name,
            ParentId = parentCollectionId ?? project.Id,
            SortOrder = GetNextSortOrder(project, parentCollectionId)
        };

        var parentList = parentCollectionId.HasValue
            ? project.FindCollection(parentCollectionId.Value)?.Children
            : project.Children;

        if (parentList == null)
            throw new InvalidOperationException($"Parent collection {parentCollectionId} not found.");

        parentList.Add(collection);
        project.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(project);
        return collection;
    }

    public async Task RenameCollectionAsync(Guid projectId, Guid collectionId, string newName)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");
        var collection = project.FindCollection(collectionId) ?? throw new InvalidOperationException($"Collection {collectionId} not found.");
        collection.Name = newName;
        project.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(project);
    }

    public async Task DeleteCollectionAsync(Guid projectId, Guid collectionId)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");
        var parentList = project.FindParentList(collectionId) ?? throw new InvalidOperationException($"Cannot find parent list for collection {collectionId}.");
        parentList.RemoveAll(c => c.Id == collectionId);
        project.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(project);
    }

    // ── FolderReference CRUD ──

    public async Task<FolderReference> AddFolderReferenceAsync(Guid projectId, string realPath, Guid? parentCollectionId = null, string? displayName = null)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");

        var folderRef = new FolderReference
        {
            RealPath = realPath,
            DisplayName = displayName,
            ParentId = parentCollectionId ?? project.Id,
            SortOrder = GetNextSortOrder(project, parentCollectionId)
        };

        var parentList = parentCollectionId.HasValue
            ? project.FindCollection(parentCollectionId.Value)?.Children
            : project.Children;

        if (parentList == null)
            throw new InvalidOperationException($"Parent collection {parentCollectionId} not found.");

        parentList.Add(folderRef);
        project.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(project);
        return folderRef;
    }

    public async Task RemoveFolderReferenceAsync(Guid projectId, Guid folderRefId)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");
        var parentList = project.FindParentList(folderRefId) ?? throw new InvalidOperationException($"Cannot find parent list for folder reference {folderRefId}.");
        parentList.RemoveAll(c => c.Id == folderRefId);
        project.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(project);
    }

    public async Task UpdateFolderReferenceAsync(Guid projectId, Guid folderRefId, string? newDisplayName = null, string? newPath = null, string? newDescription = null)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");
        var parentList = project.FindParentList(folderRefId) ?? throw new InvalidOperationException($"Cannot find parent list for folder reference {folderRefId}.");
        var folderRef = parentList.FirstOrDefault(c => c.Id == folderRefId) as FolderReference
            ?? throw new InvalidOperationException($"Folder reference {folderRefId} not found.");

        if (newDisplayName != null) folderRef.DisplayName = newDisplayName;
        if (newPath != null) folderRef.RealPath = newPath;
        if (newDescription != null) folderRef.Description = newDescription;
        project.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(project);
    }

    // ── WebResource CRUD ──

    public async Task<WebResource> AddWebResourceAsync(Guid projectId, string url, string? displayName = null, string? description = null, Guid? parentCollectionId = null)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");

        var webResource = new WebResource
        {
            Url = url,
            DisplayName = displayName,
            Description = description,
            ParentId = parentCollectionId ?? project.Id,
            SortOrder = GetNextSortOrder(project, parentCollectionId)
        };

        var parentList = parentCollectionId.HasValue
            ? project.FindCollection(parentCollectionId.Value)?.Children
            : project.Children;

        if (parentList == null)
            throw new InvalidOperationException($"Parent collection {parentCollectionId} not found.");

        parentList.Add(webResource);
        project.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(project);
        return webResource;
    }

    public async Task RemoveWebResourceAsync(Guid projectId, Guid webResourceId)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");
        var parentList = project.FindParentList(webResourceId) ?? throw new InvalidOperationException($"Cannot find parent list for web resource {webResourceId}.");
        parentList.RemoveAll(c => c.Id == webResourceId);
        project.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(project);
    }

    public async Task UpdateWebResourceAsync(Guid projectId, Guid webResourceId, string? newDisplayName = null, string? newUrl = null, string? newDescription = null)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");
        var parentList = project.FindParentList(webResourceId) ?? throw new InvalidOperationException($"Cannot find parent list for web resource {webResourceId}.");
        var webResource = parentList.FirstOrDefault(c => c.Id == webResourceId) as WebResource
            ?? throw new InvalidOperationException($"Web resource {webResourceId} not found.");

        if (newDisplayName != null) webResource.DisplayName = newDisplayName;
        if (newUrl != null) webResource.Url = newUrl;
        if (newDescription != null) webResource.Description = newDescription;
        project.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(project);
    }

    // ── Reordering ──

    public async Task ReorderChildrenAsync(Guid projectId, Guid? parentCollectionId, List<Guid> orderedChildIds)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");

        var parentList = parentCollectionId.HasValue
            ? project.FindCollection(parentCollectionId.Value)?.Children
            : project.Children;

        if (parentList == null)
            throw new InvalidOperationException("Parent not found.");

        for (int i = 0; i < orderedChildIds.Count; i++)
        {
            var child = parentList.FirstOrDefault(c => c.Id == orderedChildIds[i]);
            if (child != null)
                child.SortOrder = i;
        }

        // Re-sort the list in place
        var sorted = parentList.OrderBy(c => c.SortOrder).ToList();
        parentList.Clear();
        parentList.AddRange(sorted);

        project.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(project);
    }

    // ── Move (reparent) ──

    public async Task MoveChildAsync(Guid projectId, Guid childId, Guid? newParentCollectionId)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");

        var sourceList = project.FindParentList(childId)
            ?? throw new InvalidOperationException($"Child {childId} not found in project.");
        var child = sourceList.First(c => c.Id == childId);

        var destList = newParentCollectionId.HasValue
            ? project.FindCollection(newParentCollectionId.Value)?.Children
                ?? throw new InvalidOperationException($"Destination collection {newParentCollectionId} not found.")
            : project.Children;

        if (ReferenceEquals(sourceList, destList)) return;

        var originalParentId = child.ParentId;
        var originalSortOrder = child.SortOrder;

        sourceList.Remove(child);
        child.ParentId = newParentCollectionId ?? project.Id;
        child.SortOrder = destList.Count;
        destList.Add(child);

        if (project.HasCircularReferences())
        {
            destList.Remove(child);
            child.ParentId = originalParentId;
            child.SortOrder = originalSortOrder;
            sourceList.Add(child);
            var restored = sourceList.OrderBy(c => c.SortOrder).ToList();
            sourceList.Clear();
            sourceList.AddRange(restored);
            throw new InvalidOperationException("Cannot move a collection into one of its own descendants.");
        }

        project.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(project);
    }

    // ── Helpers ──

    private static int GetNextSortOrder(Project project, Guid? parentCollectionId)
    {
        var parentList = parentCollectionId.HasValue
            ? project.FindCollection(parentCollectionId.Value)?.Children
            : project.Children;

        return parentList?.Count ?? 0;
    }
}
