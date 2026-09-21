using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ProjectExplorer.Core.Sharing;

/// <summary>
/// Talks to the phase-1 sharing server. The code in a <see cref="PublishedShare"/>
/// is the only credential: anyone who has it can preview and fetch that egg.
/// </summary>
public sealed class NestShareClient
{
    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _http;

    public NestShareClient(HttpClient httpClient)
    {
        _http = httpClient;
    }

    public static NestShareClient CreateDefault() => new(Shared);

    public static bool TryParseServer(string? text, out Uri server)
    {
        server = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        server = uri;
        return true;
    }

    public async Task<PublishedShare> PublishAsync(Uri server, NestEggDocument egg, string machineLabel, CancellationToken cancellationToken = default)
    {
        var label = NestEggCodec.RequireMachineLabel(machineLabel);
        egg.Source.MachineLabel = label;
        NestEggCodec.Validate(egg);
        var response = await SendAsync(HttpMethod.Post, Endpoint(server, "api/shares"), new PublishShareRequest
        {
            MachineLabel = label,
            Egg = egg
        }, cancellationToken);
        return await ReadAsync<PublishedShare>(response, cancellationToken);
    }

    public async Task<SharePreview> PreviewAsync(Uri server, string code, string? machineLabel, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, ShareUri(server, code, machineLabel), null, cancellationToken);
        return await ReadAsync<SharePreview>(response, cancellationToken);
    }

    public async Task<NestEggDocument> FetchAsync(Uri server, string code, string? machineLabel, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, ShareUri(server, code, machineLabel, "/egg"), null, cancellationToken);
        using (response)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var egg = NestEggCodec.Parse(json);
            if (response.Headers.TryGetValues("X-Payload-Sha256", out var hashes))
            {
                var expected = hashes.FirstOrDefault();
                var actual = NestEggCodec.Sha256Hex(json);
                if (!string.IsNullOrEmpty(expected) && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    throw new NestShareException("The project arrived changed in transit. Try receiving it again.");
            }
            return egg;
        }
    }

    public async Task ReportImportedAsync(Uri server, string code, string machineLabel, string detail, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Post, Endpoint(server, "api/shares/" + Uri.EscapeDataString(Normalize(code)) + "/events"),
            new ShareEventRequest
            {
                EventType = ShareEventTypes.Imported,
                MachineLabel = machineLabel,
                Detail = detail
            }, cancellationToken);
        response.Dispose();
    }

    public async Task<IReadOnlyList<ShareEventRecord>> GetEventsAsync(Uri server, string code, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, Endpoint(server, "api/shares/" + Uri.EscapeDataString(Normalize(code)) + "/events"), null, cancellationToken);
        var events = await response.Content.ReadFromJsonAsync<List<ShareEventRecord>>(NestEggCodec.JsonOptions, cancellationToken);
        return events ?? [];
    }

    public async Task RevokeAsync(Uri server, string code, string? machineLabel, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Delete, ShareUri(server, code, machineLabel), null, cancellationToken);
        response.Dispose();
    }

    public async Task PingAsync(Uri server, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, Endpoint(server, "api/health"), null, cancellationToken);
        response.Dispose();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, object? body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            if (body != null)
            {
                var json = JsonSerializer.Serialize(body, NestEggCodec.JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var message = await ReadErrorAsync(response, cancellationToken);
                response.Dispose();
                throw new NestShareException(message);
            }
            return response;
        }
        catch (NestShareException)
        {
            throw;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NestShareException("The sharing server didn't answer in time. Check the address and that the server is running.");
        }
        catch (HttpRequestException)
        {
            throw new NestShareException($"Couldn't reach the sharing server at {uri.GetLeftPart(UriPartial.Authority)}. Check the address and that the server is running.");
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using (response)
        {
            var value = await response.Content.ReadFromJsonAsync<T>(NestEggCodec.JsonOptions, cancellationToken);
            if (value == null)
                throw new NestShareException("The sharing server returned an empty response.");
            return value;
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ShareApiError>(NestEggCodec.JsonOptions, cancellationToken);
            if (!string.IsNullOrWhiteSpace(error?.Error))
                return error.Error;
        }
        catch (JsonException) { /* fall through to a status-based message */ }

        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.NotFound => "No share matches that code.",
            System.Net.HttpStatusCode.Gone => "That share is no longer available.",
            _ => "The sharing server rejected the request."
        };
    }

    private static Uri ShareUri(Uri server, string code, string? machineLabel, string suffix = "")
    {
        var path = "api/shares/" + Uri.EscapeDataString(Normalize(code)) + suffix;
        if (!string.IsNullOrWhiteSpace(machineLabel))
            path += "?machine=" + Uri.EscapeDataString(machineLabel.Trim());
        return Endpoint(server, path);
    }

    private static Uri Endpoint(Uri server, string relative)
    {
        var baseUri = new Uri(server.ToString().TrimEnd('/') + "/");
        return new Uri(baseUri, relative);
    }

    private static string Normalize(string code) =>
        new string((code ?? "").Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
}

public sealed class NestShareException : Exception
{
    public NestShareException(string message) : base(message) { }
}
