using System.Globalization;
using Daignostic.Core.Capture;

namespace Daignostic.Core.Config;

public enum LibraryStatus
{
    /// <summary>daignostic reads this game's settings from its config file by itself.</summary>
    BuiltIn,

    /// <summary>The user gave the settings (typed, pasted or imported with their own AI).</summary>
    Entered,

    /// <summary>Captures of this game can be diagnosed; no settings are known.</summary>
    CapturesOnly,
}

/// <summary>One game in the Library.</summary>
public sealed record LibraryEntry(
    string Game,
    string ProcessName,
    LibraryStatus Status,
    string StatusText,
    string Detail,
    string InstallPath,
    IReadOnlyList<GameSetting> Settings,
    bool IsUserAdded);

/// <summary>
/// Builds the list of games daignostic knows: the ones it can read by itself, and every one it has seen or been told about.
/// Any game can be diagnosed from a capture; the Library says what else is known about it.
/// </summary>
public static class GameLibrary
{
    public static IReadOnlyList<LibraryEntry> Build(IReadOnlyList<GameProfile> profiles, IReadOnlyList<IGameConfigReader> readers)
    {
        var entries = new List<LibraryEntry>();
        var used = new HashSet<IGameConfigReader>();

        foreach (var profile in profiles.OrderBy(p => p.Game.Length > 0 ? p.Game : p.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            var probe = new CaptureInfo(profile.Game, profile.ProcessName, "", "", "", "", "", "", "", "", "", "");
            var reader = GameConfigReaders.Find(probe, readers);
            if (reader is not null)
            {
                used.Add(reader);
                entries.Add(FromReader(reader, profile));
                continue;
            }

            var items = ProfileSettings(profile);
            entries.Add(new LibraryEntry(
                profile.Game.Length > 0 ? profile.Game : profile.ProcessName,
                profile.ProcessName,
                items.Count > 0 ? LibraryStatus.Entered : LibraryStatus.CapturesOnly,
                items.Count > 0 ? $"Settings entered by you ({items.Count})" : "Diagnosable from captures. No settings yet",
                items.Count > 0 ? $"Saved {Local(profile.SavedAtUtc)}. Used for captures recorded after that." : "Add the game's settings so findings and the AI can take them into account.",
                profile.InstallPath,
                items,
                IsUserAdded: true));
        }

        foreach (var reader in readers.Where(r => !used.Contains(r)))
        {
            entries.Add(FromReader(reader, null));
        }

        return entries
            .OrderBy(e => e.Status)
            .ThenBy(e => e.Game, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static LibraryEntry FromReader(IGameConfigReader reader, GameProfile? profile)
    {
        var read = reader.TryRead();
        var detail = read is null
            ? "The game's config file was not found on this PC, so there are no settings to read. Run the game once, or enter them yourself."
            : $"Read from {read.SourceFileName}, last saved {read.LastModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm}. Used for captures recorded after that.";
        return new LibraryEntry(
            reader.Name,
            profile?.ProcessName ?? "",
            LibraryStatus.BuiltIn,
            read is null ? "Built in. Config file not found" : $"Built in. Settings read automatically ({read.Items.Count})",
            detail,
            profile?.InstallPath ?? "",
            read?.Items ?? [],
            IsUserAdded: false);
    }

    /// <summary>The user's entered settings as game settings; they count as saved when the profile was saved.</summary>
    public static IReadOnlyList<GameSetting> ProfileSettings(GameProfile profile) =>
        profile.Items.Select(i => new GameSetting("profile." + i.Key, i.Key, i.Value, IsRaw: true,
            Source: string.IsNullOrWhiteSpace(profile.Origin) ? "entered by you" : profile.Origin)).ToList();

    /// <summary>
    /// A game's entered settings as the "current settings" of the game, dated when they were saved, so the same rule
    /// that protects against stale config files decides which captures they may describe. Null when none were entered.
    /// </summary>
    public static GameSettings? ProfileAsSettings(GameProfile? profile)
    {
        if (profile is null || profile.Items.Count == 0 || !DateTime.TryParse(profile.SavedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var saved))
        {
            return null;
        }

        return new GameSettings(profile.Game, "entered by you", saved.ToUniversalTime(), ProfileSettings(profile));
    }

    private static string Local(string iso) =>
        DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "earlier";
}
