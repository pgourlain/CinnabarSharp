using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CinnabarSharp.Core.Adjustments;
using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Desktop.ViewModels;

/// <summary>
/// Paint.NET's Curves dialog: a luminosity curve, or red/green/blue curves (the checked channels are edited
/// together). Click to add a point, drag to move it, right-click to remove it; the canvas previews every change.
/// </summary>
public partial class CurvesDialogViewModel : PreviewDialogViewModel
{
    /// <summary>A point within this distance (curve units, 0–255) of the pointer is grabbed instead of adding one.</summary>
    public const double GrabDistance = 10;

    private readonly Dictionary<List<PointI>, int> _dragging = [];

    public CurvesDialogViewModel(EffectSession session) : base(session)
    {
        Histogram = session.Histogram();
        Reset();
    }

    public static IReadOnlyList<CurvesMode> Modes { get; } = Enum.GetValues<CurvesMode>();

    public Histogram Histogram { get; }

    public List<PointI> Luminosity { get; private set; } = [];
    public List<PointI> Red { get; private set; } = [];
    public List<PointI> Green { get; private set; } = [];
    public List<PointI> Blue { get; private set; } = [];

    [ObservableProperty]
    public partial CurvesMode Mode { get; set; }

    [ObservableProperty]
    public partial bool EditRed { get; set; } = true;

    [ObservableProperty]
    public partial bool EditGreen { get; set; } = true;

    [ObservableProperty]
    public partial bool EditBlue { get; set; } = true;

    public bool IsRgb => Mode == CurvesMode.Rgb;

    /// <summary>Incremented when the curves change, to redraw the editor.</summary>
    [ObservableProperty]
    public partial int Version { get; set; }

    public CurvesSettings Settings => new(Mode, Luminosity.ToList(), Red.ToList(), Green.ToList(), Blue.ToList());

    public override IReadOnlyList<double> Values => Settings.ToValues();

    /// <summary>The curves that pointer edits apply to.</summary>
    public IReadOnlyList<List<PointI>> EditedCurves => Mode == CurvesMode.Luminosity
        ? [Luminosity]
        : new[] { (EditRed, Red), (EditGreen, Green), (EditBlue, Blue) }.Where(c => c.Item1).Select(c => c.Item2).ToList();

    partial void OnModeChanged(CurvesMode value)
    {
        OnPropertyChanged(nameof(IsRgb));
        Changed();
    }

    partial void OnEditRedChanged(bool value) => Version++;
    partial void OnEditGreenChanged(bool value) => Version++;
    partial void OnEditBlueChanged(bool value) => Version++;

    [RelayCommand]
    private void Reset()
    {
        Luminosity = [.. CurvesSettings.Diagonal];
        Red = [.. CurvesSettings.Diagonal];
        Green = [.. CurvesSettings.Diagonal];
        Blue = [.. CurvesSettings.Diagonal];
        Changed();
    }

    /// <summary>Pointer pressed at (<paramref name="x"/>, <paramref name="y"/>) in curve units (0–255, y up).</summary>
    public void PressAt(double x, double y, bool remove)
    {
        _dragging.Clear();
        foreach (var curve in EditedCurves)
        {
            var index = Nearest(curve, x, y);
            if (remove)
            {
                if (index >= 0 && curve.Count > 2)
                    curve.RemoveAt(index);
                continue;
            }
            if (index < 0)
            {
                var point = new PointI(Clamp(x), Clamp(y));
                if (curve.Any(p => p.X == point.X))
                    continue;
                index = curve.FindIndex(p => p.X > point.X);
                if (index < 0)
                    index = curve.Count;
                curve.Insert(index, point);
            }
            _dragging[curve] = index;
        }
        Changed();
    }

    /// <summary>Moves the grabbed points; a point can't pass its neighbours.</summary>
    public void DragTo(double x, double y)
    {
        if (_dragging.Count == 0)
            return;
        foreach (var (curve, index) in _dragging)
        {
            var min = index > 0 ? curve[index - 1].X + 1 : 0;
            var max = index < curve.Count - 1 ? curve[index + 1].X - 1 : 255;
            curve[index] = new PointI(Math.Clamp(Clamp(x), min, Math.Max(min, max)), Clamp(y));
        }
        Changed();
    }

    public void Release() => _dragging.Clear();

    private static int Nearest(List<PointI> curve, double x, double y)
    {
        var best = -1;
        var bestDistance = GrabDistance;
        for (var i = 0; i < curve.Count; i++)
        {
            var d = Math.Sqrt(Math.Pow(curve[i].X - x, 2) + Math.Pow(curve[i].Y - y, 2));
            if (d <= bestDistance)
                (best, bestDistance) = (i, d);
        }
        return best;
    }

    private static int Clamp(double v) => (int)Math.Clamp(Math.Round(v), 0, 255);

    private void Changed()
    {
        Version++;
        RequestPreview();
    }
}

public enum LevelsTarget
{
    Rgb,
    Red,
    Green,
    Blue,
}

/// <summary>
/// Paint.NET's Levels dialog: input and output histograms, input/output black and white points and gamma, for
/// all channels at once or one channel; Auto stretches the input range.
/// </summary>
public partial class LevelsDialogViewModel : PreviewDialogViewModel
{
    private LevelsChannel _red = LevelsChannel.Identity;
    private LevelsChannel _green = LevelsChannel.Identity;
    private LevelsChannel _blue = LevelsChannel.Identity;

    public LevelsDialogViewModel(EffectSession session) : base(session)
    {
        InputHistogram = session.Histogram();
    }

    public static IReadOnlyList<LevelsTarget> Targets { get; } = Enum.GetValues<LevelsTarget>();

    public Histogram InputHistogram { get; }

    public Histogram OutputHistogram => InputHistogram.Map(_red.Lookup(), _green.Lookup(), _blue.Lookup());

    [ObservableProperty]
    public partial LevelsTarget Target { get; set; }

    public override IReadOnlyList<double> Values => Levels.PerChannel(_red, _green, _blue);

    /// <summary>The levels shown in the dialog: those of the chosen channel (red's when editing all three).</summary>
    private LevelsChannel Shown => Target switch
    {
        LevelsTarget.Green => _green,
        LevelsTarget.Blue => _blue,
        _ => _red,
    };

    public double InputBlack
    {
        get => Shown.InBlack;
        set => Edit(c => c with { InBlack = Math.Clamp(value, 0, c.InWhite - 1) });
    }

    public double InputWhite
    {
        get => Shown.InWhite;
        set => Edit(c => c with { InWhite = Math.Clamp(value, c.InBlack + 1, 255) });
    }

    public double Gamma
    {
        get => Shown.Gamma;
        set => Edit(c => c with { Gamma = Math.Clamp(value, 0.1, 10) });
    }

    public double OutputBlack
    {
        get => Shown.OutBlack;
        set => Edit(c => c with { OutBlack = Math.Clamp(value, 0, 255) });
    }

    public double OutputWhite
    {
        get => Shown.OutWhite;
        set => Edit(c => c with { OutWhite = Math.Clamp(value, 0, 255) });
    }

    partial void OnTargetChanged(LevelsTarget value) => Refresh(preview: false);

    [RelayCommand]
    private void Auto()
    {
        if (Target == LevelsTarget.Rgb)
        {
            var auto = LevelsChannel.Auto(InputHistogram.Luminosity);
            (_red, _green, _blue) = (auto, auto, auto);
        }
        else
        {
            Edit(_ => LevelsChannel.Auto(Target switch
            {
                LevelsTarget.Green => InputHistogram.Green,
                LevelsTarget.Blue => InputHistogram.Blue,
                _ => InputHistogram.Red,
            }));
            return;
        }
        Refresh(preview: true);
    }

    [RelayCommand]
    private void Reset()
    {
        (_red, _green, _blue) = (LevelsChannel.Identity, LevelsChannel.Identity, LevelsChannel.Identity);
        Refresh(preview: true);
    }

    private void Edit(Func<LevelsChannel, LevelsChannel> change)
    {
        if (Target is LevelsTarget.Rgb or LevelsTarget.Red)
            _red = change(_red);
        if (Target is LevelsTarget.Rgb or LevelsTarget.Green)
            _green = change(_green);
        if (Target is LevelsTarget.Rgb or LevelsTarget.Blue)
            _blue = change(_blue);
        Refresh(preview: true);
    }

    private void Refresh(bool preview)
    {
        foreach (var name in new[]
                 {
                     nameof(InputBlack), nameof(InputWhite), nameof(Gamma), nameof(OutputBlack), nameof(OutputWhite),
                     nameof(OutputHistogram),
                 })
            OnPropertyChanged(name);
        if (preview)
            RequestPreview();
    }
}
