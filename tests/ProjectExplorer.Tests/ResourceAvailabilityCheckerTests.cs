using System.Net;
using ProjectExplorer.Core.Models;
using ProjectExplorer.Core.Services;

namespace ProjectExplorer.Tests;

public class ResourceAvailabilityCheckerTests
{
    // ── ClassifyPath ──
    // Drive-letter classification (Fixed vs. Network vs. Removable) depends on DriveInfo, which
    // only resolves meaningfully on the OS that owns those paths — so only the OS-independent
    // string-based rules (UNC prefix, empty, relative) are asserted exactly here.

    [Theory]
    [InlineData(@"\\server\share\folder")]
    [InlineData(@"\\server\share")]
    [InlineData("//server/share/folder")]
    public void ClassifyPath_UncPath_IsNetworkOrRemovable(string path)
    {
        Assert.Equal(ResourceLocationKind.NetworkOrRemovable, ResourceAvailabilityChecker.ClassifyPath(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ClassifyPath_EmptyOrWhitespace_IsUnknown(string? path)
    {
        Assert.Equal(ResourceLocationKind.Unknown, ResourceAvailabilityChecker.ClassifyPath(path));
    }

    [Fact]
    public void ClassifyPath_RelativePath_IsUnknown()
    {
        Assert.Equal(ResourceLocationKind.Unknown, ResourceAvailabilityChecker.ClassifyPath("SomeFolder/Sub"));
    }

    [Fact]
    public void ClassifyPath_RealLocalDirectory_DoesNotThrow()
    {
        // Best-effort: on a real OS this should resolve to LocalDisk, but container/CI
        // filesystems can report unusual drive types — just verify it never throws.
        var kind = ResourceAvailabilityChecker.ClassifyPath(Path.GetTempPath());
        Assert.True(Enum.IsDefined(kind));
    }

    // ── CheckFolderAsync / CheckFileAsync ──

    [Fact]
    public async Task CheckFolderAsync_ExistingDirectory_IsAvailable()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ProjectExplorer_AvailTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var result = await ResourceAvailabilityChecker.CheckFolderAsync(dir);
            Assert.Equal(AvailabilityStatus.Available, result.Status);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public async Task CheckFolderAsync_MissingDirectory_IsUnavailable()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ProjectExplorer_DoesNotExist_{Guid.NewGuid():N}");
        var result = await ResourceAvailabilityChecker.CheckFolderAsync(dir);
        Assert.Equal(AvailabilityStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task CheckFolderAsync_EmptyPath_IsUnavailable()
    {
        var result = await ResourceAvailabilityChecker.CheckFolderAsync("");
        Assert.Equal(AvailabilityStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task CheckFileAsync_ExistingFile_IsAvailable()
    {
        var file = Path.Combine(Path.GetTempPath(), $"ProjectExplorer_AvailTest_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "hello");
        try
        {
            var result = await ResourceAvailabilityChecker.CheckFileAsync(file);
            Assert.Equal(AvailabilityStatus.Available, result.Status);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task CheckFileAsync_MissingFile_IsUnavailable()
    {
        var file = Path.Combine(Path.GetTempPath(), $"ProjectExplorer_DoesNotExist_{Guid.NewGuid():N}.txt");
        var result = await ResourceAvailabilityChecker.CheckFileAsync(file);
        Assert.Equal(AvailabilityStatus.Unavailable, result.Status);
    }

    // ── CheckWebResourceAsync ──

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond(request));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("simulated connection failure");
    }

    [Fact]
    public async Task CheckWebResourceAsync_SuccessResponse_IsAvailable()
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var result = await ResourceAvailabilityChecker.CheckWebResourceAsync("https://example.com", client);
        Assert.Equal(AvailabilityStatus.Available, result.Status);
        Assert.Equal(ResourceLocationKind.Web, result.LocationKind);
    }

    [Fact]
    public async Task CheckWebResourceAsync_ErrorStatusCode_IsUnavailable()
    {
        // An explicit HTTP error response (404/500/etc.) is the one confident signal that the
        // resource itself is actually broken, so this is the only case that should flag it.
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var result = await ResourceAvailabilityChecker.CheckWebResourceAsync("https://example.com/missing", client);
        Assert.Equal(AvailabilityStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task CheckWebResourceAsync_ConnectionFailure_IsUnknown()
    {
        // A connection/DNS failure or timeout proves nothing either way -- it's just as likely a
        // transient network blip as a genuinely dead link -- so it must NOT flag the resource as
        // broken (that would render active, working links as grey/strikethrough).
        using var client = new HttpClient(new ThrowingHandler());
        var result = await ResourceAvailabilityChecker.CheckWebResourceAsync("https://example.com", client);
        Assert.Equal(AvailabilityStatus.Unknown, result.Status);
    }

    [Fact]
    public async Task CheckWebResourceAsync_MalformedUrl_IsUnavailable()
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var result = await ResourceAvailabilityChecker.CheckWebResourceAsync("not a url", client);
        Assert.Equal(AvailabilityStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task CheckWebResourceAsync_NonHttpScheme_IsUnavailable()
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var result = await ResourceAvailabilityChecker.CheckWebResourceAsync("ftp://example.com/file", client);
        Assert.Equal(AvailabilityStatus.Unavailable, result.Status);
    }
}
