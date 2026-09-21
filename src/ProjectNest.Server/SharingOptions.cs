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
    /// SQLite file for the sharing tables. Empty uses data/sharing.db under the content root.
    /// </summary>
    public string DatabasePath { get; set; } = "";

    public DateTime ExpiresFrom(DateTime utcNow) =>
        LifetimeDays < 1
            ? utcNow.AddMinutes(-1)
            : utcNow.AddDays(Math.Min(LifetimeDays, 30));
}
