namespace CinnabarSharp.Core.Services;

/// <summary>Straight-alpha BGRA image exchanged with the system clipboard.</summary>
public sealed record ClipboardImage(byte[] Bgra, int Width, int Height);

/// <summary>System clipboard; implemented by the UI (Core has no access to the platform clipboard).</summary>
public interface IClipboardService
{
    Task SetImageAsync(ClipboardImage image);

    /// <summary>The clipboard image, or null if the clipboard holds no image.</summary>
    Task<ClipboardImage?> GetImageAsync();
}
