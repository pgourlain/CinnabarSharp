using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace CinnabarSharp.Desktop.Controls;

/// <summary>
/// A line icon from the Cinnabar icon set: a path on a 24 px grid, 1.6 px stroke, round caps and joins. It is drawn
/// in the inherited Foreground, so it follows the theme and dims in a disabled button.
/// </summary>
public sealed class StrokeIcon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<StrokeIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<StrokeIcon, double>(nameof(Size), 18);

    static StrokeIcon()
    {
        AffectsMeasure<StrokeIcon>(SizeProperty);
        AffectsRender<StrokeIcon>(DataProperty, SizeProperty, TextElement.ForegroundProperty);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        if (Data is not { } data)
            return;
        var pen = new Pen(TextElement.GetForeground(this), 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(Size / 24, Size / 24)))
            context.DrawGeometry(null, pen, data);
    }
}
