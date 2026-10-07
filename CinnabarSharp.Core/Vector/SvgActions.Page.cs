using CinnabarSharp.Core.Models;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector;

// The page of a drawing: Image › Resize and Canvas Size.
public sealed partial class SvgActions
{
    private const int MaxPageSide = 65535;

    /// <summary>
    /// Changes the size of the page (pixels at 96 dpi; the unit of its width and height is kept). With
    /// <paramref name="keepContentAt"/> null the page is scaled with its content (Image › Resize). With an anchor the
    /// content keeps its size and place, and the page grows or shrinks around that anchor (Canvas Size).
    /// </summary>
    public void ResizePage(ImageSize size, Anchor? keepContentAt)
    {
        if (size.Width < 1 || size.Height < 1 || size.Width > MaxPageSide || size.Height > MaxPageSide)
            throw new ArgumentException($"The page must be between 1 and {MaxPageSide} pixels on each side.");
        var (pixelsWidth, pixelsHeight) = Root.PixelSize;
        if (Math.Abs(pixelsWidth - size.Width) < 1e-9 && Math.Abs(pixelsHeight - size.Height) < 1e-9)
            return;
        var box = Root.ViewBox ?? new VRect(0, 0, Root.UserSize.Width, Root.UserSize.Height);
        var tx = Begin(keepContentAt is null ? "Resize Image" : "Canvas Size");
        tx.Edit([Root], () =>
        {
            SvgLength Scaled(SvgLength? old, double from, double to) =>
                old is { IsAbsolute: true, Value: > 0 } length ? new SvgLength(length.Value * to / from, length.Unit) : SvgLength.Px(to);
            var newBox = box;
            if (keepContentAt is { } anchor)
            {
                // User units per pixel: the content keeps its size on the page.
                var (sx, sy) = (box.Width / pixelsWidth, box.Height / pixelsHeight);
                var (ax, ay) = Fractions(anchor);
                newBox = new VRect(
                    Math.Round(box.X - (size.Width - pixelsWidth) * ax * sx, 4),
                    Math.Round(box.Y - (size.Height - pixelsHeight) * ay * sy, 4),
                    Math.Round(size.Width * sx, 4), Math.Round(size.Height * sy, 4));
            }
            else if (Math.Abs(size.Width / pixelsWidth - size.Height / pixelsHeight) > 1e-6)
            {
                // A different proportion stretches the content over the new page.
                Root.SetAttribute("preserveAspectRatio", "none");
            }
            Root.Width = Scaled(Root.Width, pixelsWidth, size.Width);
            Root.Height = Scaled(Root.Height, pixelsHeight, size.Height);
            Root.ViewBox = newBox;
        });
        tx.Commit();
    }

    // Where the anchor sits on the page: 0 = left or top, 1 = right or bottom.
    private static (double X, double Y) Fractions(Anchor anchor) => anchor switch
    {
        Anchor.NW => (0, 0),
        Anchor.N => (0.5, 0),
        Anchor.NE => (1, 0),
        Anchor.E => (1, 0.5),
        Anchor.SE => (1, 1),
        Anchor.S => (0.5, 1),
        Anchor.SW => (0, 1),
        Anchor.W => (0, 0.5),
        _ => (0.5, 0.5),
    };
}
