using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CinnabarSharp.Core.Adjustments;

namespace CinnabarSharp.Desktop.Controls;

/// <summary>Red, green and blue histograms drawn as translucent areas, as in Paint.NET's Levels dialog.</summary>
public class HistogramView : Control
{
    public static readonly StyledProperty<Histogram?> HistogramProperty =
        AvaloniaProperty.Register<HistogramView, Histogram?>(nameof(Histogram));

    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));
    private static readonly Pen Border = new(Brushes.Gray, 1);

    static HistogramView()
    {
        AffectsRender<HistogramView>(HistogramProperty);
    }

    public Histogram? Histogram
    {
        get => GetValue(HistogramProperty);
        set => SetValue(HistogramProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Background, bounds);
        if (Histogram is { } h)
        {
            // Square root keeps small counts visible next to a dominant color.
            var max = Math.Sqrt(Math.Max(1, new[] { h.Red, h.Green, h.Blue }.SelectMany(c => c).Max()));
            DrawChannel(context, h.Red, Color.FromArgb(110, 230, 40, 40), bounds, max);
            DrawChannel(context, h.Green, Color.FromArgb(110, 40, 180, 40), bounds, max);
            DrawChannel(context, h.Blue, Color.FromArgb(110, 40, 80, 230), bounds, max);
        }
        context.DrawRectangle(Border, bounds);
    }

    private static void DrawChannel(DrawingContext context, long[] counts, Color color, Rect bounds, double max)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(0, bounds.Height), isFilled: true);
            for (var i = 0; i < 256; i++)
                g.LineTo(new Point(i / 255.0 * bounds.Width, bounds.Height * (1 - Math.Sqrt(counts[i]) / max)));
            g.LineTo(new Point(bounds.Width, bounds.Height));
            g.EndFigure(isClosed: true);
        }
        context.DrawGeometry(new SolidColorBrush(color), null, geometry);
    }
}
