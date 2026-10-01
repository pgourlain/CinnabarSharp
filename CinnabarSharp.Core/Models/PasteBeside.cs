namespace CinnabarSharp.Core.Models;

/// <summary>Which side of the image a pasted image goes to (Paste Beside).</summary>
public enum PasteSide
{
    Left,
    Right,
    Top,
    Bottom,
}

/// <summary>
/// How the shorter of the two images is placed along the shared edge: <see cref="Start"/> is the top for
/// <see cref="PasteSide.Left"/>/<see cref="PasteSide.Right"/> and the left for <see cref="PasteSide.Top"/>/<see cref="PasteSide.Bottom"/>.
/// </summary>
public enum EdgeAlignment
{
    Start,
    Middle,
    End,
}

/// <summary>Where the image and the pasted image go when they are put side by side.</summary>
public readonly record struct PasteBesideLayout(ImageSize Size, PointI ImageAt, PointI PastedAt)
{
    public static PasteBesideLayout For(ImageSize image, ImageSize pasted, PasteSide side, EdgeAlignment alignment)
    {
        static int Align(int length, int total, EdgeAlignment a) => a switch
        {
            EdgeAlignment.Start => 0,
            EdgeAlignment.End => total - length,
            _ => (total - length) / 2,
        };

        if (side is PasteSide.Left or PasteSide.Right)
        {
            var size = new ImageSize(image.Width + pasted.Width, Math.Max(image.Height, pasted.Height));
            var imageY = Align(image.Height, size.Height, alignment);
            var pastedY = Align(pasted.Height, size.Height, alignment);
            return side == PasteSide.Right
                ? new(size, new PointI(0, imageY), new PointI(image.Width, pastedY))
                : new(size, new PointI(pasted.Width, imageY), new PointI(0, pastedY));
        }
        else
        {
            var size = new ImageSize(Math.Max(image.Width, pasted.Width), image.Height + pasted.Height);
            var imageX = Align(image.Width, size.Width, alignment);
            var pastedX = Align(pasted.Width, size.Width, alignment);
            return side == PasteSide.Bottom
                ? new(size, new PointI(imageX, 0), new PointI(pastedX, image.Height))
                : new(size, new PointI(imageX, pasted.Height), new PointI(pastedX, 0));
        }
    }
}
