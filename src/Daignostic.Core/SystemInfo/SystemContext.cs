namespace Daignostic.Core.SystemInfo;

public enum SettingSource
{
    /// <summary>Read automatically and read-only from Windows or the file system.</summary>
    Auto,

    /// <summary>Entered by the user. Taken at their word, never verified.</summary>
    User,
}

public sealed record SystemSetting(string Key, string Label, string Value, SettingSource Source);

/// <summary>Everything we know about the PC setup that a capture does not contain.</summary>
public sealed class SystemContext
{
    public static SystemContext Empty { get; } = new([]);

    public SystemContext(IReadOnlyList<SystemSetting> items) => Items = items;

    public IReadOnlyList<SystemSetting> Items { get; }

    public string? Get(string key) =>
        Items.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;

    public bool Is(string key, string expected) =>
        string.Equals(Get(key), expected, StringComparison.OrdinalIgnoreCase);
}

public sealed record StorageInfo(string Root, string MediaType, string BusType, string Model, double TotalGb, double FreeGb, string FileSystem)
{
    public double FreePercent => TotalGb <= 0 ? 0 : FreeGb / TotalGb * 100.0;
}

public sealed record DisplayMode(int Width, int Height, int RefreshHz);

public sealed record DirectoryTotals(long Bytes, int Files, bool Truncated);

/// <summary>
/// Read-only window onto the machine. The real implementation reads the registry and queries the storage stack;
/// tests supply a fake. Nothing here can change the system.
/// </summary>
public interface ISystemProbe
{
    int? RegistryDword(string hive, string subKey, string name);

    string? RegistryString(string hive, string subKey, string name);

    DirectoryTotals? DirectorySize(string path, int maxFiles, TimeSpan budget);

    StorageInfo? Storage(string path);

    DisplayMode? PrimaryDisplay();

    int? MonitorCount();

    string? ReadText(string path);

    IEnumerable<string> EnumerateFiles(string directory, string pattern);

    bool DirectoryExists(string path);

    bool FileExists(string path);

    IEnumerable<string> RegistrySubKeyNames(string hive, string subKey);

    bool IsProcessRunning(string processName);
}
