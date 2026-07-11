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

    public async Task UpdateProjectAsync(Guid projectId, string? newDescription = null)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");
        if (newDescription != null) project.Description = newDescription;
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

    public async Task UpdateCollectionAsync(Guid projectId, Guid collectionId, string? newDescription = null)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");
        var collection = project.FindCollection(collectionId) ?? throw new InvalidOperationException($"Collection {collectionId} not found.");

        if (newDescription != null) collection.Description = newDescription;
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

    // ── FileReference CRUD ──

    public async Task<FileReference> AddFileReferenceAsync(Guid projectId, string filePath, string? displayName = null, string? description = null, Guid? parentCollectionId = null)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");

        var fileRef = new FileReference
        {
            FilePath = filePath,
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

        parentList.Add(fileRef);
        project.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(project);
        return fileRef;
    }

    public async Task RemoveFileReferenceAsync(Guid projectId, Guid fileRefId)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");
        var parentList = project.FindParentList(fileRefId) ?? throw new InvalidOperationException($"Cannot find parent list for file reference {fileRefId}.");
        parentList.RemoveAll(c => c.Id == fileRefId);
        project.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(project);
    }

    public async Task UpdateFileReferenceAsync(Guid projectId, Guid fileRefId, string? newDisplayName = null, string? newPath = null, string? newDescription = null)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");
        var parentList = project.FindParentList(fileRefId) ?? throw new InvalidOperationException($"Cannot find parent list for file reference {fileRefId}.");
        var fileRef = parentList.FirstOrDefault(c => c.Id == fileRefId) as FileReference
            ?? throw new InvalidOperationException($"File reference {fileRefId} not found.");

        if (newDisplayName != null) fileRef.DisplayName = newDisplayName;
        if (newPath != null) fileRef.FilePath = newPath;
        if (newDescription != null) fileRef.Description = newDescription;
        project.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(project);
    }

    // ── Metadata (per-child key/value, used e.g. to suppress availability auto-retry) ──

    public async Task SetChildMetadataAsync(Guid projectId, Guid childId, string key, string? value)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");
        var parentList = project.FindParentList(childId) ?? throw new InvalidOperationException($"Cannot find parent list for child {childId}.");
        var child = parentList.FirstOrDefault(c => c.Id == childId)
            ?? throw new InvalidOperationException($"Child {childId} not found.");

        if (value == null) child.Metadata.Remove(key);
        else child.Metadata[key] = value;

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

    // ── Move (reparent) / reorder ──

    /// <summary>
    /// Moves a child to a (possibly the same) parent container. Pass beforeSiblingId = null to
    /// append at the end (original reparent-only behavior); pass the Id of a sibling already in
    /// the destination container to insert immediately before it instead, including within the
    /// same container (pure reorder). Resolving position by sibling Id — rather than a numeric
    /// index — sidesteps off-by-one errors from the source removal shifting indices when
    /// reordering within the same list.
    /// </summary>
    public async Task MoveChildAsync(Guid projectId, Guid childId, Guid? newParentCollectionId, Guid? beforeSiblingId = null)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");

        var sourceList = project.FindParentList(childId)
            ?? throw new InvalidOperationException($"Child {childId} not found in project.");
        var child = sourceList.First(c => c.Id == childId);

        var destList = newParentCollectionId.HasValue
            ? project.FindCollection(newParentCollectionId.Value)?.Children
                ?? throw new InvalidOperationException($"Destination collection {newParentCollectionId} not found.")
            : project.Children;

        var sameContainer = ReferenceEquals(sourceList, destList);
        if (sameContainer && beforeSiblingId == null) return;

        var originalParentId = child.ParentId;
        var originalIndex = sourceList.IndexOf(child);

        sourceList.Remove(child);

        var insertIndex = beforeSiblingId.HasValue
            ? destList.FindIndex(c => c.Id == beforeSiblingId.Value)
            : -1;
        if (insertIndex < 0) insertIndex = destList.Count;

        child.ParentId = newParentCollectionId ?? project.Id;
        destList.Insert(insertIndex, child);
        Renumber(destList);
        if (!sameContainer) Renumber(sourceList);

        if (project.HasCircularReferences())
        {
            destList.Remove(child);
            child.ParentId = originalParentId;
            sourceList.Insert(originalIndex, child);
            Renumber(sourceList);
            if (!sameContainer) Renumber(destList);
            throw new InvalidOperationException("Cannot move a collection into one of its own descendants.");
        }

        project.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(project);
    }

    /// <summary>Swaps a child one position earlier among its siblings. No-op if already first.</summary>
    public async Task MoveChildUpAsync(Guid projectId, Guid childId)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");
        var list = project.FindParentList(childId) ?? throw new InvalidOperationException($"Child {childId} not found in project.");
        var child = list.First(c => c.Id == childId);
        var ordered = list.OrderBy(c => c.SortOrder).ToList();
        var index = ordered.IndexOf(child);
        if (index <= 0) return;

        var parentCollectionId = child.ParentId == project.Id ? (Guid?)null : child.ParentId;
        await MoveChildAsync(projectId, childId, parentCollectionId, ordered[index - 1].Id);
    }

    /// <summary>Swaps a child one position later among its siblings. No-op if already last.</summary>
    public async Task MoveChildDownAsync(Guid projectId, Guid childId)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");
        var list = project.FindParentList(childId) ?? throw new InvalidOperationException($"Child {childId} not found in project.");
        var child = list.First(c => c.Id == childId);
        var ordered = list.OrderBy(c => c.SortOrder).ToList();
        var index = ordered.IndexOf(child);
        if (index < 0 || index >= ordered.Count - 1) return;

        var parentCollectionId = child.ParentId == project.Id ? (Guid?)null : child.ParentId;
        var beforeSiblingId = index + 2 < ordered.Count ? ordered[index + 2].Id : (Guid?)null;
        await MoveChildAsync(projectId, childId, parentCollectionId, beforeSiblingId);
    }

    // ── Move / reorder Projects (top-level; Project isn't a ProjectChild, so it has no
    // SortOrder — order is simply the _projects list order, which SaveAllAsync persists
    // as JSON array order and LoadAllAsync reads back the same way) ──

    public async Task MoveProjectAsync(Guid projectId, Guid? beforeProjectId)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");
        if (beforeProjectId == projectId) return;

        _projects.Remove(project);
        var insertIndex = beforeProjectId.HasValue ? _projects.FindIndex(p => p.Id == beforeProjectId.Value) : -1;
        if (insertIndex < 0) insertIndex = _projects.Count;
        _projects.Insert(insertIndex, project);

        await _repository.SaveAllAsync(_projects);
    }

    /// <summary>Swaps a project one position earlier among top-level projects. No-op if already first.</summary>
    public async Task MoveProjectUpAsync(Guid projectId)
    {
        var index = _projects.FindIndex(p => p.Id == projectId);
        if (index <= 0) return;
        await MoveProjectAsync(projectId, _projects[index - 1].Id);
    }

    /// <summary>Swaps a project one position later among top-level projects. No-op if already last.</summary>
    public async Task MoveProjectDownAsync(Guid projectId)
    {
        var index = _projects.FindIndex(p => p.Id == projectId);
        if (index < 0 || index >= _projects.Count - 1) return;
        var beforeId = index + 2 < _projects.Count ? _projects[index + 2].Id : (Guid?)null;
        await MoveProjectAsync(projectId, beforeId);
    }

    // ── Convert (Project <-> Collection) ──

    /// <summary>
    /// Converts an entire Project into a Collection nested under another project's tree,
    /// preserving the project's children and Id (so any references to its Id remain valid)
    /// and removing the original Project. Used by the "drag a project onto a collection" gesture.
    /// </summary>
    public async Task<Collection> ConvertProjectToCollectionAsync(Guid projectId, Guid targetProjectId, Guid? targetParentCollectionId)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");
        var targetProject = GetProject(targetProjectId) ?? throw new InvalidOperationException($"Project {targetProjectId} not found.");

        if (targetProjectId == projectId)
            throw new InvalidOperationException("Cannot convert a project into a collection inside itself.");

        var destList = targetParentCollectionId.HasValue
            ? targetProject.FindCollection(targetParentCollectionId.Value)?.Children
                ?? throw new InvalidOperationException($"Destination collection {targetParentCollectionId} not found.")
            : targetProject.Children;

        var collection = new Collection
        {
            Id = project.Id,
            ParentId = targetParentCollectionId ?? targetProject.Id,
            SortOrder = destList.Count,
            Name = project.Name,
            Description = project.Description,
            Color = project.Color,
            Children = project.Children
        };
        destList.Add(collection);

        if (targetProject.HasCircularReferences())
        {
            destList.Remove(collection);
            throw new InvalidOperationException("Cannot convert this project into a collection here.");
        }

        _projects.Remove(project);
        targetProject.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(targetProject);
        await _repository.DeleteProjectAsync(project.Id);
        return collection;
    }

    /// <summary>
    /// Converts a Collection into a new top-level Project, preserving the collection's
    /// children and Id and removing it from its original parent. Used by the
    /// "drag a collection onto the Projects root" gesture.
    /// </summary>
    public async Task<Project> ConvertCollectionToProjectAsync(Guid projectId, Guid collectionId)
    {
        var project = GetProject(projectId) ?? throw new InvalidOperationException($"Project {projectId} not found.");
        var collection = project.FindCollection(collectionId) ?? throw new InvalidOperationException($"Collection {collectionId} not found.");
        var parentList = project.FindParentList(collectionId) ?? throw new InvalidOperationException($"Cannot find parent list for collection {collectionId}.");

        var newProject = new Project
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            Color = collection.Color,
            Children = collection.Children,
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow
        };

        parentList.Remove(collection);
        _projects.Add(newProject);

        project.Modified = DateTime.UtcNow;
        await _repository.SaveProjectAsync(project);
        await _repository.SaveProjectAsync(newProject);
        return newProject;
    }

    // ── Helpers ──

    private static void Renumber(List<ProjectChild> list)
    {
        for (int i = 0; i < list.Count; i++)
            list[i].SortOrder = i;
    }

    private static int GetNextSortOrder(Project project, Guid? parentCollectionId)
    {
        var parentList = parentCollectionId.HasValue
            ? project.FindCollection(parentCollectionId.Value)?.Children
            : project.Children;

        return parentList?.Count ?? 0;
    }
}
