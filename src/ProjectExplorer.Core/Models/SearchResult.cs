namespace ProjectExplorer.Core.Models;

/// <summary>
/// One matching Project or ProjectChild found by SearchService.Search, carrying just enough for
/// a caller to jump to it (ProjectId + ChildId, with ChildId null when the Project itself
/// matched) without needing to re-walk the tree.
/// </summary>
public class SearchResult
{
    public required Guid ProjectId { get; init; }
    public required string ProjectName { get; init; }

    /// <summary>Null when the match is the Project itself rather than one of its children.</summary>
    public Guid? ChildId { get; init; }

    /// <summary>Null when the match is the Project itself.</summary>
    public ChildType? ChildType { get; init; }

    public required string DisplayText { get; init; }

    /// <summary>Which field matched first, e.g. "Name", "Description", "Path", "URL", "Metadata: tag".</summary>
    public required string MatchedField { get; init; }

    public required string MatchedSnippet { get; init; }
}
