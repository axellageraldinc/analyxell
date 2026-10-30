using Daignostic.Core.Capture;
using Daignostic.Core.Config;

namespace Daignostic.Core.Tests;

public class CaptureSettingsTests
{
    private static CaptureInfo Info(string recordedAt, string id = "abc-123") =>
        new("Game", "game.exe", "CPU", "GPU", "1.0", "1920x1080", "DX12", "Fullscreen", "OS", "16GB", recordedAt, "1.0", Id: id);

    private static GameSettings Live(DateTime savedUtc) =>
        new("Game", "GameUserSettings.ini", savedUtc, [new GameSetting("ray_tracing", "Ray tracing", "On", false)]);

    [Fact]
    public void A_config_saved_before_the_capture_is_used_and_kept_for_that_capture()
    {
        var r = CaptureSettingsResolver.Resolve(Info("2026-05-01T12:00:00Z"), Live(new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc)), null);

        Assert.NotNull(r.Settings);
        Assert.True(r.SaveSnapshot);
        Assert.Null(r.Note);
    }

    [Fact]
    public void A_config_saved_after_the_capture_is_not_used_but_stays_available_to_choose()
    {
        var live = Live(new DateTime(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc));
        var r = CaptureSettingsResolver.Resolve(Info("2026-05-01T12:00:00Z"), live, null);

        Assert.Null(r.Settings);
        Assert.False(r.SaveSnapshot);
        Assert.Contains("may describe different settings", r.Note);
        Assert.Same(live, r.LiveConfig);
    }

    [Fact]
    public void Settings_saved_for_the_capture_win_over_whatever_the_config_says_now()
    {
        var snapshot = CaptureSettingsSnapshot.From(new GameSettings("Game", "x", new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc), [new GameSetting("ray_tracing", "Ray tracing", "Off", false)]));
        var r = CaptureSettingsResolver.Resolve(Info("2026-05-01T12:00:00Z"), Live(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)), snapshot);

        Assert.Equal("Off", r.Settings!.Get("ray_tracing"));
        Assert.Null(r.Note);
        Assert.False(r.SaveSnapshot);
    }

    [Fact]
    public void Snapshots_round_trip_per_capture_and_do_not_mix()
    {
        var dir = Path.Combine(Path.GetTempPath(), "daig-snap-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CaptureSettingsStore(dir);
            store.Save("one", CaptureSettingsSnapshot.From(Live(DateTime.UtcNow)));

            Assert.Equal("On", store.Load("one")!.ToSettings().Get("ray_tracing"));
            Assert.Null(store.Load("two"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Without_a_capture_id_the_key_falls_back_to_the_process_and_time()
    {
        Assert.Equal("game.exe-2026-05-01T12:00:00Z", CaptureSettingsResolver.KeyFor(Info("2026-05-01T12:00:00Z", id: "")));
    }
}
