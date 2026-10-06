namespace CinnabarSharp.Core.Services;

/// <summary>Straight-alpha BGRA image exchanged with the system clipboard.</summary>
public sealed record ClipboardImage(byte[] Bgra, int Width, int Height);

/// <summary>System clipboard; implemented by the UI (Core has no access to the platform clipboard).</summary>
public interface IClipboardService
{
    Task SetImageAsync(ClipboardImage image);

    /// <summary>The clipboard image, or null if the clipboard holds no image.</summary>
    Task<ClipboardImage?> GetImageAsync();

    Task SetTextAsync(string text);

    /// <summary>The clipboard text, or null if the clipboard holds no text.</summary>
    Task<string?> GetTextAsync();

    /// <summary>
    /// Puts SVG objects on the clipboard as <c>image/svg+xml</c> (and as text), with a picture of them for applications that only
    /// take bitmaps.
    /// </summary>
    Task SetSvgAsync(string svg, ClipboardImage? picture);

    /// <summary>The SVG text on the clipboard (<c>image/svg+xml</c>, or text that looks like an SVG document); null when there is none.</summary>
    Task<string?> GetSvgAsync();
}
