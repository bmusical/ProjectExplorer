using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using ProjectExplorer.Core.Sharing;

namespace ProjectNest.Server;

/// <summary>
/// The phase-1 sharing database. Three tables: the egg payload, the claim code,
/// and an append-only event log so two computers can see what the other one did.
/// </summary>
public sealed class SharingStore
{
    private readonly string _connectionString;

    public SharingStore(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        EnsureSchema();
    }

    public PublishedShare Create(NestEggDocument egg, string canonicalJson, string machineLabel, DateTime expiresUtc)
    {
        var now = DateTime.UtcNow;
        var eggId = Guid.NewGuid();
        var shareId = Guid.NewGuid();
        var hash = NestEggCodec.Sha256Hex(canonicalJson);
        var code = ReserveCode();

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("""
            INSERT INTO NestEggs
                (Id, SchemaVersion, CreatedUtc, ExpiresUtc, SenderLabel, ProjectName, SourceProjectId, PayloadJson, PayloadSha256, ByteLength)
            VALUES
                (@Id, @SchemaVersion, @CreatedUtc, @ExpiresUtc, @SenderLabel, @ProjectName, @SourceProjectId, @PayloadJson, @PayloadSha256, @ByteLength)
            """, new
        {
            Id = eggId.ToString(),
            egg.SchemaVersion,
            CreatedUtc = now.ToString("O"),
            ExpiresUtc = expiresUtc.ToString("O"),
            SenderLabel = machineLabel,
            ProjectName = egg.Project.Name,
            SourceProjectId = egg.Project.SourceId.ToString(),
            PayloadJson = canonicalJson,
            PayloadSha256 = hash,
            ByteLength = Encoding.UTF8.GetByteCount(canonicalJson)
        }, transaction);

        connection.Execute("""
            INSERT INTO Shares (Id, EggId, Code, CreatedUtc, ExpiresUtc, FetchCount, ImportCount)
            VALUES (@Id, @EggId, @Code, @CreatedUtc, @ExpiresUtc, 0, 0)
            """, new
        {
            Id = shareId.ToString(),
            EggId = eggId.ToString(),
            Code = code,
            CreatedUtc = now.ToString("O"),
            ExpiresUtc = expiresUtc.ToString("O")
        }, transaction);

        InsertEvent(connection, transaction, shareId, eggId, ShareEventTypes.Created, now, machineLabel, egg.Project.Name);
        transaction.Commit();

        return new PublishedShare
        {
            Code = ShareCodes.Format(code),
            ExpiresUtc = expiresUtc,
            ProjectName = egg.Project.Name,
            ByteLength = Encoding.UTF8.GetByteCount(canonicalJson),
            PayloadSha256 = hash
        };
    }

    public ShareLookup<SharePreview> Preview(string canonicalCode, string? machineLabel)
    {
        var row = Find(canonicalCode);
        if (row.Status != ShareStatus.Available || row.Value == null)
            return new ShareLookup<SharePreview>(row.Status, null, row.Error);

        var egg = NestEggCodec.Parse(row.Value.PayloadJson);
        var summary = NestEggCodec.Summarize(egg);
        Log(row.Value, ShareEventTypes.Previewed, machineLabel, null);
        return new ShareLookup<SharePreview>(ShareStatus.Available, new SharePreview
        {
            Code = ShareCodes.Format(row.Value.Code),
            ProjectName = row.Value.ProjectName,
            SenderLabel = row.Value.SenderLabel,
            CreatedUtc = ParseUtc(row.Value.CreatedUtc),
            ExpiresUtc = ParseUtc(row.Value.ExpiresUtc),
            SchemaVersion = row.Value.SchemaVersion,
            SourceProjectId = Guid.Parse(row.Value.SourceProjectId),
            CollectionCount = summary.CollectionCount,
            FolderCount = summary.FolderCount,
            FileCount = summary.FileCount,
            WebCount = summary.WebCount,
            FolderPaths = summary.FolderPaths.ToList(),
            FilePaths = summary.FilePaths.ToList(),
            Urls = summary.Urls.ToList(),
            PathsTruncated = summary.PathsTruncated
        }, null);
    }

    public ShareLookup<EggPayload> Fetch(string canonicalCode, string? machineLabel)
    {
        var row = Find(canonicalCode);
        if (row.Status != ShareStatus.Available || row.Value == null)
            return new ShareLookup<EggPayload>(row.Status, null, row.Error);

        using var connection = Open();
        connection.Execute("UPDATE Shares SET FetchCount = FetchCount + 1 WHERE Id = @Id", new { Id = row.Value.ShareId });
        InsertEvent(connection, null, Guid.Parse(row.Value.ShareId), Guid.Parse(row.Value.EggId),
            ShareEventTypes.Fetched, DateTime.UtcNow, TrimLabel(machineLabel), null);

        return new ShareLookup<EggPayload>(ShareStatus.Available, new EggPayload(row.Value.PayloadJson, row.Value.PayloadSha256), null);
    }

    public ShareLookup<bool> ReportImported(string canonicalCode, string? machineLabel, string? detail)
    {
        var row = Find(canonicalCode);
        if (row.Status != ShareStatus.Available || row.Value == null)
            return new ShareLookup<bool>(row.Status, false, row.Error);

        using var connection = Open();
        connection.Execute("UPDATE Shares SET ImportCount = ImportCount + 1 WHERE Id = @Id", new { Id = row.Value.ShareId });
        InsertEvent(connection, null, Guid.Parse(row.Value.ShareId), Guid.Parse(row.Value.EggId),
            ShareEventTypes.Imported, DateTime.UtcNow, TrimLabel(machineLabel), TrimDetail(detail));
        return new ShareLookup<bool>(ShareStatus.Available, true, null);
    }

    public ShareLookup<IReadOnlyList<ShareEventRecord>> Events(string canonicalCode)
    {
        var row = Find(canonicalCode, recordRejection: false);
        if (row.Value == null)
            return new ShareLookup<IReadOnlyList<ShareEventRecord>>(row.Status, null, row.Error);

        using var connection = Open();
        var rows = connection.Query<EventRow>(
            "SELECT EventType, OccurredUtc, MachineLabel, Detail FROM ShareEvents WHERE ShareId = @ShareId ORDER BY OccurredUtc",
            new { ShareId = row.Value.ShareId });
        var events = rows.Select(evt => new ShareEventRecord
        {
            EventType = evt.EventType,
            OccurredUtc = ParseUtc(evt.OccurredUtc),
            MachineLabel = evt.MachineLabel,
            Detail = evt.Detail
        }).ToList();
        return new ShareLookup<IReadOnlyList<ShareEventRecord>>(ShareStatus.Available, events, null);
    }

    public ShareLookup<bool> Revoke(string canonicalCode, string? machineLabel)
    {
        var row = Find(canonicalCode, recordRejection: false);
        if (row.Status == ShareStatus.NotFound || row.Value == null)
            return new ShareLookup<bool>(ShareStatus.NotFound, false, row.Error);
        if (row.Value.RevokedUtc != null)
            return new ShareLookup<bool>(ShareStatus.Revoked, false, "This share was already revoked.");

        var now = DateTime.UtcNow;
        using var connection = Open();
        connection.Execute("UPDATE Shares SET RevokedUtc = @RevokedUtc WHERE Id = @Id",
            new { RevokedUtc = now.ToString("O"), Id = row.Value.ShareId });
        InsertEvent(connection, null, Guid.Parse(row.Value.ShareId), Guid.Parse(row.Value.EggId),
            ShareEventTypes.Revoked, now, TrimLabel(machineLabel), null);
        return new ShareLookup<bool>(ShareStatus.Available, true, null);
    }

    private ShareLookup<ShareRow> Find(string canonicalCode, bool recordRejection = true)
    {
        using var connection = Open();
        var row = connection.QuerySingleOrDefault<ShareRow>("""
            SELECT s.Id AS ShareId, s.Code, s.EggId, s.RevokedUtc, s.ExpiresUtc,
                   e.SchemaVersion, e.CreatedUtc, e.SenderLabel, e.ProjectName, e.SourceProjectId,
                   e.PayloadJson, e.PayloadSha256
            FROM Shares s
            JOIN NestEggs e ON e.Id = s.EggId
            WHERE s.Code = @Code
            """, new { Code = canonicalCode });

        if (row == null)
            return new ShareLookup<ShareRow>(ShareStatus.NotFound, null, "No share matches that code.");

        if (row.RevokedUtc != null)
        {
            if (recordRejection)
                Log(row, ShareEventTypes.Rejected, null, "revoked");
            return new ShareLookup<ShareRow>(ShareStatus.Revoked, row, "This share was revoked.");
        }

        if (DateTime.UtcNow >= ParseUtc(row.ExpiresUtc))
        {
            if (recordRejection)
                Log(row, ShareEventTypes.Rejected, null, "expired");
            return new ShareLookup<ShareRow>(ShareStatus.Expired, row, "This share has expired.");
        }

        return new ShareLookup<ShareRow>(ShareStatus.Available, row, null);
    }

    private void Log(ShareRow row, string eventType, string? machineLabel, string? detail)
    {
        using var connection = Open();
        InsertEvent(connection, null, Guid.Parse(row.ShareId), Guid.Parse(row.EggId),
            eventType, DateTime.UtcNow, TrimLabel(machineLabel), TrimDetail(detail));
    }

    private string ReserveCode()
    {
        using var connection = Open();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = ShareCodes.Generate();
            var taken = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Shares WHERE Code = @Code", new { Code = code });
            if (taken == 0) return code;
        }
        throw new InvalidOperationException("Couldn't allocate a share code.");
    }

    private static void InsertEvent(SqliteConnection connection, SqliteTransaction? transaction,
        Guid shareId, Guid eggId, string eventType, DateTime occurredUtc, string? machineLabel, string? detail)
    {
        connection.Execute("""
            INSERT INTO ShareEvents (Id, ShareId, EggId, EventType, OccurredUtc, MachineLabel, Detail)
            VALUES (@Id, @ShareId, @EggId, @EventType, @OccurredUtc, @MachineLabel, @Detail)
            """, new
        {
            Id = Guid.NewGuid().ToString(),
            ShareId = shareId.ToString(),
            EggId = eggId.ToString(),
            EventType = eventType,
            OccurredUtc = occurredUtc.ToString("O"),
            MachineLabel = machineLabel,
            Detail = detail
        }, transaction);
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS NestEggs (
                Id TEXT PRIMARY KEY,
                SchemaVersion INTEGER NOT NULL,
                CreatedUtc TEXT NOT NULL,
                ExpiresUtc TEXT NOT NULL,
                SenderLabel TEXT NOT NULL,
                ProjectName TEXT NOT NULL,
                SourceProjectId TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                PayloadSha256 TEXT NOT NULL,
                ByteLength INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Shares (
                Id TEXT PRIMARY KEY,
                EggId TEXT NOT NULL REFERENCES NestEggs(Id),
                Code TEXT NOT NULL UNIQUE,
                CreatedUtc TEXT NOT NULL,
                ExpiresUtc TEXT NOT NULL,
                RevokedUtc TEXT NULL,
                FetchCount INTEGER NOT NULL DEFAULT 0,
                ImportCount INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS ShareEvents (
                Id TEXT PRIMARY KEY,
                ShareId TEXT NOT NULL,
                EggId TEXT NOT NULL,
                EventType TEXT NOT NULL,
                OccurredUtc TEXT NOT NULL,
                MachineLabel TEXT NULL,
                Detail TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ShareEvents_ShareId ON ShareEvents(ShareId);
            """);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
        return connection;
    }

    private static DateTime ParseUtc(string text)
    {
        var parsed = DateTime.Parse(text, null, System.Globalization.DateTimeStyles.RoundtripKind);
        return parsed.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc) : parsed.ToUniversalTime();
    }

    private static string? TrimLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= NestEggLimits.MaxMachineLabelLength
            ? trimmed
            : trimmed[..NestEggLimits.MaxMachineLabelLength];
    }

    private static string? TrimDetail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }

    private sealed class EventRow
    {
        public string EventType { get; set; } = "";
        public string OccurredUtc { get; set; } = "";
        public string? MachineLabel { get; set; }
        public string? Detail { get; set; }
    }

    private sealed class ShareRow
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
}

public enum ShareStatus
{
    Available,
    NotFound,
    Expired,
    Revoked
}

public sealed record ShareLookup<T>(ShareStatus Status, T? Value, string? Error);

public sealed record EggPayload(string Json, string Sha256);
