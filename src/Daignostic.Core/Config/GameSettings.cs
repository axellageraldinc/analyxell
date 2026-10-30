using Daignostic.Core.Capture;

namespace Daignostic.Core.Config;

/// <summary>One in-game setting. <see cref="IsRaw"/> marks unmapped raw ids; <see cref="Source"/> says where it came from.</summary>
public sealed record GameSetting(string Key, string Label, string Value, bool IsRaw, string Source = "");

/// <summary>Graphics settings read (read-only) from a game's config file at its last save.</summary>
public sealed record GameSettings(
    string GameName,
    string SourceFileName,
    DateTime LastModifiedUtc,
    IReadOnlyList<GameSetting> Items)
{
    public string? Get(string key) =>
        Items.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;

    public bool? IsOn(string key) => Get(key) switch
    {
        "On" => true,
        "Off" => false,
        _ => null,
    };
}

/// <summary>Reads a game's graphics settings. Implementations must never write to the game's files.</summary>
public interface IGameConfigReader
{
    string Name { get; }

    bool Matches(CaptureInfo info);

    GameSettings? TryRead();
}

public static class GameConfigReaders
{
    public static IReadOnlyList<IGameConfigReader> CreateDefaults() => [new WukongConfigReader()];

    public static IGameConfigReader? Find(CaptureInfo info, IEnumerable<IGameConfigReader> readers) =>
        readers.FirstOrDefault(r => r.Matches(info));
}

public static class GameSettingsMerger
{
    /// <summary>
    /// Combines what a built-in reader found with settings the user provided (typed, pasted or AI-extracted and confirmed).
    /// User-provided items are always raw: their meaning is not interpreted.
    /// </summary>
    public static GameSettings? Merge(GameSettings? fromReader, GameProfile? profile, string gameName)
    {
        var extra = profile?.Items
            .Where(i => fromReader?.Get(i.Key) is null)
            .Select(i => new GameSetting("profile." + i.Key, i.Key, i.Value, IsRaw: true, Source: string.IsNullOrWhiteSpace(profile.Origin) ? "entered by you" : profile.Origin))
            .ToList() ?? [];

        if (fromReader is null && extra.Count == 0)
        {
            return null;
        }

        var items = (fromReader?.Items ?? []).Select(i => i with { Source = i.Source.Length > 0 ? i.Source : "game config file" }).Concat(extra).ToList();
        var modified = fromReader?.LastModifiedUtc
            ?? (DateTime.TryParse(profile?.SavedAtUtc, out var saved) ? saved.ToUniversalTime() : DateTime.UtcNow);
        return new GameSettings(
            fromReader?.GameName ?? gameName,
            fromReader?.SourceFileName ?? "entered by you",
            modified,
            items);
    }
}