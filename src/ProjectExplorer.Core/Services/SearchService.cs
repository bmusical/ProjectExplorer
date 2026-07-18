using ProjectExplorer.Core.Models;

namespace ProjectExplorer.Core.Services;

/// <summary>
/// In-memory cross-project search. ProjectManager already loads the entire project tree into
/// memory at startup (see the Data Flow note in CLAUDE.md), so this is a plain recursive scan
/// rather than a new storage dependency or index — it works the same way regardless of which
/// IProjectRepository is behind ProjectManager.
///
/// Matches Name/Description on every node type, plus RealPath (FolderReference), Url
/// (WebResource), FilePath (FileReference), and every Metadata value — case-insensitive
/// substring match. Each matching node produces at most one SearchResult, recording whichever
/// field matched first (Name, then Description, then Path/URL, then Metadata) rather than one
/// row per matched field, so an item that matches on two fields doesn't show up twice.
/// </summary>
public static class SearchService
{
    public static List<SearchResult> Search(IEnumerable<Project> projects, string query)
    {
        var results = new List<SearchResult>();
        if (string.IsNullOrWhiteSpace(query)) return results;

        foreach (var project in projects)
        {
            var match = FindFirstMatch(query, [("Name", project.Name), ("Description", project.Description)]);
            if (match != null)
            {
                results.Add(new SearchResult
                {
                    ProjectId = project.Id,
                    ProjectName = project.Name,
                    ChildId = null,
                    ChildType = null,
                    DisplayText = project.Name,
                    MatchedField = match.Value.Field,
                    MatchedSnippet = match.Value.Value
                });
            }

            SearchChildren(results, project.Id, project.Name, project.Children, query);
        }

        return results;
    }

    private static void SearchChildren(List<SearchResult> results, Guid projectId, string projectName,
        List<ProjectChild> children, string query)
    {
        foreach (var child in children)
        {
            switch (child)
            {
                case Collection coll:
                {
                    var fields = new List<(string Field, string? Value)> { ("Name", coll.Name), ("Description", coll.Description) };
                    fields.AddRange(MetadataFields(coll.Metadata));
                    AddIfMatch(results, projectId, projectName, coll.Id, ChildType.Collection, coll.Name, query, fields);
                    SearchChildren(results, projectId, projectName, coll.Children, query);
                    break;
                }
                case FolderReference fr:
                {
                    var fields = new List<(string Field, string? Value)>
                    {
                        ("Name", fr.EffectiveName), ("Description", fr.Description), ("Path", fr.RealPath)
                    };
                    fields.AddRange(MetadataFields(fr.Metadata));
                    AddIfMatch(results, projectId, projectName, fr.Id, ChildType.FolderReference, fr.EffectiveName, query, fields);
                    break;
                }
                case WebResource wr:
                {
                    var fields = new List<(string Field, string? Value)>
                    {
                        ("Name", wr.EffectiveName), ("Description", wr.Description), ("URL", wr.Url)
                    };
                    fields.AddRange(MetadataFields(wr.Metadata));
                    AddIfMatch(results, projectId, projectName, wr.Id, ChildType.WebResource, wr.EffectiveName, query, fields);
                    break;
                }
                case FileReference fileRef:
                {
                    var fields = new List<(string Field, string? Value)>
                    {
                        ("Name", fileRef.EffectiveName), ("Description", fileRef.Description), ("Path", fileRef.FilePath)
                    };
                    fields.AddRange(MetadataFields(fileRef.Metadata));
                    AddIfMatch(results, projectId, projectName, fileRef.Id, ChildType.FileReference, fileRef.EffectiveName, query, fields);
                    break;
                }
            }
        }
    }

    private static void AddIfMatch(List<SearchResult> results, Guid projectId, string projectName,
        Guid childId, ChildType childType, string displayText, string query, IEnumerable<(string Field, string? Value)> fields)
    {
        var match = FindFirstMatch(query, fields);
        if (match == null) return;

        results.Add(new SearchResult
        {
            ProjectId = projectId,
            ProjectName = projectName,
            ChildId = childId,
            ChildType = childType,
            DisplayText = displayText,
            MatchedField = match.Value.Field,
            MatchedSnippet = match.Value.Value
        });
    }

    private static (string Field, string Value)? FindFirstMatch(string query, IEnumerable<(string Field, string? Value)> fields)
    {
        foreach (var (field, value) in fields)
        {
            if (!string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase))
                return (field, value);
        }
        return null;
    }

    private static IEnumerable<(string Field, string? Value)> MetadataFields(Dictionary<string, string> metadata) =>
        metadata.Select(kvp => ($"Metadata: {kvp.Key}", (string?)kvp.Value));
}
