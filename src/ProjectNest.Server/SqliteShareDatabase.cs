using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ProjectNest.Server;

/// <summary>
/// File-backed stand-in for machines that have not set a SQL Server connection
/// string yet. The tables match the SQL Server script closely enough for the
/// same server behavior. Create the real database with
/// Sql/001_CreateSharingDatabase.sql.
/// </summary>
internal sealed class SqliteShareDatabase : IShareDatabase
{
    private readonly string _connectionString;

    public SqliteShareDatabase(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        EnsureSchema();
    }

    public string ReserveCode()
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

    public void CreateShare(
        Guid eggId, Guid shareId, Guid eventId, int schemaVersion, DateTime createdUtc, DateTime expiresUtc,
        string senderLabel, string projectName, Guid sourceProjectId, string payloadJson, string payloadSha256,
        int byteLength, string code)
    {
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
            SchemaVersion = schemaVersion,
            CreatedUtc = createdUtc.ToString("O"),
            ExpiresUtc = expiresUtc.ToString("O"),
            SenderLabel = senderLabel,
            ProjectName = projectName,
            SourceProjectId = sourceProjectId.ToString(),
            PayloadJson = payloadJson,
            PayloadSha256 = payloadSha256,
            ByteLength = byteLength
        }, transaction);

        connection.Execute("""
            INSERT INTO Shares (Id, EggId, Code, CreatedUtc, ExpiresUtc, FetchCount, ImportCount)
            VALUES (@Id, @EggId, @Code, @CreatedUtc, @ExpiresUtc, 0, 0)
            """, new
        {
            Id = shareId.ToString(),
            EggId = eggId.ToString(),
            Code = code,
            CreatedUtc = createdUtc.ToString("O"),
            ExpiresUtc = expiresUtc.ToString("O")
        }, transaction);

        InsertEvent(connection, transaction, eventId, shareId, eggId, "Created", createdUtc, senderLabel, projectName);
        transaction.Commit();
    }

    public StoredShare? FindByCode(string canonicalCode)
    {
        using var connection = Open();
        return connection.QuerySingleOrDefault<StoredShare>("""
            SELECT s.Id AS ShareId, s.Code, s.EggId, s.RevokedUtc, s.ExpiresUtc,
                   e.SchemaVersion, e.CreatedUtc, e.SenderLabel, e.ProjectName, e.SourceProjectId,
                   e.PayloadJson, e.PayloadSha256
            FROM Shares s
            JOIN NestEggs e ON e.Id = s.EggId
            WHERE s.Code = @Code
            """, new { Code = canonicalCode });
    }

    public void InsertEvent(Guid shareId, Guid eggId, string eventType, DateTime occurredUtc, string? machineLabel, string? detail)
    {
        using var connection = Open();
        InsertEvent(connection, null, Guid.NewGuid(), shareId, eggId, eventType, occurredUtc, machineLabel, detail);
    }

    public void RecordFetch(Guid shareId, Guid eggId, DateTime occurredUtc, string? machineLabel)
    {
        using var connection = Open();
        connection.Execute("UPDATE Shares SET FetchCount = FetchCount + 1 WHERE Id = @Id", new { Id = shareId.ToString() });
        InsertEvent(connection, null, Guid.NewGuid(), shareId, eggId, "Fetched", occurredUtc, machineLabel, null);
    }

    public void RecordImport(Guid shareId, Guid eggId, DateTime occurredUtc, string? machineLabel, string? detail)
    {
        using var connection = Open();
        connection.Execute("UPDATE Shares SET ImportCount = ImportCount + 1 WHERE Id = @Id", new { Id = shareId.ToString() });
        InsertEvent(connection, null, Guid.NewGuid(), shareId, eggId, "Imported", occurredUtc, machineLabel, detail);
    }

    public void Revoke(Guid shareId, Guid eggId, DateTime occurredUtc, string? machineLabel)
    {
        using var connection = Open();
        connection.Execute("UPDATE Shares SET RevokedUtc = @RevokedUtc WHERE Id = @Id",
            new { RevokedUtc = occurredUtc.ToString("O"), Id = shareId.ToString() });
        InsertEvent(connection, null, Guid.NewGuid(), shareId, eggId, "Revoked", occurredUtc, machineLabel, null);
    }

    public IReadOnlyList<StoredEvent> ListEvents(Guid shareId)
    {
        using var connection = Open();
        return connection.Query<StoredEvent>(
            "SELECT EventType, OccurredUtc, MachineLabel, Detail FROM ShareEvents WHERE ShareId = @ShareId ORDER BY OccurredUtc",
            new { ShareId = shareId.ToString() }).ToList();
    }

    private static void InsertEvent(SqliteConnection connection, SqliteTransaction? transaction,
        Guid eventId, Guid shareId, Guid eggId, string eventType, DateTime occurredUtc, string? machineLabel, string? detail)
    {
        connection.Execute("""
            INSERT INTO ShareEvents (Id, ShareId, EggId, EventType, OccurredUtc, MachineLabel, Detail)
            VALUES (@Id, @ShareId, @EggId, @EventType, @OccurredUtc, @MachineLabel, @Detail)
            """, new
        {
            Id = eventId.ToString(),
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
}
