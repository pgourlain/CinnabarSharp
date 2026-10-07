using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Tools;

/// <summary>A tool whose pointer positions are moved to the nearest grid intersection when snapping to the grid is on.</summary>
public interface IGridSnappingTool
{
}

/// <summary>Rounding to the grid: lines every <c>size</c> from <c>origin</c>.</summary>
public static class GridSnapping
{
    public const double MinSize = 1;
    public const double MaxSize = 1000;

    public static double Snap(double value, double size, double origin = 0)
    {
        if (!(size >= MinSize))
            return value;
        return origin + Math.Round((value - origin) / size, MidpointRounding.AwayFromZero) * size;
    }

    public static PointD Snap(PointD point, double size, PointD origin) =>
        new(Snap(point.X, size, origin.X), Snap(point.Y, size, origin.Y));

    /// <summary>
    /// The shift that brings the nearest of <paramref name="edges"/> onto a grid line, when one is within
    /// <paramref name="reach"/>; 0 otherwise. Used to snap a moved box by its edges.
    /// </summary>
    public static double ShiftToGrid(IEnumerable<double> edges, double size, double origin, double reach)
    {
        var best = 0.0;
        var distance = reach;
        foreach (var edge in edges)
        {
            var shift = Snap(edge, size, origin) - edge;
            if (Math.Abs(shift) <= distance)
            {
                distance = Math.Abs(shift);
                best = shift;
            }
        }
        return best;
    }
}
