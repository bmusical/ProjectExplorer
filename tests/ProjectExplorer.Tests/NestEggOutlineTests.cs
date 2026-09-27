using ProjectExplorer.Core.Models;
using ProjectExplorer.Core.Services;
using ProjectExplorer.Core.Sharing;

namespace ProjectExplorer.Tests;

public class NestEggOutlineTests
{
    [Fact]
    public async Task ExampleOutline_ImportsAsEmptyCollectionsInChapterOrder()
    {
        var path = FindExample();
        var egg = NestEggFile.Read(path);
        NestEggOutline.Validate(egg);

        var imported = NestEggImporter.MaterializeFromFile(egg, path, []);

        Assert.Equal("Cutting a release", imported.Name);
        Assert.Equal("video-chapter-outline.nestegg.json", imported.Metadata[SharedImportMetadata.ImportFile]);
        Assert.False(imported.Metadata.ContainsKey(SharedImportMetadata.ShareCode));
        Assert.Equal("outliner", imported.Metadata[SharedImportMetadata.SenderLabel]);
        Assert.Equal(0, LicenseManager.CountLeafNodes([imported]));

        var chapters = imported.Children.Cast<Collection>().ToList();
        Assert.Equal(
            ["1. Cold open", "2. Where the nest lives", "3. Hand it to the other computer", "4. Close"],
            chapters.Select(c => c.Name).ToArray());
        Assert.Equal(["cold-open", "where-the-nest-lives", "hand-it-over", "close"], chapters.Select(Key).ToArray());
        Assert.All(chapters, c => Assert.Equal("chapter", c.Metadata[NestEggOutline.RoleMetadataKey]));

        var coldOpen = chapters[0];
        Assert.Equal(["The problem", "The payoff"], coldOpen.Children.Cast<Collection>().Select(c => c.Name).ToArray());
        Assert.All(coldOpen.Children, c => Assert.Equal("beat", c.Metadata[NestEggOutline.RoleMetadataKey]));

        var nest = chapters[1];
        var sections = nest.Children.Cast<Collection>().ToList();
        Assert.Equal(["The database file", "Collections are not folders"], sections.Select(c => c.Name).ToArray());
        Assert.Equal("projects.db", Assert.IsType<Collection>(Assert.Single(sections[0].Children)).Name);
        Assert.Empty(sections[1].Children);
        Assert.Empty(chapters[3].Children);

        var taken = NestEggImporter.MaterializeFromFile(egg, path, [imported.Name]);
        Assert.Equal("Cutting a release (2)", taken.Name);
        var takenAgain = NestEggImporter.MaterializeFromFile(egg, path, [imported.Name, taken.Name]);
        Assert.Equal("Cutting a release (3)", takenAgain.Name);

        var dir = Path.Combine(Path.GetTempPath(), "nest-outline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var manager = new ProjectManager(new SqliteProjectRepository(dir));
            await manager.InitializeAsync();
            var license = new LicenseInfo
            {
                State = LicenseState.Free,
                ProjectCount = 0,
                ProjectLimit = LicenseManager.FreeProjectLimit,
                LeafNodeCount = 0,
                LeafNodeLimit = LicenseManager.FreeLeafNodeLimit
            };
            var saved = await manager.ImportSharedProjectAsync(imported, license);
            var reloaded = new ProjectManager(new SqliteProjectRepository(dir));
            await reloaded.InitializeAsync();
            var stored = Assert.Single(reloaded.Projects);
            Assert.Equal(saved.Id, stored.Id);
            Assert.Equal(4, stored.Children.Count);
            var storedBeat = Assert.IsType<Collection>(
                Assert.IsType<Collection>(stored.Children[0]).Children[0]);
            Assert.Equal("cold-open-problem", storedBeat.Metadata[NestEggOutline.KeyMetadataKey]);
            Assert.NotEqual(Guid.Empty, storedBeat.Id);
            Assert.Equal(
                "22222222-2222-4222-8222-222222222211",
                storedBeat.Metadata[SharedImportMetadata.SourceNodeId]);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* the assertion already ran */ }
        }
    }

    [Fact]
    public void Outline_RejectsAFolderAndAMixedChapter()
    {
        var egg = OutlineEgg();
        egg.Project.Nodes.Add(new NestEggNode
        {
            SourceId = Guid.NewGuid(),
            ParentSourceId = egg.Project.Nodes[0].SourceId,
            ChildType = "folderReference",
            SortOrder = 0,
            RealPath = @"D:\clips",
            Metadata = OutlineMetadata("beat", "b-roll")
        });

        var folder = Assert.Throws<NestEggFormatException>(() => NestEggOutline.Validate(egg));
        Assert.Contains("only collections", folder.Message, StringComparison.OrdinalIgnoreCase);

        var mixed = OutlineEgg();
        var chapterId = mixed.Project.Nodes[0].SourceId;
        mixed.Project.Nodes.Add(Collection(chapterId, 0, "section", "setup"));
        mixed.Project.Nodes.Add(Collection(chapterId, 1, "beat", "punchline"));
        var mixedError = Assert.Throws<NestEggFormatException>(() => NestEggImporter.MaterializeFromFile(mixed, "outline.nestegg.json", []));
        Assert.Contains("all sections or all beats", mixedError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Outline_WithoutRoles_ImportsAPlainCollectionTree()
    {
        var projectId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var egg = new NestEggDocument
        {
            Source = new NestEggSource { MachineLabel = "outliner", AppVersion = NestEggOutline.ProfileVersion },
            Project = new NestEggProject
            {
                SourceId = projectId,
                Name = "Notes",
                Nodes =
                [
                    new NestEggNode
                    {
                        SourceId = parentId,
                        ParentSourceId = projectId,
                        ChildType = "collection",
                        SortOrder = 0,
                        Name = "Opening"
                    },
                    new NestEggNode
                    {
                        SourceId = Guid.NewGuid(),
                        ParentSourceId = parentId,
                        ChildType = "collection",
                        SortOrder = 0,
                        Name = "First point"
                    }
                ]
            }
        };

        var imported = NestEggImporter.MaterializeFromFile(egg, "notes.nestegg.json", []);

        Assert.Equal("Notes", imported.Name);
        var opening = Assert.IsType<Collection>(Assert.Single(imported.Children));
        Assert.Equal("Opening", opening.Name);
        Assert.False(opening.Metadata.ContainsKey(NestEggOutline.RoleMetadataKey));
        Assert.Equal("First point", Assert.IsType<Collection>(Assert.Single(opening.Children)).Name);
    }

    [Fact]
    public void FileImport_OfAFullEgg_KeepsPathsAndSkipsOutlineRules()
    {
        var project = new Project { Name = "Client Site" };
        project.Children.Add(new FolderReference { RealPath = @"D:\Clients\Site", SortOrder = 0 });
        var egg = NestEggCodec.Create(project, "office-pc", "1.0.9");

        var imported = NestEggImporter.MaterializeFromFile(
            egg, Path.Combine("hand-off", "client-site.nestegg.json"), []);

        Assert.Equal("client-site.nestegg.json", imported.Metadata[SharedImportMetadata.ImportFile]);
        Assert.False(imported.Metadata.ContainsKey(SharedImportMetadata.ShareCode));
        var folder = Assert.IsType<FolderReference>(Assert.Single(imported.Children));
        Assert.Equal(@"D:\Clients\Site", folder.RealPath);
    }

    private static string Key(Collection collection) => collection.Metadata[NestEggOutline.KeyMetadataKey];

    private static NestEggDocument OutlineEgg()
    {
        var projectId = Guid.NewGuid();
        return new NestEggDocument
        {
            Source = new NestEggSource { MachineLabel = "outliner", AppVersion = NestEggOutline.ProfileVersion },
            Project = new NestEggProject
            {
                SourceId = projectId,
                Name = "Video",
                Nodes = [Collection(projectId, 0, "chapter", "cold-open", "1. Cold open")]
            }
        };
    }

    private static NestEggNode Collection(Guid parentId, int sortOrder, string role, string key, string? name = null) =>
        new()
        {
            SourceId = Guid.NewGuid(),
            ParentSourceId = parentId,
            ChildType = "collection",
            SortOrder = sortOrder,
            Name = name ?? key,
            Metadata = OutlineMetadata(role, key)
        };

    private static Dictionary<string, string> OutlineMetadata(string role, string key) => new()
    {
        [NestEggOutline.ProfileMetadataKey] = NestEggOutline.ProfileMetadataValue,
        [NestEggOutline.RoleMetadataKey] = role,
        [NestEggOutline.KeyMetadataKey] = key
    };

    private static string FindExample()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "examples", "video-chapter-outline.nestegg.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("video-chapter-outline.nestegg.json was not found above the test output directory.");
    }
}
