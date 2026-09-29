using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectExplorer.Core.Models;

namespace ProjectExplorer.Core.Sharing;

/// <summary>
/// Builds, checks, and reads a Nest Egg. The document is an explicit DTO rather
/// than a polymorphic serialization of <see cref="Project"/>, so a future server
/// can validate it without knowing the WinForms app.
/// </summary>
public static class NestEggCodec
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static NestEggDocument Create(Project project, string machineLabel, string? appVersion)
    {
        var label = RequireMachineLabel(machineLabel);
        var egg = new NestEggDocument
        {
            SchemaVersion = NestEggLimits.SchemaVersion,
            Kind = NestEggLimits.Kind,
            CreatedUtc = DateTime.UtcNow,
            Source = new NestEggSource
            {
                MachineLabel = label,
                AppVersion = TrimToNull(appVersion, NestEggLimits.MaxAppVersionLength)
            },
            Project = new NestEggProject
            {
                SourceId = project.Id,
                Name = project.Name?.Trim() ?? "",
                Description = project.Description,
                Color = project.Color,
                IconKey = project.IconKey,
                CreatedUtc = project.Created.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(project.Created, DateTimeKind.Utc)
                    : project.Created.ToUniversalTime(),
                ModifiedUtc = project.Modified.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(project.Modified, DateTimeKind.Utc)
                    : project.Modified.ToUniversalTime(),
                Nodes = Flatten(project)
            }
        };
        Validate(egg);
        return egg;
    }

    public static string ToJson(NestEggDocument egg) =>
        JsonSerializer.Serialize(egg, JsonOptions);

    public static string Sha256Hex(string json)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static NestEggDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new NestEggFormatException("The file is empty.");
        var bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes > NestEggLimits.MaxJsonBytes)
            throw new NestEggFormatException(NestEggErrors.TooLarge(bytes));

        NestEggDocument? egg;
        try
        {
            egg = JsonSerializer.Deserialize<NestEggDocument>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new NestEggFormatException(NestEggErrors.DescribeJsonException(ex));
        }

        if (egg == null)
            throw new NestEggFormatException("The Nest Egg is empty.");
        Validate(egg);
        return egg;
    }

    public static void Validate(NestEggDocument egg)
    {
        if (egg.SchemaVersion != NestEggLimits.SchemaVersion)
            throw new NestEggFormatException($"This file says schema {egg.SchemaVersion}. Only schema {NestEggLimits.SchemaVersion} is accepted.");
        if (!string.Equals(egg.Kind, NestEggLimits.Kind, StringComparison.Ordinal))
            throw new NestEggFormatException($"This file's kind is \"{egg.Kind}\". It needs to be \"{NestEggLimits.Kind}\".");
        if (egg.Source == null || string.IsNullOrWhiteSpace(egg.Source.MachineLabel))
            throw new NestEggFormatException("The file needs source.machineLabel, a short name for who wrote it (for example \"outliner\").");
        if (egg.Source.MachineLabel.Trim().Length > NestEggLimits.MaxMachineLabelLength)
            throw new NestEggFormatException($"The computer name must be {NestEggLimits.MaxMachineLabelLength} characters or fewer.");
        if (egg.Source.AppVersion != null && egg.Source.AppVersion.Length > NestEggLimits.MaxAppVersionLength)
            throw new NestEggFormatException("The app version string is too long.");

        var project = egg.Project ?? throw new NestEggFormatException("The Nest Egg has no project.");
        if (project.SourceId == Guid.Empty)
            throw new NestEggFormatException("The project is missing its id.");
        project.Name = project.Name?.Trim() ?? "";
        if (project.Name.Length == 0)
            throw new NestEggFormatException("The project needs a name.");
        if (project.Name.Length > NestEggLimits.MaxNameLength)
            throw new NestEggFormatException($"The project name must be {NestEggLimits.MaxNameLength} characters or fewer.");
        RequireText(project.Description, "The project description");
        RequireText(project.Color, "The project color");
        RequireText(project.IconKey, "The project icon");

        var nodes = project.Nodes ?? throw new NestEggFormatException("The Nest Egg is missing its node list.");
        if (nodes.Count > NestEggLimits.MaxNodes)
            throw new NestEggFormatException($"A Nest Egg can hold at most {NestEggLimits.MaxNodes:N0} items.");

        var byId = new Dictionary<Guid, NestEggNode>();
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node == null)
                throw new NestEggFormatException($"nodes[{i}] is empty. Each entry needs to be an object.");
            var where = NestEggErrors.Item(node, i);
            if (node.SourceId == Guid.Empty)
                throw new NestEggFormatException($"{where} is missing sourceId.");
            if (node.SourceId == project.SourceId)
                throw new NestEggFormatException($"{where} reused the project's id.");
            if (!byId.TryAdd(node.SourceId, node))
                throw new NestEggFormatException($"{where} uses an id that another item already uses ({node.SourceId}).");
            if (node.ParentSourceId == Guid.Empty)
                throw new NestEggFormatException($"{where} is missing parentSourceId.");
            if (node.ParentSourceId == node.SourceId)
                throw new NestEggFormatException($"{where} is listed as its own parent.");
            ValidateNodeShape(node, where);
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var where = NestEggErrors.Item(node, i);
            var seen = new HashSet<Guid> { node.SourceId };
            var parentId = node.ParentSourceId;
            while (parentId != project.SourceId)
            {
                if (!byId.TryGetValue(parentId, out var parent))
                    throw new NestEggFormatException($"{where} points at a parent that is not in the file ({parentId}).");
                if (!string.Equals(parent.ChildType, "collection", StringComparison.Ordinal))
                    throw new NestEggFormatException($"{where} is inside \"{parent.ChildType}\", which cannot contain other items. Only a collection can.");
                if (!seen.Add(parentId))
                    throw new NestEggFormatException($"{where} is part of a circular collection.");
                parentId = parent.ParentSourceId;
            }
        }
    }

    public static NestEggSummary Summarize(NestEggDocument egg)
    {
        Validate(egg);
        var nodes = egg.Project.Nodes;
        var folders = nodes.Where(n => n.ChildType == "folderReference").Select(n => n.RealPath ?? "").ToList();
        var files = nodes.Where(n => n.ChildType == "fileReference").Select(n => n.FilePath ?? "").ToList();
        var urls = nodes.Where(n => n.ChildType == "webResource").Select(n => n.Url ?? "").ToList();
        var truncated = folders.Count > NestEggLimits.PreviewListLimit
            || files.Count > NestEggLimits.PreviewListLimit
            || urls.Count > NestEggLimits.PreviewListLimit;

        return new NestEggSummary
        {
            ProjectName = egg.Project.Name,
            SourceProjectId = egg.Project.SourceId,
            CollectionCount = nodes.Count(n => n.ChildType == "collection"),
            FolderCount = folders.Count,
            FileCount = files.Count,
            WebCount = urls.Count,
            FolderPaths = folders.Take(NestEggLimits.PreviewListLimit).ToList(),
            FilePaths = files.Take(NestEggLimits.PreviewListLimit).ToList(),
            Urls = urls.Take(NestEggLimits.PreviewListLimit).ToList(),
            PathsTruncated = truncated
        };
    }

    private static List<NestEggNode> Flatten(Project project)
    {
        var nodes = new List<NestEggNode>();
        Walk(project.Id, project.Children, nodes);
        return nodes;
    }

    private static void Walk(Guid parentId, List<ProjectChild> children, List<NestEggNode> nodes)
    {
        foreach (var child in children.OrderBy(c => c.SortOrder))
        {
            var node = new NestEggNode
            {
                SourceId = child.Id,
                ParentSourceId = parentId,
                ChildType = ChildTypeKey(child.Type),
                SortOrder = child.SortOrder,
                DisplayName = child.DisplayName,
                Metadata = CopyUserMetadata(child.Metadata)
            };

            switch (child)
            {
                case Collection collection:
                    node.Name = collection.Name;
                    node.Description = collection.Description;
                    node.Color = collection.Color;
                    break;
                case FolderReference folder:
                    node.RealPath = folder.RealPath;
                    node.Description = folder.Description;
                    break;
                case WebResource web:
                    node.Url = web.Url;
                    node.Description = web.Description;
                    node.OpenExternalOnly = web.OpenExternalOnly;
                    break;
                case FileReference file:
                    node.FilePath = file.FilePath;
                    node.Description = file.Description;
                    break;
            }

            nodes.Add(node);
            if (child is Collection nested)
                Walk(nested.Id, nested.Children, nodes);
        }
    }

    private static Dictionary<string, string> CopyUserMetadata(Dictionary<string, string> metadata) =>
        metadata
            .Where(kvp => !kvp.Key.StartsWith(SharedImportMetadata.Prefix, StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    internal static string ChildTypeKey(ChildType type) => type switch
    {
        ChildType.Collection => "collection",
        ChildType.FolderReference => "folderReference",
        ChildType.WebResource => "webResource",
        ChildType.FileReference => "fileReference",
        _ => throw new NestEggFormatException($"Unknown child type '{type}'.")
    };

    private static void ValidateNodeShape(NestEggNode node, string where)
    {
        RequireText(node.DisplayName, $"{where} display name");
        RequireText(node.Description, $"{where} description");
        RequireText(node.Name, $"{where} name");
        RequireText(node.Color, $"{where} color");
        RequirePath(node.RealPath, $"{where} folder path");
        RequirePath(node.FilePath, $"{where} file path");
        RequirePath(node.Url, $"{where} URL");

        switch (node.ChildType)
        {
            case "collection":
                node.Name = node.Name?.Trim() ?? "";
                if (node.Name.Length == 0)
                    throw new NestEggFormatException($"{where} needs a name.");
                if (node.Name.Length > NestEggLimits.MaxNameLength)
                    throw new NestEggFormatException($"{where} name must be {NestEggLimits.MaxNameLength} characters or fewer.");
                break;
            case "folderReference":
            case "fileReference":
            case "webResource":
                break;
            case null:
            case "":
                throw new NestEggFormatException($"{where} is missing childType. Use \"collection\".");
            default:
                throw new NestEggFormatException($"{where} has childType \"{node.ChildType}\". Use \"collection\".");
        }

        if (node.Metadata == null)
            throw new NestEggFormatException($"{where} metadata must be a JSON object, or left out.");
        if (node.Metadata.Count > NestEggLimits.MaxMetadataEntries)
            throw new NestEggFormatException($"{where} has {node.Metadata.Count} metadata entries. The limit is {NestEggLimits.MaxMetadataEntries}.");
        foreach (var (key, value) in node.Metadata)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > NestEggLimits.MaxMetadataKeyLength)
                throw new NestEggFormatException($"{where} has a metadata key that is missing or longer than {NestEggLimits.MaxMetadataKeyLength} characters.");
            if (value != null && value.Length > NestEggLimits.MaxMetadataValueLength)
                throw new NestEggFormatException($"{where} metadata \"{Truncate(key, 40)}\" is longer than {NestEggLimits.MaxMetadataValueLength:N0} characters.");
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    public static string RequireMachineLabel(string? machineLabel)
    {
        var label = machineLabel?.Trim() ?? "";
        if (label.Length == 0)
            throw new NestEggFormatException("Enter a name for this computer so the other one can see who sent the project.");
        if (label.Length > NestEggLimits.MaxMachineLabelLength)
            throw new NestEggFormatException($"The computer name must be {NestEggLimits.MaxMachineLabelLength} characters or fewer.");
        return label;
    }

    private static void RequireText(string? value, string label)
    {
        if (value != null && value.Length > NestEggLimits.MaxTextLength)
            throw new NestEggFormatException($"{label} is longer than {NestEggLimits.MaxTextLength:N0} characters.");
    }

    private static void RequirePath(string? value, string label)
    {
        if (value != null && value.Length > NestEggLimits.MaxPathLength)
            throw new NestEggFormatException($"{label} is longer than {NestEggLimits.MaxPathLength:N0} characters.");
    }

    private static string? TrimToNull(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
