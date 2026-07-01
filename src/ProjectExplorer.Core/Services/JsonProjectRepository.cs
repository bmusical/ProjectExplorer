using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectExplorer.Core.Interfaces;
using ProjectExplorer.Core.Models;

namespace ProjectExplorer.Core.Services;

/// <summary>
/// JSON-file-based implementation of IProjectRepository.
/// Stores project definitions in %APPDATA%\ProjectExplorer\projects.json.
/// Uses manual JSON node parsing to handle polymorphic ProjectChild deserialization
/// without the circular converter issues that System.Text.Json polymorphic support has.
/// </summary>
public class JsonProjectRepository : IProjectRepository
{
    private readonly string _storageDir;
    private readonly string _projectsFile;
    private readonly string _backupFile;

    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonProjectRepository()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _storageDir = Path.Combine(appData, "ProjectExplorer");
        _projectsFile = Path.Combine(_storageDir, "projects.json");
        _backupFile = Path.Combine(_storageDir, "projects.json.bak");
    }

    public JsonProjectRepository(string storageDir)
    {
        _storageDir = storageDir;
        _projectsFile = Path.Combine(_storageDir, "projects.json");
        _backupFile = Path.Combine(_storageDir, "projects.json.bak");
    }

    public async Task<List<Project>> LoadAllAsync()
    {
        if (!File.Exists(_projectsFile))
            return new List<Project>();

        var json = await File.ReadAllTextAsync(_projectsFile);
        var node = JsonNode.Parse(json);
        if (node is not JsonArray arr)
            return new List<Project>();

        return arr.Select(ParseProject).Where(p => p != null).ToList()!;
    }

    public async Task SaveAllAsync(IEnumerable<Project> projects)
    {
        Directory.CreateDirectory(_storageDir);

        if (File.Exists(_projectsFile))
            File.Copy(_projectsFile, _backupFile, overwrite: true);

        var arr = new JsonArray();
        foreach (var project in projects)
            arr.Add(SerializeProject(project));

        await File.WriteAllTextAsync(_projectsFile, arr.ToJsonString(DefaultOptions));
    }

    public async Task SaveProjectAsync(Project project)
    {
        var projects = await LoadAllAsync();
        var existingIndex = projects.FindIndex(p => p.Id == project.Id);
        if (existingIndex >= 0)
            projects[existingIndex] = project;
        else
            projects.Add(project);

        project.Modified = DateTime.UtcNow;
        await SaveAllAsync(projects);
    }

    public async Task DeleteProjectAsync(Guid projectId)
    {
        var projects = await LoadAllAsync();
        projects.RemoveAll(p => p.Id == projectId);
        await SaveAllAsync(projects);
    }

    // ── Manual Serialization ──

    private static JsonObject SerializeProject(Project project)
    {
        var obj = new JsonObject
        {
            ["id"] = project.Id.ToString(),
            ["name"] = project.Name,
            ["description"] = project.Description,
            ["color"] = project.Color,
            ["iconKey"] = project.IconKey,
            ["created"] = project.Created.ToString("O"),
            ["modified"] = project.Modified.ToString("O"),
            ["children"] = SerializeChildren(project.Children)
        };
        return obj;
    }

    private static JsonArray SerializeChildren(List<ProjectChild> children)
    {
        var arr = new JsonArray();
        foreach (var child in children)
        {
            if (child is Collection coll)
                arr.Add(SerializeCollection(coll));
            else if (child is FolderReference fr)
                arr.Add(SerializeFolderReference(fr));
            else if (child is WebResource wr)
                arr.Add(SerializeWebResource(wr));
            else if (child is FileReference fileRef)
                arr.Add(SerializeFileReference(fileRef));
        }
        return arr;
    }

    private static JsonObject SerializeCollection(Collection coll)
    {
        var obj = new JsonObject
        {
            ["childType"] = "collection",
            ["id"] = coll.Id.ToString(),
            ["parentId"] = coll.ParentId.ToString(),
            ["sortOrder"] = coll.SortOrder,
            ["name"] = coll.Name,
            ["description"] = coll.Description,
            ["color"] = coll.Color,
            ["displayName"] = coll.DisplayName,
            ["children"] = SerializeChildren(coll.Children)
        };

        if (coll.Metadata.Count > 0)
        {
            var meta = new JsonObject();
            foreach (var kvp in coll.Metadata)
                meta[kvp.Key] = kvp.Value;
            obj["metadata"] = meta;
        }

        return obj;
    }

    private static JsonObject SerializeFolderReference(FolderReference fr)
    {
        var obj = new JsonObject
        {
            ["childType"] = "folderReference",
            ["id"] = fr.Id.ToString(),
            ["parentId"] = fr.ParentId.ToString(),
            ["sortOrder"] = fr.SortOrder,
            ["realPath"] = fr.RealPath,
            ["displayName"] = fr.DisplayName,
            ["description"] = fr.Description
        };

        if (fr.Metadata.Count > 0)
        {
            var meta = new JsonObject();
            foreach (var kvp in fr.Metadata)
                meta[kvp.Key] = kvp.Value;
            obj["metadata"] = meta;
        }

        return obj;
    }

    private static JsonObject SerializeWebResource(WebResource wr)
    {
        var obj = new JsonObject
        {
            ["childType"] = "webResource",
            ["id"] = wr.Id.ToString(),
            ["parentId"] = wr.ParentId.ToString(),
            ["sortOrder"] = wr.SortOrder,
            ["url"] = wr.Url,
            ["displayName"] = wr.DisplayName,
            ["description"] = wr.Description
        };

        if (wr.Metadata.Count > 0)
        {
            var meta = new JsonObject();
            foreach (var kvp in wr.Metadata)
                meta[kvp.Key] = kvp.Value;
            obj["metadata"] = meta;
        }

        return obj;
    }

    private static JsonObject SerializeFileReference(FileReference fileRef)
    {
        var obj = new JsonObject
        {
            ["childType"] = "fileReference",
            ["id"] = fileRef.Id.ToString(),
            ["parentId"] = fileRef.ParentId.ToString(),
            ["sortOrder"] = fileRef.SortOrder,
            ["filePath"] = fileRef.FilePath,
            ["displayName"] = fileRef.DisplayName,
            ["description"] = fileRef.Description
        };

        if (fileRef.Metadata.Count > 0)
        {
            var meta = new JsonObject();
            foreach (var kvp in fileRef.Metadata)
                meta[kvp.Key] = kvp.Value;
            obj["metadata"] = meta;
        }

        return obj;
    }

    // ── Manual Deserialization ──

    private static Project? ParseProject(JsonNode? node)
    {
        if (node == null) return null;

        var project = new Project
        {
            Id = Guid.TryParse(node["id"]?.GetValue<string>(), out var id) ? id : Guid.NewGuid(),
            Name = node["name"]?.GetValue<string>() ?? "Unnamed",
            Description = node["description"]?.GetValue<string>(),
            Color = node["color"]?.GetValue<string>(),
            IconKey = node["iconKey"]?.GetValue<string>(),
            Created = DateTime.TryParse(node["created"]?.GetValue<string>(), out var created) ? created : DateTime.UtcNow,
            Modified = DateTime.TryParse(node["modified"]?.GetValue<string>(), out var modified) ? modified : DateTime.UtcNow
        };

        var childrenArr = node["children"]?.AsArray();
        if (childrenArr != null)
        {
            foreach (var childNode in childrenArr)
            {
                var child = ParseChild(childNode);
                if (child != null)
                    project.Children.Add(child);
            }
        }

        return project;
    }

    private static ProjectChild? ParseChild(JsonNode? node)
    {
        if (node == null) return null;

        var childType = node["childType"]?.GetValue<string>();
        return childType switch
        {
            "collection" => ParseCollection(node),
            "folderReference" => ParseFolderReference(node),
            "webResource" => ParseWebResource(node),
            "fileReference" => ParseFileReference(node),
            _ => null
        };
    }

    private static Collection ParseCollection(JsonNode node)
    {
        var coll = new Collection
        {
            Id = Guid.TryParse(node["id"]?.GetValue<string>(), out var id) ? id : Guid.NewGuid(),
            ParentId = Guid.TryParse(node["parentId"]?.GetValue<string>(), out var pid) ? pid : Guid.Empty,
            SortOrder = node["sortOrder"]?.GetValue<int>() ?? 0,
            Name = node["name"]?.GetValue<string>() ?? "Unnamed",
            Description = node["description"]?.GetValue<string>(),
            Color = node["color"]?.GetValue<string>(),
            DisplayName = node["displayName"]?.GetValue<string>()
        };

        var metaObj = node["metadata"]?.AsObject();
        if (metaObj != null)
        {
            foreach (var kvp in metaObj)
                coll.Metadata[kvp.Key] = kvp.Value?.GetValue<string>() ?? "";
        }

        var childrenArr = node["children"]?.AsArray();
        if (childrenArr != null)
        {
            foreach (var childNode in childrenArr)
            {
                var child = ParseChild(childNode);
                if (child != null)
                    coll.Children.Add(child);
            }
        }

        return coll;
    }

    private static FolderReference ParseFolderReference(JsonNode node)
    {
        var fr = new FolderReference
        {
            Id = Guid.TryParse(node["id"]?.GetValue<string>(), out var id) ? id : Guid.NewGuid(),
            ParentId = Guid.TryParse(node["parentId"]?.GetValue<string>(), out var pid) ? pid : Guid.Empty,
            SortOrder = node["sortOrder"]?.GetValue<int>() ?? 0,
            RealPath = node["realPath"]?.GetValue<string>() ?? "",
            DisplayName = node["displayName"]?.GetValue<string>(),
            Description = node["description"]?.GetValue<string>()
        };

        var metaObj = node["metadata"]?.AsObject();
        if (metaObj != null)
        {
            foreach (var kvp in metaObj)
                fr.Metadata[kvp.Key] = kvp.Value?.GetValue<string>() ?? "";
        }

        return fr;
    }

    private static WebResource ParseWebResource(JsonNode node)
    {
        var wr = new WebResource
        {
            Id = Guid.TryParse(node["id"]?.GetValue<string>(), out var id) ? id : Guid.NewGuid(),
            ParentId = Guid.TryParse(node["parentId"]?.GetValue<string>(), out var pid) ? pid : Guid.Empty,
            SortOrder = node["sortOrder"]?.GetValue<int>() ?? 0,
            Url = node["url"]?.GetValue<string>() ?? "",
            DisplayName = node["displayName"]?.GetValue<string>(),
            Description = node["description"]?.GetValue<string>()
        };

        var metaObj = node["metadata"]?.AsObject();
        if (metaObj != null)
        {
            foreach (var kvp in metaObj)
                wr.Metadata[kvp.Key] = kvp.Value?.GetValue<string>() ?? "";
        }

        return wr;
    }

    private static FileReference ParseFileReference(JsonNode node)
    {
        var fileRef = new FileReference
        {
            Id = Guid.TryParse(node["id"]?.GetValue<string>(), out var id) ? id : Guid.NewGuid(),
            ParentId = Guid.TryParse(node["parentId"]?.GetValue<string>(), out var pid) ? pid : Guid.Empty,
            SortOrder = node["sortOrder"]?.GetValue<int>() ?? 0,
            FilePath = node["filePath"]?.GetValue<string>() ?? "",
            DisplayName = node["displayName"]?.GetValue<string>(),
            Description = node["description"]?.GetValue<string>()
        };

        var metaObj = node["metadata"]?.AsObject();
        if (metaObj != null)
        {
            foreach (var kvp in metaObj)
                fileRef.Metadata[kvp.Key] = kvp.Value?.GetValue<string>() ?? "";
        }

        return fileRef;
    }
}
