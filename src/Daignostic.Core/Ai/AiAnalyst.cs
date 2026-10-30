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
    string RawText,
    IReadOnlyList<string>? Unknowns = null,
    IReadOnlyList<string>? NextChecks = null,
    string? QuestionAnswer = null);

/// <summary>A read-only check or form daignostic can offer the user, so the AI never has to ask or reach into the PC.</summary>
public sealed record AiCheck(string Id, string Title, string Why);

public static class AiChecks
{
    public const string Background = "background_check";
    public const string PcSetup = "pc_setup";
    public const string GameSettings = "game_settings";

    /// <summary>The checks that would add something new for this capture. Ones already done are not offered again.</summary>
    public static IReadOnlyList<AiCheck> Available(AnalysisResult r)
    {
        var list = new List<AiCheck>();
        if (r.Background is null)
        {
            list.Add(new AiCheck(Background, "Check background programs (10 s)",
                "Shows which other programs use CPU, disk and GPU, and whether known troublemakers are running."));
        }

        if (r.System.Items.Count(i => i.Key.StartsWith("setup.", StringComparison.Ordinal)) < 3)
        {
            list.Add(new AiCheck(PcSetup, "Add your PC setup",
                "Tell daignostic what a capture cannot show: G-SYNC or FreeSync, VSync, frame cap, monitor and where the game is installed."));
        }

        if (r.Settings is null)
        {
            list.Add(new AiCheck(GameSettings, "Add the game's settings",
                "Graphics settings such as ray tracing and upscaling are not in a capture."));
        }

        return list;
    }
}

public static class AiPrompts
{
    public const string System = """
        You are a PC game performance diagnostician. You receive JSON produced by software that already measured a gameplay capture.
        Rules:
        1. Use ONLY the facts and findings in the input. Never invent numbers, settings, hardware or causes.
        2. Every claim must cite evidence: fact keys from "facts" (for example "fps.p1"), or "finding:<ID>" for a finding id.
        3. Separate measured results from hypotheses. Never present a hypothesis as proven. Suggest one change to test at a time.
        4. If the data cannot support a conclusion, say so and set "inconclusive" to true.
        5. Each finding already carries "suggestions" written by the app. Build on them: put the most valuable first, explain why for THIS data, and drop ones that clearly do not apply. You may add a suggestion only if it is an in-game setting or a simple, reversible check. Never suggest commands, registry edits, BIOS changes, downloads or deleting files.
        6. Tell the user how to check the result: change one thing, record again, compare.
        7. Be concise. Plain language.
        8. This is a single question and a single answer, not a conversation. NEVER ask the user a question and never end by asking for more information: nobody can answer you. You cannot look anything up and you have no access to the user's PC; you only see this JSON.
        9. When something you need is missing, do not ask for it. Work with what is there: state the assumption you made, give guidance that holds either way ("if X, then Y; otherwise Z"), and list what you could not determine in "unknowns" as plain statements, for example "Which drive the game is installed on" or "Whether VSync was on".
        10. If the input has "available_checks", you may name up to 3 of its ids in "next_checks": the ones whose result would most improve your answer. The app can run them for the user. You cannot run them, and naming them is not a question to the user. Use only ids from that list, and leave "next_checks" empty when none would help.
        11. How to read this data. The findings come from fixed rules in the app, so trust them over your own guess about what was measured. A capture cannot show: why a spike happened (the sensors around it only narrow it down), actual tearing (only whether it was allowed), VSync forced in the GPU driver (the capture shows what the game asked for, so a "setup.vsync" fact outranks "present.sync_interval_zero_pct"), network lag, crashes or driver quality. Settings the user typed in ("setup.*" facts and settings entered for the game) are the user's own statements: use them as given, and do not list one as unknown just because a capture cannot confirm it. Findings marked as a likely explanation are hypotheses: keep them as hypotheses. A CPU limit means the GPU waits for the processor, so lowering resolution or GPU settings will not raise the frame rate. Hitches that fade as the run goes on point to shader compilation or loading, which a second run will confirm. Never claim a fix is certain: say what to change, then to record again and compare.
        12. Drive activity facts name the monitoring software's drives: "nvme/N" is an NVMe SSD and "hdd/N" is a SATA or other drive (a hard disk or SATA SSD); N is its order in that software, not a Windows disk number, so do not guess which folder or game it holds. Compare with "system.game_drive.*" facts when present. A busy drive at the moment of a freeze is a lead, not proof.
        13. If "user_note" contains "Answers to your open questions", those are the user's own statements about their PC. Use them as given (they are not verified), do not list those questions as unknown again, and say how they change your conclusion.
        14. "user_note" can also hold a question from the user ("Question from the user"), and your own earlier answer ("Your previous answer"). Answer the question directly and plainly in "answer_to_question", using only the facts and findings. Your previous answer is not evidence and may be wrong: re-check it against the facts, and say so if you now think differently. This is still one answer; never ask the user anything back.
        15. Facts starting with "game." (for example game.upscaling, game.frame_generation) come from a community-written wiki and say which options the game OFFERS, not which are switched on. Use them to name the possible options and to know what to look for; never claim one is enabled unless a "settings.*" or "setup.*" fact says so.
        Reply with ONLY a JSON object, no markdown, in exactly this shape:
        {"summary":"one or two sentences","inconclusive":false,"findings":[{"title":"short","explanation":"what it means","evidence":["fps.p1","finding:FREEZES"],"actions":["one concrete thing to try"],"confidence":"low|medium|high"}],"unknowns":["what could not be determined from this data"],"next_checks":["background_check"],"answer_to_question":"only when the user asked a question: a direct answer, otherwise an empty string"}
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
            var checks = AiChecks.Available(result);
            if (checks.Count > 0)
            {
                w.WriteStartArray("available_checks");
                foreach (var c in checks)
                {
                    w.WriteStartObject();
                    w.WriteString("id", c.Id);
                    w.WriteString("what", c.Why);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

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
        [.. result.Facts.Keys, .. result.Findings.Where(f => !f.LocalOnly).Select(f => "finding:" + f.Id)];

    public static HashSet<string> ValidKeys(ComparisonResult comparison, AnalysisResult a, AnalysisResult b) =>
        [
            .. comparison.Facts.Keys,
            .. a.Findings.Where(f => !f.LocalOnly).Select(f => "finding:A_" + f.Id),
            .. b.Findings.Where(f => !f.LocalOnly).Select(f => "finding:B_" + f.Id),
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
        // Local-only findings mention things like program names; their anonymous numbers travel as facts instead.
        foreach (var f in findings.Where(f => !f.LocalOnly))
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
            w.WriteStartArray("suggestions");
            foreach (var s in f.Suggestions)
            {
                w.WriteStringValue(s);
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
        w.WriteString("user_note", trimmed.Length > 4000 ? trimmed[..4000] : trimmed);
    }
}

public static class AiResponseParser
{
    /// <summary>
    /// Parses the model reply and drops any item that cites no known evidence key. Items are never trusted
    /// just because they are well formed: a claim with no traceable source is discarded.
    /// </summary>
    public static AiAnalysis Parse(string text, ISet<string> validKeys, ISet<string>? validChecks = null)
    {
        var json = ExtractJsonObject(text) ?? throw new AiException("The AI reply did not contain the structured answer this app needs.", kind: AiErrorKind.Reply, rawReply: text);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new AiException("The AI reply was not valid JSON.", ex, AiErrorKind.Reply, text);
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

            var unknowns = Strings(root, "unknowns").Take(6).ToList();
            var checks = validChecks is null ? [] : Strings(root, "next_checks").Where(validChecks.Contains).Distinct().Take(3).ToList();
            var questionAnswer = Str(root, "answer_to_question").Trim();
            return new AiAnalysis(summary, inconclusive || items.Count == 0, items, dropped, text, unknowns, checks, questionAnswer.Length == 0 ? null : questionAnswer);
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
        return AiResponseParser.Parse(reply, AiPrompts.ValidKeys(result), AiChecks.Available(result).Select(c => c.Id).ToHashSet());
    }

    public async Task<AiAnalysis> CompareAsync(
        ComparisonResult comparison, AnalysisResult a, AnalysisResult b, string? userNote, CancellationToken ct)
    {
        var reply = await client.CompleteAsync(
            AiPrompts.System, AiPrompts.BuildComparison(comparison, a, b, userNote), ct).ConfigureAwait(false);
        return AiResponseParser.Parse(reply, AiPrompts.ValidKeys(comparison, a, b));
    }
}
