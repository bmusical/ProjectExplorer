using ProjectExplorer.Core.Models;
using ProjectExplorer.Core.Services;

namespace ProjectExplorer.Tests;

public class SqliteProjectRepositoryTests
{
    private readonly string _tempDir;

    public SqliteProjectRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ProjectExplorer_SqliteTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private SqliteProjectRepository CreateRepo() => new(_tempDir);

    [Fact]
    public async Task LoadAllAsync_OnFreshDatabase_ReturnsEmptyList()
    {
        var repo = CreateRepo();
        var projects = await repo.LoadAllAsync();
        Assert.Empty(projects);
        Assert.True(File.Exists(Path.Combine(_tempDir, "projects.db")));
    }

    [Fact]
    public async Task SaveProjectAsync_ThenLoadAllAsync_RoundTripsTopLevelFields()
    {
        var repo = CreateRepo();
        var project = new Project
        {
            Name = "My Project",
            Description = "A description",
            Color = "#3366FF",
            IconKey = "icon-1"
        };

        await repo.SaveProjectAsync(project);
        var loaded = await repo.LoadAllAsync();

        var reloaded = Assert.Single(loaded);
        Assert.Equal(project.Id, reloaded.Id);
        Assert.Equal("My Project", reloaded.Name);
        Assert.Equal("A description", reloaded.Description);
        Assert.Equal("#3366FF", reloaded.Color);
        Assert.Equal("icon-1", reloaded.IconKey);
    }

    [Fact]
    public async Task SaveProjectAsync_RoundTripsAllFourChildTypesAndNestedCollections()
    {
        var repo = CreateRepo();
        var project = new Project { Name = "P1" };

        var topCollection = new Collection { Name = "Top", Description = "Top collection", Color = "#ABCDEF" };
        var subCollection = new Collection { Name = "Sub" };
        var folderRef = new FolderReference { RealPath = @"C:\Dev\Code", Description = "Code folder", DisplayName = "Code" };
        var fileRef = new FileReference { FilePath = @"C:\Docs\spec.pdf", Description = "The brief" };
        var webResource = new WebResource { Url = "https://example.com", Description = "Docs" };

        subCollection.Children.Add(folderRef);
        topCollection.Children.Add(subCollection);
        topCollection.Children.Add(fileRef);
        project.Children.Add(topCollection);
        project.Children.Add(webResource);

        await repo.SaveProjectAsync(project);
        var loaded = (await repo.LoadAllAsync()).Single();

        Assert.Equal(2, loaded.Children.Count);
        var loadedTop = Assert.IsType<Collection>(loaded.Children.Single(c => c.Id == topCollection.Id));
        Assert.Equal("Top", loadedTop.Name);
        Assert.Equal("Top collection", loadedTop.Description);
        Assert.Equal("#ABCDEF", loadedTop.Color);
        Assert.Equal(2, loadedTop.Children.Count);

        var loadedSub = Assert.IsType<Collection>(loadedTop.Children.Single(c => c.Id == subCollection.Id));
        var loadedFolderRef = Assert.IsType<FolderReference>(loadedSub.Children.Single());
        Assert.Equal(@"C:\Dev\Code", loadedFolderRef.RealPath);
        Assert.Equal("Code folder", loadedFolderRef.Description);
        Assert.Equal("Code", loadedFolderRef.DisplayName);
        Assert.Equal(subCollection.Id, loadedFolderRef.ParentId);

        var loadedFileRef = Assert.IsType<FileReference>(loadedTop.Children.Single(c => c.Id == fileRef.Id));
        Assert.Equal(@"C:\Docs\spec.pdf", loadedFileRef.FilePath);
        Assert.Equal("The brief", loadedFileRef.Description);

        var loadedWebResource = Assert.IsType<WebResource>(loaded.Children.Single(c => c.Id == webResource.Id));
        Assert.Equal("https://example.com", loadedWebResource.Url);
        Assert.Equal(project.Id, loadedWebResource.ParentId);
    }

    [Fact]
    public async Task SaveProjectAsync_RoundTripsMetadata()
    {
        var repo = CreateRepo();
        var project = new Project { Name = "P1" };
        var folderRef = new FolderReference { RealPath = @"C:\Dev" };
        folderRef.Metadata["suppressAutoRetry"] = "true";
        folderRef.Metadata["note"] = "flaky drive";
        project.Children.Add(folderRef);

        await repo.SaveProjectAsync(project);
        var loaded = (await repo.LoadAllAsync()).Single();
        var loadedFolderRef = Assert.IsType<FolderReference>(loaded.Children.Single());

        Assert.Equal(2, loadedFolderRef.Metadata.Count);
        Assert.Equal("true", loadedFolderRef.Metadata["suppressAutoRetry"]);
        Assert.Equal("flaky drive", loadedFolderRef.Metadata["note"]);
    }

    [Fact]
    public async Task SaveProjectAsync_PreservesSortOrderAcrossChildrenAndSiblingProjects()
    {
        var repo = CreateRepo();
        var project = new Project { Name = "P1" };
        var first = new FolderReference { RealPath = "A", SortOrder = 0 };
        var second = new FolderReference { RealPath = "B", SortOrder = 1 };
        var third = new FolderReference { RealPath = "C", SortOrder = 2 };
        project.Children.AddRange(new ProjectChild[] { third, first, second }); // deliberately out of order

        await repo.SaveProjectAsync(project);
        var loaded = (await repo.LoadAllAsync()).Single();

        Assert.Equal(new[] { "A", "B", "C" }, loaded.Children.Cast<FolderReference>().Select(f => f.RealPath));
    }

    [Fact]
    public async Task SaveProjectAsync_OnExistingProject_OnlyReplacesThatProjectsChildren()
    {
        var repo = CreateRepo();
        var project1 = new Project { Name = "P1" };
        project1.Children.Add(new FolderReference { RealPath = "P1-Folder" });
        var project2 = new Project { Name = "P2" };
        project2.Children.Add(new FolderReference { RealPath = "P2-Folder" });

        await repo.SaveProjectAsync(project1);
        await repo.SaveProjectAsync(project2);

        // Edit project1 only — project2's rows must survive untouched.
        project1.Children.Clear();
        project1.Children.Add(new FolderReference { RealPath = "P1-Folder-Renamed" });
        await repo.SaveProjectAsync(project1);

        var loaded = await repo.LoadAllAsync();
        Assert.Equal(2, loaded.Count);
        var loadedP1 = loaded.Single(p => p.Id == project1.Id);
        var loadedP2 = loaded.Single(p => p.Id == project2.Id);
        Assert.Equal("P1-Folder-Renamed", Assert.IsType<FolderReference>(loadedP1.Children.Single()).RealPath);
        Assert.Equal("P2-Folder", Assert.IsType<FolderReference>(loadedP2.Children.Single()).RealPath);
    }

    [Fact]
    public async Task SaveProjectAsync_RenamingExistingProject_DoesNotChangeItsTopLevelPosition()
    {
        var repo = CreateRepo();
        var p1 = new Project { Name = "P1" };
        var p2 = new Project { Name = "P2" };
        var p3 = new Project { Name = "P3" };
        await repo.SaveProjectAsync(p1);
        await repo.SaveProjectAsync(p2);
        await repo.SaveProjectAsync(p3);

        p2.Name = "P2 Renamed";
        await repo.SaveProjectAsync(p2);

        var loaded = await repo.LoadAllAsync();
        Assert.Equal(new[] { "P1", "P2 Renamed", "P3" }, loaded.Select(p => p.Name));
    }

    [Fact]
    public async Task SaveAllAsync_PersistsProjectOrder()
    {
        var repo = CreateRepo();
        var p1 = new Project { Name = "P1" };
        var p2 = new Project { Name = "P2" };
        var p3 = new Project { Name = "P3" };

        await repo.SaveAllAsync(new[] { p1, p2, p3 });
        var loaded = await repo.LoadAllAsync();
        Assert.Equal(new[] { "P1", "P2", "P3" }, loaded.Select(p => p.Name));

        // Reorder: move p3 to the front.
        await repo.SaveAllAsync(new[] { p3, p1, p2 });
        var reloaded = await repo.LoadAllAsync();
        Assert.Equal(new[] { "P3", "P1", "P2" }, reloaded.Select(p => p.Name));
    }

    [Fact]
    public async Task SaveAllAsync_DropsProjectsNotInTheGivenList()
    {
        var repo = CreateRepo();
        var p1 = new Project { Name = "P1" };
        var p2 = new Project { Name = "P2" };
        await repo.SaveAllAsync(new[] { p1, p2 });

        await repo.SaveAllAsync(new[] { p1 });

        var loaded = await repo.LoadAllAsync();
        Assert.Single(loaded);
        Assert.Equal("P1", loaded[0].Name);
    }

    [Fact]
    public async Task DeleteProjectAsync_RemovesProjectAndItsChildren()
    {
        var repo = CreateRepo();
        var project = new Project { Name = "P1" };
        project.Children.Add(new FolderReference { RealPath = "C:\\Dev" });
        await repo.SaveProjectAsync(project);

        await repo.DeleteProjectAsync(project.Id);

        var loaded = await repo.LoadAllAsync();
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task LoadAllAsync_NewRepositoryInstance_SeesPreviouslySavedData()
    {
        var repo1 = CreateRepo();
        var project = new Project { Name = "Persisted" };
        project.Children.Add(new FolderReference { RealPath = "C:\\Dev" });
        await repo1.SaveProjectAsync(project);

        var repo2 = CreateRepo();
        var loaded = await repo2.LoadAllAsync();

        Assert.Single(loaded);
        Assert.Equal("Persisted", loaded[0].Name);
    }
}
