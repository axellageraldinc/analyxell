using Daignostic.Core.Ai;
using Daignostic.Core.Config;
using Daignostic.Core.Settings;
using Daignostic.Core.SystemInfo;

namespace Daignostic.Services;

/// <summary>Application-wide services, composed by hand (the app is small enough not to need a DI container).</summary>
internal static class AppServices
{
    public const string AiKeyName = "aikey";

    public static SettingsStore SettingsStore { get; } = new(SettingsStore.DefaultPath);

    public static DpapiSecretStore Secrets { get; } = new(DpapiSecretStore.DefaultDirectory);

    public static GameProfileStore Profiles { get; } = new(GameProfileStore.DefaultDirectory);

    public static UserSetupStore Setup { get; } = new(UserSetupStore.DefaultPath);

    public static ISystemProbe Probe { get; } = new WindowsSystemProbe();

    public static HttpClient Http { get; } = new() { Timeout = TimeSpan.FromSeconds(120) };

    public static IReadOnlyList<IGameConfigReader> ConfigReaders { get; } = GameConfigReaders.CreateDefaults();

    public static AppSettings Settings { get; private set; } = new();

    /// <summary>An API key kept in memory only, used when the user chose not to save it to disk.</summary>
    public static string? SessionApiKey { get; set; }

    public static string KeyFilePath => Path.Combine(DpapiSecretStore.DefaultDirectory, AiKeyName + ".bin");

    public static void Reload() => Settings = SettingsStore.Load();

    public static void Save(AppSettings settings)
    {
        SettingsStore.Save(settings);
        Settings = settings;
    }

    public static string? GetApiKey() => SessionApiKey ?? Secrets.Get(AiKeyName);

    public static bool HasStoredKey => Secrets.Get(AiKeyName) is not null;

    public static OpenAiCompatibleClient CreateOpenAiClient() =>
        new(Http, new AiOptions(Settings.AiEndpoint.Trim(), Settings.AiModel.Trim()), GetApiKey);

    public static AiAnalyst CreateAnalyst() => new(CreateOpenAiClient());
}
