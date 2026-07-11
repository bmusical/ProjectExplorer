namespace ProjectExplorer.Core.Models;

/// <summary>
/// Controls what happens when the app is launched while it's already running.
/// </summary>
public enum FocusOnRunMode
{
    PreventMultipleCopies,
    AllowMultipleCopies
}

/// <summary>
/// App-wide preferences (as opposed to project data), persisted via AppSettingsManager.
/// </summary>
public class AppSettings
{
    public FocusOnRunMode FocusOnRun { get; set; } = FocusOnRunMode.PreventMultipleCopies;

    // Persisted main window placement, used to restore the window where the user left it
    // and to detect when that position has drifted off every currently connected screen
    // (e.g. a second monitor was disconnected).
    public int? WindowLeft { get; set; }
    public int? WindowTop { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
}
