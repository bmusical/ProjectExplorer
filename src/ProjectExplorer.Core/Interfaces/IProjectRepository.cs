namespace ProjectExplorer.Core.Interfaces;

/// <summary>
/// Service for persisting and loading project definitions.
/// </summary>
public interface IProjectRepository
{
    /// <summary>
    /// Load all projects from storage.
    /// </summary>
    Task<List<Models.Project>> LoadAllAsync();

    /// <summary>
    /// Save all projects to storage.
    /// </summary>
    Task SaveAllAsync(IEnumerable<Models.Project> projects);

    /// <summary>
    /// Save a single project (updates if exists, adds if new).
    /// </summary>
    Task SaveProjectAsync(Models.Project project);

    /// <summary>
    /// Delete a project by Id.
    /// </summary>
    Task DeleteProjectAsync(Guid projectId);
}
