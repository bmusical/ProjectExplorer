using System.Text.Json;
using ProjectExplorer.Core.Models;

namespace ProjectExplorer.Core.Services;

/// <summary>
/// Loads/saves app-wide settings (as opposed to project data) at
/// %APPDATA%\ProjectExplorer\appsettings.json.
/// </summary>
public class AppSettingsManager
{
    private readonly string _settingsFile;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public AppSettingsManager()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProjectExplorer"))
    {
    }

    public AppSettingsManager(string storageDir)
    {
        _settingsFile = Path.Combine(storageDir, "appsettings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsFile))
            {
                var json = File.ReadAllText(_settingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { /* fall back to defaults */ }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
            File.WriteAllText(_settingsFile, JsonSerializer.Serialize(settings, Options));
        }
        catch { /* non-critical */ }
    }
}
