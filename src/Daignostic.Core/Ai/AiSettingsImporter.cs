using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Daignostic.Core.Config;

namespace Daignostic.Core.Ai;

public sealed record RedactedText(string Text, int LinesRemoved, int PathsAnonymised, bool Truncated);

public sealed record ImportResult(IReadOnlyList<ProfileItem> Accepted, int Dropped);

/// <summary>
/// Lets the user's own AI read a game's config file and pull out the graphics settings, so any game can be supported
/// without a hand-written reader. The AI's answer is not trusted: every key and value it returns must literally
/// appear in the file, otherwise it is discarded. Likely secrets and user names are removed before anything is sent,
/// and the user sees the exact text before it goes.
/// </summary>
public static partial class AiSettingsImporter
{
    public const int MaxChars = 48_000;

    public const string System = """
        You extract graphics and performance related settings from a game's configuration file.
        Rules:
        1. Use ONLY text that appears in the file. Never invent, infer or normalise settings.
        2. Include only settings that plausibly affect picture quality or performance: resolution, display or window mode, vsync, frame rate limit, upscaling (DLSS, FSR, XeSS), frame generation, ray tracing, quality levels, textures, shadows, effects, anti-aliasing, view distance, post-processing and similar.
        3. Skip audio, input, language, interface, accounts, paths and telemetry.
        4. For each setting return the key exactly as written in the file and the value exactly as written.
        5. Do not explain what a value means. If unsure whether a setting matters, leave it out.
        6. Do not ask questions. This is one request and one answer; if the file is unclear, return fewer settings.
        Reply with ONLY a JSON object, no markdown: {"settings":[{"key":"...","value":"..."}]}
        """;

    /// <summary>Removes lines that look like secrets and anonymises user profile paths.</summary>
    public static RedactedText Redact(string text)
    {
        var kept = new StringBuilder();
        var removed = 0;
        var anonymised = 0;
        var truncated = false;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (SecretLine().IsMatch(raw) || OpaqueToken().IsMatch(raw))
            {
                removed++;
                continue;
            }

            var line = UserPath().Replace(raw, m => { anonymised++; return m.Groups[1].Value + "<user>"; });
            if (kept.Length + line.Length + 1 > MaxChars)
            {
                truncated = true;
                break;
            }

            kept.Append(line).Append('\n');
        }

        return new RedactedText(kept.ToString(), removed, anonymised, truncated);
    }

    public static string BuildUserMessage(string gameName, string redactedText)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("game", gameName);
            w.WriteString("config_file_text", redactedText);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Parses the reply and keeps only settings whose key and value literally occur in the file text.</summary>
    public static ImportResult ParseReply(string reply, string fileText)
    {
        var json = AiResponseParser.ExtractJsonObject(reply) ?? throw new AiException("The AI reply did not contain a JSON object.");
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new AiException("The AI reply was not valid JSON.", ex);
        }

        using (doc)
        {
            var accepted = new List<ProfileItem>();
            var dropped = 0;
            if (doc.RootElement.TryGetProperty("settings", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray().Take(400))
                {
                    var key = el.ValueKind == JsonValueKind.Object && el.TryGetProperty("key", out var k) ? k.ToString() : "";
                    var value = el.ValueKind == JsonValueKind.Object && el.TryGetProperty("value", out var v) ? v.ToString() : "";
                    var grounded = key.Length > 0
                        && fileText.Contains(key, StringComparison.OrdinalIgnoreCase)
                        && (value.Length == 0 || fileText.Contains(value, StringComparison.OrdinalIgnoreCase));
                    if (grounded && !accepted.Any(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                    {
                        accepted.Add(new ProfileItem(key, value));
                    }
                    else
                    {
                        dropped++;
                    }
                }
            }

            return new ImportResult(accepted, dropped);
        }
    }

    public static async Task<ImportResult> ImportAsync(IAiClient client, string gameName, RedactedText file, CancellationToken ct)
    {
        var reply = await client.CompleteAsync(System, BuildUserMessage(gameName, file.Text), ct).ConfigureAwait(false);
        return ParseReply(reply, file.Text);
    }

    [GeneratedRegex(@"(?i)(token|passw|secret|api[_-]?key|auth|cookie|session|e-?mail|account|steam_?id|user_?id|license|serial|credential)")]
    private static partial Regex SecretLine();

    [GeneratedRegex(@"[A-Za-z0-9+/=_-]{40,}")]
    private static partial Regex OpaqueToken();

    [GeneratedRegex(@"(?i)([A-Z]:\\Users\\)[^\\""'\r\n/]+")]
    private static partial Regex UserPath();
}
