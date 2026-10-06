using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ImageMagick;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Desktop.ViewModels;

public class LayerViewModel(UserLayer layer, DocumentActions actions) : ViewModelBase
{
    public const int ThumbnailSize = 40;

    private Bitmap? _thumbnail;

    public UserLayer Layer { get; } = layer;

    public string Name => Layer.Name;

    public bool IsVisible
    {
        get => !Layer.Hidden;
        set => actions.SetLayerVisibility(Layer, value);
    }

    /// <summary>E.g. "Multiply · 50%"; empty for Normal at 100%.</summary>
    public string Details
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Layer.BlendMode != BlendMode.Normal)
                parts.Add(LayerPropertiesViewModel.DisplayName(Layer.BlendMode));
            if (Layer.Opacity < 1)
                parts.Add($"{Math.Round(Layer.Opacity * 100)}%");
            return string.Join(" · ", parts);
        }
    }

    public Bitmap Thumbnail => _thumbnail ??= CreateThumbnail();

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(Details));
    }

    public void RefreshThumbnail()
    {
        _thumbnail?.Dispose();
        _thumbnail = null;
        OnPropertyChanged(nameof(Thumbnail));
    }

    private Bitmap CreateThumbnail()
    {
        using var small = Layer.Surface.Clone();
        small.Thumbnail(new MagickGeometry(ThumbnailSize, ThumbnailSize));
        var width = (int)small.Width;
        var height = (int)small.Height;
        var pixels = small.ToBgra();

        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var fb = bitmap.Lock();
        for (var y = 0; y < height; y++)
            Marshal.Copy(pixels, y * width * 4, fb.Address + y * fb.RowBytes, width * 4);
        return bitmap;
    }
}
