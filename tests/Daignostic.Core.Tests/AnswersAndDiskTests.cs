using Daignostic.Core.Ai;
using Daignostic.Core.Analysis;
using Daignostic.Core.Capture;
using Daignostic.Core.SystemInfo;

namespace Daignostic.Core.Tests;

public class AnswersAndDiskTests
{
    [Fact]
    public void Generated_frames_are_not_counted_as_frames_above_the_display_refresh()
    {
        // Real frame, then a generated one 0.6 ms later; the screen changes every 13.3 ms (75 Hz) on a 180 Hz display.
        var frames = Enumerable.Range(0, 8000).Select(i => i % 2 == 0 ? 26.0 : 0.6).ToArray();
        var grid = TestData.SensorGrid(TestData.Times(frames)[^1] + 1);
        var run = new CaptureRun
        {
            TimeSec = TestData.Times(frames),
            FrameMs = frames,
            DisplayMs = TestData.Steady(8000, 13.3 * 0.9955),
            SensorTimeSec = grid,
            Sensors = [TestData.Sensor("/gpu/0/frequency/0", "Monitor Refresh Rate", "Frequency", TestData.Const(grid, 180))],
        };

        var extra = ExtraSignals.Compute(run);

        Assert.True((extra.FramesAboveRefreshPct ?? 0) < 1, $"was {extra.FramesAboveRefreshPct}");
    }

    [Fact]
    public void Drive_activity_gets_its_own_chart_with_plain_names()
    {
        var frames = TestData.Steady(3000, 10);
        var grid = TestData.SensorGrid(TestData.Times(frames)[^1] + 1);
        var run = TestData.Run(frames,
        [
            TestData.Sensor("/nvme/1/load/33", "Drive Total Activity", "Load", TestData.Const(grid, 5)),
            TestData.Sensor("/hdd/0/load/33", "Drive Total Activity", "Load", TestData.Const(grid, 40)),
        ], grid);

        Assert.Contains(ChartView.Disk, ChartDataBuilder.Available(run));
        var spec = ChartDataBuilder.Build(run, ChartView.Disk);
        Assert.Equal(["NVMe drive 1", "SATA or other drive 0"], spec.Lines.Select(l => l.Name));
        Assert.Equal(100, spec.FixedMax);
        Assert.DoesNotContain(ChartView.Disk, ChartDataBuilder.Available(TestData.Run(frames)));
    }

    [Fact]
    public void The_users_answers_travel_with_the_question_and_the_prompt_explains_how_to_use_them()
    {
        var result = CaptureAnalyzer.Analyze(TestData.File(TestData.Run(TestData.Steady(3000, 10))));
        var note = "Answers to your open questions:\n- Whether VSync was on: yes, in the driver";

        var message = AiPrompts.BuildSingle(result, note);

        Assert.Contains("Answers to your open questions", message);
        Assert.Contains("do not list those questions as unknown again", AiPrompts.System);
        Assert.Contains("not a Windows disk number", AiPrompts.System);
    }

    [Fact]
    public void The_disk_finding_names_the_kind_of_drive_and_what_is_known_about_the_game_drive()
    {
        var run = TestData.RunWithFreezes(95, 90);
        var system = new SystemContext(
        [
            new SystemSetting("game_drive.media", "Game drive type", "SSD", SettingSource.Auto),
            new SystemSetting("game_drive.bus", "Game drive connection", "NVMe", SettingSource.Auto),
        ]);

        var finding = CaptureAnalyzer.Analyze(TestData.File(run), system: system).Findings.First(f => f.Id == "DISK_ACTIVITY_AT_FREEZES");

        Assert.Contains("NVMe drive 1 (nvme/1)", finding.Detail);
        Assert.Contains("The game is installed on a SSD (NVMe)", finding.Detail);
    }

    [Fact]
    public void A_question_from_the_user_gets_its_own_answer_and_the_prompt_says_how_to_treat_earlier_output()
    {
        var reply = """{"summary":"x","inconclusive":false,"findings":[{"title":"t","explanation":"e","evidence":["fps.average"],"actions":[],"confidence":"low"}],"unknowns":[],"answer_to_question":"Probably not: the drive was idle."}""";
        var empty = """{"summary":"x","inconclusive":false,"findings":[{"title":"t","explanation":"e","evidence":["fps.average"],"actions":[],"confidence":"low"}],"unknowns":[],"answer_to_question":""}""";
        var keys = new HashSet<string> { "fps.average" };

        Assert.Equal("Probably not: the drive was idle.", AiResponseParser.Parse(reply, keys).QuestionAnswer);
        Assert.Null(AiResponseParser.Parse(empty, keys).QuestionAnswer);
        Assert.Contains("answer_to_question", AiPrompts.System);
        Assert.Contains("Your previous answer is not evidence", AiPrompts.System);
    }
}
