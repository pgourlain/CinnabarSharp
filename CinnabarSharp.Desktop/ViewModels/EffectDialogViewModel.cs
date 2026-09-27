using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CinnabarSharp.Core.Effects;

namespace CinnabarSharp.Desktop.ViewModels;

public partial class EffectParameterViewModel(EffectParameter parameter, Action changed) : ViewModelBase
{
    public string Name => parameter.Name;
    public double Minimum => parameter.Minimum;
    public double Maximum => parameter.Maximum;
    public double Step => parameter.Step;
    public string Format => parameter.Step < 1 ? "0.00" : "0";

    /// <summary>Items of a list parameter; null for a slider.</summary>
    public IReadOnlyList<string>? Choices => parameter.Choices;
    public bool IsChoice => parameter.Choices is not null;
    public bool IsSlider => parameter.Choices is null;

    [ObservableProperty]
    public partial double Value { get; set; } = parameter.Default;

    public int SelectedIndex
    {
        get => (int)Value;
        set => Value = value;
    }

    [RelayCommand]
    public void Reset() => Value = parameter.Default;

    partial void OnValueChanged(double value)
    {
        OnPropertyChanged(nameof(SelectedIndex));
        changed();
    }
}

/// <summary>
/// State of a dialog that previews an effect or adjustment. Every change recomputes the preview on a background
/// thread (an older, still-running preview is cancelled) and shows it on the canvas when done.
/// </summary>
public abstract class PreviewDialogViewModel(EffectSession session) : ViewModelBase
{
    private CancellationTokenSource? _pending;

    protected EffectSession Session => session;

    public string Title => session.Effect.Name;

    private byte[]? _shown;

    /// <summary>While true the canvas shows the layer before the effect (before/after comparison).</summary>
    public bool ShowOriginal
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            OnPropertyChanged();
            if (value)
                session.Cancel();
            else if (_shown is not null)
                session.Show(_shown);
        }
    }

    /// <summary>The effect's parameter values for the current state of the dialog.</summary>
    public abstract IReadOnlyList<double> Values { get; }

    /// <summary>The latest preview computation; tests await it.</summary>
    public Task PreviewTask { get; private set; } = Task.CompletedTask;

    public void RequestPreview()
    {
        _pending?.Cancel();
        var cancellation = _pending = new CancellationTokenSource();
        var values = Values;
        PreviewTask = Task.Run(() => session.Compute(values, cancellation.Token), cancellation.Token)
            .ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully || cancellation.IsCancellationRequested)
                    return;
                _shown = t.Result;
                if (!ShowOriginal)
                    session.Show(t.Result);
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
    }

    public bool Committed { get; private set; }

    /// <summary>Computes the final values in the background, then applies them as one history step.</summary>
    public async Task CommitAsync()
    {
        _pending?.Cancel();
        var values = Values;
        var pixels = await Task.Run(() => session.Compute(values));
        session.Show(pixels);
        session.Commit();
        Committed = true;
    }

    public void Cancel()
    {
        _pending?.Cancel();
        session.Cancel();
    }
}

/// <summary>Dialog generated from the effect's numeric parameters.</summary>
public partial class EffectDialogViewModel : PreviewDialogViewModel
{
    public EffectDialogViewModel(EffectSession session) : base(session)
    {
        _suggested = new Lazy<IReadOnlyList<double>?>(session.SuggestValues);
        Parameters = session.Effect.Parameters.Select(p => new EffectParameterViewModel(p, RequestPreview)).ToList();
    }

    public IReadOnlyList<EffectParameterViewModel> Parameters { get; }
    public override IReadOnlyList<double> Values => Parameters.Select(p => p.Value).ToList();

    /// <summary>The effect can suggest values for this image (Auto button).</summary>
    public bool CanAuto => _suggested.Value is not null;

    private readonly Lazy<IReadOnlyList<double>?> _suggested;

    [RelayCommand]
    private void Reset()
    {
        foreach (var p in Parameters)
            p.Reset();
    }

    [RelayCommand]
    private void Auto()
    {
        if (_suggested.Value is not { } values)
            return;
        for (var i = 0; i < Parameters.Count && i < values.Count; i++)
            Parameters[i].Value = values[i];
    }
}
