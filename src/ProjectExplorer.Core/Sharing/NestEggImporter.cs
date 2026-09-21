using ProjectExplorer.Core.Models;
using ProjectExplorer.Core.Services;

namespace ProjectExplorer.Core.Sharing;

/// <summary>
/// Turns a Nest Egg into a brand-new local project. Ids are reminted so a receive
/// never overwrites a project the user already has. Paths and URLs are copied
/// exactly as the sender stored them — a folder that exists only on the other
/// computer stays in the tree and shows up unavailable here.
/// </summary>
public static class NestEggImporter
{
    public static Project Materialize(NestEggDocument egg, string shareCode, IEnumerable<string> existingProjectNames)
    {
        NestEggCodec.Validate(egg);
        var source = egg.Project;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = UniqueName(source.Name, existingProjectNames),
            Description = source.Description,
            Color = source.Color,
            IconKey = source.IconKey,
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow
        };
        project.Metadata[SharedImportMetadata.SourceProjectId] = source.SourceId.ToString();
        project.Metadata[SharedImportMetadata.ShareCode] = shareCode;
        project.Metadata[SharedImportMetadata.SenderLabel] = egg.Source.MachineLabel;
        project.Metadata[SharedImportMetadata.ImportedUtc] = project.Created.ToString("O");

        var idMap = new Dictionary<Guid, Guid> { [source.SourceId] = project.Id };
        var created = new List<(NestEggNode Node, ProjectChild Child)>();
        foreach (var node in source.Nodes.OrderBy(n => n.SortOrder))
        {
            var child = CreateChild(node);
            child.Id = Guid.NewGuid();
            child.SortOrder = node.SortOrder;
            child.DisplayName = node.DisplayName;
            foreach (var (key, value) in node.Metadata)
                child.Metadata[key] = value;
            child.Metadata[SharedImportMetadata.SourceNodeId] = node.SourceId.ToString();
            idMap[node.SourceId] = child.Id;
            created.Add((node, child));
        }

        var byId = created.ToDictionary(pair => pair.Child.Id, pair => pair.Child);
        foreach (var (node, child) in created)
        {
            child.ParentId = idMap[node.ParentSourceId];
            if (node.ParentSourceId == source.SourceId)
                project.Children.Add(child);
            else if (byId[idMap[node.ParentSourceId]] is Collection parent)
                parent.Children.Add(child);
            else
                throw new NestEggFormatException("Only a collection can contain other items.");
        }

        if (project.HasCircularReferences())
            throw new NestEggFormatException("The Nest Egg contains a circular collection.");

        return project;
    }

    /// <summary>
    /// Returns a user-facing reason when importing <paramref name="incoming"/> would
    /// break the free tier. A licensed install returns null.
    /// </summary>
    public static string? FreeTierBlockReason(LicenseInfo license, Project incoming)
    {
        if (license.State == LicenseState.Licensed)
            return null;
        if (license.State == LicenseState.Invalid)
            return "The license on this computer isn't valid. Register again before importing a shared project.";

        var projectsAfter = license.ProjectCount + 1;
        var leavesAfter = license.LeafNodeCount + LicenseManager.CountLeafNodes([incoming]);
        if (projectsAfter > license.ProjectLimit)
            return $"Importing this project would pass the free limit of {license.ProjectLimit} projects.";
        if (leavesAfter > license.LeafNodeLimit)
            return $"Importing this project would pass the free limit of {license.LeafNodeLimit} folder, file, and web references.";
        return null;
    }

    public static string UniqueName(string desired, IEnumerable<string> existing)
    {
        var taken = new HashSet<string>(existing.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(desired)) return desired;
        var shared = desired + " (shared)";
        if (!taken.Contains(shared)) return shared;
        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{desired} (shared {n})";
            if (!taken.Contains(candidate)) return candidate;
        }
        throw new InvalidOperationException("Couldn't find a free name for the imported project.");
    }

    private static ProjectChild CreateChild(NestEggNode node) => node.ChildType switch
    {
        "collection" => new Collection
        {
            Name = node.Name ?? "Unnamed",
            Description = node.Description,
            Color = node.Color
        },
        "folderReference" => new FolderReference
        {
            RealPath = node.RealPath ?? "",
            Description = node.Description
        },
        "webResource" => new WebResource
        {
            Url = node.Url ?? "",
            Description = node.Description,
            OpenExternalOnly = node.OpenExternalOnly
        },
        "fileReference" => new FileReference
        {
            FilePath = node.FilePath ?? "",
            Description = node.Description
        },
        _ => throw new NestEggFormatException($"Unknown item type '{node.ChildType}'.")
    };
}
