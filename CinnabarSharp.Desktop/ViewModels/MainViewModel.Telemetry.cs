using System;
using System.Threading.Tasks;
using CinnabarSharp.Core.Services;
using CommunityToolkit.Mvvm.Input;

namespace CinnabarSharp.Desktop.ViewModels;

// Anonymous usage statistics (docs/telemetry.md): asked once, off unless the user agrees, and only when the build has an
// endpoint (TelemetryEndpoint). Events are counted in memory and sent in batches by TelemetryClient.
public partial class MainViewModel
{
    /// <summary>How often pending counts are sent while the app runs.</summary>
    public static readonly TimeSpan TelemetryInterval = TimeSpan.FromMinutes(15);

    private bool? _sendUsageStatistics;
    private string? _telemetryInstallId;

    /// <summary>Set by the app when the build has an endpoint; null disables the feature (tests, builds without one).</summary>
    public TelemetryClient? Telemetry { get; set; }

    /// <summary>Whether the Help menu shows the usage statistics switch.</summary>
    public bool HasTelemetry => Telemetry is not null;

    /// <summary>True when the user agreed to send usage statistics, false when they declined, null before they were asked.</summary>
    public bool? UsageStatisticsAllowed => _sendUsageStatistics;

    /// <summary>The menu check mark.</summary>
    public bool SendUsageStatistics => _sendUsageStatistics == true;

    /// <summary>
    /// Called once the window is on screen, after the update check. Asks the first time (nothing is counted or sent before
    /// the answer), then counts this start and sends it.
    /// </summary>
    public async Task StartTelemetryAsync()
    {
        if (Telemetry is null)
            return;
        if (_sendUsageStatistics is null)
        {
            if (Dialogs is null)
                return;
            _sendUsageStatistics = await Dialogs.ConfirmAsync("Help improve CinnabarSharp?",
                "CinnabarSharp can send anonymous usage statistics: how many people use it, on which system and version, " +
                "and how often each tool, menu command, effect and file format is used. It never sends your pictures, file " +
                "names, paths or anything you type, and the id it uses is random. You can turn it off at any time in Help " +
                "(the application menu on macOS).",
                "Send statistics");
            OnPropertyChanged(nameof(SendUsageStatistics));
        }
        if (_sendUsageStatistics == true)
            BeginTelemetry();
    }

    private void BeginTelemetry()
    {
        if (Telemetry is null)
            return;
        _telemetryInstallId ??= Guid.NewGuid().ToString("N");
        Telemetry.Enable(_telemetryInstallId);
        Telemetry.Track("app_start");
        Telemetry.StartPeriodicFlush(TelemetryInterval);
        _ = Telemetry.FlushAsync();
    }

    /// <summary>Counts one use; does nothing unless the user agreed. <paramref name="name"/> must come from the app's own labels.</summary>
    public void TrackUsage(string name) => Telemetry?.Track(name);

    /// <summary>The menu switch. Turning it off forgets the id and what was not sent; turning it on again starts with a new id.</summary>
    [RelayCommand]
    private void ToggleUsageStatistics()
    {
        if (Telemetry is null)
            return;
        if (_sendUsageStatistics == true)
        {
            _sendUsageStatistics = false;
            _telemetryInstallId = null;
            Telemetry.Disable();
        }
        else
        {
            _sendUsageStatistics = true;
            BeginTelemetry();
        }
        OnPropertyChanged(nameof(SendUsageStatistics));
    }

    /// <summary>Sends what is left when the app quits, waiting at most <paramref name="timeout"/>.</summary>
    public void FlushTelemetry(TimeSpan timeout)
    {
        if (Telemetry is not { IsEnabled: true } telemetry)
            return;
        try
        {
            Task.Run(() => telemetry.FlushAsync()).Wait(timeout);
        }
        catch (AggregateException)
        {
        }
    }
}
