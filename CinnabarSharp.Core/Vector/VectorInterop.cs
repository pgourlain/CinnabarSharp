using CinnabarSharp.Core.Models;
using CinnabarSharp.Vector;
using CorePointD = CinnabarSharp.Core.Models.PointD;

namespace CinnabarSharp.Core.Vector;

/// <summary>
/// Conversions between the SVG engine's primitives (<c>CinnabarSharp.Vector</c> cannot see Core) and Core's.
/// Rectangles keep x, y, width and height; <see cref="ColorBgra"/> and <see cref="VColor"/> share the BGRA layout.
/// </summary>
public static class VectorInterop
{
    public static VPoint ToVector(this CorePointD p) => new(p.X, p.Y);

    public static CorePointD ToCore(this VPoint p) => new(p.X, p.Y);

    public static VRect ToVector(this RectangleD r) => new(r.X, r.Y, r.Width, r.Height);

    public static RectangleD ToCore(this VRect r) => new(r.X, r.Y, r.Width, r.Height);

    public static VRectI ToVector(this RectangleI r) => new(r.X, r.Y, r.Width, r.Height);

    public static RectangleI ToCore(this VRectI r) => new(r.X, r.Y, r.Width, r.Height);

    public static VColor ToVector(this ColorBgra c) => new(c.B, c.G, c.R, c.A);

    public static ColorBgra ToCore(this VColor c) => ColorBgra.FromBgra(c.B, c.G, c.R, c.A);

    /// <summary>Smallest pixel rectangle covering the user-space rectangle (empty stays empty).</summary>
    public static RectangleI ToOuterPixels(this VRect r) =>
        r.IsEmpty ? RectangleI.Zero : r.ToOuterInteger().ToCore();
}
