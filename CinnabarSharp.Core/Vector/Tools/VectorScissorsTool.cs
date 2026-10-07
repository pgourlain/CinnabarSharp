using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector.Tools;

/// <summary>A tool that shows something under the pointer while no button is pressed (the canvas then redraws its overlay).</summary>
public interface IVectorHoverTool : IVectorTool
{
    /// <summary>The pointer moved over a point of the user space with no button pressed; true if the overlay changed.</summary>
    bool OnHover(SvgDocument document, PointD userPoint);
}

/// <summary>
/// Scissors (X): a click on an outline takes out the run between the two crossings with other shapes that is under the pointer
/// (see <see cref="SvgScissors"/>); the run is shown while the pointer is over it. A closed shape becomes an open path, an open
/// one is cut in two; undo puts it back. One history step per click.
/// </summary>
public sealed class VectorScissorsTool : IVectorHoverTool
{
    private const double Reach = 4;       // screen pixels

    private ScissorsHit? _hover;

    public string Name => "Scissors";

    public void OnPointerDown(SvgDocument document, ToolPointer pointer)
    {
        var hit = SvgScissors.Find(document, pointer.Position.ToVector(), document.ScreenToUser(Reach));
        _hover = null;
        if (hit is not null)
            document.Actions.RemoveSegment(hit);
    }

    public void OnPointerMove(SvgDocument document, ToolPointer pointer)
    {
    }

    public void OnPointerUp(SvgDocument document, ToolPointer pointer)
    {
    }

    public bool OnHover(SvgDocument document, PointD userPoint)
    {
        var hit = SvgScissors.Find(document, userPoint.ToVector(), document.ScreenToUser(Reach));
        var changed = hit is null != _hover is null
                      || hit is not null && _hover is not null && (hit.Shape != _hover.Shape || hit.Figure != _hover.Figure || hit.Piece != _hover.Piece);
        _hover = hit;
        return changed;
    }

    public ToolOverlay? GetOverlay(SvgDocument document)
    {
        if (_hover is not { } hit || hit.Shape.DocumentRoot != document.Root)
            return null;
        var toImage = document.UserToImage * SvgBounds.ToDocument(hit.Shape);
        var lines = new List<(PointD From, PointD To)>();
        var ends = new List<PointD>();
        foreach (var line in Flattener.Flatten(hit.Removed, toImage, 0.25))
        {
            var points = line.Points;
            for (var i = 0; i + 1 < points.Count; i++)
                lines.Add((new PointD(points[i].X, points[i].Y), new PointD(points[i + 1].X, points[i + 1].Y)));
            if (points.Count > 0)
            {
                ends.Add(new PointD(points[0].X, points[0].Y));
                ends.Add(new PointD(points[^1].X, points[^1].Y));
            }
        }
        return new ToolOverlay { Lines = lines, Handles = ends };
    }
}
