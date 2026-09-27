using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CinnabarSharp.Core.Adjustments;

namespace CinnabarSharp.Desktop.ViewModels;

public partial class AdjustmentParameterViewModel(AdjustmentParameter parameter, Action changed) : ViewModelBase
{
    public string Name => parameter.Name;
    public double Minimum => parameter.Minimum;
    public double Maximum => parameter.Maximum;
    public double Step => parameter.Step;
    public string Format => parameter.Step < 1 ? "0.00" : "0";

    [ObservableProperty]
    public partial double Value { get; set; } = parameter.Default;

    public void Reset() => Value = parameter.Default;

    partial void OnValueChanged(double value) => changed();
}

/// <summary>
/// Adjustment dialog state. Every parameter change recomputes the preview on a background thread
/// (an older, still-running preview is cancelled) and shows it on the canvas when done.
/// </summary>
public partial class AdjustmentViewModel : ViewModelBase
{
    private readonly AdjustmentSession _session;
    private CancellationTokenSource? _pending;

    public AdjustmentViewModel(AdjustmentSession session)
    {
        _session = session;
        Parameters = session.Adjustment.Parameters.Select(p => new AdjustmentParameterViewModel(p, RequestPreview)).ToList();
    }

    public string Title => _session.Adjustment.Name;
    public IReadOnlyList<AdjustmentParameterViewModel> Parameters { get; }
    public IReadOnlyList<double> Values => Parameters.Select(p => p.Value).ToList();

    /// <summary>The latest preview computation; tests await it.</summary>
    public Task PreviewTask { get; private set; } = Task.CompletedTask;

    [RelayCommand]
    private void Reset()
    {
        foreach (var p in Parameters)
            p.Reset();
    }

    public void RequestPreview()
    {
        _pending?.Cancel();
        var cancellation = _pending = new CancellationTokenSource();
        var values = Values;
        PreviewTask = Task.Run(() => _session.Compute(values, cancellation.Token), cancellation.Token)
            .ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && !cancellation.IsCancellationRequested)
                    _session.Show(t.Result);
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Applies the final values synchronously and records one history step.</summary>
    public void Commit()
    {
        _pending?.Cancel();
        _session.Show(_session.Compute(Values));
        _session.Commit();
    }

    public void Cancel()
    {
        _pending?.Cancel();
        _session.Cancel();
    }
}
