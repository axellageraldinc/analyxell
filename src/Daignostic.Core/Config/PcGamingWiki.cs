using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Daignostic.Core.Config;

/// <summary>What PCGamingWiki says about one game that helps daignostic: where its settings file is and what it can do.</summary>
public sealed record WikiGameInfo(
    string Title,
    IReadOnlyList<string> ConfigPaths,
    IReadOnlyList<ProfileItem> Features)
{
    public string PageUrl => "https://www.pcgamingwiki.com/wiki/" + Uri.EscapeDataString(Title.Replace(' ', '_')).Replace("%3A", ":");
}

/// <summary>
/// Reads the wikitext of a PCGamingWiki page. The wiki is written by volunteers, so this is treated as a lead, never as
/// proof of what is switched on in the player's copy of the game. It says which options exist, not which one is set.
/// </summary>
public static partial class PcGamingWikiParser
{
    private static readonly (string Key, string Field)[] FeatureFields =
    [
        ("upscaling", "upscaling tech"),
        ("upscaling_notes", "upscaling notes"),
        ("frame_generation", "framegen tech"),
        ("frame_generation_notes", "framegen notes"),
        ("vsync_modes", "vsync notes"),
        ("fps_cap", "120 fps notes"),
        ("ray_tracing", "ray tracing notes"),
        ("anti_aliasing", "antialiasing notes"),
        ("hdr", "hdr notes"),
        ("windowed", "windowed notes"),
        ("borderless", "borderless windowed notes"),
    ];

    public static WikiGameInfo? Parse(string title, string wikitext)
    {
        if (string.IsNullOrWhiteSpace(wikitext))
        {
            return null;
        }

        var paths = new List<string>();
        foreach (Match m in ConfigLine().Matches(wikitext))
        {
            if (m.Groups["os"].Value.Trim().Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                var path = Clean(PathToken().Replace(m.Groups["path"].Value, x => Placeholder(x.Groups[1].Value)).Split('|')[0]);
                if (path.Length > 0)
                {
                    paths.Add(path);
                }
            }
        }

        var features = new List<ProfileItem>();
        var block = VideoBlock(wikitext);
        if (block is not null)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in FieldLine().Matches(block))
            {
                fields[m.Groups["key"].Value.Trim()] = m.Groups["value"].Value.Trim();
            }

            foreach (var (key, field) in FeatureFields)
            {
                if (fields.TryGetValue(field, out var raw) && Clean(raw) is { Length: > 0 } text)
                {
                    features.Add(new ProfileItem(key, text.Length > 400 ? text[..400] + "..." : text));
                }
            }
        }

        return paths.Count == 0 && features.Count == 0 ? null : new WikiGameInfo(title, paths, features);
    }

    private static string? VideoBlock(string wikitext)
    {
        var start = wikitext.IndexOf("{{Video", StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var end = wikitext.IndexOf("\n}}", start, StringComparison.Ordinal);
        return end < 0 ? wikitext[start..] : wikitext[start..end];
    }

    /// <summary>Turns wiki markup into plain text and the wiki's folder placeholders into Windows ones.</summary>
    public static string Clean(string raw)
    {
        var s = raw;
        s = RefTag().Replace(s, "");
        s = SelfClosingRef().Replace(s, "");
        s = PathToken().Replace(s, m => Placeholder(m.Groups[1].Value));
        for (var i = 0; i < 4 && s.Contains("{{", StringComparison.Ordinal); i++)
        {
            s = SimpleTemplate().Replace(s, m => m.Groups[1].Value.StartsWith("term|", StringComparison.OrdinalIgnoreCase) ? m.Groups[1].Value[5..] : "");
        }

        s = PipedLink().Replace(s, "$2");
        s = PlainLink().Replace(s, "$1");
        s = ExternalLink().Replace(s, "$1");
        s = Breaks().Replace(s, " ");
        s = s.Replace("'''", "", StringComparison.Ordinal).Replace("''", "", StringComparison.Ordinal).Replace("&nbsp;", " ", StringComparison.Ordinal);
        return Whitespace().Replace(s, " ").Trim();
    }

    private static string Placeholder(string name) => name.Trim().ToLowerInvariant() switch
    {
        "localappdata" => "%LOCALAPPDATA%",
        "appdata" => "%APPDATA%",
        "programdata" => "%PROGRAMDATA%",
        "userprofile" => "%USERPROFILE%",
        "userprofile\\documents" or "userprofile\\\\documents" => "%USERPROFILE%\\Documents",
        "game" => "<game folder>",
        "steam" => "<steam folder>",
        "uid" => "*",
        var other => "<" + other + ">",
    };

    [GeneratedRegex(@"\{\{Game data/config\|(?<os>[^|}]+)\|(?<path>[^\n]*)\}\}[ \t\r]*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ConfigLine();

    [GeneratedRegex(@"^\|[ \t]*(?<key>[^=\n|]+?)[ \t]*=[ \t]*(?<value>[^\n]*)$", RegexOptions.Multiline)]
    private static partial Regex FieldLine();

    [GeneratedRegex(@"\{\{P\|([^}|]+)\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex PathToken();

    [GeneratedRegex(@"<ref[^>]*?>.*?</ref>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RefTag();

    [GeneratedRegex(@"<ref[^>]*/>", RegexOptions.IgnoreCase)]
    private static partial Regex SelfClosingRef();

    [GeneratedRegex(@"\{\{([^{}]*)\}\}")]
    private static partial Regex SimpleTemplate();

    [GeneratedRegex(@"\[\[([^\]|]+)\|([^\]]+)\]\]")]
    private static partial Regex PipedLink();

    [GeneratedRegex(@"\[\[([^\]]+)\]\]")]
    private static partial Regex PlainLink();

    [GeneratedRegex(@"\[https?://[^\s\]]+\s+([^\]]+)\]")]
    private static partial Regex ExternalLink();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex Breaks();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>Turns a wiki config path into a real file or folder on this PC, if it exists. Reads only.</summary>
public static class ConfigPathResolver
{
    /// <param name="exists">True when the file or folder exists (injected so it can be tested).</param>
    public static string? Resolve(IEnumerable<string> templates, string? installFolder, Func<string, bool> exists, Func<string, string>? expand = null)
    {
        expand ??= Environment.ExpandEnvironmentVariables;
        foreach (var template in templates)
        {
            var path = template;
            if (path.Contains("<game folder>", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(installFolder))
                {
                    continue;
                }

                path = path.Replace("<game folder>", installFolder, StringComparison.Ordinal);
            }

            if (path.Contains('<', StringComparison.Ordinal) || path.Contains('*', StringComparison.Ordinal))
            {
                continue;
            }

            var resolved = expand(path);
            if (exists(resolved))
            {
                return resolved;
            }
        }

        return null;
    }
}

/// <summary>
/// Looks a game up on PCGamingWiki through its public API. It sends only the game's name, and only when the user asks.
/// The wiki's web pages are behind a bot check; the API is the supported way for a program to read it.
/// </summary>
public sealed partial class PcGamingWikiClient(HttpClient http, string appVersion)
{
    private const string Api = "https://www.pcgamingwiki.com/w/api.php";

    public async Task<WikiGameInfo?> LookUpAsync(string gameName, CancellationToken ct)
    {
        string? title = null;
        foreach (var variant in NameVariants(gameName))
        {
            title = await FindTitleAsync(variant, ct).ConfigureAwait(false);
            if (title is not null)
            {
                break;
            }
        }

        if (title is null)
        {
            return null;
        }

        var json = await GetAsync($"{Api}?action=parse&page={Uri.EscapeDataString(title)}&prop=wikitext&redirects=1&format=json&formatversion=2", ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("parse", out var parse))
        {
            return null;
        }

        var resolvedTitle = parse.TryGetProperty("title", out var t) ? t.GetString() ?? title : title;
        var text = parse.TryGetProperty("wikitext", out var w) ? w.GetString() ?? "" : "";
        return PcGamingWikiParser.Parse(resolvedTitle, text);
    }

    /// <summary>
    /// Captures often record a game's process-style name ("CONTROLResonant"), which a wiki search does not find.
    /// The name as given is tried first, then with spaces at the case changes, then without punctuation.
    /// </summary>
    public static IReadOnlyList<string> NameVariants(string name)
    {
        var trimmed = name.Trim();
        var spaced = CaseBreak().Replace(trimmed, " ");
        var plain = Regex.Replace(Punctuation().Replace(spaced, " "), @"\s+", " ").Trim();
        return new[] { trimmed, spaced, plain }.Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex CaseBreak();

    [GeneratedRegex(@"[:_.\-]+")]
    private static partial Regex Punctuation();

    private async Task<string?> FindTitleAsync(string gameName, CancellationToken ct)
    {
        var json = await GetAsync($"{Api}?action=query&list=search&srsearch={Uri.EscapeDataString(gameName)}&srlimit=8&format=json", ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("query", out var q) || !q.TryGetProperty("search", out var results))
        {
            return null;
        }

        var wanted = Normalize(gameName);
        string? fallback = null;
        foreach (var r in results.EnumerateArray())
        {
            var title = r.GetProperty("title").GetString() ?? "";
            var snippet = r.TryGetProperty("snippet", out var sn) ? sn.GetString() ?? "" : "";
            if (title.Contains(':', StringComparison.Ordinal) && title.Split(':')[0].Contains("Talk", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Normalize(title) == wanted)
            {
                return title;
            }

            // Redirects and near matches are followed by the parse call; keep the first plausible one.
            fallback ??= snippet.Contains("REDIRECT", StringComparison.OrdinalIgnoreCase) || Normalize(title).Contains(wanted, StringComparison.Ordinal) || wanted.Contains(Normalize(title), StringComparison.Ordinal) ? title : null;
        }

        return fallback;
    }

    private async Task<string> GetAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("daignostic", appVersion));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/axellageraldinc/daignostic; read-only game settings lookup)"));
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static string Normalize(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
