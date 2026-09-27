using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Tools;

/// <summary>Freehand stroke: round dabs joined by segments. Left = primary color, right = secondary.</summary>
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

    protected virtual void Apply(PaintSession session, RectangleI region, CoverageMask coverage, ColorBgra color) =>
        session.ApplyColor(region, coverage, color);

    protected virtual RectangleI Dab(CoverageMask coverage, PointD from, PointD to) =>
        from == to ? coverage.Disc(to.X, to.Y, Radius, Antialias) : coverage.Segment(from, to, Radius, Antialias);

    public void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        _session = new PaintSession(document, Name);
        _coverage = new CoverageMask(document.ImageSize.Width, document.ImageSize.Height);
        _color = settings.ColorFor(pointer.Button);
        _last = pointer.Position;
        Apply(_session, Dab(_coverage, _last, _last), _coverage, _color);
    }

    public void OnPointerMove(ImageDocument document, ToolPointer pointer)
    {
        if (_session is null || _coverage is null || pointer.Position == _last)
            return;
        Apply(_session, Dab(_coverage, _last, pointer.Position), _coverage, _color);
        _last = pointer.Position;
    }

    public void OnPointerUp(ImageDocument document, ToolPointer pointer)
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

    protected override RectangleI Dab(CoverageMask coverage, PointD from, PointD to) =>
        coverage.PixelLine(Floor(from), Floor(to));

    private static PointI Floor(PointD p) => new((int)Math.Floor(p.X), (int)Math.Floor(p.Y));
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

public sealed class LineTool(ToolSettings settings) : DragShapeTool(settings)
{
    public override string Name => "Line / Curve";

    protected override void Draw(PaintSession session, PointD start, PointD end, ToolButton button)
    {
        var coverage = new CoverageMask(session.Width, session.Height);
        var region = coverage.Segment(start, end, Math.Max(0.5, Settings.BrushWidth / 2.0), Settings.Antialiasing);
        session.ApplyColor(region, coverage, Settings.ColorFor(button));
    }
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
        var ellipse = Settings.ShapeKind == ShapeKind.Ellipse;

        CoverageMask? fill = null, outline = null;
        var region = RectangleI.Zero;
        if (Settings.ShapeStyle != ShapeStyle.Outline)
        {
            fill = new CoverageMask(w, h);
            region = CoverageMask.Union(region, ellipse ? fill.FillEllipse(start, end, aa) : fill.FillRectangle(start, end, aa));
        }
        if (Settings.ShapeStyle != ShapeStyle.Fill)
        {
            outline = new CoverageMask(w, h);
            region = CoverageMask.Union(region,
                ellipse ? outline.StrokeEllipse(start, end, width, aa) : outline.StrokeRectangle(start, end, width, aa));
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

/// <summary>Fills from primary (at the start point) to secondary (at the end point); right button swaps them.</summary>
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
            var color = ColorBgra.Lerp(from, to, Math.Clamp(t, 0, 1));
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
