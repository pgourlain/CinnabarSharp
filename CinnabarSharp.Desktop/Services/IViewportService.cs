namespace CinnabarSharp.Desktop.Services;

public interface IViewportService
{
    /// <summary>Zooms the active document, keeping the image point under <paramref name="anchor"/>
    /// (viewport coordinates; null = viewport center) in place.</summary>
    void ZoomTo(double scale, Avalonia.Point? anchor = null);

    /// <summary>Image pixel shown at the top-left of the viewport (0,0 when the image isn't scrolled).</summary>
    CinnabarSharp.Core.Models.PointI VisibleImageOrigin();
}
