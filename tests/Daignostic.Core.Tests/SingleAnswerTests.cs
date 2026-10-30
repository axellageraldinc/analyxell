using Daignostic.Core.Ai;

namespace Daignostic.Core.Tests;

public class SingleAnswerTests
{
    [Theory]
    [InlineData(nameof(AiPrompts.System))]
    [InlineData(nameof(AiExportPack.ConversationalSystem))]
    public void Prompts_forbid_asking_the_user_questions_and_say_what_to_do_instead(string which)
    {
        var prompt = which == nameof(AiPrompts.System) ? AiPrompts.System : AiExportPack.ConversationalSystem;
        Assert.Contains("NOT ask the user questions", prompt.Replace("NEVER ask the user a question", "NOT ask the user questions"), StringComparison.Ordinal);
        Assert.Contains("if X, then Y", prompt);
    }

    [Fact]
    public void Built_in_prompt_tells_the_model_it_cannot_look_anything_up()
    {
        Assert.Contains("no access to the user's PC", AiPrompts.System);
        Assert.Contains("single question and a single answer", AiPrompts.System);
    }

    [Fact]
    public void Unknowns_are_parsed_trimmed_to_six_and_reported()
    {
        var reply = """{"summary":"s","findings":[{"title":"t","evidence":["fps.p1"]}],"unknowns":["a","b","c","d","e","f","g"]}""";
        var a = AiResponseParser.Parse(reply, new HashSet<string> { "fps.p1" });
        Assert.Equal(6, a.Unknowns!.Count);
        Assert.Contains("a", a.Unknowns);

        var md = Daignostic.Core.Reporting.MarkdownReport.Build(
            Daignostic.Core.Analysis.CaptureAnalyzer.Analyze(TestData.File(TestData.Run(TestData.Steady(3000, 10)))), a);
        Assert.Contains("could not tell", md);
    }

    [Fact]
    public void A_reply_without_unknowns_is_fine()
    {
        var a = AiResponseParser.Parse("""{"summary":"s","findings":[{"title":"t","evidence":["fps.p1"]}]}""", new HashSet<string> { "fps.p1" });
        Assert.Empty(a.Unknowns!);
    }
}
