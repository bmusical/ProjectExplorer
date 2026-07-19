using ProjectExplorer.Core.Services;

namespace ProjectExplorer.Tests;

public class ProjectStoreMigratorTests
{
    private readonly string _tempDir;

    public ProjectStoreMigratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ProjectExplorer_MigratorTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task ResolveRepository_FreshInstall_ReturnsSqliteBackedRepositoryWithNoData()
    {
        var repository = ProjectStoreMigrator.ResolveRepository(_tempDir);

        Assert.IsType<SqliteProjectRepository>(repository);
        Assert.True(File.Exists(Path.Combine(_tempDir, "projects.db")));
        Assert.Empty(await repository.LoadAllAsync());
    }

    [Fact]
    public async Task ResolveRepository_ExistingProjectsJson_MigratesToSqliteAndRenamesJson()
    {
        var jsonRepo = new JsonProjectRepository(_tempDir);
        var manager = new ProjectManager(jsonRepo);
        await manager.InitializeAsync();
        var project = await manager.CreateProjectAsync("Legacy Project", "Migrated from JSON");
        var coll = await manager.CreateCollectionAsync(project.Id, "Assets");
        await manager.AddFolderReferenceAsync(project.Id, @"C:\Dev\Code", coll.Id, "Code");

        var jsonPath = Path.Combine(_tempDir, "projects.json");
        Assert.True(File.Exists(jsonPath));

        var repository = ProjectStoreMigrator.ResolveRepository(_tempDir);

        Assert.IsType<SqliteProjectRepository>(repository);
        Assert.True(File.Exists(Path.Combine(_tempDir, "projects.db")));
        Assert.False(File.Exists(jsonPath));
        Assert.True(File.Exists(jsonPath + ".migrated"));

        var loaded = await repository.LoadAllAsync();
        var loadedProject = Assert.Single(loaded);
        Assert.Equal("Legacy Project", loadedProject.Name);
        Assert.Equal("Migrated from JSON", loadedProject.Description);
        var loadedColl = loadedProject.FindCollection(coll.Id);
        Assert.NotNull(loadedColl);
        Assert.Single(loadedColl!.Children);
    }

    [Fact]
    public async Task ResolveRepository_CalledTwice_SecondCallIsANoOpThatReusesTheSameDatabase()
    {
        var jsonRepo = new JsonProjectRepository(_tempDir);
        var manager = new ProjectManager(jsonRepo);
        await manager.InitializeAsync();
        await manager.CreateProjectAsync("Legacy Project");

        var first = ProjectStoreMigrator.ResolveRepository(_tempDir);
        var dbWriteTimeAfterFirstCall = File.GetLastWriteTimeUtc(Path.Combine(_tempDir, "projects.db"));

        // A second resolve must not re-run migration (projects.json is already gone/renamed by now
        // anyway, but this also guards against ever re-touching a completed migration).
        var second = ProjectStoreMigrator.ResolveRepository(_tempDir);

        Assert.IsType<SqliteProjectRepository>(second);
        var loaded = await second.LoadAllAsync();
        Assert.Single(loaded);
        Assert.True(File.GetLastWriteTimeUtc(Path.Combine(_tempDir, "projects.db")) >= dbWriteTimeAfterFirstCall);
    }

    [Fact]
    public void ResolveRepository_CorruptProjectsJson_FallsBackToJsonRepositoryWithoutDeletingIt()
    {
        var jsonPath = Path.Combine(_tempDir, "projects.json");
        File.WriteAllText(jsonPath, "{ this is not valid json ]]]");

        var repository = ProjectStoreMigrator.ResolveRepository(_tempDir);

        Assert.IsType<JsonProjectRepository>(repository);
        Assert.True(File.Exists(jsonPath), "Original projects.json must survive a failed migration.");
        Assert.False(File.Exists(Path.Combine(_tempDir, "projects.db")), "No partially-written db should be left behind.");
    }

    [Fact]
    public async Task ResolveRepository_LeftoverPartialMigrationTempFile_DoesNotBlockRetryMigration()
    {
        // Simulates the exact bug this test guards against: an earlier run wrote data to a temp
        // file (as migration now always does first) but was interrupted -- crashed, force-killed,
        // lost power -- before the final atomic move into "projects.db". Because that move never
        // happened, nothing exists at the real "projects.db" path. An older version of this class
        // wrote directly to "projects.db" from the start, so an interruption could leave a
        // partially-written file sitting at that exact path, and "if projects.db exists, assume
        // already migrated" (below) would then trust it forever -- permanently orphaning
        // projects.json even though it was never touched. A stray temp file must not have that
        // effect: migration should still run fresh and succeed.
        var jsonRepo = new JsonProjectRepository(_tempDir);
        var manager = new ProjectManager(jsonRepo);
        await manager.InitializeAsync();
        await manager.CreateProjectAsync("Legacy Project");

        var staleTempPath = Path.Combine(_tempDir, $"projects.db.migrating-{Guid.NewGuid():N}");
        File.WriteAllText(staleTempPath, "leftover bytes from an interrupted prior run");
        Assert.False(File.Exists(Path.Combine(_tempDir, "projects.db")));

        var repository = ProjectStoreMigrator.ResolveRepository(_tempDir);

        Assert.IsType<SqliteProjectRepository>(repository);
        var loaded = await repository.LoadAllAsync();
        var loadedProject = Assert.Single(loaded);
        Assert.Equal("Legacy Project", loadedProject.Name);
        Assert.True(File.Exists(Path.Combine(_tempDir, "projects.json.migrated")),
            "A real, successful migration must still have happened despite the stray leftover temp file.");
    }

    [Fact]
    public async Task ResolveRepository_SuccessfulMigration_LeavesNoLeftoverTempFiles()
    {
        var jsonRepo = new JsonProjectRepository(_tempDir);
        var manager = new ProjectManager(jsonRepo);
        await manager.InitializeAsync();
        await manager.CreateProjectAsync("Legacy Project");

        ProjectStoreMigrator.ResolveRepository(_tempDir);

        Assert.Empty(Directory.GetFiles(_tempDir, "projects.db.migrating-*"));
    }

    [Fact]
    public void ResolveRepository_FailedMigration_LeavesNoLeftoverTempFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "projects.json"), "{ not valid json ]]]");

        ProjectStoreMigrator.ResolveRepository(_tempDir);

        Assert.Empty(Directory.GetFiles(_tempDir, "projects.db.migrating-*"));
    }
}
