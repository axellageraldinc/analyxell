using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Daignostic.Core.Ai;

public sealed class AiException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Sends a system and user message to a language model and returns its text reply.</summary>
public interface IAiClient
{
    Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken);
}

public sealed record AiOptions(string Endpoint = "http://127.0.0.1:8480", string Preset = "text", double Temperature = 0.2);

/// <summary>
/// Client for any OpenAI-compatible /v1/chat/completions endpoint, such as a self-hosted xellter gateway.
/// It sends text only. The model gets no tools, so it cannot read or change anything on this PC.
/// The API key is read through a callback on every request and is never logged or included in errors.
/// </summary>
public sealed class OpenAiCompatibleClient(HttpClient http, AiOptions options, Func<string?> apiKey) : IAiClient
{
    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(options.Endpoint.TrimEnd('/') + "/v1/chat/completions", UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new AiException("The AI endpoint is not a valid http(s) URL. Check Settings.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(BuildBody(systemPrompt, userMessage), Encoding.UTF8, "application/json"),
        };

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
            throw new AiException($"Could not reach the AI endpoint at {options.Endpoint}. Is it running?", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiException("The AI request timed out.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var hint = response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                    ? " Check the API key in Settings."
                    : "";
                throw new AiException($"The AI endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}.{hint}");
            }

            return ExtractContent(body);
        }
    }

    internal string BuildBody(string systemPrompt, string userMessage)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("model", options.Preset);
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
            return string.IsNullOrWhiteSpace(content) ? throw new AiException("The AI returned an empty reply.") : content;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new AiException("The AI endpoint returned a reply this app cannot read.", ex);
        }
    }
}
