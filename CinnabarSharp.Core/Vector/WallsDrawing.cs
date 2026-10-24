using System.Globalization;
using System.Security;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Vector;
using ImageMagick;

namespace CinnabarSharp.Core.Vector;

/// <summary>
/// The tree of the drawing made from extracted walls: a locked, faded "Plan" layer with the source picture (optional) under a
/// "Walls" layer with one filled path per connected wall group, ready for the vector tools.
/// </summary>
public static class WallsDrawing
{
    public const string PlanLayer = "Plan";
    public const string WallsLayer = "Walls";

    /// <summary>
    /// <paramref name="unitsPerPixel"/> is the size of a pixel in the drawing's unit: the paths are already in that unit, and so
    /// is the page (millimetres when <paramref name="millimetres"/>, pixels otherwise). <paramref name="plan"/> is image-sized BGRA.
    /// </summary>
    public static SvgRoot Build(int pixelWidth, int pixelHeight, IReadOnlyList<VectorPath> outlines, ColorBgra fill,
        double unitsPerPixel, bool millimetres, byte[]? plan)
    {
        var inv = CultureInfo.InvariantCulture;
        string N(double v) => NumberFormat.Format(v, 4);
        double width = pixelWidth * unitsPerPixel, height = pixelHeight * unitsPerPixel;
        var suffix = millimetres ? "mm" : "";
        var color = string.Create(inv, $"#{fill.R:X2}{fill.G:X2}{fill.B:X2}");

        var text = new System.Text.StringBuilder();
        text.Append(inv, $"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:inkscape=\"http://www.inkscape.org/namespaces/inkscape\" ");
        text.Append(inv, $"xmlns:sodipodi=\"http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd\" width=\"{N(width)}{suffix}\" height=\"{N(height)}{suffix}\" viewBox=\"0 0 {N(width)} {N(height)}\">\n");
        if (plan is not null)
        {
            using var picture = Utility.FromBgra(plan, pixelWidth, pixelHeight);
            var png = Convert.ToBase64String(picture.ToByteArray(MagickFormat.Png));
            text.Append(inv, $"  <g id=\"plan\" inkscape:groupmode=\"layer\" inkscape:label=\"{PlanLayer}\" sodipodi:insensitive=\"true\" style=\"opacity:0.3\">\n");
            text.Append(inv, $"    <image id=\"plan-picture\" x=\"0\" y=\"0\" width=\"{N(width)}\" height=\"{N(height)}\" preserveAspectRatio=\"none\" href=\"data:image/png;base64,{png}\"/>\n");
            text.Append("  </g>\n");
        }
        text.Append(inv, $"  <g id=\"walls\" inkscape:groupmode=\"layer\" inkscape:label=\"{SecurityElement.Escape(WallsLayer)}\">\n");
        for (var i = 0; i < outlines.Count; i++)
            text.Append(inv, $"    <path id=\"wall{i + 1}\" d=\"{PathDataWriter.Write(outlines[i], 3)}\" fill-rule=\"evenodd\" style=\"fill:{color};stroke:none\"/>\n");
        text.Append("  </g>\n</svg>\n");
        return SvgParser.Parse(text.ToString()).Root;
    }
}
