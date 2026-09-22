namespace ProjectNest.Server;

public sealed class SharingOptions
{
    public const string SectionName = "Sharing";
    public const string DefaultDatabaseName = "ProjectNestSharing";

    /// <summary>
    /// How long a share code keeps working. Values below 1 expire immediately
    /// (used by tests). Values above 30 are capped.
    /// </summary>
    public int LifetimeDays { get; set; } = 7;

    /// <summary>
    /// SQL Server connection string for database ProjectNestSharing.
    /// When this is set, the server calls the stored procedures created by
    /// Sql/001_CreateSharingDatabase.sql. When it is empty and
    /// <see cref="DatabasePath"/> is also empty, Development uses
    /// ConnectionStrings:ControlPlane (appsettings.Development.json).
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// SQL Server database the sharing server opens. Written into the
    /// connection string as Initial Catalog before connect, so a string that
    /// names another database on the same instance still lands here.
    /// </summary>
    public string Database { get; set; } = DefaultDatabaseName;

    /// <summary>
    /// SQLite file used when <see cref="ConnectionString"/> is empty.
    /// A non-empty path wins over ConnectionStrings:ControlPlane so tests
    /// stay on a temp file. Empty, with no ControlPlane string, uses
    /// data/sharing.db under the content root.
    /// </summary>
    public string DatabasePath { get; set; } = "";

    public DateTime ExpiresFrom(DateTime utcNow) =>
        LifetimeDays < 1
            ? utcNow.AddMinutes(-1)
            : utcNow.AddDays(Math.Min(LifetimeDays, 30));
}
