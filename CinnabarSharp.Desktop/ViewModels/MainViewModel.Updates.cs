using System;
using System.Threading.Tasks;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CinnabarSharp.Desktop.ViewModels;

// Looking for a newer release: asked once, then at most once a day, in the background after the window is up.
public partial class MainViewModel
{
    private bool? _checkForUpdates;
    private string? _skippedUpdate;
    private DateTime? _lastUpdateCheck;

    /// <summary>Set by the app; null disables the feature (tests, no network service).</summary>
    public UpdateChecker? Updates { get; set; }

    /// <summary>The version the app thinks it is; tests change it.</summary>
    public Version CurrentVersion { get; set; } = AppVersion.Current;

    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(UpdateText))]
    public partial UpdateInfo? PendingUpdate { get; set; }

    public bool HasUpdate => PendingUpdate is not null;

    public string UpdateText => PendingUpdate is { } update
        ? $"CinnabarSharp {update.Version} is available (this is {CurrentVersion.ToString(3)})."
        : "";

    /// <summary>True when the user agreed to the automatic check, false when they declined, null before they were asked.</summary>
    public bool? CheckForUpdatesAllowed => _checkForUpdates;

    /// <summary>
    /// Called once the window is on screen. Asks for permission the first time (nothing is sent before the answer), then
    /// looks for a newer release unless it already did in the last 24 hours. Never throws; the UI stays free meanwhile.
    /// </summary>
    public async Task StartUpdateCheckAsync()
    {
        if (Updates is null)
            return;
        if (_checkForUpdates is null)
        {
            if (Dialogs is null)
                return;
            _checkForUpdates = await Dialogs.ConfirmAsync("Look for updates?",
                "CinnabarSharp can check GitHub for a newer version each time you start it (at most once a day). " +
                "It sends one request for the latest release and nothing about you or your files, and it never installs anything: " +
                "you are shown the download page. You can also check by hand in Help › Check for Updates.",
                "Check for updates");
        }
        if (_checkForUpdates != true)
            return;
        if (_lastUpdateCheck is { } last && UtcNow() - last < TimeSpan.FromHours(24))
            return;
        await RunUpdateCheckAsync(manual: false);
    }

    private async Task RunUpdateCheckAsync(bool manual)
    {
        if (Updates is null)
            return;
        UpdateInfo? update;
        try
        {
            update = await Updates.CheckAsync(CurrentVersion);
        }
        catch (UpdateCheckException e)
        {
            AppLog.Error("update", e.Message);
            if (manual && Dialogs is not null)
                await Dialogs.ShowErrorAsync("Could not check for updates", e.Message);
            return;
        }
        _lastUpdateCheck = UtcNow();
        if (manual)
        {
            if (Dialogs is null)
                return;
            if (update is null)
                await Dialogs.ShowErrorAsync("CinnabarSharp is up to date", $"You have the latest version ({CurrentVersion.ToString(3)}).");
            else if (await Dialogs.ConfirmAsync($"CinnabarSharp {update.Version} is available", $"This is {CurrentVersion.ToString(3)}.", "Open the download page"))
                await Dialogs.OpenUrlAsync(update.Url);
            return;
        }
        // In the background a version the user skipped stays quiet.
        if (update is not null && update.Version != _skippedUpdate)
            PendingUpdate = update;
    }

    [RelayCommand]
    private Task CheckForUpdates() => RunUpdateCheckAsync(manual: true);

    [RelayCommand(CanExecute = nameof(HasUpdate))]
    private async Task OpenUpdatePage()
    {
        if (PendingUpdate is { } update && Dialogs is not null)
            await Dialogs.OpenUrlAsync(update.Url);
    }

    [RelayCommand(CanExecute = nameof(HasUpdate))]
    private void SkipUpdate()
    {
        _skippedUpdate = PendingUpdate?.Version;
        PendingUpdate = null;
    }

    [RelayCommand(CanExecute = nameof(HasUpdate))]
    private void DismissUpdate() => PendingUpdate = null;

    partial void OnPendingUpdateChanged(UpdateInfo? value)
    {
        OpenUpdatePageCommand.NotifyCanExecuteChanged();
        SkipUpdateCommand.NotifyCanExecuteChanged();
        DismissUpdateCommand.NotifyCanExecuteChanged();
    }
}
