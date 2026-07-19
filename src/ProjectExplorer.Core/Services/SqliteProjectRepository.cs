using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using ProjectExplorer.Core.Interfaces;
using ProjectExplorer.Core.Models;

namespace ProjectExplorer.Core.Services;

/// <summary>
/// SQLite-backed implementation of IProjectRepository. Stores project definitions in
/// %APPDATA%\ProjectExplorer\projects.db.
///
/// Unlike JsonProjectRepository (which rewrites the entire file — every project, not just the
/// one being touched — on every single CRUD call), SaveProjectAsync here only replaces the rows
/// belonging to the one project being saved. That's the actual fix for the slowdown a large/deep
/// nest causes: cost now scales with the size of the project being edited, not with the size of
/// the whole store.
///
/// Two tables: Projects (one row per top-level project, with an explicit SortOrder column since
/// SQL rows have no inherent order the way a JSON array does) and ProjectChildren (one row per
/// Collection/FolderReference/WebResource/FileReference, flattened — ParentId is the owning
/// Project's Id for root-level children or a Collection's Id for nested ones, same meaning
/// ProjectChild.ParentId already has in memory). Metadata is stored as a small JSON blob per row
/// rather than a separate key/value table — it's opaque, unqueried, free-form data, so a text
/// column is the pragmatic choice without reintroducing the "rewrite everything" cost this class
/// exists to avoid.
/// </summary>
public class SqliteProjectRepository : IProjectRepository
{
    private readonly string _connectionString;

    public SqliteProjectRepository()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProjectExplorer"))
    {
    }

    public SqliteProjectRepository(string storageDir) : this(storageDir, "projects.db")
    {
    }

    /// <summary>
    /// Lets ProjectStoreMigrator point a repository at a temp file name instead of "projects.db"
    /// directly, so a migration-in-progress is never visible at the real path until it's fully
    /// written — see ProjectStoreMigrator.ResolveRepository for why that matters.
    /// </summary>
    internal SqliteProjectRepository(string storageDir, string databaseFileName)
    {
        Directory.CreateDirectory(storageDir);
        _connectionString = $"Data Source={Path.Combine(storageDir, databaseFileName)}";
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS Projects (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NULL,
                Color TEXT NULL,
                IconKey TEXT NULL,
                Created TEXT NOT NULL,
                Modified TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS ProjectChildren (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                ParentId TEXT NOT NULL,
                ChildType TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                DisplayName TEXT NULL,
                Name TEXT NULL,
                Description TEXT NULL,
                Color TEXT NULL,
                RealPath TEXT NULL,
                Url TEXT NULL,
                FilePath TEXT NULL,
                MetadataJson TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ProjectChildren_ProjectId ON ProjectChildren(ProjectId);
            """);
    }

    /// <summary>
    /// Opens a fresh connection per call (mirrors JsonProjectRepository's simple open/read-or-write/
    /// close pattern — no persistent handle to reason about). WAL mode trades a little disk space
    /// for better crash resilience; single-instance enforcement already guarantees only one process
    /// ever touches this file, so there's no cross-process contention to worry about.
    /// </summary>
    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
        return connection;
    }

    /// <summary>
    /// Forces all WAL-mode data into the main database file and truncates the WAL to empty, so
    /// the file at the connection string's Data Source is guaranteed self-contained. Used by
    /// ProjectStoreMigrator right before it atomically moves a freshly-migrated temp database
    /// into place — without this, the move could leave data stranded in a "-wal" side file that
    /// never made the trip.
    /// </summary>
    internal void Checkpoint()
    {
        using var connection = OpenConnection();
        connection.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
    }

    public async Task<List<Project>> LoadAllAsync()
    {
        using var connection = OpenConnection();

        var projectRows = (await connection.QueryAsync<ProjectRow>(
            "SELECT * FROM Projects ORDER BY SortOrder")).ToList();
        var childRows = (await connection.QueryAsync<ProjectChildRow>(
            "SELECT * FROM ProjectChildren ORDER BY SortOrder")).ToList();

        // Grouping by ParentId (rather than ProjectId) reconstructs the tree directly — a child's
        // ParentId is always either its owning Project's Id or a Collection's Id within that same
        // project, and Guids are globally unique, so there's no risk of cross-project mixing.
        var childrenByParent = childRows.GroupBy(c => c.ParentId).ToDictionary(g => g.Key, g => g.ToList());

        var projects = new List<Project>();
        foreach (var row in projectRows)
        {
            var project = ToProject(row);
            project.Children = BuildTree(childrenByParent, project.Id);
            projects.Add(project);
        }
        return projects;
    }

    public async Task SaveAllAsync(IEnumerable<Project> projects)
    {
        var projectList = projects.ToList();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        // Only used by top-level project reordering (MoveProjectAsync) — a full replace here
        // matches JsonProjectRepository's own SaveAllAsync semantics exactly. This path is rare
        // enough that the O(n) cost doesn't matter; SaveProjectAsync below is the hot path that
        // actually needed to get cheaper.
        await connection.ExecuteAsync("DELETE FROM ProjectChildren", transaction: transaction);
        await connection.ExecuteAsync("DELETE FROM Projects", transaction: transaction);

        for (int i = 0; i < projectList.Count; i++)
        {
            var project = projectList[i];
            await connection.ExecuteAsync("""
                INSERT INTO Projects (Id, Name, Description, Color, IconKey, Created, Modified, SortOrder)
                VALUES (@Id, @Name, @Description, @Color, @IconKey, @Created, @Modified, @SortOrder)
                """, ToProjectParams(project, i), transaction);

            await InsertChildrenAsync(connection, transaction, project.Id, project.Id, project.Children);
        }

        transaction.Commit();
    }

    public async Task SaveProjectAsync(Project project)
    {
        project.Modified = DateTime.UtcNow;

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        // Preserve the project's existing top-level position; only a brand-new project gets
        // appended at the end. SaveAllAsync (above) is the only path that actually reorders projects.
        var existingSortOrder = await connection.QuerySingleOrDefaultAsync<int?>(
            "SELECT SortOrder FROM Projects WHERE Id = @Id", new { Id = project.Id.ToString() }, transaction);
        var sortOrder = existingSortOrder
            ?? await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Projects", transaction: transaction);

        await connection.ExecuteAsync("""
            INSERT INTO Projects (Id, Name, Description, Color, IconKey, Created, Modified, SortOrder)
            VALUES (@Id, @Name, @Description, @Color, @IconKey, @Created, @Modified, @SortOrder)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name, Description = excluded.Description, Color = excluded.Color,
                IconKey = excluded.IconKey, Created = excluded.Created, Modified = excluded.Modified,
                SortOrder = excluded.SortOrder
            """, ToProjectParams(project, sortOrder), transaction);

        // Replace just this project's children — the actual perf win over the JSON store, which
        // has to rewrite every other project's data too just to save one edit.
        await connection.ExecuteAsync("DELETE FROM ProjectChildren WHERE ProjectId = @ProjectId",
            new { ProjectId = project.Id.ToString() }, transaction);
        await InsertChildrenAsync(connection, transaction, project.Id, project.Id, project.Children);

        transaction.Commit();
    }

    public async Task DeleteProjectAsync(Guid projectId)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync("DELETE FROM ProjectChildren WHERE ProjectId = @Id",
            new { Id = projectId.ToString() }, transaction);
        await connection.ExecuteAsync("DELETE FROM Projects WHERE Id = @Id",
            new { Id = projectId.ToString() }, transaction);
        transaction.Commit();
    }

    // ── Row <-> Model mapping ──

    private sealed class ProjectRow
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? Color { get; set; }
        public string? IconKey { get; set; }
        public string Created { get; set; } = "";
        public string Modified { get; set; } = "";
        public int SortOrder { get; set; }
    }

    private sealed class ProjectChildRow
    {
        public string Id { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string ParentId { get; set; } = "";
        public string ChildType { get; set; } = "";
        public int SortOrder { get; set; }
        public string? DisplayName { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Color { get; set; }
        public string? RealPath { get; set; }
        public string? Url { get; set; }
        public string? FilePath { get; set; }
        public string? MetadataJson { get; set; }
    }

    private static Project ToProject(ProjectRow row) => new()
    {
        Id = Guid.Parse(row.Id),
        Name = row.Name,
        Description = row.Description,
        Color = row.Color,
        IconKey = row.IconKey,
        Created = DateTime.TryParse(row.Created, out var created) ? created : DateTime.UtcNow,
        Modified = DateTime.TryParse(row.Modified, out var modified) ? modified : DateTime.UtcNow
    };

    private static object ToProjectParams(Project project, int sortOrder) => new
    {
        Id = project.Id.ToString(),
        project.Name,
        project.Description,
        project.Color,
        project.IconKey,
        Created = project.Created.ToString("O"),
        Modified = project.Modified.ToString("O"),
        SortOrder = sortOrder
    };

    private static List<ProjectChild> BuildTree(Dictionary<string, List<ProjectChildRow>> byParent, Guid parentId)
    {
        if (!byParent.TryGetValue(parentId.ToString(), out var rows)) return new List<ProjectChild>();
        return rows.OrderBy(r => r.SortOrder).Select(r => ToChild(r, byParent)).ToList();
    }

    private static ProjectChild ToChild(ProjectChildRow row, Dictionary<string, List<ProjectChildRow>> byParent)
    {
        ProjectChild child = row.ChildType switch
        {
            "collection" => new Collection
            {
                Name = row.Name ?? "Unnamed",
                Description = row.Description,
                Color = row.Color,
                Children = BuildTree(byParent, Guid.Parse(row.Id))
            },
            "folderReference" => new FolderReference { RealPath = row.RealPath ?? "", Description = row.Description },
            "webResource" => new WebResource { Url = row.Url ?? "", Description = row.Description },
            "fileReference" => new FileReference { FilePath = row.FilePath ?? "", Description = row.Description },
            _ => throw new InvalidOperationException($"Unknown ChildType '{row.ChildType}' in ProjectChildren row {row.Id}.")
        };

        child.Id = Guid.Parse(row.Id);
        child.ParentId = Guid.Parse(row.ParentId);
        child.SortOrder = row.SortOrder;
        child.DisplayName = row.DisplayName;
        if (!string.IsNullOrEmpty(row.MetadataJson))
            child.Metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(row.MetadataJson) ?? new();

        return child;
    }

    private static async Task InsertChildrenAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid projectId, Guid parentId, List<ProjectChild> children)
    {
        foreach (var child in children)
        {
            await connection.ExecuteAsync("""
                INSERT INTO ProjectChildren
                    (Id, ProjectId, ParentId, ChildType, SortOrder, DisplayName, Name, Description, Color, RealPath, Url, FilePath, MetadataJson)
                VALUES
                    (@Id, @ProjectId, @ParentId, @ChildType, @SortOrder, @DisplayName, @Name, @Description, @Color, @RealPath, @Url, @FilePath, @MetadataJson)
                """, ToChildParams(child, projectId, parentId), transaction);

            if (child is Collection collection)
                await InsertChildrenAsync(connection, transaction, projectId, collection.Id, collection.Children);
        }
    }

    private static object ToChildParams(ProjectChild child, Guid projectId, Guid parentId) => new
    {
        Id = child.Id.ToString(),
        ProjectId = projectId.ToString(),
        ParentId = parentId.ToString(),
        ChildType = ChildTypeKey(child.Type),
        child.SortOrder,
        child.DisplayName,
        Name = (child as Collection)?.Name,
        Description = child switch
        {
            Collection c => c.Description,
            FolderReference f => f.Description,
            WebResource w => w.Description,
            FileReference fr => fr.Description,
            _ => null
        },
        Color = (child as Collection)?.Color,
        RealPath = (child as FolderReference)?.RealPath,
        Url = (child as WebResource)?.Url,
        FilePath = (child as FileReference)?.FilePath,
        MetadataJson = child.Metadata.Count > 0 ? JsonSerializer.Serialize(child.Metadata) : null
    };

    private static string ChildTypeKey(ChildType type) => type switch
    {
        ChildType.Collection => "collection",
        ChildType.FolderReference => "folderReference",
        ChildType.WebResource => "webResource",
        ChildType.FileReference => "fileReference",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
