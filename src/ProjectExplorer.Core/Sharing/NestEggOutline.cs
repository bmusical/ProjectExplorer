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
        for (var i = 0; i < nodes.Count; i++)
        {
            if (!string.Equals(nodes[i].ChildType, "collection", StringComparison.Ordinal))
                throw new NestEggFormatException(
                    $"{NestEggErrors.Item(nodes[i], i)} has childType \"{nodes[i].ChildType}\". This version of the file can contain only collections.");
        }

        var byId = new Dictionary<Guid, NestEggNode>(nodes.Count);
        var whereOf = new Dictionary<NestEggNode, string>(nodes.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var where = NestEggErrors.Item(node, i);
            whereOf[node] = where;
            byId.Add(node.SourceId, node);
            ValidateCollection(node, where, keys);
        }

        var childrenByParent = nodes
            .GroupBy(n => n.ParentSourceId)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.SortOrder).ToList());

        CheckSiblings(childrenByParent, project.SourceId, parentRole: null, underProject: true, whereOf);
        foreach (var node in nodes)
        {
            var role = RoleOf(node);
            if (role == "beat" && childrenByParent.ContainsKey(node.SourceId))
                throw new NestEggFormatException($"{whereOf[node]} is a beat, so it cannot contain other collections.");

            if (role != null)
                CheckRoleParent(node, role, project.SourceId, byId, whereOf);

            CheckSiblings(childrenByParent, node.SourceId, role, underProject: false, whereOf);
        }
    }

    private static void ValidateCollection(NestEggNode node, string where, HashSet<string> keys)
    {
        if (!string.IsNullOrEmpty(node.RealPath) || !string.IsNullOrEmpty(node.Url) || !string.IsNullOrEmpty(node.FilePath))
            throw new NestEggFormatException($"{where} cannot carry a path or URL. This version of the file is collections only.");
        if (node.OpenExternalOnly)
            throw new NestEggFormatException($"{where} cannot be set to open in an external browser.");

        foreach (var metaKey in node.Metadata.Keys)
        {
            if (metaKey.StartsWith(SharedImportMetadata.Prefix, StringComparison.Ordinal))
                throw new NestEggFormatException($"{where} cannot carry metadata key \"{metaKey}\". Keys that start with shared. are added on import.");
        }

        if (node.Metadata.TryGetValue(ProfileMetadataKey, out var profile)
            && !string.IsNullOrEmpty(profile)
            && !string.Equals(profile, ProfileMetadataValue, StringComparison.Ordinal))
            throw new NestEggFormatException($"{where} has outline.profile \"{profile}\". It needs to be \"{ProfileMetadataValue}\", or left out.");

        var role = RoleOf(node);
        if (role != null && role is not ("chapter" or "section" or "beat"))
            throw new NestEggFormatException($"{where} has outline.role \"{role}\". Use chapter, section, or beat, or leave it out.");

        if (node.Metadata.TryGetValue(KeyMetadataKey, out var key) && !string.IsNullOrEmpty(key))
        {
            if (!IsOutlineKey(key))
                throw new NestEggFormatException(
                    $"{where} has outline.key \"{key}\". Use a lowercase slug, 1–64 characters, with single hyphens.");
            if (!keys.Add(key))
                throw new NestEggFormatException($"{where} reuses outline.key \"{key}\". Each key has to be unique in the file.");
        }
    }

    private static void CheckRoleParent(
        NestEggNode node,
        string role,
        Guid projectId,
        Dictionary<Guid, NestEggNode> byId,
        Dictionary<NestEggNode, string> whereOf)
    {
        var where = whereOf[node];
        if (node.ParentSourceId == projectId)
        {
            if (role != "chapter")
                throw new NestEggFormatException($"{where} is directly under the project, so its outline.role needs to be chapter (it is \"{role}\").");
            return;
        }

        if (!byId.TryGetValue(node.ParentSourceId, out var parent))
            throw new NestEggFormatException($"{where} points at a parent that is not in the file.");

        var parentRole = RoleOf(parent);
        switch (role)
        {
            case "chapter":
                throw new NestEggFormatException($"{where} is a chapter, so its parent needs to be the project, not {whereOf[parent]}.");
            case "section" when parentRole != "chapter":
                throw new NestEggFormatException($"{where} is a section, so its parent {whereOf[parent]} needs outline.role chapter.");
            case "beat" when parentRole is not ("chapter" or "section"):
                throw new NestEggFormatException($"{where} is a beat, so its parent {whereOf[parent]} needs outline.role chapter or section.");
        }
    }

    private static void CheckSiblings(
        Dictionary<Guid, List<NestEggNode>> childrenByParent,
        Guid parentId,
        string? parentRole,
        bool underProject,
        Dictionary<NestEggNode, string> whereOf)
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
        {
            var offender = kids.First(k => RoleOf(k) is string role && role != "chapter");
            throw new NestEggFormatException(
                $"{whereOf[offender]} is directly under the project, so its outline.role needs to be chapter (it is \"{RoleOf(offender)}\").");
        }

        switch (parentRole)
        {
            case "chapter" when specified.Count > 1:
                throw new NestEggFormatException(
                    $"{whereOf[kids[0]]} and its siblings mix roles ({string.Join(" and ", specified)}). A chapter's children are all sections or all beats.");
            case "chapter" when specified.Any(r => r is not ("section" or "beat")):
                throw new NestEggFormatException($"{whereOf[kids[0]]} has a child whose outline.role is not section or beat.");
            case "section" when specified.Any(r => r != "beat"):
                throw new NestEggFormatException($"{whereOf[kids[0]]} is a section, so its children need outline.role beat.");
            case "beat" when kids.Count > 0:
                throw new NestEggFormatException($"{whereOf[kids[0]]} is inside a beat. A beat cannot contain other collections.");
        }

        for (var i = 0; i < kids.Count; i++)
        {
            if (kids[i].SortOrder != i)
                throw new NestEggFormatException(
                    $"{whereOf[kids[i]]} has sortOrder {kids[i].SortOrder}. Among its siblings it needs to be {i} (0, 1, 2, … with no gaps).");
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
