namespace ProjectNest.Server;

/// <summary>
/// Persistence for one sharing server. SQL Server is the database you run.
/// SQLite is only the stand-in used when no connection string is configured,
/// including the automated tests.
/// </summary>
internal interface IShareDatabase
{
    string ReserveCode();

    void CreateShare(
        Guid eggId,
        Guid shareId,
        Guid eventId,
        int schemaVersion,
        DateTime createdUtc,
        DateTime expiresUtc,
        string senderLabel,
        string projectName,
        Guid sourceProjectId,
        string payloadJson,
        string payloadSha256,
        int byteLength,
        string code);

    StoredShare? FindByCode(string canonicalCode);

    void InsertEvent(Guid shareId, Guid eggId, string eventType, DateTime occurredUtc, string? machineLabel, string? detail);

    void RecordFetch(Guid shareId, Guid eggId, DateTime occurredUtc, string? machineLabel);

    void RecordImport(Guid shareId, Guid eggId, DateTime occurredUtc, string? machineLabel, string? detail);

    void Revoke(Guid shareId, Guid eggId, DateTime occurredUtc, string? machineLabel);

    IReadOnlyList<StoredEvent> ListEvents(Guid shareId);
}

internal sealed class StoredShare
{
    public string ShareId { get; set; } = "";
    public string Code { get; set; } = "";
    public string EggId { get; set; } = "";
    public string? RevokedUtc { get; set; }
    public string ExpiresUtc { get; set; } = "";
    public int SchemaVersion { get; set; }
    public string CreatedUtc { get; set; } = "";
    public string SenderLabel { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string SourceProjectId { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public string PayloadSha256 { get; set; } = "";
}

internal sealed class StoredEvent
{
    public string EventType { get; set; } = "";
    public string OccurredUtc { get; set; } = "";
    public string? MachineLabel { get; set; }
    public string? Detail { get; set; }
}
