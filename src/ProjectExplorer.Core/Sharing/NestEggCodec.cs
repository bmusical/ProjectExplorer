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
            throw new NestEggFormatException("The Nest Egg is empty.");
        if (Encoding.UTF8.GetByteCount(json) > NestEggLimits.MaxJsonBytes)
            throw new NestEggFormatException($"The Nest Egg is larger than {NestEggLimits.MaxJsonBytes:N0} bytes.");

        NestEggDocument? egg;
        try
        {
            egg = JsonSerializer.Deserialize<NestEggDocument>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new NestEggFormatException("The Nest Egg is not valid JSON: " + ex.Message);
        }

        if (egg == null)
            throw new NestEggFormatException("The Nest Egg is empty.");
        Validate(egg);
        return egg;
    }

    public static void Validate(NestEggDocument egg)
    {
        if (egg.SchemaVersion != NestEggLimits.SchemaVersion)
            throw new NestEggFormatException($"This sharing server accepts Nest Egg schema {NestEggLimits.SchemaVersion}.");
        if (!string.Equals(egg.Kind, NestEggLimits.Kind, StringComparison.Ordinal))
            throw new NestEggFormatException("The document is not a Nest Egg.");
        if (egg.Source == null || string.IsNullOrWhiteSpace(egg.Source.MachineLabel))
            throw new NestEggFormatException("A Nest Egg needs the name of the computer that sent it.");
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
        RequireText(project.Description, "description");
        RequireText(project.Color, "color");
        RequireText(project.IconKey, "icon");

        var nodes = project.Nodes ?? throw new NestEggFormatException("The Nest Egg is missing its node list.");
        if (nodes.Count > NestEggLimits.MaxNodes)
            throw new NestEggFormatException($"A Nest Egg can hold at most {NestEggLimits.MaxNodes:N0} items.");

        var byId = new Dictionary<Guid, NestEggNode>();
        foreach (var node in nodes)
        {
            if (node.SourceId == Guid.Empty)
                throw new NestEggFormatException("An item in the Nest Egg is missing its id.");
            if (node.SourceId == project.SourceId)
                throw new NestEggFormatException("An item reused the project's id.");
            if (!byId.TryAdd(node.SourceId, node))
                throw new NestEggFormatException("Two items in the Nest Egg share the same id.");
            if (node.ParentSourceId == Guid.Empty)
                throw new NestEggFormatException("An item is missing its parent.");
            if (node.ParentSourceId == node.SourceId)
                throw new NestEggFormatException("An item is listed as its own parent.");
            ValidateNodeShape(node);
        }

        foreach (var node in nodes)
        {
            var seen = new HashSet<Guid> { node.SourceId };
            var parentId = node.ParentSourceId;
            while (parentId != project.SourceId)
            {
                if (!byId.TryGetValue(parentId, out var parent))
                    throw new NestEggFormatException("An item points at a parent that is not in the Nest Egg.");
                if (!string.Equals(parent.ChildType, "collection", StringComparison.Ordinal))
                    throw new NestEggFormatException("Only a collection can contain other items.");
                if (!seen.Add(parentId))
                    throw new NestEggFormatException("The Nest Egg contains a circular collection.");
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

    private static void ValidateNodeShape(NestEggNode node)
    {
        RequireText(node.DisplayName, "display name");
        RequireText(node.Description, "description");
        RequireText(node.Name, "name");
        RequireText(node.Color, "color");
        RequirePath(node.RealPath, "folder path");
        RequirePath(node.FilePath, "file path");
        RequirePath(node.Url, "URL");

        switch (node.ChildType)
        {
            case "collection":
                node.Name = node.Name?.Trim() ?? "";
                if (node.Name.Length == 0)
                    throw new NestEggFormatException("A collection needs a name.");
                if (node.Name.Length > NestEggLimits.MaxNameLength)
                    throw new NestEggFormatException($"A collection name must be {NestEggLimits.MaxNameLength} characters or fewer.");
                break;
            case "folderReference":
            case "fileReference":
            case "webResource":
                break;
            default:
                throw new NestEggFormatException($"Unknown item type '{node.ChildType}'.");
        }

        if (node.Metadata.Count > NestEggLimits.MaxMetadataEntries)
            throw new NestEggFormatException("An item has too many metadata entries.");
        foreach (var (key, value) in node.Metadata)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > NestEggLimits.MaxMetadataKeyLength)
                throw new NestEggFormatException("A metadata key is missing or too long.");
            if (value != null && value.Length > NestEggLimits.MaxMetadataValueLength)
                throw new NestEggFormatException("A metadata value is too long.");
        }
    }

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
            throw new NestEggFormatException($"A {label} is longer than {NestEggLimits.MaxTextLength:N0} characters.");
    }

    private static void RequirePath(string? value, string label)
    {
        if (value != null && value.Length > NestEggLimits.MaxPathLength)
            throw new NestEggFormatException($"A {label} is longer than {NestEggLimits.MaxPathLength:N0} characters.");
    }

    private static string? TrimToNull(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
