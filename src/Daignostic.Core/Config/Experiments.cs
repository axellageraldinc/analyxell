using System.Globalization;
using System.Text.Json;
using Daignostic.Core.Analysis;

namespace Daignostic.Core.Config;

/// <summary>The numbers of the run a change was tried against. Only measurements; nothing that identifies the PC.</summary>
public sealed class ExperimentBaseline
{
    public string CaptureKey { get; set; } = "";

    public string CaptureName { get; set; } = "";

    public string RecordedAtUtc { get; set; } = "";

    public double AverageFps { get; set; }

    public double Low1Fps { get; set; }

    public double Low01Fps { get; set; }

    public int Freezes { get; set; }

    public int Stutters { get; set; }

    public static ExperimentBaseline From(AnalysisResult r, string captureKey, string name) => new()
    {
        CaptureKey = captureKey,
        CaptureName = name,
        RecordedAtUtc = r.Info.RecordedAtUtc,
        AverageFps = r.Metrics.AverageFps,
        Low1Fps = r.Metrics.Low1AverageFps,
        Low01Fps = r.Metrics.Low01AverageFps,
        Freezes = r.Freezes.Count,
        Stutters = r.Stutters.Count,
    };
}

/// <summary>A change the user said they would try, waiting for a new capture of the same game to check it.</summary>
public sealed class Experiment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string ProcessName { get; set; } = "";

    public string Game { get; set; } = "";

    public string Step { get; set; } = "";

    public string StartedAtUtc { get; set; } = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

    public ExperimentBaseline Baseline { get; set; } = new();

    /// <summary>Empty while waiting; otherwise the plain-language verdict that was shown.</summary>
    public string Result { get; set; } = "";

    public string ResultKind { get; set; } = "";

    public bool IsWaiting => Result.Length == 0;
}

public sealed class ExperimentList
{
    public List<Experiment> Items { get; set; } = [];
}

public enum ExperimentKind
{
    Helped,
    NoClearChange,
    Worse,
}

public sealed record ExperimentOutcome(ExperimentKind Kind, string Text);

/// <summary>Judges whether a change helped by comparing the run before with the run after.</summary>
public static class ExperimentJudge
{
    public static ExperimentOutcome Judge(ExperimentBaseline before, AnalysisResult after)
    {
        var lowChange = Pct(before.Low1Fps, after.Metrics.Low1AverageFps);
        var avgChange = Pct(before.AverageFps, after.Metrics.AverageFps);
        var freezeDelta = after.Freezes.Count - before.Freezes;
        var stutterChange = before.Stutters == 0 ? (after.Stutters.Count == 0 ? 0 : 100) : (after.Stutters.Count - before.Stutters) * 100.0 / before.Stutters;

        var numbers = string.Create(CultureInfo.InvariantCulture,
            $"Average {before.AverageFps:0.#} to {after.Metrics.AverageFps:0.#} fps, 1% low {before.Low1Fps:0.#} to {after.Metrics.Low1AverageFps:0.#}, freezes {before.Freezes} to {after.Freezes.Count}, stutters {before.Stutters} to {after.Stutters.Count}.");

        var worse = lowChange <= -8 || freezeDelta >= 2 || (freezeDelta >= 1 && lowChange < 0) || avgChange <= -10;
        var better = !worse && (lowChange >= 8 || freezeDelta <= -1 || avgChange >= 8 || stutterChange <= -25);

        if (better)
        {
            return new ExperimentOutcome(ExperimentKind.Helped, "It looks like it helped. " + numbers + " Keep it, and try the next step if you want more.");
        }

        if (worse)
        {
            return new ExperimentOutcome(ExperimentKind.Worse, "It looks worse. " + numbers + " Consider undoing it, and try a different step.");
        }

        return new ExperimentOutcome(ExperimentKind.NoClearChange,
            "No clear change. " + numbers + " Differences this small can be ordinary run-to-run variation, so it was probably not the cause. A scene that differs between the runs also changes the numbers.");
    }

    private static double Pct(double before, double after) => before <= 0 ? 0 : (after - before) / before * 100.0;
}

/// <summary>Remembers what the user is trying and what came of it, per game, on this PC only.</summary>
public sealed class ExperimentStore(string filePath)
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Daignostic", "experiments.json");

    public IReadOnlyList<Experiment> All() => Load().Items;

    public Experiment Start(Experiment experiment)
    {
        var data = Load();

        // One open experiment per game at a time keeps "what did that change do?" answerable.
        data.Items.RemoveAll(e => e.IsWaiting && e.ProcessName.Equals(experiment.ProcessName, StringComparison.OrdinalIgnoreCase));
        data.Items.Add(experiment);
        Save(data);
        return experiment;
    }

    public Experiment? WaitingFor(string processName) =>
        Load().Items.LastOrDefault(e => e.IsWaiting && e.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));

    public void Finish(string id, ExperimentOutcome outcome)
    {
        var data = Load();
        if (data.Items.FirstOrDefault(e => e.Id == id) is { } e)
        {
            e.Result = outcome.Text;
            e.ResultKind = outcome.Kind.ToString();
            Save(data);
        }
    }

    public void Cancel(string id)
    {
        var data = Load();
        data.Items.RemoveAll(e => e.Id == id);
        Save(data);
    }

    private ExperimentList Load()
    {
        try
        {
            return File.Exists(filePath)
                ? JsonSerializer.Deserialize(File.ReadAllText(filePath), CoreJsonContext.Default.ExperimentList) ?? new ExperimentList()
                : new ExperimentList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ExperimentList();
        }
    }

    private void Save(ExperimentList data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(data, CoreJsonContext.Default.ExperimentList));
    }
}
