using Microsoft.Data.SqlClient;

namespace ProjectNest.Server;

/// <summary>
/// The sharing server always opens SQL Server database ProjectNestSharing.
/// A local connection string may name the instance only, or name another
/// database on that instance. This writes the sharing database in before connect.
/// </summary>
public static class SharingConnection
{
    public static string UseDatabase(string connectionString, string? databaseName = null)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = string.IsNullOrWhiteSpace(databaseName)
                ? SharingOptions.DefaultDatabaseName
                : databaseName.Trim()
        };
        return builder.ConnectionString;
    }
}
