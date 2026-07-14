using System.IO.Compression;
using ProjectExplorer.Core.Services;

namespace ProjectExplorer.Tests;

public class UserDataExportServiceTests
{
    private readonly string _storageDir;
    private readonly string _zipPath;

    public UserDataExportServiceTests()
    {
        _storageDir = Path.Combine(Path.GetTempPath(), $"ProjectExplorer_ExportTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_storageDir);
        _zipPath = Path.Combine(Path.GetTempPath(), $"ProjectExplorer_ExportTest_{Guid.NewGuid():N}.zip");
    }

    [Fact]
    public void ExportAll_WithNoDataYet_CreatesEmptyArchiveAndReturnsNoneIncluded()
    {
        var service = new UserDataExportService(_storageDir);

        var included = service.ExportAll(_zipPath);

        Assert.Empty(included);
        Assert.True(File.Exists(_zipPath));
        using var archive = ZipFile.OpenRead(_zipPath);
        Assert.Empty(archive.Entries);
    }

    [Fact]
    public void ExportAll_IncludesOnlyKnownFilesThatExist()
    {
        File.WriteAllText(Path.Combine(_storageDir, "projects.json"), "[]");
        File.WriteAllText(Path.Combine(_storageDir, "license.json"), "{\"tier\":\"Free\"}");
        // Not a known data file — should be ignored (e.g. a WebView2 cache folder/file, or junk).
        File.WriteAllText(Path.Combine(_storageDir, "unrelated.txt"), "not app data");

        var service = new UserDataExportService(_storageDir);
        var included = service.ExportAll(_zipPath);

        Assert.Equal(new[] { "projects.json", "license.json" }, included);

        using var archive = ZipFile.OpenRead(_zipPath);
        Assert.Equal(2, archive.Entries.Count);
        Assert.Contains(archive.Entries, e => e.FullName == "projects.json");
        Assert.Contains(archive.Entries, e => e.FullName == "license.json");
        Assert.DoesNotContain(archive.Entries, e => e.FullName == "unrelated.txt");
    }

    [Fact]
    public void ExportAll_PreservesFileContent()
    {
        const string projectsJson = "[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"Test\"}]";
        File.WriteAllText(Path.Combine(_storageDir, "projects.json"), projectsJson);

        var service = new UserDataExportService(_storageDir);
        service.ExportAll(_zipPath);

        using var archive = ZipFile.OpenRead(_zipPath);
        var entry = archive.GetEntry("projects.json")!;
        using var reader = new StreamReader(entry.Open());
        Assert.Equal(projectsJson, reader.ReadToEnd());
    }

    [Fact]
    public void ExportAll_IncludesAllFourKnownFilesAndBakWhenPresent()
    {
        File.WriteAllText(Path.Combine(_storageDir, "projects.json"), "[]");
        File.WriteAllText(Path.Combine(_storageDir, "projects.json.bak"), "[]");
        File.WriteAllText(Path.Combine(_storageDir, "license.json"), "{}");
        File.WriteAllText(Path.Combine(_storageDir, "uisettings.json"), "{}");
        File.WriteAllText(Path.Combine(_storageDir, "appsettings.json"), "{}");

        var service = new UserDataExportService(_storageDir);
        var included = service.ExportAll(_zipPath);

        Assert.Equal(5, included.Count);
        Assert.Contains("projects.json", included);
        Assert.Contains("projects.json.bak", included);
        Assert.Contains("license.json", included);
        Assert.Contains("uisettings.json", included);
        Assert.Contains("appsettings.json", included);
    }

    [Fact]
    public void ExportAll_OverwritesExistingDestinationZip()
    {
        File.WriteAllText(Path.Combine(_storageDir, "projects.json"), "[]");
        var service = new UserDataExportService(_storageDir);

        service.ExportAll(_zipPath);
        var firstSize = new FileInfo(_zipPath).Length;

        File.WriteAllText(Path.Combine(_storageDir, "license.json"), "{}");
        var included = service.ExportAll(_zipPath);

        Assert.Equal(2, included.Count);
        using var archive = ZipFile.OpenRead(_zipPath);
        Assert.Equal(2, archive.Entries.Count);
    }

    [Fact]
    public void ExportAll_MissingStorageDirectory_CreatesEmptyArchive()
    {
        var missingDir = Path.Combine(_storageDir, "does-not-exist");
        var service = new UserDataExportService(missingDir);

        var included = service.ExportAll(_zipPath);

        Assert.Empty(included);
        using var archive = ZipFile.OpenRead(_zipPath);
        Assert.Empty(archive.Entries);
    }
}
