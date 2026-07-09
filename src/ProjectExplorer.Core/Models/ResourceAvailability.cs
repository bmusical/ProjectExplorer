namespace ProjectExplorer.Core.Models;

/// <summary>
/// Where a resource's underlying path/URL physically lives, which determines how
/// <see cref="ProjectExplorer.Core.Services.ResourceAvailabilityChecker"/> and the UI reason about
/// an unavailable resource: a missing local-disk item was likely moved or deleted, while a missing
/// network/removable/web item may just be temporarily disconnected.
/// </summary>
public enum ResourceLocationKind
{
    Unknown,
    LocalDisk,
    NetworkOrRemovable,
    Web
}

/// <summary>Result of the most recent availability check for a resource.</summary>
public enum AvailabilityStatus
{
    /// <summary>Not checked yet this session.</summary>
    Unknown,
    Available,
    Unavailable
}

/// <summary>
/// The outcome of one availability check: whether the resource was reachable, where it lives
/// (see <see cref="ResourceLocationKind"/>), and when the check ran.
/// </summary>
public readonly record struct AvailabilityCheckResult(
    AvailabilityStatus Status,
    ResourceLocationKind LocationKind,
    DateTime CheckedAtUtc);
