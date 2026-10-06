using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Desktop.ViewModels;

public partial class DocumentViewModel(IDocument document) : ViewModelBase
{
    public IDocument Document { get; } = document;

    public bool IsImage => Document is ImageDocument;

    public bool IsSvg => Document is CinnabarSharp.Core.Vector.SvgDocument;

    /// <summary>The vector drawing, or null for another kind (for bindings that cannot throw).</summary>
    public CinnabarSharp.Core.Vector.SvgDocument? SvgOrNull => Document as CinnabarSharp.Core.Vector.SvgDocument;

    /// <summary>The raster document, for code that needs layers and pixels; only call it for an image tab (<see cref="IsImage"/>).</summary>
    public ImageDocument Image => Document as ImageDocument
        ?? throw new InvalidOperationException($"This is an {Document.Kind} document, not an image.");

    /// <summary>The raster document or null (for bindings that cannot throw).</summary>
    public ImageDocument? ImageOrNull => Document as ImageDocument;

    /// <summary>The vector drawing, for code that needs it; only call it for an SVG tab (<see cref="IsSvg"/>).</summary>
    public CinnabarSharp.Core.Vector.SvgDocument Svg => Document as CinnabarSharp.Core.Vector.SvgDocument
        ?? throw new InvalidOperationException($"This is an {Document.Kind} document, not a drawing.");

    public string Title => Document.IsDirty ? Document.DisplayName + " *" : Document.DisplayName;

    /// <summary>The name without the " *" of <see cref="Title"/>: the tab shows a dot instead.</summary>
    public string Name => Document.DisplayName;

    public bool IsDirty => Document.IsDirty;

    public const int ThumbnailSide = 44;

    private Avalonia.Media.Imaging.Bitmap? _thumbnail;

    /// <summary>A small picture for the tab; made when the image is opened and after each edit (see <see cref="RefreshThumbnail"/>).</summary>
    public Avalonia.Media.Imaging.Bitmap? Thumbnail => _thumbnail;

    public void RefreshThumbnail()
    {
        if (Document is ImageDocument { Layers: var layers } && layers.Count() == 0)
            return;
        var (bgra, width, height) = Document.GetThumbnail(ThumbnailSide);
        var previous = _thumbnail;
        _thumbnail = Services.BitmapFactory.FromBgra(bgra, width, height);
        OnPropertyChanged(nameof(Thumbnail));
        previous?.Dispose();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsDirty));
    }
}
