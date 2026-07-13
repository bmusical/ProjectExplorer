using System.IO.Compression;

namespace ProjectExplorer.Core.Services;

/// <summary>
/// Bundles every file Project Nest Explorer has written to its own storage directory (the same
/// list docs/HELP.md documents under "what this app does not do": projects.json + its .bak,
/// license.json, uisettings.json, appsettings.json) into a single zip, so a user can get
/// everything the app holds about them in one place on request — a GDPR-style "give me all my
/// data" export. This is a one-way, one-shot copy of whatever's currently on disk; there is
/// deliberately no matching Import — it isn't an operational backup/restore or migration feature.
/// </summary>
public class UserDataExportService
{
    private static readonly string[] KnownDataFiles =
    {
        "projects.json",
        "projects.json.bak",
        "license.json",
        "uisettings.json",
        "appsettings.json"
    };

    private readonly string _storageDir;

    public UserDataExportService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProjectExplorer"))
    {
    }

    public UserDataExportService(string storageDir)
    {
        _storageDir = storageDir;
    }

    /// <summary>
    /// Writes a zip containing every known data file that currently exists in the storage
    /// directory to <paramref name="destinationZipFilePath"/>, overwriting it if already present.
    /// Returns the file names actually included — a fresh install may have none yet (e.g. no
    /// license.json until the user registers).
    /// </summary>
    public List<string> ExportAll(string destinationZipFilePath)
    {
        if (File.Exists(destinationZipFilePath))
            File.Delete(destinationZipFilePath);

        var included = new List<string>();
        using var archive = ZipFile.Open(destinationZipFilePath, ZipArchiveMode.Create);
        foreach (var fileName in KnownDataFiles)
        {
            var fullPath = Path.Combine(_storageDir, fileName);
            if (!File.Exists(fullPath)) continue;

            archive.CreateEntryFromFile(fullPath, fileName, CompressionLevel.Optimal);
            included.Add(fileName);
        }

        return included;
    }
}
