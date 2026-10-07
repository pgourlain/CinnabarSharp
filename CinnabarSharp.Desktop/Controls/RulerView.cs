using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CinnabarSharp.Desktop.Controls;

/// <summary>
/// A ruler along the top or the left of the picture, in picture pixels. <see cref="Origin"/> is where picture
/// coordinate 0 is on the ruler (it follows the scroll), <see cref="Scale"/> the zoom; <see cref="Marker"/> shows
/// where the pointer is. Dragging out of it creates a guide (handled by the window).
/// </summary>
public sealed class RulerView : Control
{
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<RulerView, Orientation>(nameof(Orientation), Orientation.Horizontal);

    public static readonly StyledProperty<double> ScaleProperty = AvaloniaProperty.Register<RulerView, double>(nameof(Scale), 1);

    public static readonly StyledProperty<double> OriginProperty = AvaloniaProperty.Register<RulerView, double>(nameof(Origin));

    public static readonly StyledProperty<double?> MarkerProperty = AvaloniaProperty.Register<RulerView, double?>(nameof(Marker));

    private static readonly double[] Steps = [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000, 100000];
    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(238, 235, 230));
    private static readonly IBrush TickBrush = new SolidColorBrush(Color.FromRgb(120, 116, 110));
    private static readonly IPen TickPen = new Pen(TickBrush, 1);
    private static readonly IPen MarkerPen = new Pen(new SolidColorBrush(Color.FromRgb(0, 102, 255)), 1);
    private static readonly IPen EdgePen = new Pen(new SolidColorBrush(Color.FromRgb(200, 196, 190)), 1);

    static RulerView()
    {
        AffectsRender<RulerView>(OrientationProperty, ScaleProperty, OriginProperty, MarkerProperty);
        AffectsMeasure<RulerView>(OrientationProperty);
    }

    public RulerView()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        ClipToBounds = true;
    }

    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }

    public double Scale { get => GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }

    /// <summary>Position on the ruler (device-independent pixels from its start) of picture coordinate 0.</summary>
    public double Origin { get => GetValue(OriginProperty); set => SetValue(OriginProperty, value); }

    /// <summary>Picture coordinate of the pointer along this ruler, or null when it is not over the picture.</summary>
    public double? Marker { get => GetValue(MarkerProperty); set => SetValue(MarkerProperty, value); }

    /// <summary>Picture pixels between two labelled ticks for this zoom: the first step that leaves 60 screen pixels.</summary>
    public static double StepFor(double scale)
    {
        foreach (var step in Steps)
            if (step * scale >= 60)
                return step;
        return Steps[^1];
    }

    protected override Size MeasureOverride(Size availableSize) => Orientation == Orientation.Horizontal
        ? new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, 20)
        : new Size(20, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

    public override void Render(DrawingContext context)
    {
        var horizontal = Orientation == Orientation.Horizontal;
        var length = horizontal ? Bounds.Width : Bounds.Height;
        var thickness = horizontal ? Bounds.Height : Bounds.Width;
        context.FillRectangle(Background, new Rect(Bounds.Size));
        if (horizontal)
            context.DrawLine(EdgePen, new Point(0, thickness - 0.5), new Point(length, thickness - 0.5));
        else
            context.DrawLine(EdgePen, new Point(thickness - 0.5, 0), new Point(thickness - 0.5, length));
        if (Scale <= 0 || length <= 0)
            return;

        var step = StepFor(Scale);
        var minor = step >= 10 ? step / 10 : (step >= 5 ? 1 : 0);
        var first = Math.Floor(-Origin / Scale / step) * step;
        var last = (length - Origin) / Scale;
        for (var value = first; value <= last; value += step)
        {
            var at = Math.Floor(Origin + value * Scale) + 0.5;
            Tick(context, at, thickness, thickness);
            Label(context, at, value, horizontal);
            if (minor > 0)
                for (var m = minor; m < step; m += minor)
                {
                    var small = Math.Floor(Origin + (value + m) * Scale) + 0.5;
                    Tick(context, small, thickness, (m * 2 == step ? 0.55 : 0.3) * thickness);
                }
        }
        if (Marker is { } marker)
        {
            var at = Math.Floor(Origin + marker * Scale) + 0.5;
            if (horizontal)
                context.DrawLine(MarkerPen, new Point(at, 0), new Point(at, thickness));
            else
                context.DrawLine(MarkerPen, new Point(0, at), new Point(thickness, at));
        }
    }

    private void Tick(DrawingContext context, double at, double thickness, double size)
    {
        if (Orientation == Orientation.Horizontal)
            context.DrawLine(TickPen, new Point(at, thickness - size), new Point(at, thickness));
        else
            context.DrawLine(TickPen, new Point(thickness - size, at), new Point(thickness, at));
    }

    private void Label(DrawingContext context, double at, double value, bool horizontal)
    {
        var text = new FormattedText(value.ToString("0", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 9, TickBrush);
        if (horizontal)
        {
            context.DrawText(text, new Point(at + 3, 2));
            return;
        }
        // Vertical ruler: the number reads from the bottom to the top, just after its tick.
        using (context.PushTransform(Matrix.CreateTranslation(-text.Width, 0) * Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(2, at - 3)))
            context.DrawText(text, default);
    }
}
