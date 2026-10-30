using Daignostic.Core.Capture;

namespace Daignostic.Core.Config;

/// <summary>One in-game setting read from a game's config file. <see cref="IsRaw"/> marks unmapped raw ids.</summary>
public sealed record GameSetting(string Key, string Label, string Value, bool IsRaw);

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
