using Analyxell.Core.Config;

namespace Analyxell.Core.Tests;

public class WikiTests
{
    private const string Page = """
        ==Game data==
        ===Configuration file(s) location===
        {{Game data|
        {{Game data/config|Windows|{{P|localappdata}}\Remedy\CONTROLResonant\renderer.ini}}
        {{Game data/config|Linux|{{P|linux}}/x.ini}}
        }}

        ==Video==
        {{Image|Control Resonant - Graphics.jpg|Graphics settings}}
        {{Video
        |upscaling                  = true
        |upscaling tech             = DLSS 4.5, FSR 4.1
        |upscaling notes            = Supports DLSS Ray Reconstruction 4.5 (model F<ref>{{Refcheck|user=X|date=2026-09-25|comment=}}</ref>).
        |framegen                   = true
        |framegen tech              = DLSS MFG, FSR FG
        |vsync                      = true
        |vsync notes                = Off, Full, Half, One-Third
        |120 fps notes              = Range from 30-360, or uncapped.
        |ray tracing notes          = '''Direct''' and '''Indirect Lighting''' options producing [[global illumination]] with [[Ray tracing|ray-traced]] shadows.<br>Path tracing is used.
        }}
        ==Input==
        """;

    [Fact]
    public void The_config_path_and_what_the_game_offers_are_read_from_the_wiki_page_without_markup()
    {
        var info = PcGamingWikiParser.Parse("Control Resonant", Page)!;

        Assert.Equal([@"%LOCALAPPDATA%\Remedy\CONTROLResonant\renderer.ini"], info.ConfigPaths);
        var features = info.Features.ToDictionary(f => f.Key, f => f.Value);
        Assert.Equal("DLSS 4.5, FSR 4.1", features["upscaling"]);
        Assert.Equal("DLSS MFG, FSR FG", features["frame_generation"]);
        Assert.Equal("Off, Full, Half, One-Third", features["vsync_modes"]);
        Assert.Equal("Range from 30-360, or uncapped.", features["fps_cap"]);
        Assert.Equal("Direct and Indirect Lighting options producing global illumination with ray-traced shadows. Path tracing is used.", features["ray_tracing"]);
        Assert.DoesNotContain("Refcheck", features["upscaling_notes"]);
        Assert.DoesNotContain("<ref", features["upscaling_notes"]);
        Assert.Contains("wiki/Control_Resonant", info.PageUrl);
    }

    [Theory]
    [InlineData("CONTROLResonant", "CONTROL Resonant")]
    [InlineData("Black Myth: Wukong", "Black Myth Wukong")]
    [InlineData("DEATH STRANDING 2: ON THE BEACH", "DEATH STRANDING 2 ON THE BEACH")]
    public void A_process_style_name_is_also_tried_with_spaces_and_without_punctuation(string name, string expected)
    {
        Assert.Equal(name, PcGamingWikiClient.NameVariants(name)[0]);
        Assert.Contains(expected, PcGamingWikiClient.NameVariants(name));
    }

    [Fact]
    public void A_page_without_anything_useful_gives_nothing_rather_than_a_guess()
    {
        Assert.Null(PcGamingWikiParser.Parse("X", "just some text"));
        Assert.Null(PcGamingWikiParser.Parse("X", ""));
    }

    [Fact]
    public void Config_paths_resolve_only_to_things_that_exist_and_game_folder_needs_a_known_install()
    {
        string[] templates = [@"<game folder>\b1\Saved\Config", @"%LOCALAPPDATA%\Remedy\CONTROLResonant\renderer.ini", @"%APPDATA%\*\x.ini"];
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"D:\Games\Control\b1\Saved\Config", @"C:\Local\Remedy\CONTROLResonant\renderer.ini" };
        string Expand(string p) => p.Replace("%LOCALAPPDATA%", @"C:\Local", StringComparison.Ordinal);

        Assert.Equal(@"C:\Local\Remedy\CONTROLResonant\renderer.ini", ConfigPathResolver.Resolve(templates, null, existing.Contains, Expand));
        Assert.Equal(@"D:\Games\Control\b1\Saved\Config", ConfigPathResolver.Resolve(templates, @"D:\Games\Control", existing.Contains, Expand));
        Assert.Null(ConfigPathResolver.Resolve([@"%APPDATA%\*\x.ini"], null, _ => true, Expand));
    }

    [Fact]
    public void The_game_s_offers_reach_the_ai_as_facts_that_say_they_are_not_what_is_switched_on()
    {
        var facts = new List<ProfileItem> { new("upscaling", "DLSS 4.5, FSR 4.1") };
        var result = Analyxell.Core.Analysis.CaptureAnalyzer.Analyze(TestData.File(TestData.Run(TestData.Steady(3000, 10))), gameFacts: facts);

        Assert.Equal("DLSS 4.5, FSR 4.1", result.Facts["game.upscaling"]);
        Assert.Contains("not what is switched on", result.Facts["game.info_source"]);
        Assert.Contains("OFFERS", Analyxell.Core.Ai.AiPrompts.System);
    }
}
