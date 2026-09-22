using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace ProjectNest.Server;

/// <summary>
/// SQL Server sharing database. The tables and procedures are created by
/// Sql/001_CreateSharingDatabase.sql, not by this process.
/// </summary>
internal sealed class SqlShareDatabase : IShareDatabase
{
    private readonly string _connectionString;

    public SqlShareDatabase(string connectionString)
    {
        _connectionString = connectionString;
        EnsureReady();
    }

    public string ReserveCode()
    {
        using var connection = Open();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = ShareCodes.Generate();
            if (!CodeExists(connection, code)) return code;
        }
        throw new InvalidOperationException("Couldn't allocate a share code.");
    }

    public void CreateShare(
        Guid eggId, Guid shareId, Guid eventId, int schemaVersion, DateTime createdUtc, DateTime expiresUtc,
        string senderLabel, string projectName, Guid sourceProjectId, string payloadJson, string payloadSha256,
        int byteLength, string code)
    {
        using var connection = Open();
        connection.Execute("dbo.usp_Share_Create", new
        {
            EggId = eggId,
            ShareId = shareId,
            EventId = eventId,
            SchemaVersion = schemaVersion,
            CreatedUtc = AsUtc(createdUtc),
            ExpiresUtc = AsUtc(expiresUtc),
            SenderLabel = senderLabel,
            ProjectName = projectName,
            SourceProjectId = sourceProjectId,
            PayloadJson = payloadJson,
            PayloadSha256 = payloadSha256,
            ByteLength = byteLength,
            Code = code
        }, commandType: CommandType.StoredProcedure);
    }

    public StoredShare? FindByCode(string canonicalCode)
    {
        using var connection = Open();
        var row = connection.QuerySingleOrDefault<SqlShareRow>(
            "dbo.usp_Share_GetByCode",
            new { Code = canonicalCode },
            commandType: CommandType.StoredProcedure);
        return row == null ? null : ToStored(row);
    }

    public void InsertEvent(Guid shareId, Guid eggId, string eventType, DateTime occurredUtc, string? machineLabel, string? detail)
    {
        using var connection = Open();
        connection.Execute("dbo.usp_ShareEvent_Insert", new
        {
            EventId = Guid.NewGuid(),
            ShareId = shareId,
            EggId = eggId,
            EventType = eventType,
            OccurredUtc = AsUtc(occurredUtc),
            MachineLabel = machineLabel,
            Detail = detail
        }, commandType: CommandType.StoredProcedure);
    }

    public void RecordFetch(Guid shareId, Guid eggId, DateTime occurredUtc, string? machineLabel)
    {
        using var connection = Open();
        connection.Execute("dbo.usp_Share_RecordFetch", new
        {
            ShareId = shareId,
            EggId = eggId,
            EventId = Guid.NewGuid(),
            OccurredUtc = AsUtc(occurredUtc),
            MachineLabel = machineLabel
        }, commandType: CommandType.StoredProcedure);
    }

    public void RecordImport(Guid shareId, Guid eggId, DateTime occurredUtc, string? machineLabel, string? detail)
    {
        using var connection = Open();
        connection.Execute("dbo.usp_Share_RecordImport", new
        {
            ShareId = shareId,
            EggId = eggId,
            EventId = Guid.NewGuid(),
            OccurredUtc = AsUtc(occurredUtc),
            MachineLabel = machineLabel,
            Detail = detail
        }, commandType: CommandType.StoredProcedure);
    }

    public void Revoke(Guid shareId, Guid eggId, DateTime occurredUtc, string? machineLabel)
    {
        using var connection = Open();
        connection.Execute("dbo.usp_Share_Revoke", new
        {
            ShareId = shareId,
            EggId = eggId,
            EventId = Guid.NewGuid(),
            OccurredUtc = AsUtc(occurredUtc),
            MachineLabel = machineLabel
        }, commandType: CommandType.StoredProcedure);
    }

    public IReadOnlyList<StoredEvent> ListEvents(Guid shareId)
    {
        using var connection = Open();
        var rows = connection.Query<SqlEventRow>(
            "dbo.usp_ShareEvent_List",
            new { ShareId = shareId },
            commandType: CommandType.StoredProcedure);
        return rows.Select(row => new StoredEvent
        {
            EventType = row.EventType,
            OccurredUtc = FormatUtc(row.OccurredUtc),
            MachineLabel = row.MachineLabel,
            Detail = row.Detail
        }).ToList();
    }

    private void EnsureReady()
    {
        try
        {
            using var connection = Open();
            var procedureId = connection.ExecuteScalar<int?>("SELECT OBJECT_ID(N'dbo.usp_Share_GetByCode', N'P')");
            if (procedureId == null)
            {
                throw new InvalidOperationException(
                    "SQL Server is missing dbo.usp_Share_GetByCode. Run src/ProjectNest.Server/Sql/001_CreateSharingDatabase.sql, then set Sharing:ConnectionString to database ProjectNestSharing.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException(
                "Couldn't open the SQL Server sharing database. Check Sharing:ConnectionString, and run src/ProjectNest.Server/Sql/001_CreateSharingDatabase.sql if the database does not exist yet. " + ex.Message,
                ex);
        }
    }

    private static bool CodeExists(SqlConnection connection, string code)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@Code", code);
        parameters.Add("@Exists", dbType: DbType.Boolean, direction: ParameterDirection.Output);
        connection.Execute("dbo.usp_Share_CodeExists", parameters, commandType: CommandType.StoredProcedure);
        return parameters.Get<bool>("@Exists");
    }

    private SqlConnection Open()
    {
        var connection = new SqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static StoredShare ToStored(SqlShareRow row) => new()
    {
        ShareId = row.ShareId.ToString(),
        Code = row.Code,
        EggId = row.EggId.ToString(),
        RevokedUtc = row.RevokedUtc == null ? null : FormatUtc(row.RevokedUtc.Value),
        ExpiresUtc = FormatUtc(row.ExpiresUtc),
        SchemaVersion = row.SchemaVersion,
        CreatedUtc = FormatUtc(row.CreatedUtc),
        SenderLabel = row.SenderLabel,
        ProjectName = row.ProjectName,
        SourceProjectId = row.SourceProjectId.ToString(),
        PayloadJson = row.PayloadJson,
        PayloadSha256 = row.PayloadSha256.Trim()
    };

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string FormatUtc(DateTime value) => AsUtc(value).ToString("O");

    private sealed class SqlShareRow
    {
        public Guid ShareId { get; set; }
        public string Code { get; set; } = "";
        public Guid EggId { get; set; }
        public DateTime? RevokedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public int SchemaVersion { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string SenderLabel { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public Guid SourceProjectId { get; set; }
        public string PayloadJson { get; set; } = "";
        public string PayloadSha256 { get; set; } = "";
    }

    private sealed class SqlEventRow
    {
        public string EventType { get; set; } = "";
        public DateTime OccurredUtc { get; set; }
        public string? MachineLabel { get; set; }
        public string? Detail { get; set; }
    }
}
