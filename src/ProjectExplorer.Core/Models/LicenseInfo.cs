namespace ProjectExplorer.Core.Models;

public enum LicenseState
{
    Trial,       // within trial window
    TrialExpired,
    Licensed,
    Invalid      // key present but signature failed
}

public sealed class LicenseInfo
{
    public LicenseState State { get; init; }
    public string? Email { get; init; }
    public DateTime? LicensedOn { get; init; }
    public int TrialDaysRemaining { get; init; }

    public bool IsUsable => State is LicenseState.Trial or LicenseState.Licensed;
}
