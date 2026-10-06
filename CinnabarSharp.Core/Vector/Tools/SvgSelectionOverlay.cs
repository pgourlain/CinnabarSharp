using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector.Tools;

/// <summary>The frame and the eight resize handles around the selected objects, in image coordinates (what the canvas draws).</summary>
public static class SvgSelectionOverlay
{
    /// <summary>Null when nothing with a visible box is selected.</summary>
    public static ToolOverlay? For(SvgDocument document)
    {
        if (document.Actions.BoundsOf() is not { } box || box.Width < 0 || box.Height < 0)
            return null;
        var frame = document.UserToImage.TransformBounds(box);
        return new ToolOverlay
        {
            Frame = new RectangleD(frame.X, frame.Y, frame.Width, frame.Height),
            Handles = Handles(frame),
            SquareHandles = true,
        };
    }

    /// <summary>The eight handles of a box, clockwise from the top left corner: corners and edge middles.</summary>
    public static IReadOnlyList<PointD> Handles(VRect box) =>
    [
        new(box.Left, box.Top), new(box.Left + box.Width / 2, box.Top), new(box.Right, box.Top),
        new(box.Right, box.Top + box.Height / 2), new(box.Right, box.Bottom), new(box.Left + box.Width / 2, box.Bottom),
        new(box.Left, box.Bottom), new(box.Left, box.Top + box.Height / 2),
    ];
}
