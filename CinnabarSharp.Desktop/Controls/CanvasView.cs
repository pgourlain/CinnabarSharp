using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Vector;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using PointD = CinnabarSharp.Core.Models.PointD;
using RenderOptions = Avalonia.Media.RenderOptions;

namespace CinnabarSharp.Desktop.Controls;

/// <summary>
/// Displays the flattened active document at the workspace zoom, over a transparency checkerboard.
/// </summary>
public class CanvasView : Control
{
    public static readonly StyledProperty<ImageDocument?> DocumentProperty =
        AvaloniaProperty.Register<CanvasView, ImageDocument?>(nameof(Document));

    /// <summary>The vector drawing shown instead of an image (at most one of <see cref="Document"/> and this is set).</summary>
    public static readonly StyledProperty<SvgDocument?> SvgDocumentProperty =
        AvaloniaProperty.Register<CanvasView, SvgDocument?>(nameof(SvgDocument));

    public static readonly StyledProperty<int> RenderVersionProperty =
        AvaloniaProperty.Register<CanvasView, int>(nameof(RenderVersion));

    public static readonly StyledProperty<int> ViewVersionProperty =
        AvaloniaProperty.Register<CanvasView, int>(nameof(ViewVersion));

    public static readonly StyledProperty<int> SelectionVersionProperty =
        AvaloniaProperty.Register<CanvasView, int>(nameof(SelectionVersion));

    public static readonly StyledProperty<ToolOverlay?> OverlayProperty =
        AvaloniaProperty.Register<CanvasView, ToolOverlay?>(nameof(Overlay));

    /// <summary>The grid drawn over the picture, or null for none.</summary>
    public static readonly StyledProperty<GridInfo?> GridProperty =
        AvaloniaProperty.Register<CanvasView, GridInfo?>(nameof(Grid));

    public GridInfo? Grid
    {
        get => GetValue(GridProperty);
        set => SetValue(GridProperty, value);
    }

    /// <summary>Diameter in image pixels of the brush outline drawn under the pointer; 0 for none.</summary>
    public static readonly StyledProperty<double> BrushSizeProperty =
        AvaloniaProperty.Register<CanvasView, double>(nameof(BrushSize));

    private static readonly IBrush SelectedHandleBrush = new SolidColorBrush(Color.FromRgb(0, 102, 255));
    private static readonly IPen SelectedHandlePen = new Pen(Brushes.White, 2);
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

    /// <summary>Behind a drawing: a much softer checkerboard, so the grid and the shapes stand out from it.</summary>
    private static readonly IBrush SoftCheckerBrush = CreateCheckerBrush(darker: 240);

    private WriteableBitmap? _bitmap;

    static CanvasView()
    {
        AffectsMeasure<CanvasView>(DocumentProperty, SvgDocumentProperty, RenderVersionProperty, ViewVersionProperty);
        AffectsRender<CanvasView>(ViewVersionProperty);
        AffectsRender<CanvasView>(SelectionVersionProperty, OverlayProperty, BrushSizeProperty, GridProperty);
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

    public SvgDocument? SvgDocument
    {
        get => GetValue(SvgDocumentProperty);
        set => SetValue(SvgDocumentProperty, value);
    }

    /// <summary>The document shown, of either kind.</summary>
    private IDocument? Shown => (IDocument?)Document ?? SvgDocument;

    public int RenderVersion
    {
        get => GetValue(RenderVersionProperty);
        set => SetValue(RenderVersionProperty, value);
    }

    /// <summary>Changes with the zoom: the bitmap is scaled to the new view size, not recomposited.</summary>
    public int ViewVersion
    {
        get => GetValue(ViewVersionProperty);
        set => SetValue(ViewVersionProperty, value);
    }

    /// <summary>Raised with the pointer position in image coordinates, or null when the pointer leaves.</summary>
    public event Action<PointD?>? CanvasPointerMoved;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty || change.Property == RenderVersionProperty || change.Property == SvgDocumentProperty)
        {
            RebuildBitmap();
            InvalidateVisual();
        }
        else if (change.Property == ViewVersionProperty && SvgDocument is not null)
        {
            // A new zoom: the drawing is rendered again at that size so its edges stay sharp.
            RebuildBitmap();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (SvgDocument is { } svg)
            return new Size(svg.Workspace.ViewSize.Width, svg.Workspace.ViewSize.Height);
        if (Document is not { } doc || doc.Layers.Count() == 0)
            return default;
        var view = doc.Workspace.ViewSize;
        return new Size(view.Width, view.Height);
    }

    public override void Render(DrawingContext context)
    {
        if (SvgDocument is { } drawing)
        {
            RenderDrawing(context, drawing);
            return;
        }
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

        DrawGrid(context, doc.Workspace.Scale);
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

        if (overlay.Shade is { } keep && Shown is { } doc)
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
            // Selected handles (the chosen nodes): bigger, filled with the accent color, so they stand out from the others.
            foreach (var handle in overlay.SelectedHandles)
                context.DrawRectangle(SelectedHandleBrush, SelectedHandlePen, new Rect(P(handle) - new Point(5, 5), new Size(10, 10)));
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
        var scale = Shown?.Workspace.Scale ?? 1;
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
        if (e.Handled || Shown is null || _pointerPressed)
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
        if (Shown is not { } doc)
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
        if (SvgDocument is { } drawing)
        {
            UpdateDrawingRegion(drawing, region);
            return;
        }
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
        if (SvgDocument is null)
        {
            CancelBackgroundRender();
            _drawingBitmap?.Dispose();
            _drawingBitmap = null;
            _drawingOwner = null;
        }
        else
        {
            // The last frame stays on screen while a big drawing is redrawn in the background.
            RenderVisibleDrawing();
            return;
        }
        if (Document is not { } doc || doc.Layers.Count() == 0)
            return;

        var width = doc.ImageSize.Width;
        var height = doc.ImageSize.Height;
        var pixels = doc.Layers.GetFlattenedBgra();

        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = bitmap.Lock())
        {
            var rowBytes = width * 4;
            for (var y = 0; y < height; y++)
                Marshal.Copy(pixels, y * rowBytes, fb.Address + y * fb.RowBytes, rowBytes);
        }
        _bitmap = bitmap;
    }

    // ---- SVG drawings: rendered at the current zoom, only the part in view ----

    /// <summary>Pixels of the drawing for the part of the view that is (nearly) visible, and where they go (device pixels of the zoomed picture).</summary>
    private WriteableBitmap? _drawingBitmap;
    private VRectI _drawingRegion;
    private VRectI _targetRegion;
    private double _drawingDeviceScale;
    private ScrollViewer? _scroller;

    private const int MaxFullRenderSide = 4096;
    private const int VisibleMargin = 96;

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        _scroller = this.FindLogicalAncestorOfType<ScrollViewer>();
        if (_scroller is not null)
            _scroller.ScrollChanged += OnScrolled;
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        if (_scroller is not null)
            _scroller.ScrollChanged -= OnScrolled;
        _scroller = null;
        base.OnDetachedFromLogicalTree(e);
    }

    private void OnScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (SvgDocument is null || (_drawingBitmap is null && !IsRenderingInBackground))
            return;
        // Scrolled past what was rendered (or is being rendered): render the part now in view.
        var visible = VisibleDeviceRegion(SvgDocument);
        if (visible.Intersect(_targetRegion) != visible)
        {
            RenderVisibleDrawing();
            InvalidateVisual();
        }
    }

    private double RenderScaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    /// <summary>The part of the zoomed picture to render, in device pixels: what the scroll viewer shows plus a margin.</summary>
    private VRectI VisibleDeviceRegion(SvgDocument drawing)
    {
        var scaling = RenderScaling;
        var view = drawing.Workspace.ViewSize;
        var full = new Rect(0, 0, view.Width, view.Height);
        var visible = full;
        if (_scroller is { Viewport: { Width: > 0, Height: > 0 } viewport } scroller
            && scroller.TranslatePoint(default, this) is { } topLeft)
        {
            visible = new Rect(topLeft, viewport).Inflate(VisibleMargin).Intersect(full);
        }
        else
        {
            // Not laid out yet (or no scroll viewer): the whole picture, but never more than a bounded area.
            visible = new Rect(0, 0, Math.Min(view.Width, MaxFullRenderSide), Math.Min(view.Height, MaxFullRenderSide));
        }
        var left = (int)Math.Floor(visible.Left * scaling);
        var top = (int)Math.Floor(visible.Top * scaling);
        var right = (int)Math.Ceiling(visible.Right * scaling);
        var bottom = (int)Math.Ceiling(visible.Bottom * scaling);
        return new VRectI(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private void RenderVisibleDrawing()
    {
        if (SvgDocument is not { } drawing)
        {
            CancelBackgroundRender();
            _drawingBitmap?.Dispose();
            _drawingBitmap = null;
            return;
        }
        if (!ReferenceEquals(_drawingOwner, drawing))
        {
            // Another drawing: its predecessor's frame means nothing.
            CancelBackgroundRender();
            _drawingBitmap?.Dispose();
            _drawingBitmap = null;
            _drawingOwner = drawing;
        }
        var region = VisibleDeviceRegion(drawing);
        if (region.IsEmpty)
        {
            CancelBackgroundRender();
            _drawingBitmap?.Dispose();
            _drawingBitmap = null;
            return;
        }
        var deviceScale = drawing.Workspace.Scale * RenderScaling;
        _targetRegion = region;
        if (IsHeavy(drawing))
        {
            RenderInBackground(drawing, region, deviceScale);
            return;
        }
        CancelBackgroundRender();
        _drawingBitmap?.Dispose();
        _drawingBitmap = null;
        var pixels = VectorRasterizer.Render(drawing.Root, region, deviceScale, drawing.RenderOptions);
        _drawingBitmap = ToBitmap(pixels, region.Width, region.Height);
        _drawingRegion = region;
        _drawingDeviceScale = deviceScale;
    }

    // ---- Big drawings: rendered on a background thread, from a copy of the tree, with the last frame shown meanwhile ----

    /// <summary>Drawings with more elements than this are drawn in the background (a map with thousands of paths takes about half a second a frame).</summary>
    public const int HeavyElementCount = 1500;

    private SvgDocument? _drawingOwner;
    private CancellationTokenSource? _renderCts;
    private SvgRoot? _snapshot;
    private SvgRoot? _snapshotOf;
    private int _snapshotVersion = -1;
    private SvgRoot? _heavyOf;
    private int _heavyVersion = -1;
    private bool _heavy;

    /// <summary>True while a frame is being computed in the background: the picture shown is the one before.</summary>
    public bool IsRenderingInBackground { get; private set; }

    /// <summary>A drawing is heavy when it has many elements and no text (text needs the fonts, which are used on the UI thread only).</summary>
    private bool IsHeavy(SvgDocument drawing)
    {
        var root = drawing.Root;
        if (!ReferenceEquals(_heavyOf, root) || _heavyVersion != root.Version)
        {
            var count = 0;
            var text = false;
            foreach (var node in root.Descendants())
            {
                if (node is SvgElement)
                    count++;
                if (node is SvgTextBase)
                    text = true;
            }
            _heavy = count > HeavyElementCount && !text;
            _heavyOf = root;
            _heavyVersion = root.Version;
        }
        return _heavy;
    }

    private void CancelBackgroundRender()
    {
        _renderCts?.Cancel();
        _renderCts = null;
        IsRenderingInBackground = false;
    }

    private async void RenderInBackground(SvgDocument drawing, VRectI region, double deviceScale)
    {
        _renderCts?.Cancel();
        var cts = _renderCts = new CancellationTokenSource();
        var root = drawing.Root;
        var version = root.Version;
        // The background thread reads a copy, so edits on this thread cannot disturb it.
        if (!ReferenceEquals(_snapshotOf, root) || _snapshotVersion != version || _snapshot is null)
        {
            _snapshot = (SvgRoot)root.DeepClone();
            _snapshotOf = root;
            _snapshotVersion = version;
        }
        var snapshot = _snapshot;
        var template = drawing.RenderOptions;
        var options = new CinnabarSharp.Vector.RenderOptions
        {
            ImageDecoder = template.ImageDecoder,
            BaseFolder = template.BaseFolder,
            Cancellation = cts.Token,
        };
        IsRenderingInBackground = true;
        byte[]? pixels;
        try
        {
            pixels = await Task.Run(() => VectorRasterizer.Render(snapshot, region, deviceScale, options), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!ReferenceEquals(cts, _renderCts))
            return;
        IsRenderingInBackground = false;
        _renderCts = null;
        if (!ReferenceEquals(SvgDocument, drawing))
            return;
        if (root.Version != version)
        {
            // Edited meanwhile: the frame is out of date.
            RenderVisibleDrawing();
            InvalidateVisual();
            return;
        }
        _drawingBitmap?.Dispose();
        _drawingBitmap = ToBitmap(pixels, region.Width, region.Height);
        _drawingRegion = region;
        _drawingDeviceScale = deviceScale;
        InvalidateVisual();
    }

    private void RenderDrawing(DrawingContext context, SvgDocument drawing)
    {
        var view = drawing.Workspace.ViewSize;
        // With the grid on, the page is plain white (as in draw.io) so the grid reads clearly; otherwise a soft checkerboard.
        context.FillRectangle(Grid is null ? SoftCheckerBrush : Brushes.White, new Rect(0, 0, view.Width, view.Height));
        if (_drawingBitmap is { } bitmap)
        {
            var scaling = RenderScaling;
            // A frame from before a zoom change is stretched to the new zoom until the new one is ready.
            var ratio = _drawingDeviceScale > 0 ? drawing.Workspace.Scale * scaling / _drawingDeviceScale : 1;
            var dest = new Rect(_drawingRegion.X / scaling * ratio, _drawingRegion.Y / scaling * ratio,
                _drawingRegion.Width / scaling * ratio, _drawingRegion.Height / scaling * ratio);
            var mode = Math.Abs(ratio - 1) < 1e-9 ? BitmapInterpolationMode.None : BitmapInterpolationMode.LowQuality;
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = mode }))
                context.DrawImage(bitmap, new Rect(bitmap.Size), dest);
        }
        DrawGrid(context, drawing.Workspace.Scale);
        if (Overlay is { } overlay)
            DrawOverlay(context, overlay, drawing.Workspace.Scale);
    }

    // Like draw.io: thin light grey lines, a darker one every fifth line, on a white page.
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromRgb(224, 224, 224)), 1);
    private static readonly IPen MajorGridPen = new Pen(new SolidColorBrush(Color.FromRgb(192, 192, 192)), 1);
    private const int MajorEvery = 5;

    /// <summary>Thin lines every grid step, a stronger one every fifth; when the lines would be closer than 6 screen pixels every few steps are drawn.</summary>
    private void DrawGrid(DrawingContext context, double scale)
    {
        if (Grid is not { Spacing: > 0 } grid)
            return;
        var step = grid.Spacing * scale;
        step *= Math.Max(1, (int)Math.Ceiling(6 / step));
        var view = Bounds.Size;
        // Never more than a few thousand lines, whatever the zoom.
        while (step > 0 && (view.Width / step > 3000 || view.Height / step > 3000))
            step *= 2;
        var ox = grid.OriginX * scale;
        var oy = grid.OriginY * scale;
        for (var i = (int)Math.Ceiling(-ox / step); ox + i * step <= view.Width; i++)
        {
            var x = Math.Floor(ox + i * step) + 0.5;
            context.DrawLine(i % MajorEvery == 0 ? MajorGridPen : GridPen, new Point(x, 0), new Point(x, view.Height));
        }
        for (var i = (int)Math.Ceiling(-oy / step); oy + i * step <= view.Height; i++)
        {
            var y = Math.Floor(oy + i * step) + 0.5;
            context.DrawLine(i % MajorEvery == 0 ? MajorGridPen : GridPen, new Point(0, y), new Point(view.Width, y));
        }
    }

    /// <summary>Re-renders only <paramref name="imageRegion"/> (picture pixels at 100 %) into the bitmap already there.</summary>
    private void UpdateDrawingRegion(SvgDocument drawing, RectangleI imageRegion)
    {
        if (_drawingBitmap is null || imageRegion.Width <= 0 || imageRegion.Height <= 0)
        {
            RenderVisibleDrawing();
            InvalidateVisual();
            return;
        }
        var deviceScale = drawing.Workspace.Scale * RenderScaling;
        if (Math.Abs(deviceScale - _drawingDeviceScale) > 1e-9)
        {
            RenderVisibleDrawing();
            InvalidateVisual();
            return;
        }
        var scale = deviceScale;
        var device = new VRectI((int)Math.Floor(imageRegion.X * scale) - 1, (int)Math.Floor(imageRegion.Y * scale) - 1,
            (int)Math.Ceiling(imageRegion.Width * scale) + 3, (int)Math.Ceiling(imageRegion.Height * scale) + 3).Intersect(_drawingRegion);
        if (device.IsEmpty)
            return;
        var pixels = VectorRasterizer.Render(drawing.Root, device, deviceScale, drawing.RenderOptions);
        using (var fb = _drawingBitmap.Lock())
        {
            var rowBytes = device.Width * 4;
            for (var y = 0; y < device.Height; y++)
                Marshal.Copy(pixels, y * rowBytes,
                    fb.Address + (device.Y - _drawingRegion.Y + y) * fb.RowBytes + (device.X - _drawingRegion.X) * 4, rowBytes);
        }
        InvalidateVisual();
    }

    private static WriteableBitmap ToBitmap(byte[] pixels, int width, int height)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var fb = bitmap.Lock();
        var rowBytes = width * 4;
        for (var y = 0; y < height; y++)
            Marshal.Copy(pixels, y * rowBytes, fb.Address + y * fb.RowBytes, rowBytes);
        return bitmap;
    }

    private (OverlayPicture Picture, WriteableBitmap Bitmap)? _picture;

    // Converted once per picture: the overlay is redrawn much more often than it changes.
    private WriteableBitmap PictureBitmap(OverlayPicture picture)
    {
        if (_picture is { } cached && ReferenceEquals(cached.Picture, picture))
            return cached.Bitmap;
        // Not disposed: the renderer may still draw the previous one.
        var bitmap = new WriteableBitmap(new PixelSize(picture.Width, picture.Height), new Avalonia.Vector(96, 96),
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

    private static IBrush CreateCheckerBrush(byte darker = 204)
    {
        var size = CheckerSize * 2;
        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var fb = bitmap.Lock())
        {
            var row = new byte[size * 4];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var light = (x / CheckerSize + y / CheckerSize) % 2 == 0;
                    byte v = light ? (byte)255 : darker;
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
