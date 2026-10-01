using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Tools;

/// <summary>
/// Comic speech bubbles for annotating pictures. Press on what the bubble is about (the tail's tip), drag to where
/// the bubble goes, release and type: the bubble grows with its text. A click without dragging puts the bubble
/// above-right of the point. While the bubble is edited, its tail tip and 8 resize handles can be dragged (after
/// a resize the width stays and the text wraps), dragging its border moves it with the tail tip staying put, and
/// clicking anywhere else starts the next bubble, so several are placed in a row.
/// Outline and text use the primary color, the inside the secondary color. Each bubble is one history step
/// (updated while it is edited); by default bubbles go to a "Bubbles" layer at the top, created when needed.
/// </summary>
public sealed class SpeechBubbleTool(ToolSettings settings, ITextRasterizer rasterizer) : ITextEditingTool, IOverlayTool
{
    private enum DragKind
    {
        None,
        Create,
        Tail,
        Resize,
        Move,
        Select,
    }

    private const int Grab = 8; // screen pixels around a handle

    private PaintSession? _session;
    private TextEngine _engine = new();
    private PointD _center;
    private PointD _tail;

    // Size chosen with the resize handles: the width stays (text wraps) and the height grows when the text needs it.
    private (double Width, double Height)? _size;
    private int? _number;

    private DragKind _drag;
    private int _handle;
    private PointD _dragStart;
    private (PointD Center, PointD Tail, RectangleD Body) _before;

    public string Name => "Speech Bubble";

    public static string LayerName => Translations.GetString("Bubbles");

    public TextEngine Engine => _engine;

    public bool IsEditing(ImageDocument document) => _session is { IsLive: true } s && s.Document == document;

    public bool IsTyping(ImageDocument document) => IsEditing(document);

    /// <summary>The bubble being edited (body and tail tip), for tests and the overlay.</summary>
    public BubbleShape? Shape(ImageDocument document) => IsEditing(document) ? Layout().Shape : null;

    // ---- Layout ----

    private sealed record BubbleLayout(BubbleShape Shape, TextBlock Text, double OutlineWidth);

    private double OutlineWidth => Math.Max(1, settings.BrushWidth);

    private BubbleLayout Layout()
    {
        var style = settings.BubbleStyle;
        var textStyle = settings.TextStyle;
        var lineHeight = rasterizer.LineHeight(textStyle);
        var padding = Math.Round(lineHeight * 0.5) + OutlineWidth;
        TextBlock text;
        double width, height;
        if (_size is { } size)
        {
            var wrap = Math.Max(1, BubbleShape.TextWidthFor(style, size.Width, padding));
            text = new TextBlock(_engine.Lines, textStyle, settings.TextAlignment, rasterizer, default, wrap);
            width = size.Width;
            height = Math.Max(size.Height, BubbleShape.BodySizeFor(style, wrap, text.Height, padding).Height);
        }
        else
        {
            text = new TextBlock(_engine.Lines, textStyle, settings.TextAlignment, rasterizer, default);
            (width, height) = BubbleShape.BodySizeFor(style, Math.Max(text.MaxWidth, lineHeight * 2), text.Height, padding);
        }
        var body = new RectangleD(Math.Round(_center.X - width / 2), Math.Round(_center.Y - height / 2),
            Math.Round(width), Math.Round(height));
        var area = BubbleShape.TextArea(style, body, padding);
        text.Origin = new PointD(Math.Round(area.X + (area.Width - text.BoxWidth) / 2),
            Math.Round(area.Y + (area.Height - text.Height) / 2));
        return new BubbleLayout(new BubbleShape(style, body, _tail, settings.CornerRadius), text, OutlineWidth);
    }

    /// <summary>Resize handles: top-left, top, top-right, right, bottom-right, bottom, bottom-left, left.</summary>
    private static PointD[] ResizeHandles(RectangleD b)
    {
        var (x1, y1, x2, y2) = (b.X, b.Y, b.X + b.Width, b.Y + b.Height);
        var (cx, cy) = ((x1 + x2) / 2, (y1 + y2) / 2);
        return [new(x1, y1), new(cx, y1), new(x2, y1), new(x2, cy), new(x2, y2), new(cx, y2), new(x1, y2), new(x1, cy)];
    }

    private static double Tolerance(ImageDocument document) => Grab / Math.Max(document.Workspace.Scale, 0.01);

    private int ResizeHandleAt(RectangleD body, PointD p, double tolerance)
    {
        var handles = ResizeHandles(body);
        var (best, bestDistance) = (-1, double.MaxValue);
        for (var i = 0; i < handles.Length; i++)
        {
            var (dx, dy) = (Math.Abs(handles[i].X - p.X), Math.Abs(handles[i].Y - p.Y));
            if (dx <= tolerance && dy <= tolerance && dx + dy < bestDistance)
                (best, bestDistance) = (i, dx + dy);
        }
        return best;
    }

    private static bool InTextArea(TextBlock text, PointD p)
    {
        var b = text.Bounds;
        const double margin = 4;
        return p.X >= b.X - margin && p.X <= b.X + b.Width + margin && p.Y >= b.Y - margin && p.Y <= b.Y + b.Height + margin;
    }

    private DragKind HitTest(ImageDocument document, PointD p, out int handle)
    {
        handle = -1;
        if (!IsEditing(document))
            return DragKind.None;
        var layout = Layout();
        var tolerance = Tolerance(document);
        if (_tail.Distance(p) <= tolerance)
            return DragKind.Tail;
        if ((handle = ResizeHandleAt(layout.Shape.Body, p, tolerance)) >= 0)
            return DragKind.Resize;
        if (InTextArea(layout.Text, p))
            return DragKind.Select;
        return layout.Shape.SignedDistance(p.X, p.Y) <= tolerance / 2 ? DragKind.Move : DragKind.None;
    }

    // ---- Pointer ----

    public void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        var p = pointer.Position;
        _drag = HitTest(document, p, out _handle);
        _dragStart = p;
        if (_drag != DragKind.None)
        {
            _before = (_center, _tail, Layout().Shape.Body);
            if (_drag == DragKind.Select)
                _engine.SetCursorPosition(Layout().Text.PositionAt(p), clearSelection: !pointer.Modifiers.HasFlag(ToolModifiers.Shift));
            return;
        }

        // A new bubble; the one being edited (if any) stays as it is.
        if (settings.BubbleOwnLayer)
            UseBubblesLayer(document);
        _session = new PaintSession(document, Name);
        _engine = new TextEngine();
        _tail = new PointD(Math.Round(p.X), Math.Round(p.Y));
        _center = _tail;
        _size = null;
        _number = null;
        if (settings.BubbleNumbered)
            _number = settings.BubbleNextNumber++;
        _drag = DragKind.Create;
    }

    public void OnPointerMove(ImageDocument document, ToolPointer pointer)
    {
        if (_drag == DragKind.None || !IsEditing(document))
            return;
        var p = pointer.Position;
        var (dx, dy) = (p.X - _dragStart.X, p.Y - _dragStart.Y);
        switch (_drag)
        {
            case DragKind.Create:
                if (_dragStart.Distance(p) < Tolerance(document))
                    return;
                _center = new PointD(Math.Round(p.X), Math.Round(p.Y));
                break;
            case DragKind.Tail:
                _tail = new PointD(Math.Round(_before.Tail.X + dx), Math.Round(_before.Tail.Y + dy));
                break;
            case DragKind.Move:
                _center = new PointD(Math.Round(_before.Center.X + dx), Math.Round(_before.Center.Y + dy));
                break;
            case DragKind.Resize:
                Resize(p);
                break;
            case DragKind.Select:
                _engine.SetCursorPosition(Layout().Text.PositionAt(p), clearSelection: false);
                return;
        }
        Render();
    }

    private void Resize(PointD p)
    {
        var b = _before.Body;
        double x1 = b.X, y1 = b.Y, x2 = b.X + b.Width, y2 = b.Y + b.Height;
        if (_handle is 0 or 6 or 7)
            x1 = p.X;
        if (_handle is 2 or 3 or 4)
            x2 = p.X;
        if (_handle is 0 or 1 or 2)
            y1 = p.Y;
        if (_handle is 4 or 5 or 6)
            y2 = p.Y;
        var min = rasterizer.LineHeight(settings.TextStyle) * 2;
        (x1, x2) = (Math.Min(x1, x2), Math.Max(Math.Max(x1, x2), Math.Min(x1, x2) + min));
        (y1, y2) = (Math.Min(y1, y2), Math.Max(Math.Max(y1, y2), Math.Min(y1, y2) + min));
        _size = (x2 - x1, y2 - y1);
        _center = new PointD((x1 + x2) / 2, (y1 + y2) / 2);
    }

    public void OnPointerUp(ImageDocument document, ToolPointer pointer)
    {
        if (_drag == DragKind.Create && IsEditing(document))
        {
            if (_dragStart.Distance(pointer.Position) < Tolerance(document))
            {
                // A click: the bubble goes above-right of the point.
                var offset = Math.Max(40, rasterizer.LineHeight(settings.TextStyle) * 3);
                _center = new PointD(_tail.X + offset, _tail.Y - offset);
                KeepOnImage(document);
            }
            else
            {
                _center = new PointD(Math.Round(pointer.Position.X), Math.Round(pointer.Position.Y));
            }
            Render();
        }
        else
        {
            OnPointerMove(document, pointer);
        }
        _drag = DragKind.None;
    }

    /// <summary>Moves the body (not the tail) inside the image when it fits.</summary>
    private void KeepOnImage(ImageDocument document)
    {
        var body = Layout().Shape.Body;
        var (w, h) = (document.ImageSize.Width, document.ImageSize.Height);
        var dx = body.X < 0 ? -body.X : body.X + body.Width > w ? w - body.X - body.Width : 0;
        var dy = body.Y < 0 ? -body.Y : body.Y + body.Height > h ? h - body.Y - body.Height : 0;
        if (body.Width <= w && body.Height <= h)
            _center = new PointD(_center.X + dx, _center.Y + dy);
    }

    /// <summary>Selects the "Bubbles" layer: the current one if it has that name, else the top one, else a new layer at the top.</summary>
    private static void UseBubblesLayer(ImageDocument document)
    {
        var layers = document.Layers;
        if (layers.CurrentUserLayer.Name == LayerName)
            return;
        var top = layers.UserLayers[^1];
        if (top != layers.CurrentUserLayer)
            layers.SetCurrentUserLayer(top);
        if (top.Name != LayerName)
            document.Actions.AddNewLayer(LayerName);
    }

    /// <summary>
    /// Adds a finished bubble without the mouse (for agents): tail tip at <paramref name="tail"/>, centered on
    /// <paramref name="center"/> (above-right of the tip when null), <paramref name="width"/> fixed (text wraps) or
    /// fitted to the text when null. Same history and layer handling as drawing it.
    /// </summary>
    public BubbleShape Place(ImageDocument document, PointD tail, PointD? center, string text, double? width = null)
    {
        var at = new ToolPointer(tail, ToolButton.Left, ToolModifiers.None);
        OnPointerDown(document, at);
        OnPointerUp(document, center is { } c ? at with { Position = c } : at);
        if (width is { } w)
            _size = (Math.Max(w, rasterizer.LineHeight(settings.TextStyle) * 2), 0);
        if (text.Length > 0)
            _engine.InsertLines(text);
        Render();
        var shape = Layout().Shape;
        Finish(document);
        return shape;
    }

    // ---- Keyboard and clipboard ----

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

    // ---- Overlay ----

    public ToolOverlay? GetOverlay(ImageDocument document)
    {
        if (!IsEditing(document))
            return null;
        var layout = Layout();
        var caret = layout.Text.CaretPoint(_engine.CurrentPosition);
        return new ToolOverlay
        {
            Frame = layout.Shape.Body,
            Handles = [.. ResizeHandles(layout.Shape.Body), _tail],
            SquareHandles = true,
            Lines = [(caret, new PointD(caret.X, caret.Y + layout.Text.LineHeight))],
            Highlights = _engine.HasSelection ? layout.Text.SelectionRects(_engine.CurrentPosition, _engine.SelectionStart) : [],
        };
    }

    public ToolCursor CursorAt(ImageDocument document, PointD point) => HitTest(document, point, out var handle) switch
    {
        DragKind.Tail or DragKind.Move => ToolCursor.Move,
        DragKind.Select => ToolCursor.Text,
        DragKind.Resize => handle switch
        {
            0 or 4 => ToolCursor.ResizeDiagonal,
            2 or 6 => ToolCursor.ResizeAntiDiagonal,
            1 or 5 => ToolCursor.ResizeVertical,
            _ => ToolCursor.ResizeHorizontal,
        },
        _ => ToolCursor.Default,
    };

    // ---- Rendering ----

    private void Render()
    {
        var session = _session!;
        session.Reset();
        if (settings.BubbleNumbered && _number is null)
            _number = settings.BubbleNextNumber++;

        var layout = Layout();
        var (w, h) = (session.Width, session.Height);
        var aa = settings.Antialiasing;
        var shape = layout.Shape;
        var extent = shape.Extent;

        var outer = new CoverageMask(w, h);
        var inner = new CoverageMask(w, h);
        var right = extent.X + extent.Width;
        var bottom = extent.Y + extent.Height;
        var region = outer.FillDistance(extent.X, extent.Y, right, bottom, shape.SignedDistance, aa);
        inner.FillDistance(extent.X, extent.Y, right, bottom, shape.SignedDistance, aa, inset: layout.OutlineWidth);

        var (textRegion, text) = layout.Text.Rasterize(w, h);
        region = CoverageMask.Union(region, textRegion);

        CoverageMask? badge = null;
        RectangleI digitsRegion = RectangleI.Zero;
        byte[] digits = [];
        if (settings.BubbleNumbered && _number is { } number)
        {
            var center = BubbleShape.BadgeCenter(shape.Style, shape.Body);
            var radius = Math.Max(8, Math.Round(layout.Text.LineHeight * 0.6));
            badge = new CoverageMask(w, h);
            region = CoverageMask.Union(region, badge.Disc(center.X, center.Y, radius, aa));
            var digitStyle = settings.TextStyle with { Size = Math.Round(radius * 1.1), Bold = true, Italic = false, Underline = false };
            var label = new TextBlock([number.ToString()], digitStyle, TextAlignment.Left, rasterizer, default);
            label.Origin = new PointD(Math.Round(center.X - label.MaxWidth / 2), Math.Round(center.Y - label.Height / 2));
            (digitsRegion, digits) = label.Rasterize(w, h);
        }

        var outline = settings.PrimaryColor;
        var fill = settings.SecondaryColor;
        session.Apply(region, (x, y, pixel) =>
        {
            PaintSession.BlendCoverage(pixel, outline, outer[x, y]);
            PaintSession.BlendCoverage(pixel, fill, inner[x, y]);
            PaintSession.BlendCoverage(pixel, outline, At(text, textRegion, x, y));
            if (badge is not null)
            {
                PaintSession.BlendCoverage(pixel, outline, badge[x, y]);
                PaintSession.BlendCoverage(pixel, fill, At(digits, digitsRegion, x, y));
            }
        });
        session.Commit();
    }

    private static byte At(byte[] coverage, RectangleI region, int x, int y)
    {
        var (lx, ly) = (x - region.X, y - region.Y);
        return (uint)lx < (uint)region.Width && (uint)ly < (uint)region.Height ? coverage[ly * region.Width + lx] : (byte)0;
    }
}
