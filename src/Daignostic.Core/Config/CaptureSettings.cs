using System.Globalization;
using System.Text.Json;
using Daignostic.Core.Capture;

namespace Daignostic.Core.Config;

/// <summary>
/// The game settings that belong to ONE capture. A game's config file only holds its latest settings, so reading it
/// for an older capture would describe a different setup than the one that was measured.
/// </summary>
public sealed class CaptureSettingsSnapshot
{
    public string GameName { get; set; } = "";

    public string SourceFileName { get; set; } = "";

    public string SavedAtUtc { get; set; } = "";

    public string LastModifiedUtc { get; set; } = "";

    public List<GameSetting> Items { get; set; } = [];

    public GameSettings ToSettings() => new(
        GameName,
        SourceFileName,
        DateTime.TryParse(LastModifiedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t.ToUniversalTime() : DateTime.UtcNow,
        Items);

    public static CaptureSettingsSnapshot From(GameSettings settings) => new()
    {
        GameName = settings.GameName,
        SourceFileName = settings.SourceFileName,
        LastModifiedUtc = settings.LastModifiedUtc.ToString("o", CultureInfo.InvariantCulture),
        SavedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
        Items = [.. settings.Items],
    };
}

/// <summary>Keeps one settings snapshot per capture, on this PC only.</summary>
public sealed class CaptureSettingsStore(string directory)
{
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Daignostic", "capture-settings");

    public CaptureSettingsSnapshot? Load(string key)
    {
        var path = PathFor(key);
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), CoreJsonContext.Default.CaptureSettingsSnapshot)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string key, CaptureSettingsSnapshot snapshot)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(PathFor(key), JsonSerializer.Serialize(snapshot, CoreJsonContext.Default.CaptureSettingsSnapshot));
    }

    private string PathFor(string key) => Path.Combine(directory, new string(key.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray()) + ".json");
}

/// <param name="Settings">What may be used for this capture, or null.</param>
/// <param name="Note">Why a game config was not used, in plain words, or null.</param>
/// <param name="LiveConfig">The game's current config, kept so the user can choose to apply it to this capture.</param>
/// <param name="SaveSnapshot">True when <paramref name="Settings"/> came from the live config and should be kept for this capture.</param>
public sealed record ResolvedSettings(GameSettings? Settings, string? Note, GameSettings? LiveConfig, bool SaveSnapshot);

public static class CaptureSettingsResolver
{
    /// <summary>A config saved within this long after the capture started is treated as unchanged during it.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(10);

    public static string KeyFor(CaptureInfo info) =>
        info.Id.Length > 0 ? info.Id : $"{info.ProcessName}-{info.RecordedAtUtc}";

    /// <summary>
    /// Settings saved for the capture win. Otherwise the game's live config is used only if it was last saved before
    /// the capture was recorded; a config saved later may describe different settings, so it is not used.
    /// </summary>
    public static ResolvedSettings Resolve(CaptureInfo info, GameSettings? live, CaptureSettingsSnapshot? snapshot)
    {
        if (snapshot is { Items.Count: > 0 })
        {
            return new ResolvedSettings(snapshot.ToSettings(), null, live, false);
        }

        if (live is null)
        {
            return new ResolvedSettings(null, null, null, false);
        }

        if (!DateTime.TryParse(info.RecordedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var recorded))
        {
            // No recording time: nothing to compare with, so keep the old behaviour of trusting the config.
            return new ResolvedSettings(live, null, live, true);
        }

        if (live.LastModifiedUtc <= recorded.ToUniversalTime() + Tolerance)
        {
            return new ResolvedSettings(live, null, live, true);
        }

        var note = $"The game's config file was last saved on {live.LastModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm}, after this capture was recorded ({recorded.ToLocalTime():yyyy-MM-dd HH:mm}). "
            + "It may describe different settings than you played with, so daignostic is not using it for this capture.";
        return new ResolvedSettings(null, note, live, false);
    }
}
