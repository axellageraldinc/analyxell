using System.Text;
using System.Text.Json;
using Daignostic.Core.Analysis;

namespace Daignostic.Core.Ai;

public sealed record AiItem(
    string Title,
    string Explanation,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Actions,
    string Confidence);

public sealed record AiAnalysis(
    string Summary,
    bool Inconclusive,
    IReadOnlyList<AiItem> Items,
    int DroppedUncited,
    string RawText);

public static class AiPrompts
{
    public const string System = """
        You are a PC game performance diagnostician. You receive JSON produced by software that already measured a gameplay capture.
        Rules:
        1. Use ONLY the facts and findings in the input. Never invent numbers, settings, hardware or causes.
        2. Every claim must cite evidence: fact keys from "facts" (for example "fps.p1"), or "finding:<ID>" for a finding id.
        3. Separate measured results from hypotheses. Never present a hypothesis as proven. Suggest one change to test at a time.
        4. If the data cannot support a conclusion, say so and set "inconclusive" to true.
        5. Suggest only in-game settings or simple checks. Never suggest commands, registry edits, BIOS changes, downloads or deleting files.
        6. Be concise. Plain language.
        Reply with ONLY a JSON object, no markdown, in exactly this shape:
        {"summary":"one or two sentences","inconclusive":false,"findings":[{"title":"short","explanation":"what it means","evidence":["fps.p1","finding:FREEZES"],"actions":["one concrete thing to try"],"confidence":"low|medium|high"}]}
        """;

    /// <summary>The exact user message sent to the model for a single capture. The UI shows this before sending.</summary>
    public static string BuildSingle(AnalysisResult result, string? userNote)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("task", "analyze one capture");
            WriteFacts(w, result.Facts);
            WriteFindings(w, result.Findings, prefix: "");
            WriteNote(w, userNote);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string BuildComparison(ComparisonResult comparison, AnalysisResult a, AnalysisResult b, string? userNote)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("task", "compare two captures of the same game. B is the later run; A is the baseline");
            WriteFacts(w, comparison.Facts);
            w.WriteStartArray("findings");
            WriteFindingItems(w, a.Findings, "A_");
            WriteFindingItems(w, b.Findings, "B_");
            w.WriteEndArray();
            WriteNote(w, userNote);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Evidence keys the model is allowed to cite for a single capture.</summary>
    public static HashSet<string> ValidKeys(AnalysisResult result) =>
        [.. result.Facts.Keys, .. result.Findings.Select(f => "finding:" + f.Id)];

    public static HashSet<string> ValidKeys(ComparisonResult comparison, AnalysisResult a, AnalysisResult b) =>
        [
            .. comparison.Facts.Keys,
            .. a.Findings.Select(f => "finding:A_" + f.Id),
            .. b.Findings.Select(f => "finding:B_" + f.Id),
        ];

    private static void WriteFacts(Utf8JsonWriter w, IReadOnlyDictionary<string, string> facts)
    {
        w.WriteStartObject("facts");
        foreach (var (key, value) in facts)
        {
            w.WriteString(key, value);
        }

        w.WriteEndObject();
    }

    private static void WriteFindings(Utf8JsonWriter w, IReadOnlyList<Finding> findings, string prefix)
    {
        w.WriteStartArray("findings");
        WriteFindingItems(w, findings, prefix);
        w.WriteEndArray();
    }

    private static void WriteFindingItems(Utf8JsonWriter w, IReadOnlyList<Finding> findings, string prefix)
    {
        foreach (var f in findings)
        {
            w.WriteStartObject();
            w.WriteString("id", prefix + f.Id);
            w.WriteString("severity", f.Severity.ToString());
            w.WriteString("title", f.Title);
            w.WriteString("detail", f.Detail);
            w.WriteStartArray("evidence");
            foreach (var e in f.Evidence)
            {
                w.WriteStringValue(e);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }
    }

    private static void WriteNote(Utf8JsonWriter w, string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return;
        }

        var trimmed = note.Trim();
        w.WriteString("user_note", trimmed.Length > 600 ? trimmed[..600] : trimmed);
    }
}

public static class AiResponseParser
{
    /// <summary>
    /// Parses the model reply and drops any item that cites no known evidence key. Items are never trusted
    /// just because they are well formed: a claim with no traceable source is discarded.
    /// </summary>
    public static AiAnalysis Parse(string text, ISet<string> validKeys)
    {
        var json = ExtractJsonObject(text) ?? throw new AiException("The AI reply did not contain a JSON object.");

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
            var root = doc.RootElement;
            var summary = Str(root, "summary");
            var inconclusive = root.TryGetProperty("inconclusive", out var inc) && inc.ValueKind == JsonValueKind.True;
            var items = new List<AiItem>();
            var dropped = 0;

            if (root.TryGetProperty("findings", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var evidence = Strings(el, "evidence").Where(validKeys.Contains).Distinct().ToList();
                    if (evidence.Count == 0)
                    {
                        dropped++;
                        continue;
                    }

                    items.Add(new AiItem(
                        Str(el, "title"),
                        Str(el, "explanation"),
                        evidence,
                        Strings(el, "actions").ToList(),
                        Str(el, "confidence")));
                }
            }

            return new AiAnalysis(summary, inconclusive || items.Count == 0, items, dropped, text);
        }
    }

    internal static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    private static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static IEnumerable<string> Strings(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
            {
                yield return s;
            }
        }
    }
}

public sealed class AiAnalyst(IAiClient client)
{
    public async Task<AiAnalysis> AnalyzeAsync(AnalysisResult result, string? userNote, CancellationToken ct)
    {
        var reply = await client.CompleteAsync(AiPrompts.System, AiPrompts.BuildSingle(result, userNote), ct).ConfigureAwait(false);
        return AiResponseParser.Parse(reply, AiPrompts.ValidKeys(result));
    }

    public async Task<AiAnalysis> CompareAsync(
        ComparisonResult comparison, AnalysisResult a, AnalysisResult b, string? userNote, CancellationToken ct)
    {
        var reply = await client.CompleteAsync(
            AiPrompts.System, AiPrompts.BuildComparison(comparison, a, b, userNote), ct).ConfigureAwait(false);
        return AiResponseParser.Parse(reply, AiPrompts.ValidKeys(comparison, a, b));
    }
}
