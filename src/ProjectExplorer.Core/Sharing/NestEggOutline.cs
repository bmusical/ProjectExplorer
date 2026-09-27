namespace ProjectExplorer.Core.Sharing;

/// <summary>
/// Nest Import's collections-only profile. Identified by
/// <see cref="NestEggSource.AppVersion"/> equal to <see cref="ProfileVersion"/>.
/// A later egg that hangs resources on those collections uses a different app
/// version and is not checked here.
/// </summary>
public static class NestEggOutline
{
    public const string ProfileVersion = "outline-1";
    public const string ProfileMetadataKey = "outline.profile";
    public const string ProfileMetadataValue = "collections-only";
    public const string RoleMetadataKey = "outline.role";
    public const string KeyMetadataKey = "outline.key";

    public static bool IsOutline(NestEggDocument egg) =>
        string.Equals(egg.Source?.AppVersion, ProfileVersion, StringComparison.Ordinal);

    public static void Validate(NestEggDocument egg)
    {
        NestEggCodec.Validate(egg);
        if (!IsOutline(egg))
            throw new NestEggFormatException("This file is not an outline Nest Egg.");

        var project = egg.Project;
        var nodes = project.Nodes;
        if (nodes.Any(n => !string.Equals(n.ChildType, "collection", StringComparison.Ordinal)))
            throw new NestEggFormatException("An outline Nest Egg can contain only collections.");

        var byId = new Dictionary<Guid, NestEggNode>(nodes.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            byId.Add(node.SourceId, node);
            ValidateCollection(node, keys);
        }

        var childrenByParent = nodes
            .GroupBy(n => n.ParentSourceId)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.SortOrder).ToList());

        CheckSiblings(childrenByParent, project.SourceId, parentRole: null, underProject: true);
        foreach (var node in nodes)
        {
            var role = RoleOf(node);
            if (role == "beat" && childrenByParent.ContainsKey(node.SourceId))
                throw new NestEggFormatException("A beat cannot contain other collections.");

            if (role != null)
                CheckRoleParent(node, role, project.SourceId, byId);

            CheckSiblings(childrenByParent, node.SourceId, role, underProject: false);
        }
    }

    private static void ValidateCollection(NestEggNode node, HashSet<string> keys)
    {
        if (!string.IsNullOrEmpty(node.RealPath) || !string.IsNullOrEmpty(node.Url) || !string.IsNullOrEmpty(node.FilePath))
            throw new NestEggFormatException("An outline collection cannot carry a path or URL.");
        if (node.OpenExternalOnly)
            throw new NestEggFormatException("An outline collection cannot be set to open in an external browser.");

        foreach (var metaKey in node.Metadata.Keys)
        {
            if (metaKey.StartsWith(SharedImportMetadata.Prefix, StringComparison.Ordinal))
                throw new NestEggFormatException("An outline collection cannot carry shared. metadata.");
        }

        if (node.Metadata.TryGetValue(ProfileMetadataKey, out var profile)
            && !string.IsNullOrEmpty(profile)
            && !string.Equals(profile, ProfileMetadataValue, StringComparison.Ordinal))
            throw new NestEggFormatException("outline.profile must be collections-only.");

        var role = RoleOf(node);
        if (role != null && role is not ("chapter" or "section" or "beat"))
            throw new NestEggFormatException("An outline.role is chapter, section, or beat.");

        if (node.Metadata.TryGetValue(KeyMetadataKey, out var key) && !string.IsNullOrEmpty(key))
        {
            if (!IsOutlineKey(key))
                throw new NestEggFormatException(
                    $"The outline key '{key}' must be a lowercase slug, 1–64 characters, with single hyphens.");
            if (!keys.Add(key))
                throw new NestEggFormatException($"Two collections share the outline key '{key}'.");
        }
    }

    private static void CheckRoleParent(
        NestEggNode node,
        string role,
        Guid projectId,
        Dictionary<Guid, NestEggNode> byId)
    {
        if (node.ParentSourceId == projectId)
        {
            if (role != "chapter")
                throw new NestEggFormatException(
                    "A collection directly under the project, when it has an outline.role, is a chapter.");
            return;
        }

        if (!byId.TryGetValue(node.ParentSourceId, out var parent))
            throw new NestEggFormatException("An item points at a parent that is not in the Nest Egg.");

        var parentRole = RoleOf(parent);
        switch (role)
        {
            case "chapter":
                throw new NestEggFormatException("A chapter's parent is the project.");
            case "section" when parentRole != "chapter":
                throw new NestEggFormatException("A section's parent is a chapter.");
            case "beat" when parentRole is not ("chapter" or "section"):
                throw new NestEggFormatException("A beat's parent is a chapter or a section.");
        }
    }

    private static void CheckSiblings(
        Dictionary<Guid, List<NestEggNode>> childrenByParent,
        Guid parentId,
        string? parentRole,
        bool underProject)
    {
        if (!childrenByParent.TryGetValue(parentId, out var kids))
            return;

        var specified = kids
            .Select(RoleOf)
            .Where(r => r != null)
            .Select(r => r!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (underProject && specified.Any(r => r != "chapter"))
            throw new NestEggFormatException(
                "A collection directly under the project, when it has an outline.role, is a chapter.");

        switch (parentRole)
        {
            case "chapter" when specified.Count > 1:
                throw new NestEggFormatException("A chapter's children are all sections or all beats.");
            case "chapter" when specified.Any(r => r is not ("section" or "beat")):
                throw new NestEggFormatException("A chapter's children are sections or beats.");
            case "section" when specified.Any(r => r != "beat"):
                throw new NestEggFormatException("A section's children are beats.");
            case "beat" when kids.Count > 0:
                throw new NestEggFormatException("A beat cannot contain other collections.");
        }

        for (var i = 0; i < kids.Count; i++)
        {
            if (kids[i].SortOrder != i)
                throw new NestEggFormatException(
                    "Sibling sortOrder values must be 0, 1, 2, and so on, with no gaps or duplicates.");
        }
    }

    private static string? RoleOf(NestEggNode node)
    {
        if (!node.Metadata.TryGetValue(RoleMetadataKey, out var role) || string.IsNullOrEmpty(role))
            return null;
        return role;
    }

    private static bool IsOutlineKey(string key)
    {
        if (key.Length is < 1 or > 64)
            return false;
        if (key[0] == '-' || key[^1] == '-')
            return false;

        var hyphen = false;
        foreach (var c in key)
        {
            if (c == '-')
            {
                if (hyphen)
                    return false;
                hyphen = true;
                continue;
            }

            hyphen = false;
            var ok = c is >= 'a' and <= 'z' || c is >= '0' and <= '9';
            if (!ok)
                return false;
        }

        return true;
    }
}
