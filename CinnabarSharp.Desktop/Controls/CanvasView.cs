using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CinnabarSharp.Core.Models;
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

    private const int CheckerSize = 8;
    private static readonly IBrush CheckerBrush = CreateCheckerBrush();

    private WriteableBitmap? _bitmap;

    static CanvasView()
    {
        AffectsMeasure<CanvasView>(DocumentProperty, RenderVersionProperty);
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
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Document is not { } doc)
            return;
        var pos = e.GetPosition(this);
        var scale = doc.Workspace.Scale;
        CanvasPointerMoved?.Invoke(new PointD(pos.X / scale, pos.Y / scale));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        CanvasPointerMoved?.Invoke(null);
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
