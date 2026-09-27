using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Tools;

/// <summary>
/// Freehand stroke: round dabs joined by segments. Left = primary color, right = secondary.
/// Pen pressure scales the width; hardness softens the edge.
/// </summary>
public class PaintbrushTool(ToolSettings settings) : ITool
{
    private PaintSession? _session;
    private CoverageMask? _coverage;
    private PointD _last;
    private ColorBgra _color;

    public virtual string Name => "Paintbrush";

    protected ToolSettings Settings => settings;
    protected virtual double Radius => Math.Max(0.5, settings.BrushWidth / 2.0);
    protected virtual bool Antialias => settings.Antialiasing;
    protected virtual double Hardness => settings.Hardness / 100.0;

    /// <summary>Button of the current stroke.</summary>
    protected ToolButton Button { get; private set; }

    /// <summary>Position of the last dab of the current stroke.</summary>
    protected PointD LastPosition => _last;

    protected bool IsPainting => _session is not null;

    protected virtual void Apply(PaintSession session, RectangleI region, CoverageMask coverage, ColorBgra color) =>
        session.ApplyColor(region, coverage, color);

    protected virtual RectangleI Dab(CoverageMask coverage, PointD from, PointD to, double pressure)
    {
        var radius = Math.Max(0.5, Radius * Math.Clamp(pressure, 0, 1));
        return from == to
            ? coverage.Disc(to.X, to.Y, radius, Antialias, Hardness)
            : coverage.Segment(from, to, radius, Antialias, Hardness);
    }

    public virtual void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        _session = new PaintSession(document, Name);
        _coverage = new CoverageMask(document.ImageSize.Width, document.ImageSize.Height);
        Button = pointer.Button;
        _color = settings.ColorFor(pointer.Button);
        _last = pointer.Position;
        Apply(_session, Dab(_coverage, _last, _last, pointer.Pressure), _coverage, _color);
    }

    public virtual void OnPointerMove(ImageDocument document, ToolPointer pointer)
    {
        if (_session is null || _coverage is null || pointer.Position == _last)
            return;
        Apply(_session, Dab(_coverage, _last, pointer.Position, pointer.Pressure), _coverage, _color);
        _last = pointer.Position;
    }

    public virtual void OnPointerUp(ImageDocument document, ToolPointer pointer)
    {
        OnPointerMove(document, pointer);
        _session?.Commit();
        _session = null;
        _coverage = null;
    }
}

/// <summary>Like the Paintbrush, but makes pixels transparent.</summary>
public sealed class EraserTool(ToolSettings settings) : PaintbrushTool(settings)
{
    public override string Name => "Eraser";

    protected override void Apply(PaintSession session, RectangleI region, CoverageMask coverage, ColorBgra color) =>
        session.ApplyErase(region, coverage);
}

/// <summary>Hard 1-pixel lines, never antialiased.</summary>
public sealed class PencilTool(ToolSettings settings) : PaintbrushTool(settings)
{
    public override string Name => "Pencil";

    protected override RectangleI Dab(CoverageMask coverage, PointD from, PointD to, double pressure) =>
        coverage.PixelLine(Floor(from), Floor(to));

    private static PointI Floor(PointD p) => new((int)Math.Floor(p.X), (int)Math.Floor(p.Y));
}

/// <summary>
/// ⌘/Ctrl-click sets the source point; painting then copies the layer's pixels from the source, keeping the same
/// offset between source and brush for every following stroke (until a new source is set).
/// </summary>
public sealed class CloneStampTool(ToolSettings settings) : PaintbrushTool(settings), IOverlayTool
{
    private PointD? _source;
    private PointI? _offset;
    private bool _settingSource;

    public override string Name => "Clone Stamp";

    public bool HasSource => _source is not null;

    public override void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        if (pointer.Modifiers.HasFlag(ToolModifiers.Command))
        {
            _source = pointer.Position;
            _offset = null;
            _settingSource = true;
            return;
        }
        if (_source is not { } source)
            return;
        _offset ??= new PointI((int)Math.Round(source.X - pointer.Position.X), (int)Math.Round(source.Y - pointer.Position.Y));
        base.OnPointerDown(document, pointer);
    }

    public override void OnPointerMove(ImageDocument document, ToolPointer pointer)
    {
        if (_settingSource)
            _source = pointer.Position;
        else
            base.OnPointerMove(document, pointer);
    }

    public override void OnPointerUp(ImageDocument document, ToolPointer pointer)
    {
        if (_settingSource)
        {
            _source = pointer.Position;
            _settingSource = false;
            return;
        }
        base.OnPointerUp(document, pointer);
    }

    protected override void Apply(PaintSession session, RectangleI region, CoverageMask coverage, ColorBgra color)
    {
        if (_offset is not { } offset)
            return;
        var source = session.BasePixels;
        var (w, h) = (session.Width, session.Height);
        session.Apply(region, (x, y, pixel) =>
        {
            var c = coverage[x, y];
            int sx = x + offset.X, sy = y + offset.Y;
            if (c == 0 || sx < 0 || sy < 0 || sx >= w || sy >= h)
                return;
            var i = (sy * w + sx) * 4;
            PaintSession.BlendCoverage(pixel, ColorBgra.FromBgra(source[i], source[i + 1], source[i + 2], source[i + 3]), c);
        });
    }

    public ToolOverlay? GetOverlay(ImageDocument document)
    {
        if (_source is not { } source)
            return null;
        var point = IsPainting && _offset is { } o ? new PointD(LastPosition.X + o.X, LastPosition.Y + o.Y) : source;
        return new ToolOverlay { Handles = [point] };
    }
}

/// <summary>
/// Replaces colors close to the secondary color (within the tolerance) with the primary color where the brush
/// passes; the right button does the opposite. Alpha is kept.
/// </summary>
public sealed class RecolorTool(ToolSettings settings) : PaintbrushTool(settings)
{
    public override string Name => "Recolor";

    protected override void Apply(PaintSession session, RectangleI region, CoverageMask coverage, ColorBgra color)
    {
        var from = Settings.ColorFor(Button == ToolButton.Right ? ToolButton.Left : ToolButton.Right);
        // Same distance as the magic wand: squared distance over the 4 channels against the scaled tolerance.
        var limit = (long)Math.Round(Math.Pow(Math.Clamp(Settings.Tolerance, 0, 100) / 100.0 * 255, 2) * 4);
        session.Apply(region, (x, y, pixel) =>
        {
            var c = coverage[x, y];
            if (c == 0)
                return;
            long db = pixel[0] - from.B, dg = pixel[1] - from.G, dr = pixel[2] - from.R, da = pixel[3] - from.A;
            if (db * db + dg * dg + dr * dr + da * da > limit)
                return;
            var t = c / 255.0;
            pixel[0] = (byte)Math.Round(pixel[0] + (color.B - pixel[0]) * t);
            pixel[1] = (byte)Math.Round(pixel[1] + (color.G - pixel[1]) * t);
            pixel[2] = (byte)Math.Round(pixel[2] + (color.R - pixel[2]) * t);
        });
    }
}

/// <summary>
/// Drag to draw; the preview is redrawn from the original pixels on every move and committed on release.
/// </summary>
public abstract class DragShapeTool(ToolSettings settings) : ITool
{
    private PaintSession? _session;
    private PointD _start;
    private ToolButton _button;

    public abstract string Name { get; }
    protected ToolSettings Settings => settings;

    protected abstract void Draw(PaintSession session, PointD start, PointD end, ToolButton button);

    public void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        _session = new PaintSession(document, Name);
        _start = pointer.Position;
        _button = pointer.Button;
    }

    public void OnPointerMove(ImageDocument document, ToolPointer pointer)
    {
        if (_session is null)
            return;
        _session.Reset();
        Draw(_session, _start, pointer.Position, _button);
    }

    public void OnPointerUp(ImageDocument document, ToolPointer pointer)
    {
        if (_session is null)
            return;
        if (pointer.Position != _start)
            OnPointerMove(document, pointer);
        _session.Commit();
        _session = null;
    }
}

/// <summary>
/// Drag to draw a line. It stays editable until finished (Enter, click away, another tool): drag its end points to
/// move them, or its two control points to bend it into a cubic Bézier curve, as in Paint.NET.
/// </summary>
public sealed class LineTool(ToolSettings settings) : IKeyboardTool, IOverlayTool
{
    private const int Creating = -2;
    private const int None = -1;

    private PaintSession? _session;
    // Start, control 1, control 2, end.
    private readonly PointD[] _points = new PointD[4];
    private int _dragging = None;
    private bool _curved;
    private ToolButton _button;

    public string Name => "Line / Curve";

    public IReadOnlyList<PointD> Points => _points;

    public bool IsEditing(ImageDocument document) => _session is { IsLive: true } s && s.Document == document;

    public void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        if (IsEditing(document) && HandleAt(document, pointer.Position) is >= 0 and var handle)
        {
            _dragging = handle;
            return;
        }
        _session = new PaintSession(document, Name);
        _button = pointer.Button;
        Array.Fill(_points, pointer.Position);
        _curved = false;
        _dragging = Creating;
    }

    public void OnPointerMove(ImageDocument document, ToolPointer pointer)
    {
        if (_session is null || _dragging == None)
            return;
        if (_dragging == Creating)
        {
            _points[3] = pointer.Position;
        }
        else
        {
            _points[_dragging] = pointer.Position;
            _curved |= _dragging is 1 or 2;
        }
        if (!_curved)
        {
            _points[1] = Lerp(_points[0], _points[3], 1 / 3.0);
            _points[2] = Lerp(_points[0], _points[3], 2 / 3.0);
        }
        Draw();
    }

    public void OnPointerUp(ImageDocument document, ToolPointer pointer)
    {
        if (_session is null || _dragging == None)
            return;
        OnPointerMove(document, pointer);
        if (_dragging == Creating && _points[0] == _points[3])
            _session = null; // A click without dragging just finishes the previous line.
        else
            _session.Commit();
        _dragging = None;
    }

    public void Finish(ImageDocument document)
    {
        if (_session?.Document == document)
            _session = null;
    }

    public void Refresh(ImageDocument document)
    {
        if (!IsEditing(document) || _dragging != None)
            return;
        Draw();
        _session!.Commit();
    }

    public bool OnKeyDown(ImageDocument document, ToolKey key, ToolModifiers modifiers)
    {
        if (!IsEditing(document) || key is not (ToolKey.Enter or ToolKey.Escape))
            return false;
        Finish(document);
        return true;
    }

    public ToolOverlay? GetOverlay(ImageDocument document) => IsEditing(document) && _points[0] != _points[3]
        ? new ToolOverlay { Handles = _points, Lines = [(_points[0], _points[1]), (_points[3], _points[2])] }
        : null;

    private void Draw()
    {
        var session = _session!;
        session.Reset();
        var coverage = new CoverageMask(session.Width, session.Height);
        var radius = Math.Max(0.5, settings.BrushWidth / 2.0);
        var aa = settings.Antialiasing;
        RectangleI region;
        if (!_curved)
        {
            region = coverage.Segment(_points[0], _points[3], radius, aa);
        }
        else
        {
            var bezier = Bezier(_points[0], _points[1], _points[2], _points[3]);
            region = RectangleI.Zero;
            for (var i = 1; i < bezier.Count; i++)
                region = CoverageMask.Union(region, coverage.Segment(bezier[i - 1], bezier[i], radius, aa));
        }
        session.ApplyColor(region, coverage, settings.ColorFor(_button));
    }

    /// <summary>Points along a cubic Bézier curve, about 2 pixels apart.</summary>
    public static List<PointD> Bezier(PointD p0, PointD p1, PointD p2, PointD p3)
    {
        var length = p0.Distance(p1) + p1.Distance(p2) + p2.Distance(p3);
        var steps = Math.Clamp((int)Math.Ceiling(length / 2), 1, 4096);
        var points = new List<PointD>(steps + 1);
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            var u = 1 - t;
            points.Add(new PointD(
                u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X,
                u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y));
        }
        return points;
    }

    // Handles are grabbed within 8 screen pixels, whatever the zoom; control points win over end points.
    private int HandleAt(ImageDocument document, PointD p)
    {
        var tolerance = 8 / Math.Max(document.Workspace.Scale, 0.01);
        var best = None;
        var bestDistance = tolerance;
        foreach (var i in new[] { 1, 2, 0, 3 })
        {
            var d = _points[i].Distance(p);
            if (d <= bestDistance)
                (best, bestDistance) = (i, d);
        }
        return best;
    }

    private static PointD Lerp(PointD a, PointD b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
}

/// <summary>Rectangle or ellipse; outline, fill, or outline in primary + fill in secondary.</summary>
public sealed class ShapesTool(ToolSettings settings) : DragShapeTool(settings)
{
    public override string Name => "Shapes";

    protected override void Draw(PaintSession session, PointD start, PointD end, ToolButton button)
    {
        var (w, h) = (session.Width, session.Height);
        var outlineColor = Settings.ColorFor(button);
        var fillColor = Settings.ShapeStyle == ShapeStyle.OutlineAndFill
            ? Settings.ColorFor(button == ToolButton.Right ? ToolButton.Left : ToolButton.Right)
            : outlineColor;
        var aa = Settings.Antialiasing;
        var width = Math.Max(1, Settings.BrushWidth);
        var kind = Settings.ShapeKind;
        var radius = Settings.CornerRadius;

        CoverageMask? fill = null, outline = null;
        var region = RectangleI.Zero;
        if (Settings.ShapeStyle != ShapeStyle.Outline)
        {
            fill = new CoverageMask(w, h);
            region = CoverageMask.Union(region, kind switch
            {
                ShapeKind.Ellipse => fill.FillEllipse(start, end, aa),
                ShapeKind.RoundedRectangle => fill.FillRoundedRectangle(start, end, radius, aa),
                _ => fill.FillRectangle(start, end, aa),
            });
        }
        if (Settings.ShapeStyle != ShapeStyle.Fill)
        {
            outline = new CoverageMask(w, h);
            region = CoverageMask.Union(region, kind switch
            {
                ShapeKind.Ellipse => outline.StrokeEllipse(start, end, width, aa),
                ShapeKind.RoundedRectangle => outline.StrokeRoundedRectangle(start, end, radius, width, aa),
                _ => outline.StrokeRectangle(start, end, width, aa),
            });
        }

        session.Apply(region, (x, y, pixel) =>
        {
            if (fill is not null)
                PaintSession.BlendCoverage(pixel, fillColor, fill[x, y]);
            if (outline is not null)
                PaintSession.BlendCoverage(pixel, outlineColor, outline[x, y]);
        });
    }
}

/// <summary>
/// Fills from primary (at the start point) to secondary (at the end point); right button swaps them.
/// In transparency mode it fades the layer's alpha instead, from opaque at the start to transparent at the end.
/// </summary>
public sealed class GradientTool(ToolSettings settings) : DragShapeTool(settings)
{
    public override string Name => "Gradient";

    protected override void Draw(PaintSession session, PointD start, PointD end, ToolButton button)
    {
        var from = Settings.ColorFor(button);
        var to = Settings.ColorFor(button == ToolButton.Right ? ToolButton.Left : ToolButton.Right);
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1)
            return;
        var (ux, uy) = (dx / length, dy / length);
        var kind = Settings.GradientKind;
        var transparency = Settings.GradientTransparency;
        var reverse = button == ToolButton.Right;

        session.Apply(new RectangleI(0, 0, session.Width, session.Height), (x, y, pixel) =>
        {
            var px = x + 0.5 - start.X;
            var py = y + 0.5 - start.Y;
            var along = (px * ux + py * uy) / length;
            var across = (-px * uy + py * ux) / length;
            var t = kind switch
            {
                GradientKind.Linear => along,
                GradientKind.Radial => Math.Sqrt(along * along + across * across),
                GradientKind.Diamond => Math.Abs(along) + Math.Abs(across),
                GradientKind.Conical => Math.Abs(Math.Atan2(across, along)) / Math.PI,
                _ => along,
            };
            t = Math.Clamp(t, 0, 1);
            if (transparency)
            {
                pixel[3] = (byte)Math.Round(pixel[3] * (reverse ? t : 1 - t));
                return;
            }
            var color = ColorBgra.Lerp(from, to, t);
            (pixel[0], pixel[1], pixel[2], pixel[3]) = (color.B, color.G, color.R, color.A);
        });
    }
}

/// <summary>Fills similar colors (tolerance, contiguous or global) with the primary/secondary color.</summary>
public sealed class PaintBucketTool(ToolSettings settings) : ITool
{
    public string Name => "Paint Bucket";

    public void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        var (w, h) = (document.ImageSize.Width, document.ImageSize.Height);
        var start = new PointI((int)Math.Floor(pointer.Position.X), (int)Math.Floor(pointer.Position.Y));
        if (start.X < 0 || start.Y < 0 || start.X >= w || start.Y >= h)
            return;
        if (document.Selection is { } selection && !selection.Contains(start.X, start.Y))
            return;

        var session = new PaintSession(document, Name);
        var global = settings.GlobalFill || pointer.Modifiers.HasFlag(ToolModifiers.Shift);
        var area = SelectionMask.MagicWand(session.Base, w, h, start, settings.Tolerance, global);
        var coverage = new CoverageMask(w, h);
        session.ApplyColor(coverage.Fill(area), coverage, settings.ColorFor(pointer.Button));
        session.Commit();
    }

    public void OnPointerMove(ImageDocument document, ToolPointer pointer) { }
    public void OnPointerUp(ImageDocument document, ToolPointer pointer) { }
}

/// <summary>Left click sets the primary color, right click the secondary, from the layer (or the merged image).</summary>
public sealed class ColorPickerTool(ToolSettings settings) : ITool
{
    private bool _active;
    private ToolButton _button;

    public string Name => "Color Picker";

    public void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        _active = true;
        _button = pointer.Button;
        Pick(document, pointer.Position);
    }

    public void OnPointerMove(ImageDocument document, ToolPointer pointer)
    {
        if (_active)
            Pick(document, pointer.Position);
    }

    public void OnPointerUp(ImageDocument document, ToolPointer pointer) => _active = false;

    private void Pick(ImageDocument document, PointD position)
    {
        var (w, h) = (document.ImageSize.Width, document.ImageSize.Height);
        var x = (int)Math.Floor(position.X);
        var y = (int)Math.Floor(position.Y);
        if (x < 0 || y < 0 || x >= w || y >= h)
            return;

        byte[] px;
        if (settings.SampleImage)
        {
            var flat = document.Layers.GetFlattenedBgra(includeToolLayer: false);
            px = flat[((y * w + x) * 4)..((y * w + x) * 4 + 4)];
        }
        else
        {
            px = document.Layers.CurrentUserLayer.Surface.ReadRegion(new RectangleI(x, y, 1, 1));
        }

        var color = ColorBgra.FromBgra(px[0], px[1], px[2], px[3]);
        if (_button == ToolButton.Right)
            settings.SecondaryColor = color;
        else
            settings.PrimaryColor = color;
    }
}
