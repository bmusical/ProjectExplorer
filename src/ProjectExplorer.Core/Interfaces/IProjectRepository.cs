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
    /// Delete <paramref name="replacedProjectId"/> and save <paramref name="project"/> as one
    /// commit. The deleted project's child rows are removed before the saved project's children
    /// are inserted, so child ids that move between them do not collide. If the save fails, the
    /// deleted project is left unchanged.
    /// </summary>
    Task SaveProjectReplacingAsync(Models.Project project, Guid replacedProjectId);

    /// <summary>
    /// Delete a project by Id.
    /// </summary>
    Task DeleteProjectAsync(Guid projectId);
}
