namespace CinnabarSharp.Desktop.Services;

public interface IViewportService
{
    /// <summary>Zooms the active document, keeping the image point under <paramref name="anchor"/>
    /// (viewport coordinates; null = viewport center) in place.</summary>
    void ZoomTo(double scale, Avalonia.Point? anchor = null);
}
