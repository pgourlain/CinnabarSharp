using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CinnabarSharp.Core.Adjustments;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Controls;

/// <summary>
/// The Curves dialog's graph: input on x, output on y (0–255), over the luminosity histogram. Edits go to
/// <see cref="CurvesDialogViewModel"/>: click adds or grabs a point, drag moves it, right-click removes it.
/// </summary>
public class CurveEditor : Control
{
    public static readonly StyledProperty<CurvesDialogViewModel?> ModelProperty =
        AvaloniaProperty.Register<CurveEditor, CurvesDialogViewModel?>(nameof(Model));

    public static readonly StyledProperty<int> VersionProperty =
        AvaloniaProperty.Register<CurveEditor, int>(nameof(Version));

    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(252, 252, 252));
    private static readonly IBrush HistogramBrush = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0));
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), 1);
    private static readonly Pen DiagonalPen = new(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 1, new DashStyle([4, 4], 0));
    private static readonly Pen Border = new(Brushes.Gray, 1);

    private bool _dragging;

    static CurveEditor()
    {
        AffectsRender<CurveEditor>(ModelProperty, VersionProperty);
    }

    public CurvesDialogViewModel? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public int Version
    {
        get => GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    private Point ToScreen(double x, double y) => new(x / 255 * Bounds.Width, (1 - y / 255) * Bounds.Height);

    private (double X, double Y) ToCurve(Point p) =>
        (p.X / Bounds.Width * 255, (1 - p.Y / Bounds.Height) * 255);

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Background, bounds);
        if (Model is not { } model)
            return;

        DrawHistogram(context, model.Histogram.Luminosity);
        for (var i = 1; i < 4; i++)
        {
            context.DrawLine(GridPen, new Point(bounds.Width * i / 4, 0), new Point(bounds.Width * i / 4, bounds.Height));
            context.DrawLine(GridPen, new Point(0, bounds.Height * i / 4), new Point(bounds.Width, bounds.Height * i / 4));
        }
        context.DrawLine(DiagonalPen, ToScreen(0, 0), ToScreen(255, 255));

        if (model.Mode == CurvesMode.Luminosity)
        {
            DrawCurve(context, model.Luminosity, Colors.Black, edited: true);
        }
        else
        {
            DrawCurve(context, model.Red, Color.FromRgb(220, 30, 30), model.EditRed);
            DrawCurve(context, model.Green, Color.FromRgb(30, 160, 30), model.EditGreen);
            DrawCurve(context, model.Blue, Color.FromRgb(30, 60, 220), model.EditBlue);
        }
        context.DrawRectangle(Border, bounds);
    }

    private void DrawHistogram(DrawingContext context, long[] counts)
    {
        var max = 1.0;
        foreach (var c in counts)
            max = Math.Max(max, Math.Sqrt(c));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(ToScreen(0, 0), isFilled: true);
            for (var i = 0; i < 256; i++)
                g.LineTo(ToScreen(i, Math.Sqrt(counts[i]) / max * 255));
            g.LineTo(ToScreen(255, 0));
            g.EndFigure(isClosed: true);
        }
        context.DrawGeometry(HistogramBrush, null, geometry);
    }

    private void DrawCurve(DrawingContext context, IReadOnlyList<PointI> points, Color color, bool edited)
    {
        var lut = Curves.Spline(points);
        var brush = new SolidColorBrush(color, edited ? 1 : 0.3);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(ToScreen(0, lut[0]), isFilled: false);
            for (var i = 1; i < 256; i++)
                g.LineTo(ToScreen(i, lut[i]));
            g.EndFigure(isClosed: false);
        }
        context.DrawGeometry(null, new Pen(brush, edited ? 2 : 1), geometry);
        if (!edited)
            return;
        foreach (var p in points)
        {
            var s = ToScreen(p.X, p.Y);
            context.DrawRectangle(Brushes.White, new Pen(brush, 1.5), new Rect(s.X - 4, s.Y - 4, 8, 8));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Model is not { } model)
            return;
        var props = e.GetCurrentPoint(this).Properties;
        var (x, y) = ToCurve(e.GetPosition(this));
        model.PressAt(x, y, remove: props.IsRightButtonPressed);
        _dragging = props.IsLeftButtonPressed;
        if (_dragging)
            e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || Model is not { } model)
            return;
        var (x, y) = ToCurve(e.GetPosition(this));
        model.DragTo(x, y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
            return;
        _dragging = false;
        e.Pointer.Capture(null);
        Model?.Release();
    }
}
