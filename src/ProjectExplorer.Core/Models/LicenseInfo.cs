namespace ProjectExplorer.Core.Models;

public enum LicenseState
{
    Free,       // under free limits
    LimitReached,
    Licensed,
    Invalid     // key present but signature failed
}

public sealed record LicenseInfo
{
    public LicenseState State { get; init; }
    public string? Email { get; init; }
    public DateTime? LicensedOn { get; init; }

    // Free-tier usage (populated for Free and LimitReached states)
    public int ProjectCount { get; init; }
    public int LeafNodeCount { get; init; }
    public int ProjectLimit { get; init; }
    public int LeafNodeLimit { get; init; }

    public bool IsUsable => State is LicenseState.Free or LicenseState.Licensed;

    // True when adding another project would breach the limit
    public bool AtProjectLimit => State == LicenseState.Free && ProjectCount >= ProjectLimit;

    // True when adding another leaf node would breach the limit
    public bool AtLeafLimit => State == LicenseState.Free && LeafNodeCount >= LeafNodeLimit;
}
