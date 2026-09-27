using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Desktop.Services;

/// <summary>System clipboard through Avalonia; converts between Avalonia bitmaps and straight-alpha BGRA.</summary>
public class AvaloniaClipboardService(TopLevel topLevel) : IClipboardService
{
    public async Task SetImageAsync(ClipboardImage image)
    {
        if (topLevel.Clipboard is not { } clipboard)
            return;
        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = bitmap.Lock())
        {
            for (var y = 0; y < image.Height; y++)
                Marshal.Copy(image.Bgra, y * image.Width * 4, fb.Address + y * fb.RowBytes, image.Width * 4);
        }
        await clipboard.SetBitmapAsync(bitmap);
    }

    public async Task<ClipboardImage?> GetImageAsync()
    {
        if (topLevel.Clipboard is not { } clipboard || await clipboard.TryGetBitmapAsync() is not { } bitmap)
            return null;
        return ToClipboardImage(bitmap);
    }

    public async Task SetTextAsync(string text)
    {
        if (topLevel.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    public async Task<string?> GetTextAsync() =>
        topLevel.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;

    /// <summary>Copies any Avalonia bitmap into straight-alpha BGRA by drawing it into a known format.</summary>
    public static ClipboardImage ToClipboardImage(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        using var target = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = target.Lock())
            bitmap.CopyPixels(fb);

        var bgra = new byte[size.Width * size.Height * 4];
        using (var fb = target.Lock())
        {
            for (var y = 0; y < size.Height; y++)
                Marshal.Copy(fb.Address + y * fb.RowBytes, bgra, y * size.Width * 4, size.Width * 4);
        }
        return new ClipboardImage(bgra, size.Width, size.Height);
    }
}
