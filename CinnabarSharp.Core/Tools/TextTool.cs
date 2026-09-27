using System.Globalization;
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
public sealed class TextTool(ToolSettings settings, ITextRasterizer rasterizer) : IKeyboardTool, IOverlayTool
{
    private PaintSession? _session;
    private TextEngine _engine = new();
    private PointD _origin;
    private ToolButton _button;
    private bool _selecting;
    private (PointD Pointer, PointD Origin)? _moving;

    public string Name => "Text";

    public TextEngine Engine => _engine;

    public bool IsEditing(ImageDocument document) => _session is { IsLive: true } s && s.Document == document;

    public bool IsTyping(ImageDocument document) => IsEditing(document);

    public void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        if (IsEditing(document) && MoveHandle().Distance(pointer.Position) <= 8 / Math.Max(document.Workspace.Scale, 0.01))
        {
            _moving = (pointer.Position, _origin);
            return;
        }
        if (IsEditing(document) && Contains(pointer.Position, margin: 4))
        {
            _engine.SetCursorPosition(PositionAt(pointer.Position), clearSelection: !pointer.Modifiers.HasFlag(ToolModifiers.Shift));
            _selecting = true;
            return;
        }
        _session = new PaintSession(document, Name);
        _engine = new TextEngine();
        _origin = new PointD(Math.Floor(pointer.Position.X), Math.Floor(pointer.Position.Y));
        _button = pointer.Button;
    }

    public void OnPointerMove(ImageDocument document, ToolPointer pointer)
    {
        if (!IsEditing(document))
            return;
        if (_moving is { } move)
        {
            _origin = new PointD(Math.Round(move.Origin.X + pointer.Position.X - move.Pointer.X),
                Math.Round(move.Origin.Y + pointer.Position.Y - move.Pointer.Y));
            Render();
        }
        else if (_selecting)
        {
            _engine.SetCursorPosition(PositionAt(pointer.Position), clearSelection: false);
        }
    }

    public void OnPointerUp(ImageDocument document, ToolPointer pointer)
    {
        OnPointerMove(document, pointer);
        _selecting = false;
        _moving = null;
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
        var shift = modifiers.HasFlag(ToolModifiers.Shift);
        // Word-wise moves: Alt on macOS, Ctrl elsewhere; accept both.
        var word = (modifiers & (ToolModifiers.Command | ToolModifiers.Alt)) != 0;
        switch (key)
        {
            case ToolKey.Escape:
                Finish(document);
                return true;
            case ToolKey.Enter:
                _engine.PerformEnter();
                break;
            case ToolKey.Backspace:
                _engine.PerformBackspace();
                break;
            case ToolKey.Delete:
                _engine.PerformDelete();
                break;
            case ToolKey.Left:
                _engine.PerformLeft(word, shift);
                return true;
            case ToolKey.Right:
                _engine.PerformRight(word, shift);
                return true;
            case ToolKey.Up:
                _engine.PerformUp(shift);
                return true;
            case ToolKey.Down:
                _engine.PerformDown(shift);
                return true;
            case ToolKey.Home:
                _engine.PerformHome(word, shift);
                return true;
            case ToolKey.End:
                _engine.PerformEnd(word, shift);
                return true;
            default:
                return false;
        }
        Render();
        return true;
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
        var caret = CaretPoint(layout, _engine.CurrentPosition);
        var highlights = new List<RectangleD>();
        if (_engine.HasSelection)
        {
            var start = TextPosition.Min(_engine.CurrentPosition, _engine.SelectionStart);
            var end = TextPosition.Max(_engine.CurrentPosition, _engine.SelectionStart);
            for (var line = start.Line; line <= end.Line; line++)
            {
                var from = CaretPoint(layout, new TextPosition(line, line == start.Line ? start.Offset : 0));
                var to = CaretPoint(layout, new TextPosition(line, line == end.Line ? end.Offset : _engine.Lines[line].Length));
                highlights.Add(new RectangleD(from.X, from.Y, Math.Max(2, to.X - from.X), layout.LineHeight));
            }
        }
        var bounds = Bounds(layout);
        return new ToolOverlay
        {
            Frame = new RectangleD(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4),
            Lines = [(caret, new PointD(caret.X, caret.Y + layout.LineHeight))],
            Highlights = highlights,
            Handles = [MoveHandle()],
        };
    }

    /// <summary>Dragging the handle at the bottom-right corner of the text moves it, as in Paint.NET.</summary>
    private PointD MoveHandle()
    {
        var bounds = Bounds(Layout());
        return new PointD(bounds.X + bounds.Width + 2, bounds.Y + bounds.Height + 2);
    }

    private sealed record TextLayout(double LineHeight, double[] Widths, double MaxWidth);

    private TextLayout Layout()
    {
        var style = settings.TextStyle;
        var widths = _engine.Lines.Select(l => rasterizer.MeasureWidth(l, style)).ToArray();
        return new TextLayout(rasterizer.LineHeight(style), widths, widths.DefaultIfEmpty(0).Max());
    }

    private double LineX(TextLayout layout, int line) => _origin.X + settings.TextAlignment switch
    {
        TextAlignment.Center => (layout.MaxWidth - layout.Widths[line]) / 2,
        TextAlignment.Right => layout.MaxWidth - layout.Widths[line],
        _ => 0,
    };

    private PointD CaretPoint(TextLayout layout, TextPosition p)
    {
        var line = Math.Clamp(p.Line, 0, _engine.LineCount - 1);
        var text = _engine.Lines[line];
        var prefix = text[..Math.Clamp(p.Offset, 0, text.Length)];
        return new PointD(LineX(layout, line) + rasterizer.MeasureWidth(prefix, settings.TextStyle),
            _origin.Y + line * layout.LineHeight);
    }

    private RectangleD Bounds(TextLayout layout) =>
        new(_origin.X, _origin.Y, Math.Max(layout.MaxWidth, 1), layout.LineHeight * _engine.LineCount);

    private bool Contains(PointD p, double margin)
    {
        var b = Bounds(Layout());
        return p.X >= b.X - margin && p.X <= b.X + b.Width + margin && p.Y >= b.Y - margin && p.Y <= b.Y + b.Height + margin;
    }

    /// <summary>The caret position closest to an image point.</summary>
    private TextPosition PositionAt(PointD p)
    {
        var layout = Layout();
        var line = Math.Clamp((int)Math.Floor((p.Y - _origin.Y) / layout.LineHeight), 0, _engine.LineCount - 1);
        var text = _engine.Lines[line];
        var x = p.X - LineX(layout, line);
        var best = 0;
        var bestDistance = double.MaxValue;
        foreach (var offset in StringInfo.ParseCombiningCharacters(text).Append(text.Length))
        {
            var d = Math.Abs(rasterizer.MeasureWidth(text[..offset], settings.TextStyle) - x);
            if (d < bestDistance)
                (best, bestDistance) = (offset, d);
        }
        return new TextPosition(line, best);
    }

    private void Render()
    {
        var session = _session!;
        session.Reset();

        var style = settings.TextStyle;
        var layout = Layout();
        var (w, h) = (session.Width, session.Height);
        var lines = new List<(TextRaster Raster, int X, int Y)>();
        var region = RectangleI.Zero;
        for (var i = 0; i < _engine.LineCount; i++)
        {
            if (_engine.Lines[i].Length == 0)
                continue;
            var raster = rasterizer.RenderLine(_engine.Lines[i], style);
            var x = (int)Math.Round(LineX(layout, i)) - raster.OriginX;
            var y = (int)Math.Round(_origin.Y + i * layout.LineHeight) - raster.OriginY;
            lines.Add((raster, x, y));
            region = CoverageMask.Union(region, Clip(new RectangleI(x, y, raster.Width, raster.Height), w, h));
        }

        if (!region.IsEmpty)
        {
            // Lines don't overlap much, but combine them with "max" like brush dabs.
            var coverage = new byte[region.Width * region.Height];
            foreach (var (raster, lx, ly) in lines)
            {
                for (var ry = 0; ry < raster.Height; ry++)
                {
                    var y = ly + ry - region.Y;
                    if (y < 0 || y >= region.Height)
                        continue;
                    for (var rx = 0; rx < raster.Width; rx++)
                    {
                        var x = lx + rx - region.X;
                        if (x < 0 || x >= region.Width)
                            continue;
                        var c = raster.Coverage[ry * raster.Width + rx];
                        if (!style.Antialias)
                            c = c >= 128 ? (byte)255 : (byte)0;
                        ref var cell = ref coverage[y * region.Width + x];
                        if (c > cell)
                            cell = c;
                    }
                }
            }
            var color = settings.ColorFor(_button);
            session.Apply(region, (x, y, pixel) =>
                PaintSession.BlendCoverage(pixel, color, coverage[(y - region.Y) * region.Width + x - region.X]));
        }
        session.Commit();
    }

    private static RectangleI Clip(RectangleI r, int w, int h)
    {
        int x0 = Math.Clamp(r.X, 0, w), y0 = Math.Clamp(r.Y, 0, h);
        int x1 = Math.Clamp(r.X + r.Width, 0, w), y1 = Math.Clamp(r.Y + r.Height, 0, h);
        return x1 > x0 && y1 > y0 ? new RectangleI(x0, y0, x1 - x0, y1 - y0) : RectangleI.Zero;
    }
}
