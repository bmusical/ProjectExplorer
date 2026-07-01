using ProjectExplorer.Core.Models;
using ProjectExplorer.Core.Services;
using ProjectExplorer.Core.Interfaces;

namespace ProjectExplorer.Tests;

public class ProjectManagerTests
{
    private readonly string _tempDir;

    public ProjectManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ProjectExplorer_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private async Task<ProjectManager> CreateManagerAsync()
    {
        var repo = new JsonProjectRepository(_tempDir);
        var manager = new ProjectManager(repo);
        await manager.InitializeAsync();
        return manager;
    }

    [Fact]
    public async Task CreateProject_SetsNameAndId()
    {
        var mgr = await CreateManagerAsync();
        var project = await mgr.CreateProjectAsync("Test Project", "A test");

        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal("Test Project", project.Name);
        Assert.Equal("A test", project.Description);
        Assert.Single(mgr.Projects);
    }

    [Fact]
    public async Task CreateCollection_AddsToProject()
    {
        var mgr = await CreateManagerAsync();
        var project = await mgr.CreateProjectAsync("P1");
        var coll = await mgr.CreateCollectionAsync(project.Id, "Assets");

        Assert.NotEqual(Guid.Empty, coll.Id);
        Assert.Equal("Assets", coll.Name);
        Assert.Equal(project.Id, coll.ParentId);

        var loaded = mgr.GetProject(project.Id);
        Assert.Single(loaded!.Children);
        Assert.IsType<Collection>(loaded.Children[0]);
    }

    [Fact]
    public async Task CreateNestedCollection_Works()
    {
        var mgr = await CreateManagerAsync();
        var project = await mgr.CreateProjectAsync("P1");
        var parentColl = await mgr.CreateCollectionAsync(project.Id, "Top");
        var childColl = await mgr.CreateCollectionAsync(project.Id, "Sub", parentColl.Id);

        Assert.Equal(parentColl.Id, childColl.ParentId);

        var loaded = mgr.GetProject(project.Id);
        var top = loaded!.FindCollection(parentColl.Id);
        Assert.NotNull(top);
        Assert.Single(top!.Children);
    }

    [Fact]
    public async Task AddFolderReference_ToProject()
    {
        var mgr = await CreateManagerAsync();
        var project = await mgr.CreateProjectAsync("P1");
        var fr = await mgr.AddFolderReferenceAsync(project.Id, @"C:\Dev\MyProject", displayName: "My Code");

        Assert.NotEqual(Guid.Empty, fr.Id);
        Assert.Equal(@"C:\Dev\MyProject", fr.RealPath);
        Assert.Equal("My Code", fr.DisplayName);

        var loaded = mgr.GetProject(project.Id);
        Assert.Single(loaded!.Children);
        var loadedFr = Assert.IsType<FolderReference>(loaded.Children[0]);
        Assert.Equal("My Code", loadedFr.EffectiveName);
    }

    [Fact]
    public async Task AddFolderReference_ToCollection()
    {
        var mgr = await CreateManagerAsync();
        var project = await mgr.CreateProjectAsync("P1");
        var coll = await mgr.CreateCollectionAsync(project.Id, "Assets");
        var fr = await mgr.AddFolderReferenceAsync(project.Id, @"D:\Images", coll.Id);

        var loaded = mgr.GetProject(project.Id);
        var loadedColl = loaded!.FindCollection(coll.Id);
        Assert.NotNull(loadedColl);
        Assert.Single(loadedColl!.Children);
    }

    [Fact]
    public void FolderReference_EffectiveName_NoDisplayName_UsesFolderName()
    {
        var fr = new FolderReference { RealPath = @"C:\Users\Dev\MyProject" };
        Assert.Equal("MyProject", fr.EffectiveName);
    }

    [Fact]
    public void FolderReference_EffectiveName_WithDisplayName_UsesOverride()
    {
        var fr = new FolderReference { RealPath = @"C:\Users\Dev\MyProject", DisplayName = "Active Code" };
        Assert.Equal("Active Code", fr.EffectiveName);
    }

    [Fact]
    public async Task DeleteProject_RemovesFromList()
    {
        var mgr = await CreateManagerAsync();
        var p1 = await mgr.CreateProjectAsync("P1");
        var p2 = await mgr.CreateProjectAsync("P2");
        await mgr.DeleteProjectAsync(p1.Id);

        Assert.Single(mgr.Projects);
        Assert.Equal("P2", mgr.Projects[0].Name);
    }

    [Fact]
    public async Task DeleteCollection_RemovesFromProject()
    {
        var mgr = await CreateManagerAsync();
        var project = await mgr.CreateProjectAsync("P1");
        var coll = await mgr.CreateCollectionAsync(project.Id, "Assets");
        await mgr.AddFolderReferenceAsync(project.Id, @"C:\Something", coll.Id);

        await mgr.DeleteCollectionAsync(project.Id, coll.Id);

        var loaded = mgr.GetProject(project.Id);
        Assert.Empty(loaded!.Children);
    }

    [Fact]
    public async Task RemoveFolderReference_Works()
    {
        var mgr = await CreateManagerAsync();
        var project = await mgr.CreateProjectAsync("P1");
        var fr = await mgr.AddFolderReferenceAsync(project.Id, @"C:\Code");

        await mgr.RemoveFolderReferenceAsync(project.Id, fr.Id);

        var loaded = mgr.GetProject(project.Id);
        Assert.Empty(loaded!.Children);
    }

    [Fact]
    public void Project_FindCollection_FindsNested()
    {
        var project = new Project { Name = "Test" };
        var top = new Collection { Id = Guid.NewGuid(), Name = "Top" };
        var sub = new Collection { Id = Guid.NewGuid(), Name = "Sub" };
        top.Children.Add(sub);
        project.Children.Add(top);

        Assert.Equal(top, project.FindCollection(top.Id));
        Assert.Equal(sub, project.FindCollection(sub.Id));
        Assert.Null(project.FindCollection(Guid.NewGuid()));
    }

    [Fact]
    public async Task Persistence_SaveAndLoad_RoundTrips()
    {
        var mgr = await CreateManagerAsync();
        var project = await mgr.CreateProjectAsync("Persistent Project", "Testing persistence");
        var coll = await mgr.CreateCollectionAsync(project.Id, "Assets");
        await mgr.AddFolderReferenceAsync(project.Id, @"C:\Dev\Project1", coll.Id, "My Code");
        await mgr.AddFolderReferenceAsync(project.Id, @"D:\Art", coll.Id);

        // Read the raw JSON to debug
        var jsonFile = Path.Combine(_tempDir, "projects.json");
        var rawJson = await File.ReadAllTextAsync(jsonFile);
        Assert.False(string.IsNullOrWhiteSpace(rawJson), "JSON file should not be empty");
        Assert.Contains("persistent project", rawJson.ToLowerInvariant());

        // Create a new manager instance that loads from the same directory
        var mgr2 = await CreateManagerAsync();
        Assert.Single(mgr2.Projects);
        var loaded = mgr2.Projects[0];
        Assert.Equal("Persistent Project", loaded.Name);
        Assert.Equal("Testing persistence", loaded.Description);

        var loadedColl = loaded.FindCollection(coll.Id);
        Assert.NotNull(loadedColl);
        Assert.Equal("Assets", loadedColl!.Name);
        Assert.Equal(2, loadedColl.Children.Count);
    }

    [Fact]
    public async Task Serialization_Debug_WritesValidJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ProjectExplorer_JsonDebug_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var repo = new JsonProjectRepository(dir);
            var mgr = new ProjectManager(repo);
            await mgr.InitializeAsync();

            var project = await mgr.CreateProjectAsync("Debug Project");
            var coll = await mgr.CreateCollectionAsync(project.Id, "Col1");
            await mgr.AddFolderReferenceAsync(project.Id, @"/test/path", coll.Id);

            var jsonFile = Path.Combine(dir, "projects.json");
            Assert.True(File.Exists(jsonFile), "projects.json should exist after save");
            var json = await File.ReadAllTextAsync(jsonFile);

            // Output the JSON for debugging
            Console.WriteLine("=== RAW JSON START ===");
            Console.WriteLine(json);
            Console.WriteLine("=== RAW JSON END ===");

            Assert.False(string.IsNullOrWhiteSpace(json), "JSON should not be empty");

            // Verify it's valid JSON by parsing
            var doc = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal(System.Text.Json.JsonValueKind.Array, doc.RootElement.ValueKind);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // ── FileReference tests ──

    [Fact]
    public async Task AddFileReference_AddsToProjectRoot()
    {
        var mgr = await CreateManagerAsync();
        var project = await mgr.CreateProjectAsync("P");

        var fr = await mgr.AddFileReferenceAsync(project.Id, @"C:\Docs\spec.pdf", displayName: "Spec", description: "The brief");

        Assert.NotEqual(Guid.Empty, fr.Id);
        Assert.Equal(@"C:\Docs\spec.pdf", fr.FilePath);
        Assert.Equal("Spec", fr.DisplayName);
        Assert.Equal("The brief", fr.Description);

        var reloaded = await new JsonProjectRepository(_tempDir).LoadAllAsync();
        var loaded = reloaded.FirstOrDefault(p => p.Id == project.Id);
        Assert.NotNull(loaded);
        Assert.Single(loaded!.Children);
        var loadedFr = Assert.IsType<FileReference>(loaded.Children[0]);
        Assert.Equal("Spec", loadedFr.EffectiveName);
        Assert.Equal(@"C:\Docs\spec.pdf", loadedFr.FilePath);
    }

    [Fact]
    public async Task AddFileReference_IntoCollection()
    {
        var mgr = await CreateManagerAsync();
        var project = await mgr.CreateProjectAsync("P");
        var coll = await mgr.CreateCollectionAsync(project.Id, "Docs");

        var fr = await mgr.AddFileReferenceAsync(project.Id, @"D:\report.docx", parentCollectionId: coll.Id);

        Assert.Equal(coll.Id, fr.ParentId);
        var loadedColl = mgr.GetProject(project.Id)!.FindCollection(coll.Id);
        Assert.NotNull(loadedColl);
        Assert.Single(loadedColl!.Children);
        Assert.IsType<FileReference>(loadedColl.Children[0]);
    }

    [Fact]
    public async Task FileReference_EffectiveName_FallsBackToFileName()
    {
        var mgr = await CreateManagerAsync();
        var project = await mgr.CreateProjectAsync("P");

        var fr = await mgr.AddFileReferenceAsync(project.Id, @"C:\Dev\notes.txt");

        Assert.Equal("notes.txt", fr.EffectiveName);
        Assert.Equal(".txt", fr.Extension);
    }

    [Fact]
    public async Task FileReference_Extension_HandlesPosixAndNoExtension()
    {
        var mgr = await CreateManagerAsync();
        var project = await mgr.CreateProjectAsync("P");

        var withExt = await mgr.AddFileReferenceAsync(project.Id, "/home/dev/image.PNG");
        Assert.Equal(".png", withExt.Extension);
        Assert.Equal("image.PNG", withExt.EffectiveName);

        var noExt = await mgr.AddFileReferenceAsync(project.Id, "/home/dev/Makefile");
        Assert.Equal(string.Empty, noExt.Extension);
        Assert.Equal("Makefile", noExt.EffectiveName);
    }

    [Fact]
    public async Task UpdateFileReference_ChangesFields()
    {
        var mgr = await CreateManagerAsync();
        var project = await mgr.CreateProjectAsync("P");
        var fr = await mgr.AddFileReferenceAsync(project.Id, @"C:\old.txt");

        await mgr.UpdateFileReferenceAsync(project.Id, fr.Id,
            newDisplayName: "Renamed", newPath: @"C:\new.md", newDescription: "updated");

        var loaded = mgr.GetProject(project.Id)!.Children.OfType<FileReference>().Single();
        Assert.Equal("Renamed", loaded.DisplayName);
        Assert.Equal(@"C:\new.md", loaded.FilePath);
        Assert.Equal("updated", loaded.Description);
    }

    [Fact]
    public async Task RemoveFileReference_RemovesFromProject()
    {
        var mgr = await CreateManagerAsync();
        var project = await mgr.CreateProjectAsync("P");
        var fr = await mgr.AddFileReferenceAsync(project.Id, @"C:\gone.txt");

        await mgr.RemoveFileReferenceAsync(project.Id, fr.Id);

        var loaded = (await new JsonProjectRepository(_tempDir).LoadAllAsync())
            .First(p => p.Id == project.Id);
        Assert.Empty(loaded.Children);
    }

    [Fact]
    public async Task FileReference_CountsAsLeafNode()
    {
        var mgr = await CreateManagerAsync();
        var project = await mgr.CreateProjectAsync("P");
        await mgr.AddFileReferenceAsync(project.Id, @"C:\a.txt");
        await mgr.AddFileReferenceAsync(project.Id, @"C:\b.txt");

        Assert.Equal(2, LicenseManager.CountLeafNodes(mgr.Projects));
    }
}
