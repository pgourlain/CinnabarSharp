using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Tools;

public enum CropAspect
{
    Free,

    /// <summary>16:9, TVs and screens.</summary>
    Wide,

    /// <summary>9:16, phone screens.</summary>
    Tall,

    /// <summary>4:3.</summary>
    Standard,

    /// <summary>3:2, most cameras.</summary>
    Photo,

    Square,
}

/// <summary>
/// Drag a crop frame (locked to <see cref="ToolSettings.CropAspect"/>), then drag inside it to move it or drag a
/// corner to resize it; Enter crops the image, Escape removes the frame. The frame shows the rule of thirds and
/// shades what will be cut away.
/// </summary>
public sealed class CropTool(ToolSettings settings) : IKeyboardTool, IOverlayTool, IGridSnappingTool
{
    private enum DragKind
    {
        None,
        Create,
        Move,
        Corner,
    }

    private ImageDocument? _document;
    private RectangleD? _frame;
    private DragKind _drag;
    private PointD _anchor;
    private PointD _start;
    private RectangleD _startFrame;

    public string Name => "Crop";

    public static double? Ratio(CropAspect aspect) => aspect switch
    {
        CropAspect.Wide => 16 / 9.0,
        CropAspect.Tall => 9 / 16.0,
        CropAspect.Standard => 4 / 3.0,
        CropAspect.Photo => 3 / 2.0,
        CropAspect.Square => 1,
        _ => null,
    };

    /// <summary>The crop frame in whole pixels, or null when there is none on this document.</summary>
    public RectangleI? Frame(ImageDocument document) => IsEditing(document) && _frame is { } f
        ? new RectangleI((int)Math.Round(f.X), (int)Math.Round(f.Y), (int)Math.Round(f.Width), (int)Math.Round(f.Height))
        : null;

    public bool IsEditing(ImageDocument document) => _document == document && _frame is not null;

    /// <summary>Ratio used instead of <see cref="ToolSettings.CropAspect"/> (Prepare for TV locks 16:9).</summary>
    public double? ForcedRatio { get; set; }

    /// <summary>False to only move and resize the current frame: dragging elsewhere doesn't start a new one.</summary>
    public bool CanDrawNewFrame { get; set; } = true;

    private double? CurrentRatio => ForcedRatio ?? Ratio(settings.CropAspect);

    /// <summary>Shows <paramref name="frame"/> on the document, ready to be moved or resized.</summary>
    public void Propose(ImageDocument document, RectangleD frame)
    {
        _document = document;
        _frame = frame;
        _drag = DragKind.None;
    }

    public void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        var p = pointer.Position;
        _start = p;
        if (IsEditing(document) && _frame is { } frame)
        {
            _startFrame = frame;
            var tolerance = 8 / Math.Max(document.Workspace.Scale, 0.01);
            var corners = Corners(frame);
            for (var i = 0; i < 4; i++)
            {
                if (corners[i].Distance(p) <= tolerance)
                {
                    _drag = DragKind.Corner;
                    // A frame larger than the image (Prepare for TV) shrinks back onto it.
                    _anchor = Clamp(document, corners[(i + 2) % 4]);
                    return;
                }
            }
            if (p.X >= frame.X && p.X <= frame.X + frame.Width && p.Y >= frame.Y && p.Y <= frame.Y + frame.Height)
            {
                _drag = DragKind.Move;
                return;
            }
        }
        if (!CanDrawNewFrame)
        {
            _drag = DragKind.None;
            return;
        }
        _document = document;
        _frame = null;
        _drag = DragKind.Create;
        _anchor = Clamp(document, p);
    }

    public void OnPointerMove(ImageDocument document, ToolPointer pointer)
    {
        if (_drag == DragKind.None || _document != document)
            return;
        if (_drag == DragKind.Move)
        {
            var f = _startFrame;
            _frame = f with
            {
                X = KeepOnImage(f.X + pointer.Position.X - _start.X, f.Width, document.ImageSize.Width),
                Y = KeepOnImage(f.Y + pointer.Position.Y - _start.Y, f.Height, document.ImageSize.Height),
            };
        }
        else
        {
            _frame = FrameFrom(document, _anchor, pointer.Position);
        }
    }

    public void OnPointerUp(ImageDocument document, ToolPointer pointer)
    {
        OnPointerMove(document, pointer);
        if (_drag == DragKind.Create && _frame is { } f && (f.Width < 2 || f.Height < 2))
            _frame = null;
        _drag = DragKind.None;
    }

    public bool OnKeyDown(ImageDocument document, ToolKey key, ToolModifiers modifiers)
    {
        if (!IsEditing(document))
            return false;
        switch (key)
        {
            case ToolKey.Enter:
                Apply(document);
                return true;
            case ToolKey.Escape:
                Finish(document);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Crops the image to the frame (one history step) and removes the frame.</summary>
    public void Apply(ImageDocument document)
    {
        if (Frame(document) is not { } frame)
            return;
        Finish(document);
        document.Actions.CropToRectangle(frame);
    }

    public void Finish(ImageDocument document)
    {
        if (_document == document)
            _frame = null;
    }

    /// <summary>Fits the frame to a newly chosen aspect ratio, keeping its center.</summary>
    public void Refresh(ImageDocument document)
    {
        if (!IsEditing(document) || _frame is not { } f || CurrentRatio is not { } ratio)
            return;
        var (cx, cy) = (f.X + f.Width / 2, f.Y + f.Height / 2);
        var (w, h) = f.Width / f.Height > ratio ? (f.Height * ratio, f.Height) : (f.Width, f.Width / ratio);
        var (iw, ih) = (document.ImageSize.Width, document.ImageSize.Height);
        _frame = new RectangleD(KeepOnImage(cx - w / 2, w, iw), KeepOnImage(cy - h / 2, h, ih), w, h);
    }

    public ToolOverlay? GetOverlay(ImageDocument document)
    {
        if (!IsEditing(document) || _frame is not { } f)
            return null;
        var thirds = new List<(PointD, PointD)>();
        for (var i = 1; i < 3; i++)
        {
            var x = f.X + f.Width * i / 3;
            var y = f.Y + f.Height * i / 3;
            thirds.Add((new PointD(x, f.Y), new PointD(x, f.Y + f.Height)));
            thirds.Add((new PointD(f.X, y), new PointD(f.X + f.Width, y)));
        }
        return new ToolOverlay { Frame = f, Shade = f, Handles = Corners(f), Lines = thirds };
    }

    public ToolCursor CursorAt(ImageDocument document, PointD point)
    {
        if (!IsEditing(document) || _frame is not { } f)
            return ToolCursor.Default;
        var tolerance = 8 / Math.Max(document.Workspace.Scale, 0.01);
        var corners = Corners(f);
        for (var i = 0; i < 4; i++)
            if (corners[i].Distance(point) <= tolerance)
                return i % 2 == 0 ? ToolCursor.ResizeDiagonal : ToolCursor.ResizeAntiDiagonal;
        return point.X >= f.X && point.X <= f.X + f.Width && point.Y >= f.Y && point.Y <= f.Y + f.Height
            ? ToolCursor.Move
            : ToolCursor.Default;
    }

    // Top-left, top-right, bottom-right, bottom-left: the opposite corner of i is (i + 2) % 4.
    private static PointD[] Corners(RectangleD f) =>
    [
        new(f.X, f.Y), new(f.X + f.Width, f.Y), new(f.X + f.Width, f.Y + f.Height), new(f.X, f.Y + f.Height),
    ];

    /// <summary>
    /// Position of a frame side of length <paramref name="size"/> on an image side of length <paramref name="image"/>:
    /// inside the image, or covering it when the frame is larger (Prepare for TV at a resolution above the photo's).
    /// </summary>
    public static double KeepOnImage(double position, double size, double image) =>
        Math.Clamp(position, Math.Min(0, image - size), Math.Max(0, image - size));

    private static PointD Clamp(ImageDocument document, PointD p) =>
        new(Math.Clamp(p.X, 0, document.ImageSize.Width), Math.Clamp(p.Y, 0, document.ImageSize.Height));

    /// <summary>The frame spanned from <paramref name="anchor"/> towards <paramref name="to"/>, locked to the ratio and inside the image.</summary>
    private RectangleD FrameFrom(ImageDocument document, PointD anchor, PointD to)
    {
        to = Clamp(document, to);
        var (sx, sy) = (to.X >= anchor.X ? 1 : -1, to.Y >= anchor.Y ? 1 : -1);
        var (w, h) = (Math.Abs(to.X - anchor.X), Math.Abs(to.Y - anchor.Y));
        if (CurrentRatio is { } ratio)
        {
            // Follow the larger movement, then shrink to fit in the image on the dragged side.
            (w, h) = w / ratio >= h ? (w, w / ratio) : (h * ratio, h);
            var maxW = sx > 0 ? document.ImageSize.Width - anchor.X : anchor.X;
            var maxH = sy > 0 ? document.ImageSize.Height - anchor.Y : anchor.Y;
            var fit = Math.Min(1, Math.Min(w > 0 ? maxW / w : 1, h > 0 ? maxH / h : 1));
            (w, h) = (w * fit, h * fit);
        }
        return new RectangleD(sx > 0 ? anchor.X : anchor.X - w, sy > 0 ? anchor.Y : anchor.Y - h, w, h);
    }
}
