using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Desktop.Services;
using CinnabarSharp.Vector;
using GradientStop = CinnabarSharp.Core.Vector.GradientStop;

namespace CinnabarSharp.Desktop.ViewModels;

public enum PaintMode
{
    None,
    Flat,
    Linear,
    Radial,
    /// <summary>A paint the editor does not handle (a pattern): left as it is.</summary>
    Other,
}

/// <summary>One stop of the gradient being edited.</summary>
public partial class GradientStopViewModel(PaintEditor owner, double offset, Color color) : ViewModelBase
{
    [ObservableProperty]
    public partial double Offset { get; set; } = offset;

    [ObservableProperty]
    public partial Color Color { get; set; } = color;

    public IBrush Brush => new SolidColorBrush(Color.FromRgb(Color.R, Color.G, Color.B));

    partial void OnOffsetChanged(double value) => owner.StopChanged(this);

    partial void OnColorChanged(Color value)
    {
        OnPropertyChanged(nameof(Brush));
        owner.StopChanged(this);
    }

    [RelayCommand]
    private Task PickColor() => owner.PickStopColorAsync(this);

    public void SetQuietly(double offset, Color color)
    {
        Offset = offset;
        Color = color;
    }
}

/// <summary>The fill or the stroke of the selected objects: none, a flat color or a gradient, with its opacity.</summary>
public partial class PaintEditor : ViewModelBase
{
    private readonly SvgPropertiesViewModel _owner;
    private readonly bool _stroke;
    private Color _lastFlat = Colors.Black;

    public PaintEditor(SvgPropertiesViewModel owner, bool stroke)
    {
        _owner = owner;
        _stroke = stroke;
    }

    public static IReadOnlyList<PaintMode> Modes { get; } = [PaintMode.None, PaintMode.Flat, PaintMode.Linear, PaintMode.Radial];

    [ObservableProperty]
    public partial PaintMode Mode { get; set; }

    [ObservableProperty]
    public partial Color Color { get; set; } = Colors.Black;

    /// <summary>Opacity of the paint in percent (fill-opacity or stroke-opacity).</summary>
    [ObservableProperty]
    public partial double Opacity { get; set; } = 100;

    public ObservableCollection<GradientStopViewModel> Stops { get; } = [];

    [ObservableProperty]
    public partial GradientStopViewModel? SelectedStop { get; set; }

    public bool IsFlat => Mode == PaintMode.Flat;

    public bool IsGradient => Mode is PaintMode.Linear or PaintMode.Radial;

    public bool HasPaint => Mode is PaintMode.Flat or PaintMode.Linear or PaintMode.Radial;

    public IBrush Brush => new SolidColorBrush(Color);

    private string Property => _stroke ? "stroke" : "fill";

    partial void OnModeChanged(PaintMode value)
    {
        OnPropertyChanged(nameof(IsFlat));
        OnPropertyChanged(nameof(IsGradient));
        OnPropertyChanged(nameof(HasPaint));
        if (!_owner.Updating)
            _owner.ApplyMode(this, _stroke, value, _lastFlat);
    }

    partial void OnColorChanged(Color value)
    {
        OnPropertyChanged(nameof(Brush));
        if (value.A == 255 && Mode == PaintMode.Flat)
            _lastFlat = value;
        if (!_owner.Updating && Mode == PaintMode.Flat)
            _owner.ApplyColor(_stroke, value);
    }

    partial void OnOpacityChanged(double value)
    {
        if (!_owner.Updating)
            _owner.ApplyOpacity(_stroke, value);
    }

    [RelayCommand]
    private async Task PickColor()
    {
        if (_owner.Host.Dialogs is { } dialogs && await dialogs.PickColorAsync(_stroke ? "Stroke Color" : "Fill Color", Color) is { } picked)
            Color = Color.FromRgb(picked.R, picked.G, picked.B);
    }

    public Task PickStopColorAsync(GradientStopViewModel stop) => _owner.PickStopColorAsync(stop);

    public void StopChanged(GradientStopViewModel stop)
    {
        if (!_owner.Updating)
            _owner.ApplyStops(this, _stroke, coalesce: true);
    }

    [RelayCommand]
    private void AddStop()
    {
        if (Stops.Count < 2)
            return;
        // In the widest gap, half way, with the color there.
        var ordered = Stops.OrderBy(s => s.Offset).ToList();
        var (left, right) = Enumerable.Range(0, ordered.Count - 1).Select(i => (ordered[i], ordered[i + 1]))
            .MaxBy(p => p.Item2.Offset - p.Item1.Offset);
        var color = Color.FromArgb((byte)((left.Color.A + right.Color.A) / 2), (byte)((left.Color.R + right.Color.R) / 2),
            (byte)((left.Color.G + right.Color.G) / 2), (byte)((left.Color.B + right.Color.B) / 2));
        var added = new GradientStopViewModel(this, (left.Offset + right.Offset) / 2, color);
        Stops.Add(added);
        SelectedStop = added;
        _owner.ApplyStops(this, _stroke, coalesce: false);
    }

    [RelayCommand]
    private void RemoveStop()
    {
        if (Stops.Count <= 2 || SelectedStop is not { } stop)
            return;
        Stops.Remove(stop);
        SelectedStop = Stops.FirstOrDefault();
        _owner.ApplyStops(this, _stroke, coalesce: false);
    }

    /// <summary>Stops as the document should have them: by offset.</summary>
    public IReadOnlyList<GradientStop> ToStops() => Stops.OrderBy(s => s.Offset)
        .Select(s => new GradientStop(Math.Clamp(s.Offset, 0, 1), VColor.FromRgba(s.Color.R, s.Color.G, s.Color.B, s.Color.A))).ToList();

    public void Load(PaintMode mode, Color color, double opacity, IReadOnlyList<GradientStop> stops)
    {
        Mode = mode;
        if (mode != PaintMode.None && mode != PaintMode.Other)
            Color = color;
        if (mode == PaintMode.Flat)
            _lastFlat = color;
        Opacity = opacity;
        // Edit the existing stop view models when the count is the same (the editor keeps its selection while dragging).
        if (stops.Count == Stops.Count && Stops.Count > 0)
        {
            for (var i = 0; i < stops.Count; i++)
                Stops[i].SetQuietly(stops[i].Offset, ToColor(stops[i].Color));
        }
        else
        {
            Stops.Clear();
            foreach (var stop in stops)
                Stops.Add(new GradientStopViewModel(this, stop.Offset, ToColor(stop.Color)));
            SelectedStop = Stops.FirstOrDefault();
        }
    }

    private static Color ToColor(VColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);
}

/// <summary>
/// The Properties panel for the selected objects: fill, stroke, opacity, geometry. Every change is one history step through
/// <c>SvgActions</c> (a slider drag is one step), applied to all selected objects.
/// </summary>
public partial class SvgPropertiesViewModel : ViewModelBase
{
    private readonly MainViewModel _host;
    private bool _updating;

    public SvgPropertiesViewModel(MainViewModel host)
    {
        _host = host;
        Fill = new PaintEditor(this, stroke: false);
        Stroke = new PaintEditor(this, stroke: true);
    }

    internal MainViewModel Host => _host;

    internal bool Updating => _updating;

    private SvgDocument? Drawing => _host.ActiveSvg;

    private IReadOnlyList<SvgElement> Selected => Drawing?.Selection.Nodes ?? [];

    public PaintEditor Fill { get; }

    public PaintEditor Stroke { get; }

    public bool HasSelection => Selected.Count > 0;

    /// <summary>The end of a slider drag: the next change starts a new history step.</summary>
    public void EndGesture() => Drawing?.Actions.EndCoalescing();

    public string Summary => _host.ObjectSelectionText;

    // ---- Stroke style ----

    public static IReadOnlyList<LineCap> Caps { get; } = Enum.GetValues<LineCap>();

    public static IReadOnlyList<LineJoin> Joins { get; } = Enum.GetValues<LineJoin>();

    public sealed record DashPreset(string Name, string Pattern);

    public static IReadOnlyList<DashPreset> DashPresets { get; } =
    [
        new("Solid", ""),
        new("Dashed", "6 4"),
        new("Dotted", "1 3"),
        new("Dash dot", "8 3 1 3"),
    ];

    [ObservableProperty]
    public partial double StrokeWidth { get; set; } = 1;

    [ObservableProperty]
    public partial LineCap Cap { get; set; }

    [ObservableProperty]
    public partial LineJoin Join { get; set; }

    [ObservableProperty]
    public partial DashPreset SelectedDash { get; set; } = DashPresets[0];

    /// <summary>Opacity of the whole object in percent.</summary>
    [ObservableProperty]
    public partial double ObjectOpacity { get; set; } = 100;

    // ---- Geometry (the selection's bounding box, document units) ----

    [ObservableProperty]
    public partial decimal? X { get; set; }

    [ObservableProperty]
    public partial decimal? Y { get; set; }

    [ObservableProperty]
    public partial decimal? Width { get; set; }

    [ObservableProperty]
    public partial decimal? Height { get; set; }

    [ObservableProperty]
    public partial decimal? Rotation { get; set; }

    private double _lastRotation;

    partial void OnStrokeWidthChanged(double value)
    {
        if (!_updating && value >= 0)
            Drawing?.Actions.SetStyle(Selected, "stroke-width", NumberFormat.Format(value, 4), "Set Stroke Width", coalesce: true);
    }

    partial void OnCapChanged(LineCap value)
    {
        if (!_updating)
            Drawing?.Actions.SetStyle(Selected, "stroke-linecap", value.ToString().ToLowerInvariant(), "Set Line Cap");
    }

    partial void OnJoinChanged(LineJoin value)
    {
        if (!_updating)
            Drawing?.Actions.SetStyle(Selected, "stroke-linejoin", value.ToString().ToLowerInvariant(), "Set Line Join");
    }

    partial void OnSelectedDashChanged(DashPreset value)
    {
        if (!_updating)
            Drawing?.Actions.SetStyle(Selected, "stroke-dasharray", value.Pattern.Length == 0 ? "none" : value.Pattern, "Set Dashes");
    }

    partial void OnObjectOpacityChanged(double value)
    {
        if (!_updating)
            Drawing?.Actions.SetStyle(Selected, "opacity", NumberFormat.Format(Math.Clamp(value, 0, 100) / 100, 3), "Set Opacity", coalesce: true);
    }

    partial void OnXChanged(decimal? value) => ApplyGeometry();

    partial void OnYChanged(decimal? value) => ApplyGeometry();

    partial void OnWidthChanged(decimal? value) => ApplyGeometry();

    partial void OnHeightChanged(decimal? value) => ApplyGeometry();

    private void ApplyGeometry()
    {
        if (_updating || Drawing is not { } drawing || !HasSelection)
            return;
        if (X is not { } x || Y is not { } y || Width is not { } w || Height is not { } h || w <= 0 || h <= 0)
            return;
        drawing.Actions.Resize(Selected, new VRect((double)x, (double)y, (double)w, (double)h));
    }

    partial void OnRotationChanged(decimal? value)
    {
        if (_updating || value is not { } degrees || Drawing is not { } drawing || !HasSelection)
            return;
        var delta = (double)degrees - _lastRotation;
        if (Math.Abs(delta) < 1e-9)
            return;
        drawing.Actions.Rotate(Selected, delta);
        _lastRotation = (double)degrees;
    }

    // ---- Applying paint edits ----

    internal void ApplyMode(PaintEditor editor, bool stroke, PaintMode mode, Color lastFlat)
    {
        if (Drawing is not { } drawing || !HasSelection)
            return;
        var targets = Selected;
        switch (mode)
        {
            case PaintMode.None:
                drawing.Actions.SetStyle(targets, stroke ? "stroke" : "fill", "none", stroke ? "Set Stroke" : "Set Fill");
                break;
            case PaintMode.Flat:
                drawing.Actions.SetStyle(targets, stroke ? "stroke" : "fill",
                    SvgPaint.FromColor(VColor.FromRgb(lastFlat.R, lastFlat.G, lastFlat.B)).ToText(), stroke ? "Set Stroke" : "Set Fill");
                break;
            case PaintMode.Linear:
            case PaintMode.Radial:
                var kind = mode == PaintMode.Linear ? SvgGradientKind.Linear : SvgGradientKind.Radial;
                foreach (var node in targets)
                    drawing.Actions.ChangeGradientKind(node, stroke, kind);
                break;
        }
        Refresh();
    }

    internal void ApplyColor(bool stroke, Color color)
    {
        if (Drawing is not { } drawing || !HasSelection)
            return;
        var paint = SvgPaint.FromColor(VColor.FromRgb(color.R, color.G, color.B));
        drawing.Actions.SetStyle(Selected, stroke ? "stroke" : "fill", paint.ToText(), stroke ? "Set Stroke" : "Set Fill", coalesce: true);
    }

    internal void ApplyOpacity(bool stroke, double percent) =>
        Drawing?.Actions.SetStyle(Selected, stroke ? "stroke-opacity" : "fill-opacity",
            NumberFormat.Format(Math.Clamp(percent, 0, 100) / 100, 3), stroke ? "Set Stroke Opacity" : "Set Fill Opacity", coalesce: true);

    internal void ApplyStops(PaintEditor editor, bool stroke, bool coalesce)
    {
        if (Drawing is not { } drawing || Selected.LastOrDefault() is not { } node)
            return;
        var paint = StyleResolver.ComputeFor(node) is var style ? (stroke ? style.Stroke : style.Fill) : null;
        if (paint?.Kind != PaintKind.Url || drawing.Root.FindById(paint.Id) is not SvgGradient gradient)
            return;
        var owner = gradient.InheritanceChain().FirstOrDefault(g => g.OwnStops.Any()) ?? gradient;
        drawing.Actions.SetStops(owner, editor.ToStops(), coalesce);
    }

    internal async Task PickStopColorAsync(GradientStopViewModel stop)
    {
        if (_host.Dialogs is { } dialogs && await dialogs.PickColorAsync("Stop Color", stop.Color) is { } picked)
            stop.Color = picked;
    }

    /// <summary>Called when the drawing's selection or content changed: shows what the selected objects have.</summary>
    public void Refresh()
    {
        _updating = true;
        try
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(Summary));
            if (Drawing is not { } drawing || Selected.LastOrDefault() is not { } node)
                return;
            var style = StyleResolver.ComputeFor(node);
            LoadPaint(Fill, style.Fill, style.FillOpacity, style, drawing);
            LoadPaint(Stroke, style.Stroke, style.StrokeOpacity, style, drawing);
            StrokeWidth = style.StrokeWidth;
            Cap = style.LineCap;
            Join = style.LineJoin;
            SelectedDash = DashPresets.FirstOrDefault(p => style.DashArray is null
                ? p.Pattern.Length == 0
                : p.Pattern == string.Join(' ', style.DashArray.Select(d => NumberFormat.Format(d, 3)))) ?? DashPresets[0];
            ObjectOpacity = Math.Round(style.Opacity * 100, 1);
            if (drawing.Actions.BoundsOf(Selected) is { } box)
            {
                X = (decimal)Math.Round(box.X, 3);
                Y = (decimal)Math.Round(box.Y, 3);
                Width = (decimal)Math.Round(box.Width, 3);
                Height = (decimal)Math.Round(box.Height, 3);
            }
            _lastRotation = Selected.Count == 1 ? NormalizeDegrees(SvgBounds.ToDocument(node).Decompose().Rotation) : 0;
            Rotation = (decimal)Math.Round(_lastRotation, 2);
        }
        finally
        {
            _updating = false;
        }
    }

    private static double NormalizeDegrees(double degrees)
    {
        degrees %= 360;
        return degrees > 180 ? degrees - 360 : degrees <= -180 ? degrees + 360 : degrees;
    }

    private static void LoadPaint(PaintEditor editor, SvgPaint paint, double opacity, ComputedStyle style, SvgDocument drawing)
    {
        var stops = new List<GradientStop>();
        var mode = PaintMode.None;
        var color = Colors.Black;
        switch (paint.Kind)
        {
            case PaintKind.Color:
                mode = PaintMode.Flat;
                color = Color.FromRgb(paint.Color.R, paint.Color.G, paint.Color.B);
                break;
            case PaintKind.CurrentColor:
                mode = PaintMode.Flat;
                color = Color.FromRgb(style.Color.R, style.Color.G, style.Color.B);
                break;
            case PaintKind.Url:
                if (drawing.Root.FindById(paint.Id) is SvgGradient gradient)
                {
                    mode = gradient is SvgLinearGradient ? PaintMode.Linear : PaintMode.Radial;
                    stops = gradient.ResolvedStops().Select(s => new GradientStop(s.Offset, s.Color)).ToList();
                    if (stops.Count > 0)
                        color = Color.FromRgb(stops[0].Color.R, stops[0].Color.G, stops[0].Color.B);
                }
                else
                {
                    mode = paint.Fallback is null ? PaintMode.Other : PaintMode.Flat;
                }
                break;
        }
        editor.Load(mode, color, Math.Round(opacity * 100, 1), stops);
    }
}
