using Analyxell.Core.Analysis;
using Analyxell.Core.Capture;
using Analyxell.Core.Config;

namespace Analyxell.Core.Tests;

public class LibraryAndSettingsRuleTests
{
    private static GameSettings Settings(params (string Key, string Value)[] items) =>
        new("Game", "x.ini", DateTime.UtcNow, [.. items.Select(i => new GameSetting(i.Key, i.Key, i.Value, false))]);

    private static AnalysisResult Analyze(CaptureRun run, GameSettings settings) =>
        CaptureAnalyzer.Analyze(TestData.File(run), settings: settings);

    private static CaptureRun GpuBound(double ms = 25) =>
        TestData.Run(TestData.Steady(3000, ms), [TestData.Sensor("g", "GPU Core", "Load", TestData.Const(TestData.SensorGrid(TestData.Times(TestData.Steady(3000, ms))[^1] + 1), 98))],
            TestData.SensorGrid(TestData.Times(TestData.Steady(3000, ms))[^1] + 1));

    [Fact]
    public void Upscaling_off_while_the_gpu_is_the_limit_is_pointed_out()
    {
        var r = Analyze(GpuBound(), Settings(("dlss", "Off")));

        Assert.Contains(r.Findings, f => f.Id == "UPSCALING_OFF_GPU_BOUND");
        Assert.DoesNotContain(Analyze(GpuBound(), Settings(("dlss", "On"))).Findings, f => f.Id == "UPSCALING_OFF_GPU_BOUND");
    }

    [Fact]
    public void Frame_generation_is_only_questioned_when_something_is_wrong()
    {
        var hitchy = TestData.RunWithFreezes(95, 5);

        Assert.Contains(Analyze(hitchy, Settings(("frame_generation", "On (raw id 1)"))).Findings, f => f.Id == "FRAME_GENERATION_ON");
        Assert.DoesNotContain(Analyze(TestData.Run(TestData.Steady(3000, 10)), Settings(("frame_generation", "On"))).Findings, f => f.Id == "FRAME_GENERATION_ON");
        Assert.DoesNotContain(Analyze(hitchy, Settings(("frame_generation", "Off"))).Findings, f => f.Id == "FRAME_GENERATION_ON");
    }

    [Fact]
    public void The_game_s_own_vsync_setting_counts_when_the_user_entered_none()
    {
        var frames = TestData.Steady(6000, 8);
        var run = new CaptureRun
        {
            TimeSec = TestData.Times(frames),
            FrameMs = frames,
            AllowsTearing = frames.Select(_ => 1.0).ToArray(),
            SyncInterval = frames.Select(_ => 0.0).ToArray(),
        };

        var r = Analyze(run, Settings(("vsync", "On")));

        Assert.Contains(r.Findings, f => f.Id == "VSYNC_ENTERED_BUT_NOT_SEEN");
        Assert.DoesNotContain(r.Findings, f => f.Id.StartsWith("TEARING_P"));
    }

    private sealed class FakeReader(bool found) : IGameConfigReader
    {
        public string Name => "Fake Game";

        public bool Matches(CaptureInfo info) => info.ProcessName == "fake.exe";

        public GameSettings? TryRead() => found ? Settings(("ray_tracing", "On")) : null;
    }

    [Fact]
    public void The_library_lists_built_in_games_and_games_the_user_added_with_what_is_known_about_each()
    {
        var profiles = new List<GameProfile>
        {
            new() { ProcessName = "mine.exe", Game = "My Game", Items = [new ProfileItem("shadows", "high")], SavedAtUtc = DateTime.UtcNow.ToString("o") },
            new() { ProcessName = "bare.exe", Game = "Bare Game" },
        };

        var entries = GameLibrary.Build(profiles, [new FakeReader(found: true)]);

        Assert.Equal(LibraryStatus.BuiltIn, entries.First(e => e.Game == "Fake Game").Status);
        Assert.Equal(LibraryStatus.Entered, entries.First(e => e.Game == "My Game").Status);
        var bare = entries.First(e => e.Game == "Bare Game");
        Assert.Equal(LibraryStatus.CapturesOnly, bare.Status);
        Assert.Contains("Diagnosable", bare.StatusText);
        Assert.True(bare.IsUserAdded);
    }

    [Fact]
    public void A_built_in_game_whose_config_is_missing_says_so()
    {
        var entry = Assert.Single(GameLibrary.Build([], [new FakeReader(found: false)]));

        Assert.Contains("not found", entry.StatusText);
    }

    [Fact]
    public void A_seen_game_that_has_a_reader_is_one_entry_not_two()
    {
        var entries = GameLibrary.Build([new GameProfile { ProcessName = "fake.exe", Game = "Fake Game" }], [new FakeReader(found: true)]);

        Assert.Single(entries);
    }

    [Fact]
    public void Entered_settings_count_as_saved_when_the_profile_was_saved()
    {
        var saved = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);
        var profile = new GameProfile { ProcessName = "a.exe", Game = "A", Items = [new ProfileItem("k", "v")], SavedAtUtc = saved.ToString("o") };

        Assert.Equal(saved, GameLibrary.ProfileAsSettings(profile)!.LastModifiedUtc);
        Assert.Null(GameLibrary.ProfileAsSettings(new GameProfile { ProcessName = "a.exe" }));
    }

    [Fact]
    public void Games_seen_in_captures_are_remembered_without_overwriting_what_the_user_entered()
    {
        var dir = Path.Combine(Path.GetTempPath(), "daig-games-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new GameProfileStore(dir);
            store.EnsureSeen("seen.exe", "Seen Game");
            store.Save(new GameProfile { ProcessName = "mine.exe", Game = "Mine", Items = [new ProfileItem("k", "v")] });
            store.EnsureSeen("mine.exe", "Other Name");

            Assert.Equal(2, store.List().Count);
            Assert.Single(store.Load("mine.exe")!.Items);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
