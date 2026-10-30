using System.Text.Json;
using System.Text.Json.Nodes;

namespace Analyxell.Core.Config;

/// <summary>A setting the user supplied (typed, pasted, or extracted from a file with their AI and then confirmed).</summary>
public sealed record ProfileItem(string Key, string Value);

/// <summary>Settings for one game that the user provided. <see cref="Origin"/> says how they were obtained.</summary>
public sealed class GameProfile
{
    public string ProcessName { get; set; } = "";

    public string Game { get; set; } = "";

    public string Origin { get; set; } = "";

    public string SavedAtUtc { get; set; } = "";

    public string InstallPath { get; set; } = "";

    /// <summary>The game's settings file (or its folder) as found on this PC, when a lookup located it.</summary>
    public string ConfigPath { get; set; } = "";

    /// <summary>What the game can do, from a lookup the user asked for (upscalers, frame generation, VSync modes...). Community-written.</summary>
    public List<ProfileItem> GameFacts { get; set; } = [];

    /// <summary>Where <see cref="GameFacts"/> came from, for example the wiki page address.</summary>
    public string FactsSource { get; set; } = "";

    public List<ProfileItem> Items { get; set; } = [];
}

/// <summary>Per-game profiles stored as small JSON files in the app's own data folder.</summary>
public sealed class GameProfileStore(string directory)
{
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Analyxell", "games");

    public GameProfile? Load(string processName)
    {
        var path = PathFor(processName);
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), CoreJsonContext.Default.GameProfile)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(GameProfile profile)
    {
        Directory.CreateDirectory(directory);
        profile.SavedAtUtc = DateTime.UtcNow.ToString("o");
        File.WriteAllText(PathFor(profile.ProcessName), JsonSerializer.Serialize(profile, CoreJsonContext.Default.GameProfile));
    }

    /// <summary>Every game profile on this PC: games that were opened in a capture or added by the user.</summary>
    public IReadOnlyList<GameProfile> List()
    {
        var result = new List<GameProfile>();
        if (!Directory.Exists(directory))
        {
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize(File.ReadAllText(file), CoreJsonContext.Default.GameProfile) is { ProcessName.Length: > 0 } profile)
                {
                    result.Add(profile);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // A damaged file is skipped; the rest of the Library still shows.
            }
        }

        return result;
    }

    /// <summary>Remembers that a game was seen in a capture, without changing anything the user entered.</summary>
    public void EnsureSeen(string processName, string game)
    {
        if (processName.Length == 0 || Load(processName) is not null)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(PathFor(processName), JsonSerializer.Serialize(new GameProfile { ProcessName = processName, Game = game }, CoreJsonContext.Default.GameProfile));
    }

    public void Delete(string processName)
    {
        var path = PathFor(processName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(string processName)
    {
        var safe = new string(processName.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
        if (safe.Length == 0)
        {
            throw new ArgumentException("A process name is required.", nameof(processName));
        }

        return Path.Combine(directory, safe + ".json");
    }
}

/// <summary>Setup answers that cannot be read automatically (G-SYNC, driver options, monitor). Stored locally.</summary>
public sealed class UserSetupData
{
    public Dictionary<string, string> Values { get; set; } = [];
}

public sealed class UserSetupStore(string filePath)
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Analyxell", "setup.json");

    public UserSetupData Load()
    {
        try
        {
            if (File.Exists(filePath))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(filePath), CoreJsonContext.Default.UserSetupData) ?? new UserSetupData();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fall back to an empty setup rather than failing to start.
        }

        return new UserSetupData();
    }

    public void Save(UserSetupData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(data, CoreJsonContext.Default.UserSetupData));
    }
}

public sealed record FreeFormResult(IReadOnlyList<ProfileItem> Items, IReadOnlyList<string> Warnings);

/// <summary>Turns text the user pasted (JSON, key=value, key: value, or INI lines) into settings.</summary>
public static class FreeFormSettingsParser
{
    public const int MaxItems = 500;

    public static FreeFormResult Parse(string text)
    {
        var warnings = new List<string>();
        var items = new List<ProfileItem>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new FreeFormResult(items, ["Nothing to read."]);
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                Flatten(JsonNode.Parse(trimmed, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }), "", items);
                return Finish(items, warnings);
            }
            catch (JsonException)
            {
                warnings.Add("It looked like JSON but was not valid, so it was read line by line instead.");
            }
        }

        foreach (var raw in trimmed.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//") || line.StartsWith(';') || (line.StartsWith('[') && line.EndsWith(']')))
            {
                continue;
            }

            var sep = FirstSeparator(line);
            if (sep <= 0)
            {
                warnings.Add($"Skipped a line with no key and value: \"{Shorten(line)}\"");
                continue;
            }

            var key = line[..sep].Trim().Trim('"');
            var value = line[(sep + 1)..].Trim().TrimEnd(',').Trim('"');
            if (key.Length > 0)
            {
                items.Add(new ProfileItem(key, value));
            }
        }

        return Finish(items, warnings);
    }

    private static FreeFormResult Finish(List<ProfileItem> items, List<string> warnings)
    {
        if (items.Count > MaxItems)
        {
            warnings.Add($"Only the first {MaxItems} settings were kept.");
            items = items.Take(MaxItems).ToList();
        }

        if (items.Count == 0)
        {
            warnings.Add("No settings were found.");
        }

        return new FreeFormResult(items, warnings);
    }

    private static int FirstSeparator(string line)
    {
        var eq = line.IndexOf('=');
        var colon = line.IndexOf(':');
        if (eq < 0)
        {
            return colon;
        }

        return colon < 0 ? eq : Math.Min(eq, colon);
    }

    private static void Flatten(JsonNode? node, string prefix, List<ProfileItem> items)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj)
                {
                    Flatten(child, prefix.Length == 0 ? key : prefix + "." + key, items);
                }

                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    Flatten(arr[i], $"{prefix}[{i}]", items);
                }

                break;
            case JsonValue value when prefix.Length > 0:
                items.Add(new ProfileItem(prefix, value.ToString()));
                break;
        }
    }

    private static string Shorten(string s) => s.Length <= 60 ? s : s[..57] + "...";
}
