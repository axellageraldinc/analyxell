using System.Globalization;
using System.Text.RegularExpressions;

namespace Daignostic.Core.SystemInfo;

/// <summary>
/// The fields a user can fill in about things Windows and the GPU driver do not let us read safely
/// (G-SYNC and FreeSync state, driver profile options, the monitor).
/// </summary>
public static class SetupFields
{
    public sealed record Field(string Key, string Label, string[] Options, string Hint);

    public static IReadOnlyList<Field> All { get; } =
    [
        new("display_sync", "Display sync", ["Unknown", "G-SYNC", "G-SYNC Compatible", "FreeSync", "VRR (HDMI/other)", "Off"], "NVIDIA Control Panel > Set up G-SYNC, or Radeon software, or your monitor's menu."),
        new("vsync", "VSync", ["Unknown", "Off", "On (in-game)", "On (driver)", "Fast / Adaptive"], "The setting used while the capture was recorded."),
        new("low_latency", "Low latency mode", ["Unknown", "Off", "On", "Ultra / Reflex Boost"], "NVIDIA Reflex or Low Latency Mode, in the game or the driver."),
        new("frame_cap", "Frame rate cap (fps)", [], "Leave empty if there is no cap. Example: 141."),
        new("shader_cache", "Driver shader cache size", ["Unknown", "Default", "Unlimited", "Disabled"], "NVIDIA Control Panel > Manage 3D settings > Shader Cache Size."),
        new("power_mode", "GPU power management", ["Unknown", "Optimal / Balanced", "Prefer maximum performance", "Adaptive"], "NVIDIA Control Panel > Manage 3D settings > Power management mode."),
        new("monitor_model", "Monitor", [], "Model name, for example from the monitor's menu."),
        new("monitor_max_hz", "Monitor maximum refresh rate (Hz)", [], "Example: 144."),
    ];
}

/// <summary>
/// Collects read-only facts about the PC that a capture does not hold: Windows gaming toggles, the display mode,
/// where the game is installed and what kind of drive that is, and the size of the GPU shader cache.
/// </summary>
public static class SystemSettingsReader
{
    private static readonly Dictionary<string, string> PowerPlans = new(StringComparer.OrdinalIgnoreCase)
    {
        ["381b4222-f694-41f0-9685-ff5bb260df2e"] = "Balanced",
        ["8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"] = "High performance",
        ["a1841308-3541-4fab-bc81-f71556f20b4a"] = "Power saver",
        ["e9a42b02-d5df-448d-aa00-03f14749eb61"] = "Ultimate performance",
        ["961cc777-2547-4f9b-8174-7d86181b8a7a"] = "Best performance",
    };

    public static SystemContext Read(
        ISystemProbe probe,
        IReadOnlyDictionary<string, string> userValues,
        string? gameInstallPath,
        string gpuName)
    {
        var items = new List<SystemSetting>();
        void Auto(string key, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                items.Add(new SystemSetting(key, label, value, SettingSource.Auto));
            }
        }

        // Windows gaming toggles.
        var gameMode = probe.RegistryDword("HKCU", @"Software\Microsoft\GameBar", "AutoGameModeEnabled");
        Auto("game_mode", "Windows Game Mode", gameMode is null ? "Default (on)" : gameMode == 0 ? "Off" : "On");

        var hags = probe.RegistryDword("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
        Auto("hags", "Hardware-accelerated GPU scheduling", hags switch { 2 => "On", 1 => "Off", _ => null });

        var gpuPrefs = probe.RegistryString("HKCU", @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings");
        if (gpuPrefs is not null)
        {
            Auto("windowed_vrr", "Variable refresh rate for windowed games", gpuPrefs.Contains("VRROptimizeEnable=1", StringComparison.OrdinalIgnoreCase) ? "On" : "Off");
        }

        // Windows features that can cost a little smoothness. Only read here; daignostic never changes them.
        var dvr = probe.RegistryDword("HKCU", @"System\GameConfigStore", "GameDVR_Enabled");
        var capture = probe.RegistryDword("HKCU", @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");
        if (dvr is not null || capture is not null)
        {
            Auto("game_dvr", "Windows background game recording", dvr == 1 || capture == 1 ? "On" : "Off");
        }

        var hvci = probe.RegistryDword("HKLM", @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");
        Auto("memory_integrity", "Windows Memory integrity", hvci switch { 1 => "On", 0 => "Off", _ => null });

        var plan = probe.RegistryString("HKLM", @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes", "ActivePowerScheme");
        if (plan is not null)
        {
            Auto("power_plan", "Windows power plan", PowerPlans.TryGetValue(plan.Trim(), out var planName) ? planName : "Custom");
        }

        // Display.
        if (probe.PrimaryDisplay() is { } display)
        {
            Auto("display_mode", "Primary display mode", $"{display.Width}x{display.Height} at {display.RefreshHz} Hz");
            Auto("display_current_hz", "Primary display refresh now", display.RefreshHz.ToString(CultureInfo.InvariantCulture));
        }

        Auto("monitor_count", "Connected monitors", probe.MonitorCount()?.ToString(CultureInfo.InvariantCulture));

        // Where the game lives and what kind of drive that is.
        if (!string.IsNullOrWhiteSpace(gameInstallPath))
        {
            Auto("game_install_path_known", "Game install folder", "Known");
            if (probe.Storage(gameInstallPath) is { } storage)
            {
                Auto("game_drive.media", "Game drive type", storage.MediaType);
                Auto("game_drive.bus", "Game drive connection", storage.BusType);
                Auto("game_drive.model", "Game drive model", storage.Model);
                Auto("game_drive.free_pct", "Game drive free space", storage.FreePercent.ToString("0", CultureInfo.InvariantCulture));
                Auto("game_drive.free_gb", "Game drive free (GB)", storage.FreeGb.ToString("0", CultureInfo.InvariantCulture));
                Auto("game_drive.total_gb", "Game drive size (GB)", storage.TotalGb.ToString("0", CultureInfo.InvariantCulture));
                Auto("game_drive.filesystem", "Game drive file system", storage.FileSystem);
            }
        }

        // Shader caches. An empty cache means the GPU has to compile shaders while you play.
        var cacheDirs = ShaderCacheDirectories(gpuName);
        long bytes = 0;
        var files = 0;
        var any = false;
        foreach (var dir in cacheDirs)
        {
            if (!probe.DirectoryExists(dir))
            {
                continue;
            }

            any = true;
            if (probe.DirectorySize(dir, 60_000, TimeSpan.FromSeconds(2)) is { } totals)
            {
                bytes += totals.Bytes;
                files += totals.Files;
            }
        }

        if (any)
        {
            Auto("shader_cache_mb", "Driver shader cache on disk (MB)", (bytes / 1024.0 / 1024.0).ToString("0", CultureInfo.InvariantCulture));
            Auto("shader_cache_files", "Driver shader cache files", files.ToString(CultureInfo.InvariantCulture));
        }

        // What the user told us (never verified).
        foreach (var field in SetupFields.All)
        {
            if (userValues.TryGetValue(field.Key, out var value) && !string.IsNullOrWhiteSpace(value) && !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new SystemSetting("setup." + field.Key, field.Label, value.Trim(), SettingSource.User));
            }
        }

        return new SystemContext(items);
    }

    internal static IEnumerable<string> ShaderCacheDirectories(string gpuName)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local))
        {
            yield break;
        }

        if (gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || gpuName.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(local, "NVIDIA", "DXCache");
            yield return Path.Combine(local, "NVIDIA", "GLCache");
        }
        else if (gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) || gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(local, "AMD", "DxCache");
        }

        yield return Path.Combine(local, "D3DSCache");
    }
}

/// <summary>Finds a game's install folder without asking the user, where a launcher records it.</summary>
public static partial class GameLocator
{
    public static string? FindSteamInstall(ISystemProbe probe, string gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName))
        {
            return null;
        }

        var steam = probe.RegistryString("HKCU", @"Software\Valve\Steam", "SteamPath");
        if (string.IsNullOrWhiteSpace(steam))
        {
            return null;
        }

        var libraries = new List<string> { steam.Replace('/', '\\') };
        var vdf = probe.ReadText(Path.Combine(libraries[0], "steamapps", "libraryfolders.vdf"));
        if (vdf is not null)
        {
            foreach (Match m in LibraryPath().Matches(vdf))
            {
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
        }

        var wanted = Normalize(gameName);
        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(library, "steamapps");
            foreach (var manifest in probe.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                var text = probe.ReadText(manifest);
                if (text is null)
                {
                    continue;
                }

                var name = AcfName().Match(text);
                var dir = AcfInstallDir().Match(text);
                if (name.Success && dir.Success && Normalize(name.Groups[1].Value) == wanted)
                {
                    var path = Path.Combine(apps, "common", dir.Groups[1].Value);
                    if (probe.DirectoryExists(path))
                    {
                        return path;
                    }
                }
            }
        }

        return null;
    }

    private static string Normalize(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPath();

    [GeneratedRegex("\"name\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AcfName();

    [GeneratedRegex("\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AcfInstallDir();
}
