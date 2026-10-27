using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace OpenCodeVision.Core;

public sealed record ServerInfo(string Version, int Pid);

public sealed record ServerEvent(string Type, DateTimeOffset Created, string? SessionId);

public sealed class OpenCodeException : Exception
{
    public OpenCodeException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

public interface IOpenCodeClient : IDisposable
{
    Task<ServerInfo> GetInfoAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionInfo>> GetSessionsAsync(CancellationToken cancellationToken);

    Task<int> GetPendingPermissionCountAsync(string sessionId, CancellationToken cancellationToken);

    IAsyncEnumerable<ServerEvent> SubscribeAsync(CancellationToken cancellationToken);
}

public sealed class OpenCodeClient : IOpenCodeClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public OpenCodeClient(Uri baseUrl, string password, HttpMessageHandler? handler = null)
    {
        _baseUrl = baseUrl.ToString().TrimEnd('/');
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"opencode:{password}")));
    }

    public async Task<ServerInfo> GetInfoAsync(CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("/api/info", cancellationToken);
        var root = document.RootElement;
        return new ServerInfo(Str(root, "version") ?? "unknown", (int)(Long(root, "pid") ?? 0));
    }

    public async Task<IReadOnlyList<SessionInfo>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("/api/session", cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SessionInfo>();
        }

        var sessions = new List<SessionInfo>();
        foreach (var element in data.EnumerateArray())
        {
            var id = Str(element, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var time = element.TryGetProperty("time", out var timeElement) ? timeElement : default;
            var location = element.TryGetProperty("location", out var locationElement) ? locationElement : default;

            sessions.Add(new SessionInfo
            {
                Id = id,
                ParentId = Str(element, "parentID"),
                Title = Str(element, "title") ?? "",
                Directory = location.ValueKind == JsonValueKind.Object ? Str(location, "directory") ?? "" : "",
                Outcome = Str(element, "outcome"),
                Created = Time(time, "created"),
                Updated = Time(time, "updated"),
                Idle = Time(time, "idle"),
                Viewed = Time(time, "viewed"),
            });
        }

        return sessions;
    }

    public async Task<int> GetPendingPermissionCountAsync(string sessionId, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            $"/api/session/{Uri.EscapeDataString(sessionId)}/permission",
            cancellationToken);

        return document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? data.GetArrayLength()
            : 0;
    }

    public async IAsyncEnumerable<ServerEvent> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/api/event");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        await foreach (var payload in SseParser.ReadDataAsync(reader, cancellationToken))
        {
            ServerEvent? serverEvent = null;
            try
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var type = Str(root, "type");
                if (string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }

                string? sessionId = null;
                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    sessionId = Str(data, "sessionID");
                }

                var created = Long(root, "created") is { } milliseconds
                    ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
                    : DateTimeOffset.UtcNow;

                serverEvent = new ServerEvent(type, created, sessionId);
            }
            catch (JsonException)
            {
                // A malformed frame must not kill the stream.
            }

            if (serverEvent is not null)
            {
                yield return serverEvent;
            }
        }
    }

    public void Dispose() => _http.Dispose();

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, timeout.Token);
        await EnsureSuccessAsync(response, timeout.Token);

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        return await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 300)
        {
            body = body[..300];
        }

        throw new OpenCodeException((int)response.StatusCode, $"HTTP {(int)response.StatusCode}: {body}");
    }

    private static string? Str(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? Long(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
            ? number
            : null;

    private static DateTimeOffset? Time(JsonElement element, string name)
        => Long(element, name) is { } milliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : null;
}
