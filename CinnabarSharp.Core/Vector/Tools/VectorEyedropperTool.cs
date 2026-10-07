using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector.Tools;

/// <summary>
/// Eyedropper (K): a click on an object takes its stroke as the primary color and its fill as the secondary color (a gradient
/// gives the color rendered under the pointer); with Shift the rendered pixel under the pointer is taken, with the left button as
/// primary and the right as secondary. It changes the tool settings only: nothing goes into the history.
/// </summary>
public sealed class VectorEyedropperTool(ToolSettings settings) : IVectorTool
{
    public string Name => "Eyedropper";

    public void OnPointerDown(SvgDocument document, ToolPointer pointer)
    {
        var p = pointer.Position.ToVector();
        if (pointer.Modifiers.HasFlag(ToolModifiers.Shift))
        {
            if (RenderedColor(document, p) is { } rendered)
            {
                if (pointer.Button == ToolButton.Right)
                    settings.SecondaryColor = rendered.ToCore();
                else
                    settings.PrimaryColor = rendered.ToCore();
            }
            return;
        }
        if (SvgHitTester.HitTest(document, p, document.ScreenToUser(3), enterGroups: true) is not { } hit)
            return;
        var style = StyleResolver.ComputeFor(hit);
        var stroke = PaintColor(document, style.Stroke, style.StrokeOpacity, style, p);
        var fill = PaintColor(document, style.Fill, style.FillOpacity, style, p);
        // Both are taken at once when the object has them; a missing one leaves the setting as it is.
        if (stroke is { } s)
            settings.PrimaryColor = s.ToCore();
        if (fill is { } f)
            settings.SecondaryColor = f.ToCore();
    }

    public void OnPointerMove(SvgDocument document, ToolPointer pointer)
    {
    }

    public void OnPointerUp(SvgDocument document, ToolPointer pointer)
    {
    }

    private static VColor? PaintColor(SvgDocument document, SvgPaint paint, double opacity, ComputedStyle style, VPoint at) => paint.Kind switch
    {
        PaintKind.Color => paint.Color.WithOpacity(opacity),
        PaintKind.CurrentColor => style.Color.WithOpacity(opacity),
        PaintKind.Url => RenderedColor(document, at),
        _ => null,
    };

    /// <summary>The color of the picture at a point of the user space (straight alpha), from a 1 × 1 render.</summary>
    public static VColor? RenderedColor(SvgDocument document, VPoint userPoint)
    {
        var image = document.UserToImagePoint(userPoint);
        var x = (int)Math.Floor(image.X);
        var y = (int)Math.Floor(image.Y);
        var size = document.ImageSize;
        if (x < 0 || y < 0 || x >= size.Width || y >= size.Height)
            return null;
        var pixels = VectorRasterizer.Render(document.Root, new VRectI(x, y, 1, 1), 1, document.RenderOptions);
        return new VColor(pixels[0], pixels[1], pixels[2], pixels[3]);
    }
}
