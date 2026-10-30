using System.Collections.ObjectModel;
using Daignostic.Core.Analysis;
using Daignostic.Core.Capture;
using Daignostic.Core.Config;

namespace Daignostic.Services;

public sealed record LoadedCapture(string Name, string Path, CaptureFile File, AnalysisResult Result);

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
            var settings = GameConfigReaders.Find(file.Info, AppServices.ConfigReaders)?.TryRead();
            var result = CaptureAnalyzer.Analyze(file, 0, settings);
            return new LoadedCapture(file.DisplayName, path, file, result);
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
}

