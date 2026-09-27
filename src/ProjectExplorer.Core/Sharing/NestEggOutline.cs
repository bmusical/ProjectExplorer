namespace ProjectExplorer.Core.Sharing;

/// <summary>
/// The outline profile of a schema-1 Nest Egg: a project made only of empty
/// collections. Identified by <see cref="NestEggSource.AppVersion"/> equal to
/// <see cref="ProfileVersion"/>. A later egg that hangs resources on those
/// collections uses a different app version and is not checked here.
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

        CheckSiblings(childrenByParent, project.SourceId, parentRole: null);
        foreach (var node in nodes)
        {
            var role = node.Metadata[RoleMetadataKey];
            if (role == "beat" && childrenByParent.ContainsKey(node.SourceId))
                throw new NestEggFormatException("A beat cannot contain other collections.");

            if (node.ParentSourceId == project.SourceId)
            {
                if (role != "chapter")
                    throw new NestEggFormatException("The project's children are chapters.");
            }
            else if (!byId.TryGetValue(node.ParentSourceId, out var parent))
            {
                throw new NestEggFormatException("An item points at a parent that is not in the Nest Egg.");
            }
            else
            {
                var parentRole = parent.Metadata[RoleMetadataKey];
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

            CheckSiblings(childrenByParent, node.SourceId, parentRole: role);
        }
    }

    private static void ValidateCollection(NestEggNode node, HashSet<string> keys)
    {
        if (!string.Equals(node.ChildType, "collection", StringComparison.Ordinal))
            throw new NestEggFormatException("An outline Nest Egg can contain only collections.");
        if (!string.IsNullOrEmpty(node.RealPath) || !string.IsNullOrEmpty(node.Url) || !string.IsNullOrEmpty(node.FilePath))
            throw new NestEggFormatException("An outline collection cannot carry a path or URL.");
        if (node.OpenExternalOnly)
            throw new NestEggFormatException("An outline collection cannot be set to open in an external browser.");

        Require(node, ProfileMetadataKey, ProfileMetadataValue,
            "An outline collection needs metadata outline.profile = collections-only.");

        var role = Require(node, RoleMetadataKey, expected: null,
            "An outline collection needs a role of chapter, section, or beat.");
        if (role is not ("chapter" or "section" or "beat"))
            throw new NestEggFormatException("An outline collection needs a role of chapter, section, or beat.");

        var key = Require(node, KeyMetadataKey, expected: null, "An outline collection needs an outline.key.");
        if (!IsOutlineKey(key))
            throw new NestEggFormatException(
                $"The outline key '{key}' must be a lowercase slug, 1–64 characters, with single hyphens.");
        if (!keys.Add(key))
            throw new NestEggFormatException($"Two collections share the outline key '{key}'.");

        if (node.Metadata.Count != 3)
            throw new NestEggFormatException(
                "An outline collection's metadata is only outline.profile, outline.role, and outline.key.");
    }

    private static void CheckSiblings(
        Dictionary<Guid, List<NestEggNode>> childrenByParent,
        Guid parentId,
        string? parentRole)
    {
        if (!childrenByParent.TryGetValue(parentId, out var kids))
            return;

        var roles = kids.Select(k => k.Metadata[RoleMetadataKey]).Distinct(StringComparer.Ordinal).ToList();
        switch (parentRole)
        {
            case null when roles.Any(r => r != "chapter"):
                throw new NestEggFormatException("The project's children are chapters.");
            case "chapter" when roles.Count > 1:
                throw new NestEggFormatException("A chapter's children are all sections or all beats.");
            case "section" when roles.Any(r => r != "beat"):
                throw new NestEggFormatException("A section's children are beats.");
            case "beat":
                throw new NestEggFormatException("A beat cannot contain other collections.");
        }

        for (var i = 0; i < kids.Count; i++)
        {
            if (kids[i].SortOrder != i)
                throw new NestEggFormatException(
                    "Sibling sortOrder values must be 0, 1, 2, and so on, with no gaps or duplicates.");
        }
    }

    private static string Require(NestEggNode node, string key, string? expected, string message)
    {
        if (!node.Metadata.TryGetValue(key, out var value) || string.IsNullOrEmpty(value))
            throw new NestEggFormatException(message);
        if (expected != null && !string.Equals(value, expected, StringComparison.Ordinal))
            throw new NestEggFormatException(message);
        return value;
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
