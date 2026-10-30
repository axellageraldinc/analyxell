namespace Daignostic.Core.Background;

public sealed record KnownApp(string Category, string Advice);

/// <summary>
/// Programs that commonly compete with a game for CPU, disk or GPU. Matching is by process name, so it is a hint
/// ("this is running") and never proof that the program caused a problem.
/// </summary>
public static class KnownApps
{
    private static readonly Dictionary<string, KnownApp> Apps = Build();

    public static KnownApp? Find(string processName)
    {
        var key = BackgroundScanner.NormalizeProcess(processName);
        return Apps.TryGetValue(key, out var app) ? app : null;
    }

    private static Dictionary<string, KnownApp> Build()
    {
        var map = new Dictionary<string, KnownApp>(StringComparer.OrdinalIgnoreCase);

        void Add(string category, string advice, params string[] names)
        {
            foreach (var n in names)
            {
                map[n] = new KnownApp(category, advice);
            }
        }

        Add("Recorder", "Screen recorders and streaming tools use the GPU encoder and CPU. Close them, or lower their quality, when you do not need them.",
            "obs64", "obs32", "streamlabs obs", "bandicam", "fraps", "xsplit.core", "medal", "outplayed", "action_x64");
        Add("Overlay", "In-game overlays hook into every frame. Try turning the overlay off in the app's settings.",
            "discord", "discordptb", "discordcanary", "gamebar", "gamebarftserver", "gamebarpresencewriter", "nvidia overlay", "nvidia share", "nvsphelper64", "galaxyclient", "overwolf");
        Add("Browser", "Browsers with video, many tabs or hardware acceleration use CPU, GPU and memory. Close them while gaming.",
            "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "msedgewebview2");
        Add("Launcher", "Game launchers can download, update or scan in the background. Pause downloads while you play.",
            "steam", "steamwebhelper", "epicgameslauncher", "epicwebhelper", "battle.net", "agent", "eadesktop", "origin", "ubisoftconnect", "upc", "goggalaxy", "riotclientservices");
        Add("Cloud sync", "Cloud sync reads and writes files continuously. Pause syncing while you play.",
            "onedrive", "dropbox", "googledrivefs", "megasync", "icloudservices");
        Add("Security scan", "Antivirus scans and Windows Defender can use a lot of disk and CPU. Avoid scheduled scans during play.",
            "msmpeng", "mpdefendercoreservice", "nissrv", "mbamservice", "avgsvc", "avastsvc", "avp", "mcshield", "sentinelagent");
        Add("Indexing/update", "Windows search indexing and updates use disk and CPU. Let them finish, or schedule them outside play time.",
            "searchindexer", "searchhost", "tiworker", "trustedinstaller", "musnotification", "wuauclt", "usoclient", "compattelrunner");
        Add("Vendor software", "Hardware control software (RGB, fan, mouse, keyboard) can poll devices heavily. Close it, or reduce its effects.",
            "icue", "lghub", "lghub_agent", "armourycrate.service", "armourycrate.usersessionhelper", "synapse3", "razer synapse service", "steelseriesgg", "steelseriesengine", "nahimicservice", "corsair.service");
        Add("Monitoring tool", "Monitoring tools add a little overhead. That is fine while measuring, but close extra ones you do not need.",
            "hwinfo64", "hwinfo32", "hwmonitor", "capframex", "msiafterburner", "rtss", "rtsshooksloader64", "cpuz", "gpuz", "openhardwaremonitor", "librehardwaremonitor", "presentmon");
        Add("Downloader", "Downloads and torrents use disk and network. Pause them while you play.",
            "utorrent", "qbittorrent", "bittorrent", "deluge", "transmission-qt", "idman");
        Add("Media", "Music or video players use little CPU, but video playback uses the GPU. Close video players.",
            "spotify", "vlc", "wmplayer", "musicbee", "itunes");
        Add("Virtual machine", "Virtual machines reserve CPU cores and memory even when idle. Shut them down before gaming.",
            "vmmem", "vmmemwsl", "vmware-vmx", "virtualboxvm", "vmwp", "docker desktop", "com.docker.backend");
        Add("Remote access", "Remote desktop tools capture the screen continuously. Close them while gaming.",
            "teamviewer", "teamviewer_service", "anydesk", "parsecd", "rustdesk", "mstsc");
        return map;
    }
}
