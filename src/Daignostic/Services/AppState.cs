using System.Collections.ObjectModel;
using Daignostic.Core.Analysis;
using Daignostic.Core.Background;
using Daignostic.Core.Capture;
using Daignostic.Core.Config;
using Daignostic.Core.SystemInfo;

namespace Daignostic.Services;

public sealed record LoadedCapture(string Name, string Path, CaptureFile File, AnalysisResult Result, BackgroundScanResult? Background)
{
    public string ProcessName => File.Info.ProcessName;
}

/// <summary>Navigation requests that view models can make without knowing about the window.</summary>
internal static class AppNavigation
{
    public static event Action<string>? Requested;

    public static void GoTo(string page) => Requested?.Invoke(page);
}

/// <summary>Captures loaded during this session, shared by the Analyze and Compare pages.</summary>
internal static class AppState
{
    public static ObservableCollection<LoadedCapture> Loaded { get; } = [];

    public static LoadedCapture? Selected { get; set; }

    /// <summary>Capture paths passed on the command line, opened in order once the Analyze page is ready.</summary>
    public static IReadOnlyList<string> PendingOpenPaths { get; set; } = [];

    /// <summary>Parses and analyzes a capture off the UI thread. Must be awaited from the UI thread.</summary>
    public static async Task<LoadedCapture> LoadAsync(string path)
    {
        var loaded = await Task.Run(() =>
        {
            var file = CaptureReader.Load(path);
            var result = Analyze(file, null);
            return new LoadedCapture(file.DisplayName, path, file, result, null);
        });

        var existing = Loaded.FirstOrDefault(l => string.Equals(l.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Loaded.Remove(existing);
        }

        Loaded.Insert(0, loaded);
        Selected = loaded;
        return loaded;
    }

    /// <summary>
    /// Re-runs the analysis for a loaded capture, for example after the user entered their setup or ran the
    /// background check. The capture file itself is not read again.
    /// </summary>
    public static async Task<LoadedCapture> ReanalyzeAsync(LoadedCapture current, BackgroundScanResult? background, bool keepBackground = true)
    {
        var scan = background ?? (keepBackground ? current.Background : null);
        var updated = await Task.Run(() => current with { Result = Analyze(current.File, scan), Background = scan });

        var index = Loaded.IndexOf(current);
        if (index >= 0)
        {
            Loaded[index] = updated;
        }

        if (ReferenceEquals(Selected, current))
        {
            Selected = updated;
        }

        return updated;
    }

    private static AnalysisResult Analyze(CaptureFile file, BackgroundScanResult? background)
    {
        var info = file.Info;
        var fromReader = GameConfigReaders.Find(info, AppServices.ConfigReaders)?.TryRead();
        var profile = info.ProcessName.Length > 0 ? AppServices.Profiles.Load(info.ProcessName) : null;
        var settings = GameSettingsMerger.Merge(fromReader, profile, info.Game);

        var installPath = !string.IsNullOrWhiteSpace(profile?.InstallPath)
            ? profile.InstallPath
            : GameLocator.FindSteamInstall(AppServices.Probe, info.Game);
        var system = SystemSettingsReader.Read(AppServices.Probe, AppServices.Setup.Load().Values, installPath, info.Gpu);

        return CaptureAnalyzer.Analyze(file, 0, settings, system, background, AppServices.Settings.ShareBackgroundAppNames);
    }
}
