using System.Text;
using System.Text.Json;

namespace Daignostic.Core.Mcp;

public sealed class McpException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class McpToolNotAllowedException(string tool)
    : Exception($"The MCP tool '{tool}' is not on the read-only allow-list and was blocked.");

/// <summary>
/// Allow-list of CapFrameX MCP tools that only read data. CapFrameX also exposes tools that start captures
/// or change its settings; this app never calls them. Adding a tool here is a deliberate, reviewed decision.
/// </summary>
public static class McpToolPolicy
{
    private static readonly HashSet<string> ReadOnly = new(StringComparer.Ordinal)
    {
        "cfx_ping",
        "cfx_list_records",
        "cfx_search_records",
        "cfx_get_capture_status",
        "cfx_get_current_system",
    };

    public static bool IsAllowed(string tool) => ReadOnly.Contains(tool);

    public static IReadOnlyCollection<string> AllowedTools => ReadOnly;
}

public sealed record CaptureListing(string Id, string Game, string ProcessName, DateTimeOffset? RecordedAt, double DurationSec)
{
    public string FileName => Path.GetFileName(Id);

    public string RecordedAtText => RecordedAt?.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture) ?? "";
}

/// <summary>
/// Minimal MCP-over-HTTP client for CapFrameX's built-in server. It exists to find captures and show whether
/// CapFrameX is reachable. Calls are restricted by <see cref="McpToolPolicy"/>.
/// </summary>
public sealed class CapFrameXMcpClient(HttpClient http, Uri endpoint)
{
    private int _nextId;
    private string? _sessionId;
    private bool _initialized;

    public static Uri DefaultEndpoint { get; } = new("http://localhost:1337/mcp");

    public async Task<bool> PingAsync(CancellationToken ct)
    {
        try
        {
            await CallToolAsync("cfx_ping", "{}", ct).ConfigureAwait(false);
            return true;
        }
        catch (McpException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<CaptureListing>> ListRecordsAsync(int limit, CancellationToken ct)
    {
        var text = await CallToolAsync("cfx_list_records", $"{{\"limit\":{Math.Max(0, limit)}}}", ct).ConfigureAwait(false);
        return ParseListings(text);
    }

    public async Task<string> CallToolAsync(string tool, string argumentsJson, CancellationToken ct)
    {
        if (!McpToolPolicy.IsAllowed(tool))
        {
            throw new McpToolNotAllowedException(tool);
        }

        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        var id = Interlocked.Increment(ref _nextId);
        var request = $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"tools/call\",\"params\":{{\"name\":{JsonSerializer.Serialize(tool, CoreJsonContext.Default.String)},\"arguments\":{argumentsJson}}}}}";
        using var doc = await SendAsync(request, expectResponse: true, ct).ConfigureAwait(false);
        var root = doc!.RootElement;

        if (root.TryGetProperty("error", out var err))
        {
            throw new McpException("CapFrameX returned an error: " + (err.TryGetProperty("message", out var m) ? m.GetString() : "unknown"));
        }

        var result = root.GetProperty("result");
        if (result.TryGetProperty("isError", out var isErr) && isErr.ValueKind == JsonValueKind.True)
        {
            throw new McpException("The CapFrameX tool reported an error.");
        }

        var sb = new StringBuilder();
        if (result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var t) && t.GetString() is { } s)
                {
                    sb.Append(s);
                }
            }
        }

        return sb.ToString();
    }

    public static IReadOnlyList<CaptureListing> ParseListings(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var list = new List<CaptureListing>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var id = Str(el, "id");
                if (id.Length == 0)
                {
                    continue;
                }

                DateTimeOffset? at = DateTimeOffset.TryParse(Str(el, "recordedAt"), out var parsed) ? parsed : null;
                var duration = el.TryGetProperty("durationSec", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
                list.Add(new CaptureListing(id, Str(el, "game"), Str(el, "processName"), at, duration));
            }

            return list;
        }
        catch (JsonException ex)
        {
            throw new McpException("CapFrameX returned a capture list this app cannot read.", ex);
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }

        var id = Interlocked.Increment(ref _nextId);
        var init = $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"initialize\",\"params\":{{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{{}},\"clientInfo\":{{\"name\":\"daignostic\",\"version\":\"0.1\"}}}}}}";
        using var _ = await SendAsync(init, expectResponse: true, ct).ConfigureAwait(false);
        using var __ = await SendAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", expectResponse: false, ct).ConfigureAwait(false);
        _initialized = true;
    }

    private async Task<JsonDocument?> SendAsync(string body, bool expectResponse, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        if (_sessionId is not null)
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new McpException($"Could not reach CapFrameX at {endpoint}. Is it running with MCP enabled?", ex);
        }

        using (response)
        {
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var ids))
            {
                _sessionId = ids.FirstOrDefault();
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new McpException($"CapFrameX returned {(int)response.StatusCode}.");
            }

            if (!expectResponse)
            {
                return null;
            }

            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var json = mediaType == "text/event-stream" ? LastSseData(text) : text;
            try
            {
                return JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new McpException("CapFrameX did not answer with valid MCP JSON. Update CapFrameX and enable MCP.", ex);
            }
        }
    }

    private static string LastSseData(string sse)
    {
        string? last = null;
        foreach (var line in sse.Split('\n'))
        {
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                last = line[5..].Trim();
            }
        }

        return last ?? "";
    }

    private static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

