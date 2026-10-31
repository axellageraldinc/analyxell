using System.Text.Json;

namespace Analyxell.Core.Updates;

/// <summary>Which download fits how analyxell was installed.</summary>
public enum InstallKind
{
    /// <summary>Installed from the .msix installer.</summary>
    Installer,

    /// <summary>Unzipped from the portable zip.</summary>
    Portable,
}

/// <param name="Version">The newest release's version, for example 1.4.0.</param>
/// <param name="DownloadUrl">Direct link to the file matching the install kind; falls back to the release page.</param>
/// <param name="PageUrl">The release page on GitHub.</param>
/// <param name="Notes">Release notes, may be empty.</param>
public sealed record UpdateInfo(Version Version, string DownloadUrl, string PageUrl, string Notes);

/// <summary>
/// Asks GitHub for the newest release. It runs only when the user presses "Check for updates": analyxell never
/// contacts GitHub by itself. Nothing but the request itself is sent (no version, id or machine details beyond
/// the standard User-Agent). It does not install anything: it hands back a link for the user to open.
/// </summary>
public sealed class UpdateChecker(HttpClient http)
{
    public const string LatestReleaseApi = "https://api.github.com/repos/axellageraldinc/analyxell/releases/latest";

    public async Task<UpdateInfo?> GetLatestAsync(InstallKind kind, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.ParseAdd("analyxell-update-check");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return Parse(json, kind);
    }

    public static UpdateInfo? Parse(string json, InstallKind kind)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tag_name", out var tag) || !TryParseVersion(tag.GetString(), out var version))
        {
            return null;
        }

        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        var wanted = kind == InstallKind.Installer ? ".msix" : "-portable-win-x64.zip";
        var download = page;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (name.EndsWith(wanted, StringComparison.OrdinalIgnoreCase) &&
                    asset.TryGetProperty("browser_download_url", out var u) && u.GetString() is { Length: > 0 } url)
                {
                    download = url;
                    break;
                }
            }
        }

        return new UpdateInfo(version, download, page, notes);
    }

    public static bool IsNewer(Version latest, Version current) => latest > Normalize(current);

    /// <summary>Accepts "v1.4.0", "1.4.0" and "1.4.0-beta"; a missing part counts as 0.</summary>
    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var core = text.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        if (!Version.TryParse(core, out var parsed))
        {
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
