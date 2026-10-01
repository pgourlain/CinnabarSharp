using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Tools;

/// <summary>
/// Coverage (0–255 per pixel) of one line of text. The line box's top-left corner is at (<see cref="OriginX"/>,
/// <see cref="OriginY"/>) in the raster: glyphs can overhang the line box (italics, accents), hence the margins.
/// </summary>
public sealed record TextRaster(byte[] Coverage, int Width, int Height, int OriginX, int OriginY);

/// <summary>Draws text with the platform's fonts. Implemented by the UI: Core does no font rendering.</summary>
public interface ITextRasterizer
{
    TextRaster RenderLine(string text, TextStyle style);

    /// <summary>Advance width of <paramref name="text"/>, trailing spaces included.</summary>
    double MeasureWidth(string text, TextStyle style);

    double LineHeight(TextStyle style);
}

/// <summary>
/// Click to place a text box, then type. The text stays editable (caret, selection, clipboard, font, color and
/// alignment changes) until it is finished: Escape, a click outside it, another tool, or any other edit.
/// It is painted into the current layer and recorded as one history step that is updated while editing.
/// </summary>
public sealed class TextTool(ToolSettings settings, ITextRasterizer rasterizer) : ITextEditingTool, IOverlayTool
{
    private PaintSession? _session;
    private TextEngine _engine = new();
    private PointD _origin;
    private double _angle; // radians, around the text's own center; 0 = unrotated
    private ToolButton _button;
    private bool _selecting;
    private (PointD Pointer, PointD Origin)? _moving;
    private (double PointerAngle, double TextAngle)? _rotating;

    public string Name => "Text";

    public TextEngine Engine => _engine;

    public bool IsEditing(ImageDocument document) => _session is { IsLive: true } s && s.Document == document;

    public bool IsTyping(ImageDocument document) => IsEditing(document);

    public void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        if (IsEditing(document))
        {
            var local = ToLocal(pointer.Position);
            var threshold = 8 / Math.Max(document.Workspace.Scale, 0.01);
            if (RotateHandle().Distance(local) <= threshold)
            {
                var pivot = Pivot(Bounds(Layout()));
                _rotating = (Math.Atan2(pointer.Position.Y - pivot.Y, pointer.Position.X - pivot.X), _angle);
                return;
            }
            if (MoveHandle().Distance(local) <= threshold)
            {
                _moving = (pointer.Position, _origin);
                return;
            }
            if (Contains(local, margin: 4))
            {
                _engine.SetCursorPosition(PositionAt(local), clearSelection: !pointer.Modifiers.HasFlag(ToolModifiers.Shift));
                _selecting = true;
                return;
            }
        }
        _session = new PaintSession(document, Name);
        _engine = new TextEngine();
        _origin = new PointD(Math.Floor(pointer.Position.X), Math.Floor(pointer.Position.Y));
        _angle = 0;
        _button = pointer.Button;
    }

    public void OnPointerMove(ImageDocument document, ToolPointer pointer)
    {
        if (!IsEditing(document))
            return;
        if (_rotating is { } rotate)
        {
            var pivot = Pivot(Bounds(Layout()));
            var pointerAngle = Math.Atan2(pointer.Position.Y - pivot.Y, pointer.Position.X - pivot.X);
            _angle = rotate.TextAngle + (pointerAngle - rotate.PointerAngle);
            Render();
        }
        else if (_moving is { } move)
        {
            _origin = new PointD(Math.Round(move.Origin.X + pointer.Position.X - move.Pointer.X),
                Math.Round(move.Origin.Y + pointer.Position.Y - move.Pointer.Y));
            Render();
        }
        else if (_selecting)
        {
            _engine.SetCursorPosition(PositionAt(ToLocal(pointer.Position)), clearSelection: false);
        }
    }

    public void OnPointerUp(ImageDocument document, ToolPointer pointer)
    {
        OnPointerMove(document, pointer);
        _selecting = false;
        _moving = null;
        _rotating = null;
    }

    public void OnTextInput(ImageDocument document, string text)
    {
        if (!IsEditing(document))
            return;
        text = new string(text.Where(c => !char.IsControl(c)).ToArray());
        if (text.Length == 0)
            return;
        _engine.InsertText(text);
        Render();
    }

    public bool OnKeyDown(ImageDocument document, ToolKey key, ToolModifiers modifiers)
    {
        if (!IsEditing(document))
            return false;
        switch (TextBlock.HandleKey(_engine, key, modifiers))
        {
            case TextBlock.KeyResult.Escape:
                Finish(document);
                return true;
            case TextBlock.KeyResult.Edited:
                Render();
                return true;
            case TextBlock.KeyResult.Moved:
                return true;
            default:
                return false;
        }
    }

    public void Finish(ImageDocument document)
    {
        if (_session?.Document == document)
            _session = null;
    }

    public void Refresh(ImageDocument document)
    {
        if (IsEditing(document))
            Render();
    }

    public void SelectAll(ImageDocument document)
    {
        if (IsEditing(document))
            _engine.SelectAll();
    }

    public async Task Copy(IClipboardService clipboard)
    {
        if (_engine.HasSelection)
            await _engine.PerformCopy(clipboard);
    }

    public async Task Cut(ImageDocument document, IClipboardService clipboard)
    {
        if (!IsEditing(document) || !_engine.HasSelection)
            return;
        await _engine.PerformCut(clipboard);
        Render();
    }

    public async Task Paste(ImageDocument document, IClipboardService clipboard)
    {
        if (IsEditing(document) && await _engine.PerformPaste(clipboard) && IsEditing(document))
            Render();
    }

    public ToolOverlay? GetOverlay(ImageDocument document)
    {
        if (!IsEditing(document))
            return null;
        var layout = Layout();
        var caret = layout.CaretPoint(_engine.CurrentPosition);
        var highlights = _engine.HasSelection ? layout.SelectionRects(_engine.CurrentPosition, _engine.SelectionStart) : [];
        var bounds = Bounds(layout);
        return new ToolOverlay
        {
            Frame = new RectangleD(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4),
            Lines = [(caret, new PointD(caret.X, caret.Y + layout.LineHeight))],
            Highlights = highlights,
            Handles = [MoveHandle()],
            RotateHandle = RotateHandle(),
            Rotation = (_angle, Pivot(bounds)),
        };
    }

    /// <summary>Dragging the handle at the bottom-right corner of the text moves it, as in Paint.NET.</summary>
    private PointD MoveHandle()
    {
        var bounds = Bounds(Layout());
        return new PointD(bounds.X + bounds.Width + 2, bounds.Y + bounds.Height + 2);
    }

    /// <summary>Dragging the handle at the top-right corner rotates the text around its own center.</summary>
    private PointD RotateHandle()
    {
        var bounds = Bounds(Layout());
        return new PointD(bounds.X + bounds.Width + 6, bounds.Y - 8);
    }

    private static PointD Pivot(RectangleD bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

    /// <summary>An image-space point (e.g. the raw pointer position) as it would be before the current rotation —
    /// everything else (bounds, handles, caret) is computed in that same unrotated space.</summary>
    private PointD ToLocal(PointD p) => _angle == 0 ? p : RotateAround(p, Pivot(Bounds(Layout())), -_angle);

    private static PointD RotateAround(PointD p, PointD pivot, double angle)
    {
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var dx = p.X - pivot.X;
        var dy = p.Y - pivot.Y;
        return new PointD(pivot.X + dx * cos - dy * sin, pivot.Y + dx * sin + dy * cos);
    }

    private TextBlock Layout() => new(_engine.Lines, settings.TextStyle, settings.TextAlignment, rasterizer, _origin);

    private static RectangleD Bounds(TextBlock layout) => layout.Bounds;

    private bool Contains(PointD p, double margin)
    {
        var b = Bounds(Layout());
        return p.X >= b.X - margin && p.X <= b.X + b.Width + margin && p.Y >= b.Y - margin && p.Y <= b.Y + b.Height + margin;
    }

    /// <summary>The caret position closest to an image point.</summary>
    private TextPosition PositionAt(PointD p) => Layout().PositionAt(p);

    private void Render()
    {
        var session = _session!;
        session.Reset();

        var layout = Layout();
        var (w, h) = (session.Width, session.Height);
        var (region, coverage) = layout.Rasterize(w, h);
        if (!region.IsEmpty)
        {
            if (_angle != 0)
                (region, coverage) = Rotate(region, coverage, Pivot(Bounds(layout)), _angle, w, h, layout.Style.Antialias);

            if (!region.IsEmpty)
            {
                var color = settings.ColorFor(_button);
                session.Apply(region, (x, y, pixel) =>
                    PaintSession.BlendCoverage(pixel, color, coverage[(y - region.Y) * region.Width + x - region.X]));
            }
        }
        session.Commit();
    }

    /// <summary>
    /// Rotates a coverage buffer around <paramref name="pivot"/> by <paramref name="angle"/> radians into a new
    /// buffer sized to the rotated (axis-aligned) bounding box, clipped to the image. Bilinear-sampled when
    /// <paramref name="antialias"/> (matching the glyphs themselves); nearest-neighbor otherwise, so turning
    /// antialiasing off gives fully hard edges even on a rotated angle, not just an axis-aligned one.
    /// </summary>
    private static (RectangleI Region, byte[] Coverage) Rotate(RectangleI region, byte[] coverage, PointD pivot,
        double angle, int width, int height, bool antialias)
    {
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        PointD Forward(PointD p)
        {
            var dx = p.X - pivot.X;
            var dy = p.Y - pivot.Y;
            return new PointD(pivot.X + dx * cos - dy * sin, pivot.Y + dx * sin + dy * cos);
        }

        var corners = new[]
        {
            new PointD(region.X, region.Y), new PointD(region.X + region.Width, region.Y),
            new PointD(region.X, region.Y + region.Height), new PointD(region.X + region.Width, region.Y + region.Height),
        }.Select(Forward).ToArray();
        var x0 = Math.Clamp((int)Math.Floor(corners.Min(c => c.X)), 0, width);
        var y0 = Math.Clamp((int)Math.Floor(corners.Min(c => c.Y)), 0, height);
        var x1 = Math.Clamp((int)Math.Ceiling(corners.Max(c => c.X)), 0, width);
        var y1 = Math.Clamp((int)Math.Ceiling(corners.Max(c => c.Y)), 0, height);
        if (x1 <= x0 || y1 <= y0)
            return (RectangleI.Zero, []);

        var rotated = new RectangleI(x0, y0, x1 - x0, y1 - y0);
        var result = new byte[rotated.Width * rotated.Height];
        for (var ry = 0; ry < rotated.Height; ry++)
        {
            for (var rx = 0; rx < rotated.Width; rx++)
            {
                // Inverse-rotate this destination pixel's center back into the unrotated source.
                var dx = rotated.X + rx + 0.5 - pivot.X;
                var dy = rotated.Y + ry + 0.5 - pivot.Y;
                var src = new PointD(pivot.X + dx * cos + dy * sin, pivot.Y - dx * sin + dy * cos);
                result[ry * rotated.Width + rx] = antialias ? Sample(coverage, region, src) : SampleNearest(coverage, region, src);
            }
        }
        return (rotated, result);
    }

    private static byte Sample(byte[] coverage, RectangleI region, PointD p)
    {
        var x = p.X - region.X - 0.5;
        var y = p.Y - region.Y - 0.5;
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        double At(int cx, int cy) =>
            cx >= 0 && cy >= 0 && cx < region.Width && cy < region.Height ? coverage[cy * region.Width + cx] : 0;
        var top = At(x0, y0) * (1 - fx) + At(x0 + 1, y0) * fx;
        var bottom = At(x0, y0 + 1) * (1 - fx) + At(x0 + 1, y0 + 1) * fx;
        return (byte)Math.Round(top * (1 - fy) + bottom * fy);
    }

    private static byte SampleNearest(byte[] coverage, RectangleI region, PointD p)
    {
        var x = (int)Math.Floor(p.X - region.X);
        var y = (int)Math.Floor(p.Y - region.Y);
        return x >= 0 && y >= 0 && x < region.Width && y < region.Height ? coverage[y * region.Width + x] : (byte)0;
    }
}
