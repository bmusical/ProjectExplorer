using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ProjectExplorer.Core.Models;
using ProjectExplorer.Core.Services;
using ProjectExplorer.Core.Sharing;

namespace ProjectExplorer.Tests;

public class SharingPhase1Tests : IClassFixture<SharingPhase1Tests.SharingServerFixture>
{
    private readonly SharingServerFixture _server;

    public SharingPhase1Tests(SharingServerFixture server) => _server = server;

    [Fact]
    public void Create_ThenMaterialize_RemintsIdsAndKeepsPaths()
    {
        var original = SampleProject();
        var egg = NestEggCodec.Create(original, "office-pc", "1.0.9");
        var json = NestEggCodec.ToJson(egg);
        var parsed = NestEggCodec.Parse(json);

        var imported = NestEggImporter.Materialize(parsed, "ABCD-EFGH", ["Other"]);

        Assert.NotEqual(original.Id, imported.Id);
        Assert.Equal("Client Site", imported.Name);
        Assert.Equal(original.Id.ToString(), imported.Metadata[SharedImportMetadata.SourceProjectId]);
        Assert.Equal("ABCD-EFGH", imported.Metadata[SharedImportMetadata.ShareCode]);
        Assert.Equal("office-pc", imported.Metadata[SharedImportMetadata.SenderLabel]);

        var collection = Assert.IsType<Collection>(Assert.Single(imported.Children, c => c is Collection));
        Assert.Equal("Assets", collection.Name);
        Assert.NotEqual(original.Children.OfType<Collection>().Single().Id, collection.Id);
        Assert.Equal(imported.Id, collection.ParentId);

        var folder = Assert.IsType<FolderReference>(Assert.Single(collection.Children, c => c is FolderReference));
        Assert.Equal(@"D:\Clients\Site\assets", folder.RealPath);
        Assert.Equal("Logo", folder.DisplayName);
        Assert.Equal("keep-me", folder.Metadata["tag"]);
        Assert.Equal(collection.Id, folder.ParentId);
        Assert.True(folder.Metadata.ContainsKey(SharedImportMetadata.SourceNodeId));

        var web = Assert.IsType<WebResource>(Assert.Single(imported.Children, c => c is WebResource));
        Assert.Equal("https://example.com/brief", web.Url);
        Assert.True(web.OpenExternalOnly);

        var reshared = NestEggCodec.Create(imported, "laptop", "1.0.9");
        Assert.Equal(imported.Id, reshared.Project.SourceId);
        Assert.DoesNotContain(reshared.Project.Nodes, n => n.Metadata.Keys.Any(k => k.StartsWith("shared.")));
    }

    [Fact]
    public void Materialize_WhenNameTaken_SuffixesShared()
    {
        var egg = NestEggCodec.Create(new Project { Name = "Client Site" }, "office-pc", null);
        var first = NestEggImporter.Materialize(egg, "AAAA-BBBB", ["Client Site"]);
        var second = NestEggImporter.Materialize(egg, "CCCC-DDDD", ["Client Site", first.Name]);
        Assert.Equal("Client Site (shared)", first.Name);
        Assert.Equal("Client Site (shared 2)", second.Name);
    }

    [Fact]
    public void Validate_RejectsACycle()
    {
        var projectId = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var egg = new NestEggDocument
        {
            Source = new NestEggSource { MachineLabel = "office-pc" },
            Project = new NestEggProject
            {
                SourceId = projectId,
                Name = "Loop",
                Nodes =
                [
                    new NestEggNode { SourceId = a, ParentSourceId = b, ChildType = "collection", Name = "A" },
                    new NestEggNode { SourceId = b, ParentSourceId = a, ChildType = "collection", Name = "B" }
                ]
            }
        };

        var error = Assert.Throws<NestEggFormatException>(() => NestEggCodec.Validate(egg));
        Assert.Contains("circular", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreeTier_BlocksASixthProjectAndTooManyLeaves()
    {
        var egg = NestEggCodec.Create(SampleProject(), "office-pc", null);
        var incoming = NestEggImporter.Materialize(egg, "ABCD-EFGH", []);
        var atProjectLimit = new LicenseInfo
        {
            State = LicenseState.Free,
            ProjectCount = LicenseManager.FreeProjectLimit,
            ProjectLimit = LicenseManager.FreeProjectLimit,
            LeafNodeLimit = LicenseManager.FreeLeafNodeLimit
        };
        Assert.Contains("projects", NestEggImporter.FreeTierBlockReason(atProjectLimit, incoming));

        var atLeafLimit = new LicenseInfo
        {
            State = LicenseState.Free,
            ProjectCount = 1,
            ProjectLimit = LicenseManager.FreeProjectLimit,
            LeafNodeCount = LicenseManager.FreeLeafNodeLimit - 1,
            LeafNodeLimit = LicenseManager.FreeLeafNodeLimit
        };
        Assert.Contains("references", NestEggImporter.FreeTierBlockReason(atLeafLimit, incoming));

        var licensed = new LicenseInfo { State = LicenseState.Licensed };
        Assert.Null(NestEggImporter.FreeTierBlockReason(licensed, incoming));
    }

    [Fact]
    public async Task TwoComputers_PublishPreviewFetchImportAndRevoke()
    {
        using var http = _server.CreateClient();
        var api = new NestShareClient(http);
        var server = http.BaseAddress!;

        await api.PingAsync(server);
        Assert.True(File.Exists(_server.DbPath));

        var sender = SampleProject();
        var egg = NestEggCodec.Create(sender, "office-pc", "1.0.9");
        var published = await api.PublishAsync(server, egg, "office-pc");
        Assert.Matches("^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{4}-[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{4}$", published.Code);

        var preview = await api.PreviewAsync(server, published.Code.Replace("-", ""), "laptop");
        Assert.Equal("Client Site", preview.ProjectName);
        Assert.Equal("office-pc", preview.SenderLabel);
        Assert.Equal(1, preview.CollectionCount);
        Assert.Equal(1, preview.FolderCount);
        Assert.Equal(1, preview.FileCount);
        Assert.Equal(1, preview.WebCount);
        Assert.Contains(@"D:\Clients\Site\assets", preview.FolderPaths);

        var fetched = await api.FetchAsync(server, published.Code, "laptop");
        var received = NestEggImporter.Materialize(fetched, published.Code, []);

        var dir = Path.Combine(Path.GetTempPath(), "nest-import-" + Guid.NewGuid().ToString("N"));
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
            var saved = await manager.ImportSharedProjectAsync(received, license);
            var reloaded = new ProjectManager(new SqliteProjectRepository(dir));
            await reloaded.InitializeAsync();
            var stored = Assert.Single(reloaded.Projects);
            Assert.Equal(saved.Id, stored.Id);
            Assert.Equal(sender.Id.ToString(), stored.Metadata[SharedImportMetadata.SourceProjectId]);
            var storedFolder = Assert.IsType<FolderReference>(
                Assert.IsType<Collection>(stored.Children.Single(c => c is Collection)).Children.Single(c => c is FolderReference));
            Assert.Equal(@"D:\Clients\Site\assets", storedFolder.RealPath);

            await api.ReportImportedAsync(server, published.Code, "laptop", "new project " + saved.Id);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* the assertion already ran */ }
        }

        var events = await api.GetEventsAsync(server, published.Code);
        Assert.Contains(events, e => e.EventType == ShareEventTypes.Created && e.MachineLabel == "office-pc");
        Assert.Contains(events, e => e.EventType == ShareEventTypes.Previewed && e.MachineLabel == "laptop");
        Assert.Contains(events, e => e.EventType == ShareEventTypes.Fetched && e.MachineLabel == "laptop");
        Assert.Contains(events, e => e.EventType == ShareEventTypes.Imported && e.MachineLabel == "laptop");

        await api.RevokeAsync(server, published.Code, "office-pc");
        var afterRevoke = await api.GetEventsAsync(server, published.Code);
        Assert.Contains(afterRevoke, e => e.EventType == ShareEventTypes.Revoked && e.MachineLabel == "office-pc");
        var revoked = await Assert.ThrowsAsync<NestShareException>(() => api.FetchAsync(server, published.Code, "laptop"));
        Assert.Contains("revoked", revoked.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Publish_RejectsACycle_AndUnknownCodeIsNotFound()
    {
        using var http = _server.CreateClient();
        var api = new NestShareClient(http);
        var server = http.BaseAddress!;

        var broken = new NestEggDocument
        {
            Source = new NestEggSource { MachineLabel = "office-pc" },
            Project = new NestEggProject { SourceId = Guid.NewGuid(), Name = "Nope" }
        };
        var child = Guid.NewGuid();
        broken.Project.Nodes.Add(new NestEggNode
        {
            SourceId = child,
            ParentSourceId = child,
            ChildType = "collection",
            Name = "Self"
        });

        var rejectedLocally = Assert.Throws<NestEggFormatException>(() => NestEggCodec.Validate(broken));
        Assert.Contains("parent", rejectedLocally.Message, StringComparison.OrdinalIgnoreCase);

        var payload = JsonSerializer.Serialize(new PublishShareRequest { MachineLabel = "office-pc", Egg = broken }, NestEggCodec.JsonOptions);
        var raw = await http.PostAsync(new Uri(server, "api/shares"), new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, raw.StatusCode);

        var missing = await Assert.ThrowsAsync<NestShareException>(() => api.PreviewAsync(server, "ABCD-EFGH", "laptop"));
        Assert.Contains("code", missing.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExpiredShare_IsRejected()
    {
        await using var expired = new SharingServerFixture { LifetimeDays = 0 };
        using var http = expired.CreateClient();
        var api = new NestShareClient(http);
        var published = await api.PublishAsync(http.BaseAddress!, NestEggCodec.Create(new Project { Name = "Old" }, "office-pc", null), "office-pc");
        var error = await Assert.ThrowsAsync<NestShareException>(() => api.FetchAsync(http.BaseAddress!, published.Code, "laptop"));
        Assert.Contains("expired", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Project SampleProject()
    {
        var project = new Project
        {
            Name = "Client Site",
            Description = "Redesign",
            Color = "#3366FF"
        };
        var assets = new Collection { Name = "Assets", Description = "Art", Color = "#ABCDEF", ParentId = project.Id, SortOrder = 0 };
        var folder = new FolderReference
        {
            RealPath = @"D:\Clients\Site\assets",
            DisplayName = "Logo",
            Description = "Source art",
            ParentId = assets.Id,
            SortOrder = 0
        };
        folder.Metadata["tag"] = "keep-me";
        folder.Metadata[SharedImportMetadata.SourceNodeId] = Guid.NewGuid().ToString();
        var file = new FileReference
        {
            FilePath = @"D:\Clients\Site\brief.pdf",
            Description = "The brief",
            ParentId = assets.Id,
            SortOrder = 1
        };
        assets.Children.Add(folder);
        assets.Children.Add(file);
        project.Children.Add(assets);
        project.Children.Add(new WebResource
        {
            Url = "https://example.com/brief",
            Description = "Live brief",
            OpenExternalOnly = true,
            ParentId = project.Id,
            SortOrder = 1
        });
        return project;
    }

    public class SharingServerFixture : WebApplicationFactory<Program>
    {
        public string DbPath { get; } = Path.Combine(Path.GetTempPath(), "nest-share-" + Guid.NewGuid().ToString("N") + ".db");
        public int LifetimeDays { get; init; } = 7;

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Sharing:DatabasePath", DbPath);
            builder.UseSetting("Sharing:LifetimeDays", LifetimeDays.ToString());
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Sharing:DatabasePath"] = DbPath,
                    ["Sharing:LifetimeDays"] = LifetimeDays.ToString()
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            TryDelete(DbPath);
        }

        private static void TryDelete(string path)
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try
                {
                    var candidate = path + suffix;
                    if (File.Exists(candidate)) File.Delete(candidate);
                }
                catch { /* the test result does not depend on deleting the temp file */ }
            }
        }
    }
}
