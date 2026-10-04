using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CinnabarSharp.Desktop.Services;

public static class BitmapFactory
{
    /// <summary>A bitmap from straight-alpha BGRA pixels.</summary>
    public static WriteableBitmap FromBgra(byte[] pixels, int width, int height)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var fb = bitmap.Lock();
        for (var y = 0; y < height; y++)
            Marshal.Copy(pixels, y * width * 4, fb.Address + y * fb.RowBytes, width * 4);
        return bitmap;
    }
}
