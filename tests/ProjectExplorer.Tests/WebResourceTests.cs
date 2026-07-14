using ProjectExplorer.Core.Models;

namespace ProjectExplorer.Tests;

public class WebResourceTests
{
    [Theory]
    [InlineData("https://example.com", "https", "example.com")]
    [InlineData("http://example.com/path", "http", "example.com")]
    [InlineData("example.com", "https", "example.com")]
    [InlineData("www.example.com/docs", "https", "www.example.com")]
    public void TryGetNavigableUri_ValidHost_ResolvesToHttpsWhenSchemeMissing(string url, string expectedScheme, string expectedHost)
    {
        var resolved = WebResource.TryGetNavigableUri(url, out var uri);

        Assert.True(resolved);
        Assert.Equal(expectedScheme, uri!.Scheme);
        Assert.Equal(expectedHost, uri.Host);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    public void TryGetNavigableUri_EmptyOrUnparseable_ReturnsFalse(string? url)
    {
        var resolved = WebResource.TryGetNavigableUri(url, out var uri);

        Assert.False(resolved);
        Assert.Null(uri);
    }

    [Fact]
    public void TryGetNavigableUri_NonHttpScheme_ReturnsFalse()
    {
        // Already has an explicit scheme that isn't http(s) -- left unresolved rather than
        // guessing at intent by prepending "https://" onto an already-schemed string.
        var resolved = WebResource.TryGetNavigableUri("ftp://example.com/file", out var uri);

        Assert.False(resolved);
        Assert.Null(uri);
    }
}
