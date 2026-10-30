using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Daignostic.Core.Ai;

/// <summary>Which setting an AI problem points at, so the UI can show the message next to that field.</summary>
public enum AiErrorKind
{
    Other,
    Endpoint,
    Key,
    Model,

    /// <summary>The request ran out of time.</summary>
    Timeout,

    /// <summary>The AI answered, but not in a form this app can read.</summary>
    Reply,
}

public sealed class AiException(string message, Exception? inner = null, AiErrorKind kind = AiErrorKind.Other, string? rawReply = null) : Exception(message, inner)
{
    public AiErrorKind Kind { get; } = kind;

    /// <summary>What the AI actually said, kept so the user can see why it could not be read.</summary>
    public string? RawReply { get; } = rawReply;
}

/// <summary>Sends a system and user message to a language model and returns its text reply.</summary>
public interface IAiClient
{
    Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken);
}

public sealed record AiOptions(string Endpoint = "", string Model = "", double Temperature = 0.2);

/// <summary>
/// Client for any OpenAI-compatible endpoint (/v1/chat/completions and /v1/models), such as a self-hosted gateway.
/// It sends text only. The model gets no tools, so it cannot read or change anything on this PC.
/// The API key is read through a callback on every request and is never logged or included in errors.
/// </summary>
public sealed class OpenAiCompatibleClient(HttpClient http, AiOptions options, Func<string?> apiKey) : IAiClient
{
    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Model))
        {
            throw new AiException("No model name is set. Enter one in Settings (use List models to see what your endpoint offers).", kind: AiErrorKind.Model);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("chat/completions"))
        {
            Content = new StringContent(BuildBody(systemPrompt, userMessage), Encoding.UTF8, "application/json"),
        };

        var body = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return ExtractContent(body);
    }

    /// <summary>Asks the endpoint which models it offers (GET /v1/models). Works with most OpenAI-compatible servers.</summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri("models"));
        var body = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return ParseModelIds(body);
    }

    internal static IReadOnlyList<string> ParseModelIds(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var array = root.ValueKind == JsonValueKind.Array ? root
                : root.TryGetProperty("data", out var data) ? data
                : root.TryGetProperty("models", out var models) ? models
                : default;
            if (array.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return array.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()
                    : e.TryGetProperty("id", out var id) ? id.GetString()
                    : e.TryGetProperty("name", out var name) ? name.GetString() : null)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .Distinct()
                .Order()
                .ToList();
        }
        catch (JsonException ex)
        {
            throw new AiException("The endpoint returned a model list this app cannot read.", ex);
        }
    }

    /// <summary>
    /// Builds an API address from the user's endpoint. Providers document their base address both with and without
    /// "/v1" (for example http://host:8480 and http://host:8480/v1), so both are accepted and "/v1" is never doubled.
    /// </summary>
    internal static Uri BuildUri(string endpoint, string resource)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        var full = trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? $"{trimmed}/{resource}" : $"{trimmed}/v1/{resource}";
        if (!Uri.TryCreate(full, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new AiException("The AI endpoint is not a valid http(s) address. Check Settings.", kind: AiErrorKind.Endpoint);
        }

        return uri;
    }

    private Uri BuildUri(string resource) => BuildUri(options.Endpoint, resource);

    private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = apiKey();
        if (!string.IsNullOrWhiteSpace(key))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AiException($"Could not reach the AI endpoint at {options.Endpoint}. Is it running?", ex, AiErrorKind.Endpoint);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiException("The AI did not answer in time.", ex, AiErrorKind.Timeout);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var hint = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => " Check the API key in Settings.",
                    System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed => " Check the endpoint address and the model name. The address can be given with or without /v1.",
                    _ => "",
                };
                var kind = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => AiErrorKind.Key,
                    System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed => AiErrorKind.Model,
                    _ => AiErrorKind.Other,
                };
                throw new AiException($"The AI endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}.{hint}", kind: kind);
            }

            return body;
        }
    }

    internal string BuildBody(string systemPrompt, string userMessage)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("model", options.Model.Trim());
            w.WriteNumber("temperature", options.Temperature);
            w.WriteBoolean("stream", false);
            w.WriteStartArray("messages");
            WriteMessage(w, "system", systemPrompt);
            WriteMessage(w, "user", userMessage);
            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteMessage(Utf8JsonWriter w, string role, string content)
    {
        w.WriteStartObject();
        w.WriteString("role", role);
        w.WriteString("content", content);
        w.WriteEndObject();
    }

    internal static string ExtractContent(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
            var content = message.GetProperty("content").GetString();
            return string.IsNullOrWhiteSpace(content) ? throw new AiException("The AI returned an empty reply.", kind: AiErrorKind.Reply) : content;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new AiException("The AI endpoint returned a reply this app cannot read.", ex, AiErrorKind.Reply, responseBody);
        }
    }
}
