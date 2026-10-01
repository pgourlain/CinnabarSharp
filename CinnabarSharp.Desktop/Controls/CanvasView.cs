using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using Avalonia.Threading;
using PointD = CinnabarSharp.Core.Models.PointD;

namespace CinnabarSharp.Desktop.Controls;

/// <summary>
/// Displays the flattened active document at the workspace zoom, over a transparency checkerboard.
/// </summary>
public class CanvasView : Control
{
    public static readonly StyledProperty<ImageDocument?> DocumentProperty =
        AvaloniaProperty.Register<CanvasView, ImageDocument?>(nameof(Document));

    public static readonly StyledProperty<int> RenderVersionProperty =
        AvaloniaProperty.Register<CanvasView, int>(nameof(RenderVersion));

    public static readonly StyledProperty<int> SelectionVersionProperty =
        AvaloniaProperty.Register<CanvasView, int>(nameof(SelectionVersion));

    public static readonly StyledProperty<ToolOverlay?> OverlayProperty =
        AvaloniaProperty.Register<CanvasView, ToolOverlay?>(nameof(Overlay));

    /// <summary>Diameter in image pixels of the brush outline drawn under the pointer; 0 for none.</summary>
    public static readonly StyledProperty<double> BrushSizeProperty =
        AvaloniaProperty.Register<CanvasView, double>(nameof(BrushSize));

    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(80, 51, 153, 255));
    private static readonly Pen OverlayLight = new(Brushes.White, 3);
    private static readonly Pen OverlayDark = new(Brushes.Black, 1);
    private Point? _pointer;

    private static readonly IBrush EmphasisBrush = new SolidColorBrush(Color.FromRgb(255, 140, 0));
    private static readonly IBrush AntsLight = Brushes.White;
    private static readonly IBrush AntsDark = Brushes.Black;

    private readonly DispatcherTimer _antsTimer;
    private double _antsOffset;
    private (SelectionMask Mask, double Scale, Geometry Geometry)? _outline;
    private bool _pointerPressed;
    private ToolPointer _lastPointer;

    private const int CheckerSize = 8;
    private static readonly IBrush CheckerBrush = CreateCheckerBrush();

    private WriteableBitmap? _bitmap;

    static CanvasView()
    {
        AffectsMeasure<CanvasView>(DocumentProperty, RenderVersionProperty);
        AffectsRender<CanvasView>(SelectionVersionProperty, OverlayProperty, BrushSizeProperty);
    }

    public CanvasView()
    {
        // Takes the keyboard focus when clicked, so typing goes to the Text tool rather than a text box.
        Focusable = true;
        _antsTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) =>
        {
            if (Document?.Selection is null)
                return;
            _antsOffset = (_antsOffset + 1) % 8;
            InvalidateVisual();
        });
    }

    public ToolOverlay? Overlay
    {
        get => GetValue(OverlayProperty);
        set => SetValue(OverlayProperty, value);
    }

    public double BrushSize
    {
        get => GetValue(BrushSizeProperty);
        set => SetValue(BrushSizeProperty, value);
    }

    public int SelectionVersion
    {
        get => GetValue(SelectionVersionProperty);
        set => SetValue(SelectionVersionProperty, value);
    }

    /// <summary>Tool input in image coordinates: pressed, moved while pressed, released.</summary>
    public event Action<ToolPointer>? ToolPointerPressed;
    public event Action<ToolPointer>? ToolPointerMoved;
    public event Action<ToolPointer>? ToolPointerReleased;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _antsTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _antsTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    public ImageDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public int RenderVersion
    {
        get => GetValue(RenderVersionProperty);
        set => SetValue(RenderVersionProperty, value);
    }

    /// <summary>Raised with the pointer position in image coordinates, or null when the pointer leaves.</summary>
    public event Action<PointD?>? CanvasPointerMoved;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty || change.Property == RenderVersionProperty)
        {
            RebuildBitmap();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Document is not { } doc || doc.Layers.Count() == 0)
            return default;
        var view = doc.Workspace.ViewSize;
        return new Size(view.Width, view.Height);
    }

    public override void Render(DrawingContext context)
    {
        if (_bitmap is null || Document is not { } doc)
            return;

        var view = doc.Workspace.ViewSize;
        var dest = new Rect(0, 0, view.Width, view.Height);
        context.FillRectangle(CheckerBrush, dest);

        // Nearest-neighbour when zoomed in so individual pixels stay sharp, as in Paint.NET.
        var interpolation = doc.Workspace.Scale >= 1
            ? BitmapInterpolationMode.None
            : BitmapInterpolationMode.HighQuality;
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = interpolation }))
        {
            context.DrawImage(_bitmap, new Rect(_bitmap.Size), dest);
        }

        if (doc.Selection is { } selection)
        {
            var geometry = OutlineGeometry(selection, doc.Workspace.Scale);
            context.DrawGeometry(null, new Pen(AntsLight, 1), geometry);
            context.DrawGeometry(null, new Pen(AntsDark, 1, new DashStyle([4, 4], _antsOffset)), geometry);
        }

        if (Overlay is { } overlay)
            DrawOverlay(context, overlay, doc.Workspace.Scale);

        if (BrushSize > 0 && _pointer is { } p)
        {
            var radius = BrushSize * doc.Workspace.Scale / 2;
            if (radius >= 2)
            {
                context.DrawEllipse(null, new Pen(Brushes.White, 1), p, radius + 1, radius + 1);
                context.DrawEllipse(null, OverlayDark, p, radius, radius);
            }
        }
    }

    private static readonly IBrush ShadeBrush = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));

    private void DrawOverlay(DrawingContext context, ToolOverlay overlay, double scale)
    {
        Point P(Core.Models.PointD p) => new(p.X * scale, p.Y * scale);
        Rect R(Core.Models.RectangleD r) => new(r.X * scale, r.Y * scale, r.Width * scale, r.Height * scale);

        if (overlay.Shade is { } keep && Document is { } doc)
        {
            var view = doc.Workspace.ViewSize;
            // The kept area can extend beyond the image (Prepare for TV): shade only the image around it.
            var k = R(keep).Intersect(new Rect(0, 0, view.Width, view.Height));
            context.FillRectangle(ShadeBrush, new Rect(0, 0, view.Width, k.Top));
            context.FillRectangle(ShadeBrush, new Rect(0, k.Bottom, view.Width, Math.Max(0, view.Height - k.Bottom)));
            context.FillRectangle(ShadeBrush, new Rect(0, k.Top, k.Left, k.Height));
            context.FillRectangle(ShadeBrush, new Rect(k.Right, k.Top, Math.Max(0, view.Width - k.Right), k.Height));
        }
        if (overlay.Picture is { } picture)
        {
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                context.DrawImage(PictureBitmap(picture), R(picture.Area));
        }
        // Everything below (frame, caret/lines, highlights, handles) is drawn as if unrotated, then rotated as a
        // whole around the overlay's pivot — same geometry the tool used to compute it, just spun on screen.
        // The canvas pixels themselves are rotated separately by the tool, not by this transform.
        var pivot = overlay.Rotation is { } rot ? P(rot.Pivot) : default;
        var rotation = overlay.Rotation is { } r
            ? Matrix.CreateTranslation(-pivot.X, -pivot.Y) * Matrix.CreateRotation(r.Angle) * Matrix.CreateTranslation(pivot.X, pivot.Y)
            : Matrix.Identity;
        using (context.PushTransform(rotation))
        {
            foreach (var highlight in overlay.Highlights)
                context.FillRectangle(HighlightBrush, R(highlight));
            if (overlay.Frame is { } frame && overlay.EmphasizeFrame)
            {
                var box = R(frame).Deflate(2);
                context.DrawRectangle(new Pen(Brushes.White, 5), box);
                context.DrawRectangle(new Pen(EmphasisBrush, 3), box);
            }
            else if (overlay.Frame is { } thin)
            {
                context.DrawRectangle(new Pen(AntsLight, 1), R(thin));
                context.DrawRectangle(new Pen(AntsDark, 1, new DashStyle([3, 3], 0)), R(thin));
            }
            foreach (var (from, to) in overlay.Lines)
            {
                context.DrawLine(OverlayLight, P(from), P(to));
                context.DrawLine(OverlayDark, P(from), P(to));
            }
            foreach (var handle in overlay.Handles)
            {
                if (overlay.SquareHandles)
                    context.DrawRectangle(Brushes.White, OverlayDark, new Rect(P(handle) - new Point(3.5, 3.5), new Size(7, 7)));
                else
                    context.DrawEllipse(Brushes.White, OverlayDark, P(handle), 4, 4);
            }
            if (overlay.RotateHandle is { } rotateHandle)
                DrawRotateIcon(context, P(rotateHandle));
        }
    }

    /// <summary>Two curved, blended arrows forming a rotate symbol, centered on <paramref name="center"/>.</summary>
    private static void DrawRotateIcon(DrawingContext context, Point center)
    {
        const double radius = 6;
        const double headLength = 3.5;
        const double headWidth = 4;
        var outline = OverlayDark;
        var stroke = new Pen(Brushes.White, 2, lineCap: PenLineCap.Round);

        // Two ~145° arcs on opposite sides of a small circle, each swept clockwise and capped with an
        // arrowhead pointing along the arc's own direction — reads as two arrows chasing each other in a loop.
        void Arrow(double fromDegrees, double toDegrees)
        {
            var from = OnCircle(center, radius, fromDegrees);
            var to = OnCircle(center, radius, toDegrees);
            var arc = new StreamGeometry();
            using (var ctx = arc.Open())
            {
                ctx.BeginFigure(from, isFilled: false);
                ctx.ArcTo(to, new Size(radius, radius), 0, isLargeArc: false, SweepDirection.Clockwise);
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, stroke, arc);
            context.DrawGeometry(null, outline, arc);

            // Tangent direction of a clockwise arc at its end point, and the arrowhead triangle around it.
            var tangent = (toDegrees + 90) * Math.PI / 180;
            var back = new Point(to.X - Math.Cos(tangent) * headLength, to.Y - Math.Sin(tangent) * headLength);
            var normal = tangent + Math.PI / 2;
            var side = new Point(Math.Cos(normal) * headWidth / 2, Math.Sin(normal) * headWidth / 2);
            var head = new StreamGeometry();
            using (var ctx = head.Open())
            {
                ctx.BeginFigure(to, isFilled: true);
                ctx.LineTo(new Point(back.X + side.X, back.Y + side.Y));
                ctx.LineTo(new Point(back.X - side.X, back.Y - side.Y));
                ctx.EndFigure(true);
            }
            context.DrawGeometry(Brushes.White, outline, head);
        }

        Arrow(200, 345);
        Arrow(20, 165);
    }

    private static Point OnCircle(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private Geometry OutlineGeometry(SelectionMask selection, double scale)
    {
        if (_outline is { } cached && cached.Mask == selection && cached.Scale == scale)
            return cached.Geometry;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            foreach (var (x1, y1, x2, y2) in selection.GetOutline())
            {
                ctx.BeginFigure(new Point(x1 * scale, y1 * scale), isFilled: false);
                ctx.LineTo(new Point(x2 * scale, y2 * scale));
                ctx.EndFigure(isClosed: false);
            }
        }
        _outline = (selection, scale, geometry);
        return geometry;
    }

    private ToolPointer ToToolPointer(PointerEventArgs e, ToolButton button)
    {
        var pos = e.GetPosition(this);
        var scale = Document?.Workspace.Scale ?? 1;
        var mods = ToolModifiers.None;
        if ((e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0)
            mods |= ToolModifiers.Command;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            mods |= ToolModifiers.Alt;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            mods |= ToolModifiers.Shift;
        // Mice report a fixed pressure (0.5); only pens have a meaningful one.
        var pressure = e.Pointer.Type == PointerType.Pen ? e.GetCurrentPoint(this).Properties.Pressure : 1;
        return new ToolPointer(new PointD(pos.X / scale, pos.Y / scale), button, mods, pressure,
            e is PointerPressedEventArgs pressed ? pressed.ClickCount : 1);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled || Document is null || _pointerPressed)
            return;
        var props = e.GetCurrentPoint(this).Properties;
        var button = props.IsRightButtonPressed ? ToolButton.Right : ToolButton.Left;
        if (!props.IsLeftButtonPressed && !props.IsRightButtonPressed)
            return;

        _pointerPressed = true;
        Focus();
        _lastPointer = ToToolPointer(e, button);
        e.Pointer.Capture(this);
        e.Handled = true;
        ToolPointerPressed?.Invoke(_lastPointer);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pointerPressed)
            return;
        _pointerPressed = false;
        _lastPointer = ToToolPointer(e, _lastPointer.Button);
        e.Pointer.Capture(null);
        e.Handled = true;
        ToolPointerReleased?.Invoke(_lastPointer);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!_pointerPressed)
            return;
        _pointerPressed = false;
        ToolPointerReleased?.Invoke(_lastPointer);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Document is not { } doc)
            return;
        var pos = e.GetPosition(this);
        var scale = doc.Workspace.Scale;
        CanvasPointerMoved?.Invoke(new PointD(pos.X / scale, pos.Y / scale));
        _pointer = pos;
        if (BrushSize > 0)
            InvalidateVisual();
        if (_pointerPressed)
        {
            _lastPointer = ToToolPointer(e, _lastPointer.Button);
            ToolPointerMoved?.Invoke(_lastPointer);
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        CanvasPointerMoved?.Invoke(null);
        _pointer = null;
        if (BrushSize > 0)
            InvalidateVisual();
    }

    /// <summary>Re-composites and redraws only <paramref name="region"/> (image coordinates).</summary>
    public void UpdateRegion(RectangleI region)
    {
        if (Document is not { } doc || _bitmap is null
            || _bitmap.PixelSize.Width != doc.ImageSize.Width || _bitmap.PixelSize.Height != doc.ImageSize.Height)
        {
            RebuildBitmap();
            InvalidateVisual();
            return;
        }

        var pixels = doc.Layers.GetFlattenedBgra(region);
        using (var fb = _bitmap.Lock())
        {
            var rowBytes = region.Width * 4;
            for (var y = 0; y < region.Height; y++)
                Marshal.Copy(pixels, y * rowBytes, fb.Address + (region.Y + y) * fb.RowBytes + region.X * 4, rowBytes);
        }
        InvalidateVisual();
    }

    private void RebuildBitmap()
    {
        _bitmap?.Dispose();
        _bitmap = null;

        if (Document is not { } doc || doc.Layers.Count() == 0)
            return;

        var width = doc.ImageSize.Width;
        var height = doc.ImageSize.Height;
        var pixels = doc.Layers.GetFlattenedBgra();

        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = bitmap.Lock())
        {
            var rowBytes = width * 4;
            for (var y = 0; y < height; y++)
                Marshal.Copy(pixels, y * rowBytes, fb.Address + y * fb.RowBytes, rowBytes);
        }
        _bitmap = bitmap;
    }

    private (OverlayPicture Picture, WriteableBitmap Bitmap)? _picture;

    // Converted once per picture: the overlay is redrawn much more often than it changes.
    private WriteableBitmap PictureBitmap(OverlayPicture picture)
    {
        if (_picture is { } cached && ReferenceEquals(cached.Picture, picture))
            return cached.Bitmap;
        // Not disposed: the renderer may still draw the previous one.
        var bitmap = new WriteableBitmap(new PixelSize(picture.Width, picture.Height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = bitmap.Lock())
        {
            var rowBytes = picture.Width * 4;
            for (var y = 0; y < picture.Height; y++)
                Marshal.Copy(picture.Bgra, y * rowBytes, fb.Address + y * fb.RowBytes, rowBytes);
        }
        _picture = (picture, bitmap);
        return bitmap;
    }

    private static IBrush CreateCheckerBrush()
    {
        var size = CheckerSize * 2;
        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var fb = bitmap.Lock())
        {
            var row = new byte[size * 4];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var light = (x / CheckerSize + y / CheckerSize) % 2 == 0;
                    byte v = light ? (byte)255 : (byte)204;
                    row[x * 4] = v;
                    row[x * 4 + 1] = v;
                    row[x * 4 + 2] = v;
                    row[x * 4 + 3] = 255;
                }
                Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
            }
        }
        return new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, size, size, RelativeUnit.Absolute),
        };
    }
}
