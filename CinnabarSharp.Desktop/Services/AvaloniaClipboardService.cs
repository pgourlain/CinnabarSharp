using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Desktop.Services;

/// <summary>
/// System clipboard through Avalonia; converts between Avalonia bitmaps and straight-alpha BGRA.
/// <para>
/// A copied picture is not handed to Avalonia as a <see cref="Bitmap"/>: Avalonia would encode it as PNG at the best
/// compression on the UI thread whenever someone reads it (on macOS, the clipboard history reads it at once), several
/// seconds for a 12-megapixel photo. On macOS and Linux the PNG is encoded here, on a background thread at the fastest
/// compression; Windows keeps the bitmap so applications that only take a DIB still get it. The last copied image is
/// also kept with an id on the clipboard, so pasting it back into this app needs no decoding at all.
/// </para>
/// </summary>
public class AvaloniaClipboardService(TopLevel topLevel) : IClipboardService
{
    private static readonly DataFormat<string> CopyId = DataFormat.CreateStringApplicationFormat("cinnabarsharp-copy-id");

    private static readonly DataFormat<byte[]>? PngFormat =
        OperatingSystem.IsMacOS() ? DataFormat.CreateBytesPlatformFormat("public.png")
        : OperatingSystem.IsWindows() ? null
        : DataFormat.CreateBytesPlatformFormat("image/png");

    private string? _lastId;
    private ClipboardImage? _last;

    public async Task SetImageAsync(ClipboardImage image)
    {
        if (topLevel.Clipboard is not { } clipboard)
            return;
        var item = new DataTransferItem();
        await AddPictureAsync(item, image);
        await SetAsync(clipboard, item, image);
    }

    public async Task<ClipboardImage?> GetImageAsync()
    {
        if (topLevel.Clipboard is not { } clipboard)
            return null;
        if (_last is not null && await ReadCopyIdAsync(clipboard) == _lastId)
            return _last;
        if (await clipboard.TryGetBitmapAsync() is not { } bitmap)
            return null;
        using (bitmap)
            return ToClipboardImage(bitmap);
    }

    public async Task SetTextAsync(string text)
    {
        if (topLevel.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    public async Task<string?> GetTextAsync() =>
        topLevel.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;

    private static readonly DataFormat<string> SvgText = DataFormat.CreateStringPlatformFormat("image/svg+xml");
    private static readonly DataFormat<byte[]> SvgBytes = DataFormat.CreateBytesPlatformFormat("image/svg+xml");

    public async Task SetSvgAsync(string svg, ClipboardImage? picture)
    {
        if (topLevel.Clipboard is not { } clipboard)
            return;
        var item = new DataTransferItem();
        item.Set(SvgText, svg);
        item.SetText(svg);
        if (picture is not null)
            await AddPictureAsync(item, picture);
        await SetAsync(clipboard, item, picture);
    }

    public async Task<string?> GetSvgAsync()
    {
        if (topLevel.Clipboard is not { } clipboard || await clipboard.TryGetDataAsync() is not { } data)
            return null;
        try
        {
            if (await data.TryGetValueAsync(SvgText) is { Length: > 0 } text)
                return text;
            if (await data.TryGetValueAsync(SvgBytes) is { Length: > 0 } bytes)
                return System.Text.Encoding.UTF8.GetString(bytes);
            return await data.TryGetTextAsync() is { } plain && CinnabarSharp.Core.Vector.SvgClipboard.LooksLikeSvg(plain) ? plain : null;
        }
        finally
        {
            (data as IDisposable)?.Dispose();
        }
    }

    /// <summary>Puts the item on the clipboard with a new id; <paramref name="image"/> is what pasting it back here returns.</summary>
    private async Task SetAsync(IClipboard clipboard, DataTransferItem item, ClipboardImage? image)
    {
        var id = Guid.NewGuid().ToString("N");
        item.Set(CopyId, id);
        var transfer = new DataTransfer();
        transfer.Add(item);
        await clipboard.SetDataAsync(transfer);
        (_lastId, _last) = (id, image);
    }

    private static async Task AddPictureAsync(DataTransferItem item, ClipboardImage image)
    {
        if (PngFormat is null)
        {
            item.SetBitmap(ToBitmap(image));
            return;
        }
        item.Set(PngFormat, await Task.Run(() => EncodePng(image)));
    }

    private static byte[] EncodePng(ClipboardImage image)
    {
        using var bitmap = ToBitmap(image);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions { CompressionLevel = CompressionLevel.Fastest });
        return stream.ToArray();
    }

    private static async Task<string?> ReadCopyIdAsync(IClipboard clipboard)
    {
        if (await clipboard.TryGetDataAsync() is not { } data)
            return null;
        try
        {
            return await data.TryGetValueAsync(CopyId);
        }
        finally
        {
            (data as IDisposable)?.Dispose();
        }
    }

    private static WriteableBitmap ToBitmap(ClipboardImage image)
    {
        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var fb = bitmap.Lock();
        for (var y = 0; y < image.Height; y++)
            Marshal.Copy(image.Bgra, y * image.Width * 4, fb.Address + y * fb.RowBytes, image.Width * 4);
        return bitmap;
    }

    /// <summary>Copies any Avalonia bitmap into straight-alpha BGRA by drawing it into a known format.</summary>
    public static ClipboardImage ToClipboardImage(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        using var target = new WriteableBitmap(size, new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
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
