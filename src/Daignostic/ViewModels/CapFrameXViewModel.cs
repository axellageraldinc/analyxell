using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daignostic.Core.SystemInfo;
using Daignostic.Services;

namespace Daignostic.ViewModels;

/// <summary>
/// Whether CapFrameX is installed, with ways to get it or open it. daignostic only starts the program when the user
/// presses the button; it never records, configures or controls CapFrameX.
/// </summary>
public sealed partial class CapFrameXViewModel : ObservableObject
{
    public CapFrameXViewModel()
    {
        Headline = "Looking for CapFrameX...";
        Detail = "";
        Message = new FieldMessage();
        Refresh();
    }

    public FieldMessage Message { get; }

    [ObservableProperty]
    public partial string Headline { get; set; }

    [ObservableProperty]
    public partial string Detail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotFound), nameof(HeaderButtonText))]
    public partial bool Found { get; set; }

    public string? ExePath { get; private set; }

    public bool NotFound => !Found;

    public string HeaderButtonText => Found ? "Open CapFrameX" : "Get CapFrameX";

    public void Refresh() => _ = RefreshAsync();

    public async Task RefreshAsync()
    {
        var install = await Task.Run(() => CapFrameXLocator.Find(AppServices.Probe, AppServices.Settings.CapFrameXPath));
        ExePath = install.ExePath;
        Found = install.Found;
        if (install.Found)
        {
            Headline = install.Running ? "CapFrameX is installed and running" : "CapFrameX is installed";
            Detail = $"Found in {install.Source}: {install.ExePath}";
        }
        else
        {
            Headline = install.Running ? "CapFrameX is running, but daignostic could not find where it is installed" : "CapFrameX was not found on this PC";
            Detail = "daignostic reads captures that CapFrameX records. CapFrameX is free and open source. If you use the portable version, choose its CapFrameX.exe.";
        }
    }

    /// <summary>Opens CapFrameX if it is installed, otherwise the official download page.</summary>
    [RelayCommand]
    private async Task OpenOrGetAsync()
    {
        if (Found)
        {
            await OpenAsync();
        }
        else
        {
            await GetAsync();
        }
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        Message.Clear();
        if (ExePath is null)
        {
            Message.Warning("CapFrameX was not found. Get it first, or choose its CapFrameX.exe.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(ExePath) ?? "" })?.Dispose();
            Message.Info("Starting CapFrameX. If Windows asks for permission, that is CapFrameX requesting it.");
            await Task.Delay(1500);
            await RefreshAsync();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Message.Error(ex is Win32Exception { NativeErrorCode: 1223 }
                ? "Windows' permission prompt was cancelled, so CapFrameX did not start."
                : "CapFrameX could not be started: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task GetAsync()
    {
        Message.Clear();
        if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(CapFrameXInstall.DownloadUrl)))
        {
            Message.Error($"Your browser could not be opened. The page is {CapFrameXInstall.DownloadUrl}");
        }
    }

    [RelayCommand]
    private async Task ChooseAsync()
    {
        Message.Clear();
        var path = await Pickers.PickExecutableAsync();
        if (path is null)
        {
            return;
        }

        if (!Path.GetFileName(path).Equals(CapFrameXLocator.ExeName, StringComparison.OrdinalIgnoreCase))
        {
            Message.Error("That is not CapFrameX.exe. Choose the file named CapFrameX.exe.");
            return;
        }

        var settings = AppServices.Settings.Clone();
        settings.CapFrameXPath = path;
        AppServices.Save(settings);
        await RefreshAsync();
    }
}
