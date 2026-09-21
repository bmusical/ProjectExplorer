namespace ProjectExplorer.Core.Models;

/// <summary>
/// App-wide preferences (as opposed to project data), persisted via AppSettingsManager.
/// </summary>
public class AppSettings
{
    // Persisted main window placement, used to restore the window where the user left it
    // and to detect when that position has drifted off every currently connected screen
    // (e.g. a second monitor was disconnected).
    public int? WindowLeft { get; set; }
    public int? WindowTop { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>
    /// Address of the phase-1 sharing server (for example http://192.168.1.20:5088).
    /// Remembered so both Share and Receive use the same host.
    /// </summary>
    public string? ShareServerUrl { get; set; }

    /// <summary>
    /// Name this computer sends with a Nest Egg, so the other computer can see who it came from.
    /// </summary>
    public string? ShareMachineLabel { get; set; }
}
