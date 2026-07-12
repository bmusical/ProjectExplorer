using ProjectExplorer.Core.Models;

namespace ProjectExplorer.Core.Services;

/// <summary>
/// Classifies a FolderReference/FileReference/WebResource's location (local disk vs.
/// network/removable drive vs. web) and checks whether it's currently reachable. Stateless and
/// side-effect-free — the WinForms layer owns caching results and deciding when to re-check.
/// </summary>
public static class ResourceAvailabilityChecker
{
    /// <summary>
    /// Metadata key (in <see cref="ProjectChild.Metadata"/>) a user can set to "true" to stop the
    /// automatic background re-check of a resource that's currently unavailable — e.g. a network
    /// drive they know is gone for good and don't want polled. Only meaningful for
    /// <see cref="ResourceLocationKind.NetworkOrRemovable"/>/<see cref="ResourceLocationKind.Web"/>
    /// resources; local-disk resources are never auto-retried in the first place.
    /// </summary>
    public const string SuppressAutoRetryMetadataKey = "availabilitySuppressAutoRetry";

    private static readonly TimeSpan DefaultPathTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan DefaultWebTimeout = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Classifies a filesystem path without touching the disk beyond a drive-type lookup.
    /// UNC paths (\\server\share\...) are always <see cref="ResourceLocationKind.NetworkOrRemovable"/>,
    /// since <see cref="Path"/>/<see cref="DriveInfo"/> only recognize Windows drive letters on the
    /// OS that's actually running them.
    /// </summary>
    public static ResourceLocationKind ClassifyPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ResourceLocationKind.Unknown;

        var trimmed = path.Trim();
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
            return ResourceLocationKind.NetworkOrRemovable;

        string? root;
        try
        {
            root = Path.GetPathRoot(trimmed);
        }
        catch (ArgumentException)
        {
            return ResourceLocationKind.Unknown;
        }

        if (string.IsNullOrEmpty(root))
            return ResourceLocationKind.Unknown;

        try
        {
            var driveType = new DriveInfo(root).DriveType;
            return driveType switch
            {
                DriveType.Fixed => ResourceLocationKind.LocalDisk,
                DriveType.Ram => ResourceLocationKind.LocalDisk,
                DriveType.Network => ResourceLocationKind.NetworkOrRemovable,
                DriveType.Removable => ResourceLocationKind.NetworkOrRemovable,
                DriveType.CDRom => ResourceLocationKind.NetworkOrRemovable,
                _ => ResourceLocationKind.Unknown
            };
        }
        catch
        {
            // Not a recognizable drive on this OS (e.g. a Windows drive letter evaluated on
            // Linux, or a malformed root) — we simply can't tell.
            return ResourceLocationKind.Unknown;
        }
    }

    /// <summary>Checks whether a FolderReference's directory currently exists.</summary>
    public static Task<AvailabilityCheckResult> CheckFolderAsync(
        string? path, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        CheckPathAsync(path, isFolder: true, timeout, cancellationToken);

    /// <summary>Checks whether a FileReference's file currently exists.</summary>
    public static Task<AvailabilityCheckResult> CheckFileAsync(
        string? path, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        CheckPathAsync(path, isFolder: false, timeout, cancellationToken);

    private static async Task<AvailabilityCheckResult> CheckPathAsync(
        string? path, bool isFolder, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var kind = ClassifyPath(path);
        if (string.IsNullOrWhiteSpace(path))
            return new AvailabilityCheckResult(AvailabilityStatus.Unavailable, kind, DateTime.UtcNow);

        // Directory.Exists/File.Exists on a dead network share can block far longer than a UI
        // should ever wait, so race it against a timeout rather than calling it inline.
        var checkTask = Task.Run(() => isFolder ? Directory.Exists(path) : File.Exists(path), cancellationToken);
        var winner = await Task.WhenAny(checkTask, Task.Delay(timeout ?? DefaultPathTimeout, cancellationToken));

        var exists = winner == checkTask && checkTask.Status == TaskStatus.RanToCompletion && checkTask.Result;
        return new AvailabilityCheckResult(
            exists ? AvailabilityStatus.Available : AvailabilityStatus.Unavailable, kind, DateTime.UtcNow);
    }

    /// <summary>
    /// Checks whether a WebResource's URL is currently reachable. Only an explicit HTTP error
    /// response (status &gt;= 400 — a 404, a 500, etc.) marks it <see cref="AvailabilityStatus.Unavailable"/>;
    /// a successful response (any status &lt; 400) marks it <see cref="AvailabilityStatus.Available"/>.
    /// A connection failure, DNS failure, or timeout proves nothing either way — it's just as
    /// likely a transient network blip, VPN hiccup, or a site that's slow/unusual about answering
    /// bots — so those are reported as <see cref="AvailabilityStatus.Unknown"/> rather than
    /// confidently flagging the link as broken.
    /// </summary>
    public static async Task<AvailabilityCheckResult> CheckWebResourceAsync(
        string? url, HttpClient httpClient, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return new AvailabilityCheckResult(AvailabilityStatus.Unavailable, ResourceLocationKind.Web, DateTime.UtcNow);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? DefaultWebTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var status = (int)response.StatusCode >= 400 ? AvailabilityStatus.Unavailable : AvailabilityStatus.Available;
            return new AvailabilityCheckResult(status, ResourceLocationKind.Web, DateTime.UtcNow);
        }
        catch
        {
            return new AvailabilityCheckResult(AvailabilityStatus.Unknown, ResourceLocationKind.Web, DateTime.UtcNow);
        }
    }
}
