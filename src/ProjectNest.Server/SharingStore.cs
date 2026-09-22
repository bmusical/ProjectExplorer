using System.Text;
using ProjectExplorer.Core.Sharing;

namespace ProjectNest.Server;

/// <summary>
/// Share codes, expiry, and the activity log. Storage is either SQL Server
/// (when Sharing:ConnectionString is set) or the SQLite stand-in.
/// </summary>
public sealed class SharingStore
{
    private readonly IShareDatabase _database;

    internal SharingStore(IShareDatabase database)
    {
        _database = database;
    }

    public static SharingStore Sqlite(string databasePath) => new(new SqliteShareDatabase(databasePath));

    public static SharingStore SqlServer(string connectionString) => new(new SqlShareDatabase(connectionString));

    public PublishedShare Create(NestEggDocument egg, string canonicalJson, string machineLabel, DateTime expiresUtc)
    {
        var now = DateTime.UtcNow;
        var eggId = Guid.NewGuid();
        var shareId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var hash = NestEggCodec.Sha256Hex(canonicalJson);
        var code = _database.ReserveCode();

        _database.CreateShare(
            eggId, shareId, eventId, egg.SchemaVersion, now, expiresUtc,
            machineLabel, egg.Project.Name, egg.Project.SourceId, canonicalJson, hash,
            Encoding.UTF8.GetByteCount(canonicalJson), code);

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

        _database.RecordFetch(Guid.Parse(row.Value.ShareId), Guid.Parse(row.Value.EggId), DateTime.UtcNow, TrimLabel(machineLabel));
        return new ShareLookup<EggPayload>(ShareStatus.Available, new EggPayload(row.Value.PayloadJson, row.Value.PayloadSha256), null);
    }

    public ShareLookup<bool> ReportImported(string canonicalCode, string? machineLabel, string? detail)
    {
        var row = Find(canonicalCode);
        if (row.Status != ShareStatus.Available || row.Value == null)
            return new ShareLookup<bool>(row.Status, false, row.Error);

        _database.RecordImport(
            Guid.Parse(row.Value.ShareId), Guid.Parse(row.Value.EggId), DateTime.UtcNow,
            TrimLabel(machineLabel), TrimDetail(detail));
        return new ShareLookup<bool>(ShareStatus.Available, true, null);
    }

    public ShareLookup<IReadOnlyList<ShareEventRecord>> Events(string canonicalCode)
    {
        var row = Find(canonicalCode, recordRejection: false);
        if (row.Value == null)
            return new ShareLookup<IReadOnlyList<ShareEventRecord>>(row.Status, null, row.Error);

        var events = _database.ListEvents(Guid.Parse(row.Value.ShareId))
            .Select(evt => new ShareEventRecord
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

        _database.Revoke(Guid.Parse(row.Value.ShareId), Guid.Parse(row.Value.EggId), DateTime.UtcNow, TrimLabel(machineLabel));
        return new ShareLookup<bool>(ShareStatus.Available, true, null);
    }

    private ShareLookup<StoredShare> Find(string canonicalCode, bool recordRejection = true)
    {
        var row = _database.FindByCode(canonicalCode);
        if (row == null)
            return new ShareLookup<StoredShare>(ShareStatus.NotFound, null, "No share matches that code.");

        if (row.RevokedUtc != null)
        {
            if (recordRejection)
                Log(row, ShareEventTypes.Rejected, null, "revoked");
            return new ShareLookup<StoredShare>(ShareStatus.Revoked, row, "This share was revoked.");
        }

        if (DateTime.UtcNow >= ParseUtc(row.ExpiresUtc))
        {
            if (recordRejection)
                Log(row, ShareEventTypes.Rejected, null, "expired");
            return new ShareLookup<StoredShare>(ShareStatus.Expired, row, "This share has expired.");
        }

        return new ShareLookup<StoredShare>(ShareStatus.Available, row, null);
    }

    private void Log(StoredShare row, string eventType, string? machineLabel, string? detail)
    {
        _database.InsertEvent(
            Guid.Parse(row.ShareId), Guid.Parse(row.EggId), eventType, DateTime.UtcNow,
            TrimLabel(machineLabel), TrimDetail(detail));
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
