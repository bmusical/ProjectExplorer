namespace ProjectExplorer.Core.Sharing;

public sealed class PublishShareRequest
{
    public string MachineLabel { get; set; } = "";
    public NestEggDocument? Egg { get; set; }
}

public sealed class PublishedShare
{
    public string Code { get; set; } = "";
    public DateTime ExpiresUtc { get; set; }
    public string ProjectName { get; set; } = "";
    public int ByteLength { get; set; }
    public string PayloadSha256 { get; set; } = "";
}

public sealed class SharePreview
{
    public string Code { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string SenderLabel { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public int SchemaVersion { get; set; }
    public Guid SourceProjectId { get; set; }
    public int CollectionCount { get; set; }
    public int FolderCount { get; set; }
    public int FileCount { get; set; }
    public int WebCount { get; set; }
    public List<string> FolderPaths { get; set; } = new();
    public List<string> FilePaths { get; set; } = new();
    public List<string> Urls { get; set; } = new();
    public bool PathsTruncated { get; set; }
}

public sealed class ShareEventRequest
{
    public string EventType { get; set; } = "";
    public string? MachineLabel { get; set; }
    public string? Detail { get; set; }
}

public sealed class ShareEventRecord
{
    public string EventType { get; set; } = "";
    public DateTime OccurredUtc { get; set; }
    public string? MachineLabel { get; set; }
    public string? Detail { get; set; }
}

public sealed class ShareApiError
{
    public string Error { get; set; } = "";
}

public static class ShareEventTypes
{
    public const string Created = "Created";
    public const string Previewed = "Previewed";
    public const string Fetched = "Fetched";
    public const string Imported = "Imported";
    public const string Revoked = "Revoked";
    public const string Rejected = "Rejected";

    public static bool IsClientReportable(string? eventType) =>
        string.Equals(eventType, Imported, StringComparison.Ordinal);
}
