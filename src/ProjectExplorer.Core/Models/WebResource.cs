using System.Diagnostics.CodeAnalysis;

namespace ProjectExplorer.Core.Models;

/// <summary>
/// A reference to a web resource (URL). Can be documentation, wikis, staging environments,
/// design mockups, project boards, or any other web-based resource related to the project.
/// </summary>
public class WebResource : ProjectChild
{
    public override ChildType Type => ChildType.WebResource;

    /// <summary>
    /// The URL to launch when this resource is activated.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Optional description explaining what this resource is and when to use it.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Returns the effective display name: DisplayName override if set,
    /// otherwise extracts a reasonable name from the URL.
    /// </summary>
    public string EffectiveName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisplayName))
                return DisplayName;

            if (!string.IsNullOrWhiteSpace(Url))
            {
                // Try to extract something meaningful from URL
                // e.g., "https://docs.microsoft.com/aspnet" -> "docs.microsoft.com"
                if (Uri.TryCreate(Url, UriKind.Absolute, out var uri))
                {
                    return uri.Host;
                }
                return Url;
            }

            return "(unknown web resource)";
        }
    }

    /// <summary>
    /// Resolves a stored URL to a navigable absolute http(s) <see cref="Uri"/>, treating a URL typed
    /// without a scheme (e.g. "example.com") as "https://example.com" rather than failing to parse.
    /// Used both to launch a resource in the external browser and to check its availability, so a
    /// scheme-less URL neither opens as a local file path nor gets flagged broken for that reason alone.
    /// </summary>
    public static bool TryGetNavigableUri(string? url, [NotNullWhen(true)] out Uri? uri)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            uri = null;
            return false;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out uri))
        {
            // Already has an explicit scheme. Only http(s) is navigable in a browser -- anything
            // else (ftp://, mailto:, etc.) is left unresolved rather than guessing at intent by
            // prepending "https://" onto an already-schemed string.
            if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                return true;

            uri = null;
            return false;
        }

        // No scheme at all (e.g. "example.com") -- assume https, the common case for a bare domain.
        return Uri.TryCreate("https://" + url, UriKind.Absolute, out uri);
    }
}
