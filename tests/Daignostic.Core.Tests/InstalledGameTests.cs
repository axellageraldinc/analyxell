using Daignostic.Core.Config;
using Daignostic.Core.SystemInfo;

namespace Daignostic.Core.Tests;

public class InstalledGameTests
{
    private static FakeProbe SteamOnTwoDrives()
    {
        var probe = new FakeProbe();
        probe.Strings["HKCU|Software\\Valve\\Steam|SteamPath"] = "c:/steam";
        probe.Files[@"c:\steam\steamapps\libraryfolders.vdf"] = "\"libraryfolders\" { \"0\" { \"path\" \"d:\\\\SteamLibrary\" } }";
        probe.Files[@"c:\steam\steamapps\appmanifest_1.acf"] = "\"AppState\" { \"name\" \"Control\" \"installdir\" \"Control\" }";
        probe.Files[@"d:\SteamLibrary\steamapps\appmanifest_2.acf"] = "\"AppState\" { \"name\" \"DEATH STRANDING 2\" \"installdir\" \"DS2\" }";
        probe.Files[@"d:\SteamLibrary\steamapps\appmanifest_3.acf"] = "\"AppState\" { \"name\" \"Steamworks Common Redistributables\" \"installdir\" \"Redist\" }";
        probe.Dirs[@"c:\steam\steamapps\common\Control"] = new DirectoryTotals(0, 0, false);
        probe.Dirs[@"d:\SteamLibrary\steamapps\common\DS2"] = new DirectoryTotals(0, 0, false);
        probe.Dirs[@"d:\SteamLibrary\steamapps\common\Redist"] = new DirectoryTotals(0, 0, false);
        return probe;
    }

    [Fact]
    public void Games_on_every_steam_library_drive_are_found_and_tools_are_left_out()
    {
        var games = InstalledGameFinder.Find(SteamOnTwoDrives());

        Assert.Equal(["Control", "DEATH STRANDING 2"], games.Select(g => g.Name));
        Assert.Equal("D:", games.First(g => g.Name == "DEATH STRANDING 2").Drive);
        Assert.All(games, g => Assert.Equal("Steam", g.Launcher));
    }

    [Fact]
    public void Epic_manifests_are_read_and_damaged_ones_are_ignored()
    {
        var game = InstalledGameFinder.ParseEpic("{\"DisplayName\":\"Some Game\",\"InstallLocation\":\"E:\\\\Games\\\\Some\"}");

        Assert.Equal("Some Game", game!.Name);
        Assert.Equal("Epic Games", game.Launcher);
        Assert.Null(InstalledGameFinder.ParseEpic("not json"));
        Assert.Null(InstalledGameFinder.ParseEpic("{\"DisplayName\":\"\"}"));
    }

    [Fact]
    public void The_library_lists_installed_games_nobody_has_captured_and_does_not_duplicate_known_ones()
    {
        var detected = InstalledGameFinder.Find(SteamOnTwoDrives());
        var profiles = new List<GameProfile> { new() { ProcessName = "control.exe", Game = "Control" } };

        var entries = GameLibrary.Build(profiles, [], detected);

        Assert.Single(entries, e => e.Game == "Control");
        Assert.Equal(@"c:\steam\steamapps\common\Control", entries.First(e => e.Game == "Control").InstallPath);
        var ds2 = entries.First(e => e.Game == "DEATH STRANDING 2");
        Assert.Equal(LibraryStatus.Detected, ds2.Status);
        Assert.Contains("D:", ds2.StatusText);
    }
}
