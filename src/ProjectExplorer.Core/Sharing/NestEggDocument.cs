namespace ProjectExplorer.Core.Sharing;

/// <summary>
/// A Nest Egg is one project packed for another computer. It carries references
/// (names, descriptions, paths, URLs, metadata), never file bytes, license data,
/// or window settings.
/// </summary>
public sealed class NestEggDocument
{
    public int SchemaVersion { get; set; } = NestEggLimits.SchemaVersion;
    public string Kind { get; set; } = NestEggLimits.Kind;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public NestEggSource Source { get; set; } = new();
    public NestEggProject Project { get; set; } = new();
}

public sealed class NestEggSource
{
    public string MachineLabel { get; set; } = "";
    public string? AppVersion { get; set; }
}

/// <summary>
/// Flat on purpose: each node names its parent by the id it had on the sending
/// computer. The receiver mints new ids and rebuilds the tree from this list.
/// </summary>
public sealed class NestEggProject
{
    public Guid SourceId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Color { get; set; }
    public string? IconKey { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public List<NestEggNode> Nodes { get; set; } = new();
}

public sealed class NestEggNode
{
    public Guid SourceId { get; set; }
    public Guid ParentSourceId { get; set; }
    public string ChildType { get; set; } = "";
    public int SortOrder { get; set; }
    public string? DisplayName { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
    public string? RealPath { get; set; }
    public string? Url { get; set; }
    public string? FilePath { get; set; }
    public bool OpenExternalOnly { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public sealed class NestEggSummary
{
    public string ProjectName { get; init; } = "";
    public Guid SourceProjectId { get; init; }
    public int CollectionCount { get; init; }
    public int FolderCount { get; init; }
    public int FileCount { get; init; }
    public int WebCount { get; init; }
    public IReadOnlyList<string> FolderPaths { get; init; } = [];
    public IReadOnlyList<string> FilePaths { get; init; } = [];
    public IReadOnlyList<string> Urls { get; init; } = [];
    public bool PathsTruncated { get; init; }
}

public static class NestEggLimits
{
    public const int SchemaVersion = 1;
    public const string Kind = "project-nest-egg";
    public const int MaxJsonBytes = 2_000_000;
    public const int MaxNodes = 5_000;
    public const int MaxNameLength = 200;
    public const int MaxTextLength = 4_000;
    public const int MaxPathLength = 2_048;
    public const int MaxMachineLabelLength = 80;
    public const int MaxAppVersionLength = 40;
    public const int MaxMetadataEntries = 40;
    public const int MaxMetadataKeyLength = 80;
    public const int MaxMetadataValueLength = 2_000;
    public const int PreviewListLimit = 20;
}

/// <summary>
/// Keys written onto the imported project and its children so a received nest
/// can still be traced back to the share that created it. Stripped again if
/// that project is later shared onward.
/// </summary>
public static class SharedImportMetadata
{
    public const string Prefix = "shared.";
    public const string SourceProjectId = "shared.sourceProjectId";
    public const string ShareCode = "shared.shareCode";
    public const string SenderLabel = "shared.senderLabel";
    public const string ImportedUtc = "shared.importedUtc";
    public const string SourceNodeId = "shared.sourceNodeId";
}

public sealed class NestEggFormatException : Exception
{
    public NestEggFormatException(string message) : base(message) { }
}
