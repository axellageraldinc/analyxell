using System.Net;
using Daignostic.Core.Ai;
using Daignostic.Core.Analysis;
using Daignostic.Core.Mcp;
using Daignostic.Core.Reporting;
using Daignostic.Core.Settings;

namespace Daignostic.Core.Tests;

public class ReportTests
{
    [Fact]
    public void Report_lists_metrics_findings_and_ai_without_paths()
    {
        var file = new Daignostic.Core.Capture.CaptureFile
        {
            Info = TestData.File(TestData.Run(TestData.Steady(10, 10))).Info,
            Runs = [TestData.RunWithFreezes(95, 10)],
            SourcePath = @"C:\Users\someone\capture.json",
        };
        var result = CaptureAnalyzer.Analyze(file);
        var ai = new AiAnalysis("Short summary.", false,
            [new AiItem("Freezes", "They hurt lows.", ["finding:FREEZES"], ["Turn off ray tracing"], "medium")], 0, "raw");

        var md = MarkdownReport.Build(result, ai);

        Assert.Contains("# Test Game - performance report", md);
        Assert.Contains("| Freezes (200 ms or more) | 2 |", md);
        Assert.Contains("FREEZES", md.ToUpperInvariant());
        Assert.Contains("Try: Turn off ray tracing", md);
        Assert.Contains("not a verdict", md);
        Assert.DoesNotContain("someone", md);
        Assert.DoesNotContain("raw", md.Split('\n').Where(l => l.StartsWith("Evidence")).FirstOrDefault() ?? "");
    }
}

public class CatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "daignostic-cat-" + Guid.NewGuid().ToString("N"));

    public CatalogTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Folder_listing_is_newest_first_and_json_only()
    {
        File.WriteAllText(Path.Combine(_dir, "old.json"), "{}");
        File.SetLastWriteTimeUtc(Path.Combine(_dir, "old.json"), DateTime.UtcNow.AddDays(-2));
        File.WriteAllText(Path.Combine(_dir, "new.json"), "{}");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "x");

        var items = CaptureCatalog.ListFolder(_dir);
        Assert.Equal(["new.json", "old.json"], items.Select(i => i.FileName));
    }

    [Fact]
    public void Missing_folder_gives_an_empty_list() =>
        Assert.Empty(CaptureCatalog.ListFolder(Path.Combine(_dir, "missing")));

    [Fact]
    public async Task Falls_back_to_the_folder_when_capframex_is_unreachable()
    {
        File.WriteAllText(Path.Combine(_dir, "a.json"), "{}");
        var http = new HttpClient(new StubHandler(_ => throw new HttpRequestException("down")));
        var settings = new AppSettings { CapturesFolder = _dir, UseCapFrameXMcp = true };

        var result = await CaptureCatalog.ListAsync(settings, http, CancellationToken.None);

        Assert.Equal("Captures folder", result.Source);
        Assert.Single(result.Items);
        Assert.Contains("Could not reach CapFrameX", result.Warning);
    }

    [Fact]
    public async Task Uses_the_folder_directly_when_mcp_is_disabled()
    {
        File.WriteAllText(Path.Combine(_dir, "a.json"), "{}");
        var http = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))));
        var result = await CaptureCatalog.ListAsync(new AppSettings { CapturesFolder = _dir, UseCapFrameXMcp = false }, http, CancellationToken.None);
        Assert.Null(result.Warning);
        Assert.Single(result.Items);
    }
}
