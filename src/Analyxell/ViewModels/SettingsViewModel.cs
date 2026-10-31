using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Analyxell.Core.Ai;
using Analyxell.Core.Mcp;
using Analyxell.Core.Settings;
using Analyxell.Core.Updates;
using Analyxell.Services;
using Microsoft.UI.Xaml.Controls;

namespace Analyxell.ViewModels;

/// <summary>
/// Every problem is reported next to the field it is about (see the *Msg properties), never in one banner at the
/// top of the page. That way the user sees exactly what to fix and where.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public SettingsViewModel()
    {
        var s = AppServices.Settings;
        CapturesFolder = s.CapturesFolder;
        UseMcp = s.UseCapFrameXMcp;
        McpUrl = s.CapFrameXMcpUrl;
        AiEnabled = s.AiEnabled;
        AiEndpoint = s.AiEndpoint;
        AiModel = s.AiModel;
        SaveApiKey = s.SaveApiKey;
        ShareBackgroundNames = s.ShareBackgroundAppNames;
        HasSavedKey = AppServices.HasStoredKey;
        NewApiKey = "";
        KeyStoredInfo = "";
        CapFrameX = new CapFrameXViewModel();
    }

    public CapFrameXViewModel CapFrameX { get; }

    public ObservableCollection<string> Models { get; } = [];

    public FieldMessage FolderMsg { get; } = new();

    public FieldMessage McpUrlMsg { get; } = new();

    public FieldMessage McpStatusMsg { get; } = new();

    public FieldMessage EndpointMsg { get; } = new();

    public FieldMessage ModelMsg { get; } = new();

    public FieldMessage KeyMsg { get; } = new();

    public FieldMessage AiTestMsg { get; } = new();

    public FieldMessage SaveMsg { get; } = new();

    public FieldMessage UpdateMsg { get; } = new();

    public string CurrentVersionText { get; } = "You have version " + CurrentVersion.ToString(3) + (IsInstalled ? " (installer)." : " (portable).");

    private static Version CurrentVersion => typeof(SettingsViewModel).Assembly.GetName().Version ?? new Version(0, 0, 0);

    private static bool IsInstalled
    {
        get
        {
            try
            {
                _ = Windows.ApplicationModel.Package.Current.Id;
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                return false;
            }
        }
    }

    private string? updateUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheckUpdate))]
    public partial bool IsCheckingUpdate { get; set; }

    public bool CanCheckUpdate => !IsCheckingUpdate;

    [ObservableProperty]
    public partial bool UpdateAvailable { get; set; }

    [ObservableProperty]
    public partial string CapturesFolder { get; set; }

    [ObservableProperty]
    public partial bool UseMcp { get; set; }

    [ObservableProperty]
    public partial string McpUrl { get; set; }

    [ObservableProperty]
    public partial bool AiEnabled { get; set; }

    [ObservableProperty]
    public partial string AiEndpoint { get; set; }

    [ObservableProperty]
    public partial string AiModel { get; set; }

    /// <summary>The PasswordBox pushes into this; it is cleared after saving so the key does not linger in memory.</summary>
    [ObservableProperty]
    public partial string NewApiKey { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyStatus))]
    public partial bool SaveApiKey { get; set; }

    [ObservableProperty]
    public partial bool ShareBackgroundNames { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyStatus))]
    public partial bool HasSavedKey { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeyInfo))]
    public partial string KeyStoredInfo { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotTesting))]
    public partial bool IsTesting { get; set; }

    public bool NotTesting => !IsTesting;

    public bool HasModels => Models.Count > 0;

    public bool HasKeyInfo => KeyStoredInfo.Length > 0;

    public string KeyStatus => HasSavedKey
        ? "A key is saved on this PC, encrypted for your Windows account."
        : SaveApiKey
            ? "No API key saved. Local gateways without authentication work without one."
            : "The key will be kept in memory for this session only and never written to disk.";

    partial void OnCapturesFolderChanged(string value) => FolderMsg.Clear();

    partial void OnMcpUrlChanged(string value) => McpUrlMsg.Clear();

    partial void OnAiEndpointChanged(string value) => EndpointMsg.Clear();

    partial void OnAiModelChanged(string value) => ModelMsg.Clear();

    partial void OnNewApiKeyChanged(string value) => KeyMsg.Clear();

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var folder = await Pickers.PickFolderAsync();
        if (folder is not null)
        {
            CapturesFolder = folder;
        }
    }

    [RelayCommand]
    private void Save()
    {
        ClearAll();
        var problems = 0;

        if (!string.IsNullOrWhiteSpace(CapturesFolder) && !Directory.Exists(CapturesFolder.Trim()))
        {
            FolderMsg.Error("This folder does not exist. Pick an existing folder, or leave it empty.");
            problems++;
        }

        if (UseMcp)
        {
            if (!Uri.TryCreate(McpUrl.Trim(), UriKind.Absolute, out var mcp))
            {
                McpUrlMsg.Error("This is not a valid address. The usual one is http://localhost:1337/mcp.");
                problems++;
            }
            else if (mcp.Scheme is not ("http" or "https"))
            {
                McpUrlMsg.Error("The address must start with http:// or https://.");
                problems++;
            }
        }

        if (AiEnabled)
        {
            if (string.IsNullOrWhiteSpace(AiEndpoint))
            {
                EndpointMsg.Error("Enter the address of your AI service, for example http://127.0.0.1:8480 or https://api.example.com.");
                problems++;
            }
            else if (!Uri.TryCreate(AiEndpoint.Trim(), UriKind.Absolute, out var ai) || (ai.Scheme != Uri.UriSchemeHttp && ai.Scheme != Uri.UriSchemeHttps))
            {
                EndpointMsg.Error("This is not a valid address. It must start with http:// or https://.");
                problems++;
            }

            if (string.IsNullOrWhiteSpace(AiModel))
            {
                ModelMsg.Error("Enter a model name, or press List models to pick one from your endpoint.");
                problems++;
            }
        }

        if (problems > 0)
        {
            SaveMsg.Warning(problems == 1 ? "Not saved yet: one field above needs attention." : $"Not saved yet: {problems} fields above need attention.");
            return;
        }

        var settings = AppServices.Settings.Clone();
        settings.CapturesFolder = CapturesFolder.Trim();
        settings.UseCapFrameXMcp = UseMcp;
        settings.CapFrameXMcpUrl = McpUrl.Trim();
        settings.AiEnabled = AiEnabled;
        settings.AiEndpoint = AiEndpoint.Trim().TrimEnd('/');
        settings.AiModel = AiModel.Trim();
        settings.SaveApiKey = SaveApiKey;
        settings.ShareBackgroundAppNames = ShareBackgroundNames;
        AppServices.Save(settings);

        if (!string.IsNullOrWhiteSpace(NewApiKey))
        {
            if (SaveApiKey)
            {
                AppServices.Secrets.Set(AppServices.AiKeyName, NewApiKey.Trim());
                AppServices.SessionApiKey = null;
            }
            else
            {
                AppServices.SessionApiKey = NewApiKey.Trim();
                AppServices.Secrets.Delete(AppServices.AiKeyName);
            }

            NewApiKey = "";
        }
        else if (!SaveApiKey)
        {
            // Switching to session-only: forget any key that was stored on disk.
            AppServices.Secrets.Delete(AppServices.AiKeyName);
        }

        HasSavedKey = AppServices.HasStoredKey;
        SaveMsg.Success("Settings saved.");
    }

    [RelayCommand]
    private void ClearKey()
    {
        AppServices.Secrets.Delete(AppServices.AiKeyName);
        AppServices.SessionApiKey = null;
        HasSavedKey = false;
        KeyStoredInfo = "";
        KeyMsg.Success("The API key was deleted from this PC.");
    }

    /// <summary>Runs only when the user presses the button. Never automatic (see docs/privacy.md).</summary>
    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        IsCheckingUpdate = true;
        UpdateAvailable = false;
        UpdateMsg.Info("Checking GitHub for the newest release...");
        try
        {
            var kind = IsInstalled ? InstallKind.Installer : InstallKind.Portable;
            var latest = await new UpdateChecker(AppServices.Http).GetLatestAsync(kind, CancellationToken.None);
            if (latest is null)
            {
                UpdateMsg.Warning("GitHub's answer did not contain a version number, so analyxell cannot tell whether there is an update.");
            }
            else if (UpdateChecker.IsNewer(latest.Version, CurrentVersion))
            {
                updateUrl = latest.DownloadUrl;
                UpdateAvailable = true;
                UpdateMsg.Success(IsInstalled
                    ? $"Version {latest.Version.ToString(3)} is available. Download it, then open the file to install over this version."
                    : $"Version {latest.Version.ToString(3)} is available. Download the zip, close analyxell, and unzip it over this folder (or into a new one).");
            }
            else
            {
                UpdateMsg.Success("You are on the latest version.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            UpdateMsg.Error("Could not reach GitHub: " + ex.Message + " You can download releases yourself from https://github.com/axellageraldinc/analyxell/releases");
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (updateUrl is null || !await Windows.System.Launcher.LaunchUriAsync(new Uri(updateUrl)))
        {
            UpdateMsg.Error("Your browser could not be opened. Releases are at https://github.com/axellageraldinc/analyxell/releases");
        }
    }

    /// <summary>Lets the user see for themselves that the stored key is encrypted, not readable text.</summary>
    [RelayCommand]
    private void ShowStoredKey()
    {
        var path = AppServices.KeyFilePath;
        if (!File.Exists(path))
        {
            KeyStoredInfo = "Nothing is stored on disk.";
            return;
        }

        var bytes = File.ReadAllBytes(path);
        var head = string.Join(" ", bytes.Take(24).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
        KeyStoredInfo = $"File: {path}\nSize: {bytes.Length} bytes of encrypted data.\nFirst bytes: {head} ...\nThis is a Windows DPAPI blob. It is not your key in readable form, and it cannot be decrypted on another Windows account or another PC.";
    }

    [RelayCommand]
    private async Task OpenCapFrameXFolderAsync()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CapFrameX");
        var folder = Directory.Exists(Path.Combine(root, "Configuration")) ? Path.Combine(root, "Configuration") : root;
        if (!Directory.Exists(folder))
        {
            McpStatusMsg.Info("CapFrameX's settings folder was not found. If you use the portable version, open the Configuration folder next to CapFrameX.exe.");
            return;
        }

        await Windows.System.Launcher.LaunchFolderPathAsync(folder);
    }

    [RelayCommand]
    private async Task TestMcpAsync()
    {
        IsTesting = true;
        McpStatusMsg.Clear();
        try
        {
            if (!Uri.TryCreate(McpUrl.Trim(), UriKind.Absolute, out var url))
            {
                McpUrlMsg.Error("This is not a valid address. The usual one is http://localhost:1337/mcp.");
                return;
            }

            var diagnosis = await new CapFrameXMcpClient(AppServices.Http, url).DiagnoseAsync(CancellationToken.None);
            if (diagnosis.Status == Core.Mcp.McpStatus.Connected)
            {
                McpStatusMsg.Success(diagnosis.Message);
            }
            else
            {
                McpStatusMsg.Warning(diagnosis.Message);
            }
        }
        finally
        {
            IsTesting = false;
        }
    }

    private CancellationTokenSource? _testCts;

    [RelayCommand]
    private async Task ListModelsAsync()
    {
        IsTesting = true;
        ModelMsg.Clear();
        EndpointMsg.Clear();
        KeyMsg.Clear();
        try
        {
            var models = await Client().ListModelsAsync(CancellationToken.None);
            Models.Clear();
            foreach (var m in models)
            {
                Models.Add(m);
            }

            OnPropertyChanged(nameof(HasModels));
            if (models.Count == 0)
            {
                ModelMsg.Warning("The endpoint answered but lists no models. Type the model name yourself.");
            }
            else
            {
                ModelMsg.Success($"{models.Count} model(s) found. Pick one below, or type a name.");
            }
        }
        catch (AiException ex)
        {
            Report(ex, " You can still type a model name yourself.");
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task TestAiAsync()
    {
        IsTesting = true;
        AiTestMsg.Clear();
        EndpointMsg.Clear();
        ModelMsg.Clear();
        KeyMsg.Clear();
        _testCts = new CancellationTokenSource();
        var cts = _testCts;
        _ = ShowWaitingAsync(cts.Token);
        try
        {
            var reply = await Client().CompleteAsync("Reply with the single word: ok", "ping", cts.Token);
            cts.Cancel();
            AiTestMsg.Success("The AI endpoint answered: " + (reply.Length > 60 ? reply[..60] + "..." : reply));
        }
        catch (AiException ex)
        {
            cts.Cancel();
            Report(ex, "");
        }
        catch (OperationCanceledException)
        {
            AiTestMsg.Warning("Cancelled. Nothing was changed.");
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private void CancelTest() => _testCts?.Cancel();

    /// <summary>Keeps the user informed while the AI is slow: how long it has been and when analyxell gives up.</summary>
    private async Task ShowWaitingAsync(CancellationToken stop)
    {
        var started = DateTime.UtcNow;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var elapsed = DateTime.UtcNow - started;
                var left = Math.Max(0, AppServices.AiTimeoutSeconds - (int)elapsed.TotalSeconds);
                AiTestMsg.Info($"Waiting for the AI to answer... {(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}. analyxell gives up after {AppServices.AiTimeoutSeconds / 60} minutes ({left} s left). Some models take a while to start; you can cancel.");
                await Task.Delay(500, stop);
            }
        }
        catch (OperationCanceledException)
        {
            // The test finished or was cancelled.
        }
    }

    private OpenAiCompatibleClient Client()
    {
        var key = string.IsNullOrWhiteSpace(NewApiKey) ? AppServices.GetApiKey() : NewApiKey.Trim();
        return new OpenAiCompatibleClient(AppServices.Http, new AiOptions(AiEndpoint.Trim(), AiModel.Trim()), () => key);
    }

    /// <summary>Puts an AI problem under the field it points at, and a short pointer under the button that was pressed.</summary>
    private void Report(AiException ex, string suffix)
    {
        var text = ex.Message + suffix;
        switch (ex.Kind)
        {
            case AiErrorKind.Endpoint:
                EndpointMsg.Error(text);
                break;
            case AiErrorKind.Key:
                KeyMsg.Error(text);
                break;
            case AiErrorKind.Model:
                ModelMsg.Error(text);
                break;
            default:
                AiTestMsg.Error(text);
                return;
        }

        AiTestMsg.Warning("It did not work. See the message under the field it is about.");
    }

    private void ClearAll()
    {
        FolderMsg.Clear();
        McpUrlMsg.Clear();
        EndpointMsg.Clear();
        ModelMsg.Clear();
        KeyMsg.Clear();
        AiTestMsg.Clear();
        SaveMsg.Clear();
    }
}
