namespace ProjectNest.Server;

public sealed class SharingOptions
{
    public const string SectionName = "Sharing";

    /// <summary>
    /// How long a share code keeps working. Values below 1 expire immediately
    /// (used by tests). Values above 30 are capped.
    /// </summary>
    public int LifetimeDays { get; set; } = 7;

    /// <summary>
    /// SQL Server connection string for database ProjectNestSharing.
    /// When this is set, the server calls the stored procedures created by
    /// Sql/001_CreateSharingDatabase.sql. When it is empty, the server uses a
    /// local SQLite file instead (tests, and a machine that has not created the
    /// SQL Server database yet).
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// SQLite file used only when <see cref="ConnectionString"/> is empty.
    /// Empty uses data/sharing.db under the content root.
    /// </summary>
    public string DatabasePath { get; set; } = "";

    public DateTime ExpiresFrom(DateTime utcNow) =>
        LifetimeDays < 1
            ? utcNow.AddMinutes(-1)
            : utcNow.AddDays(Math.Min(LifetimeDays, 30));
}
