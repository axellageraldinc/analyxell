using Daignostic.Core.Ai;
using Daignostic.Core.Config;
using Daignostic.Core.Mcp;
using Daignostic.Core.Settings;

namespace Daignostic.Services;

/// <summary>Application-wide services, composed by hand (the app is small enough not to need a DI container).</summary>
internal static class AppServices
{
    public const string AiKeyName = "aikey";

    public static SettingsStore SettingsStore { get; } = new(SettingsStore.DefaultPath);

    public static DpapiSecretStore Secrets { get; } = new(DpapiSecretStore.DefaultDirectory);

    public static HttpClient Http { get; } = new() { Timeout = TimeSpan.FromSeconds(120) };

    public static IReadOnlyList<IGameConfigReader> ConfigReaders { get; } = GameConfigReaders.CreateDefaults();

    public static AppSettings Settings { get; private set; } = new();

    public static void Reload() => Settings = SettingsStore.Load();

    public static void Save(AppSettings settings)
    {
        SettingsStore.Save(settings);
        Settings = settings;
    }

    public static AiAnalyst CreateAnalyst() => new(CreateAiClient());

    public static IAiClient CreateAiClient() =>
        new OpenAiCompatibleClient(Http, new AiOptions(Settings.AiEndpoint, Settings.AiPreset), () => Secrets.Get(AiKeyName));
}
