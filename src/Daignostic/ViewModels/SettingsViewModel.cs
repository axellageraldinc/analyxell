using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daignostic.Core.Ai;
using Daignostic.Core.Mcp;
using Daignostic.Core.Settings;
using Daignostic.Services;
using Microsoft.UI.Xaml.Controls;

namespace Daignostic.ViewModels;

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
    public partial bool IsTesting { get; set; }

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
        try
        {
            var reply = await Client().CompleteAsync("Reply with the single word: ok", "ping", CancellationToken.None);
            AiTestMsg.Success("The AI endpoint answered: " + (reply.Length > 60 ? reply[..60] + "..." : reply));
        }
        catch (AiException ex)
        {
            Report(ex, "");
        }
        finally
        {
            IsTesting = false;
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
