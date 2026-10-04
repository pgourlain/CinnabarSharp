using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Photo;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Desktop.Services;
using ImageMagick;

namespace CinnabarSharp.Desktop.ViewModels;

/// <summary>A photo that can go in a panel: an open image (flattened) or a file added in the dialog.</summary>
public sealed record ComicSource(string Name, BgraImage Photo);

public partial class ComicSourceViewModel(ComicSource source, bool included) : ViewModelBase
{
    public const int ThumbnailSize = 48;

    private Bitmap? _thumbnail;

    public ComicSource Source => source;
    public string Name => source.Name;

    /// <summary>A small picture of the photo for the panel's photo list; null for "(empty)".</summary>
    public Bitmap? Thumbnail => _thumbnail ??= CreateThumbnail();

    private Bitmap? CreateThumbnail()
    {
        var photo = source.Photo;
        if (photo.Width == 0 || photo.Height == 0)
            return null;
        var (pixels, width, height) = CinnabarSharp.Core.Effects.PhotoMath.Downscale(photo.Pixels, photo.Width, photo.Height, ThumbnailSize);
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var fb = bitmap.Lock();
        for (var y = 0; y < height; y++)
            Marshal.Copy(pixels, y * width * 4, fb.Address + y * fb.RowBytes, width * 4);
        return bitmap;
    }

    [ObservableProperty]
    public partial bool IsIncluded { get; set; } = included;
}

/// <summary>A layout with a small drawing of its panels for the dialog, shaped like the chosen page.</summary>
public sealed partial class ComicLayoutViewModel : ViewModelBase
{
    /// <summary>Side of the square the thumbnail's page is fitted into.</summary>
    public const double ThumbnailSize = 54;

    private readonly ComicLayout _layout;

    public ComicLayoutViewModel(ComicLayout layout, ImageSize page)
    {
        _layout = layout;
        Thumbnail = Draw(page);
    }

    public ComicLayout Layout => _layout;
    public string Name => _layout.Name;

    [ObservableProperty]
    public partial Geometry Thumbnail { get; set; }

    [ObservableProperty]
    public partial double ThumbnailWidth { get; set; }

    [ObservableProperty]
    public partial double ThumbnailHeight { get; set; }

    /// <summary>Redraws the thumbnail for a page of this size (A4 portrait, 16:9, square...).</summary>
    public void SetPage(ImageSize page) => Thumbnail = Draw(page);

    private Geometry Draw(ImageSize page)
    {
        var aspect = page.Width / (double)Math.Max(1, page.Height);
        (ThumbnailWidth, ThumbnailHeight) = aspect >= 1
            ? (ThumbnailSize, ThumbnailSize / aspect)
            : (ThumbnailSize * aspect, ThumbnailSize);
        var (w, h) = (ThumbnailWidth - 4, ThumbnailHeight - 4);
        return new GeometryGroup
        {
            Children = new GeometryCollection(_layout.Panels.Select(p =>
                (Geometry)new RectangleGeometry(new Rect(p.X * w + 1.5, p.Y * h + 1.5, p.Width * w - 3, p.Height * h - 3)))),
        };
    }
}

/// <summary>
/// Page de BD: in the start dialog, which photos, the page format and the layout; then, while the page is edited on the
/// canvas (<see cref="Tool"/>), the options bar: layout, gutter, border, background, and the selected panel's photo and
/// zoom.
/// </summary>
public partial class ComicPageViewModel : ViewModelBase
{
    public sealed record Option<T>(T Value, string Label);

    /// <summary>The "no photo" choice for a panel.</summary>
    public static ComicSourceViewModel Empty { get; } = new(new ComicSource("(empty)", new BgraImage([], 0, 0)), false);

    private bool _syncing;

    /// <summary>Layouts hidden from the dialog: a single panel is not worth a comic page.</summary>
    private static readonly string[] HiddenLayouts = ["1 panel"];

    private readonly List<string> _recentLayouts;

    public ComicPageViewModel(IEnumerable<ComicSource> openImages, ComicSettings? settings = null)
    {
        settings ??= new();
        foreach (var source in openImages)
            Sources.Add(new ComicSourceViewModel(source, included: true));
        _recentLayouts = [.. settings.RecentLayouts];
        // Recently used layouts first, then the others in catalog order.
        var available = ComicPage.Layouts.Where(l => !HiddenLayouts.Contains(l.Name)).ToList();
        var recent = _recentLayouts.Select(n => available.FirstOrDefault(l => l.Name == n)).OfType<ComicLayout>();
        var tv4K = ComicPage.Formats.First(f => f.Name.StartsWith(DefaultFormatName));
        var format = ComicPage.Formats.FirstOrDefault(f => f.Name == settings.Format) ?? tv4K;
        Layouts = [.. recent.Concat(available.Except(recent)).Select(l => new ComicLayoutViewModel(l, format.Size))];
        SelectedLayout = LayoutFor(Sources.Count);
        SelectedFormat = format;
        SelectedBackground = Backgrounds[settings.BlackPage ? 1 : 0];
        Gutter = settings.Gutter;
        BorderWidth = settings.Border;
    }

    public const string DefaultFormatName = "16:9 TV 4K";

    /// <summary>What to remember for the next page; <paramref name="rememberLayout"/> puts the selected layout first.</summary>
    public ComicSettings ToSettings(bool rememberLayout) => new()
    {
        Format = SelectedFormat.Name,
        Gutter = Gutter,
        Border = BorderWidth,
        BlackPage = SelectedBackground.Value,
        RecentLayouts = rememberLayout
            ? [SelectedLayout.Name, .. _recentLayouts.Where(n => n != SelectedLayout.Name).Take(MaxRecentLayouts - 1)]
            : [.. _recentLayouts],
    };

    private const int MaxRecentLayouts = 5;

    public ObservableCollection<ComicSourceViewModel> Sources { get; } = [];

    /// <summary>Layouts to choose from, recently used first.</summary>
    public IReadOnlyList<ComicLayoutViewModel> Layouts { get; }

    public static IReadOnlyList<ComicPageFormat> Formats => ComicPage.Formats;

    public static IReadOnlyList<Option<bool>> Backgrounds { get; } = [new(false, "White page"), new(true, "Black page")];

    [ObservableProperty]
    public partial ComicLayoutViewModel SelectedLayout { get; set; }

    [ObservableProperty]
    public partial ComicPageFormat SelectedFormat { get; set; }

    [ObservableProperty]
    public partial Option<bool> SelectedBackground { get; set; }

    [ObservableProperty]
    public partial int Gutter { get; set; } = 20;

    [ObservableProperty]
    public partial int BorderWidth { get; set; } = 8;

    /// <summary>How far the overlay panel of the layout covers its neighbours, 10-25 %.</summary>
    [ObservableProperty]
    public partial double OverlapPercent { get; set; } = 15;

    /// <summary>The selected layout has a panel that overlaps the others.</summary>
    public bool HasOverlay => SelectedLayout.Layout.OverlayPanel is not null;

    /// <summary>The layout that suits this many photos.</summary>
    public ComicLayoutViewModel LayoutFor(int photos) => Layouts.First(l => l.Name == photos switch
    {
        <= 1 => "2 rows",
        2 => "2 rows",
        3 => "1 large + 2 small",
        4 => "2 × 2 grid",
        5 => "Classic (2 + 1 + 2)",
        _ => "3 × 3 grid",
    });

    /// <summary>The photos to put in the panels, in order.</summary>
    public IReadOnlyList<ComicSource> Included => Sources.Where(s => s.IsIncluded).Select(s => s.Source).ToList();

    /// <summary>Black borders on a white page, white borders on a black one.</summary>
    public ComicPageOptions Options => new(SelectedFormat.Size, Gutter, BorderWidth,
        SelectedBackground.Value ? ColorBgra.White : ColorBgra.Black,
        SelectedBackground.Value ? ColorBgra.Black : ColorBgra.White, OverlapPercent / 100);

    /// <summary>Adds image files (EXIF orientation applied, sRGB); unreadable files are skipped. Returns how many were added.</summary>
    public int AddFiles(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths)
        {
            try
            {
                Sources.Add(new ComicSourceViewModel(new ComicSource(Path.GetFileName(path), TvExport.Load(new FileInfo(path))), included: true));
                added++;
            }
            catch (Exception e) when (e is MagickException or IOException or UnauthorizedAccessException)
            {
            }
        }
        if (added > 0 && Tool is null)
            SelectedLayout = LayoutFor(Sources.Count(s => s.IsIncluded));
        return added;
    }

    [RelayCommand]
    private void MoveUp(ComicSourceViewModel source)
    {
        var i = Sources.IndexOf(source);
        if (i > 0)
            Sources.Move(i, i - 1);
    }

    [RelayCommand]
    private void MoveDown(ComicSourceViewModel source)
    {
        var i = Sources.IndexOf(source);
        if (i >= 0 && i < Sources.Count - 1)
            Sources.Move(i, i + 1);
    }

    // ---- Editing the page on the canvas ----

    /// <summary>Set when the page is being edited on the canvas.</summary>
    public ComicPageTool? Tool { get; private set; }

    /// <summary>Starts editing: the tool gets the included photos in the layout's panels.</summary>
    public ComicPageTool CreateTool()
    {
        var photos = Included;
        var contents = Enumerable.Range(0, SelectedLayout.Layout.Panels.Count)
            .Select(i => i < photos.Count ? new ComicPanelContent(photos[i].Photo) : null);
        Tool = new ComicPageTool(SelectedLayout.Layout, Options, contents);
        Tool.SelectionChanged += RefreshPanel;
        Tool.Changed += RefreshPanel;
        RefreshPanel();
        return Tool;
    }

    /// <summary>Choices for a panel: nothing, or any of the photos.</summary>
    public IEnumerable<ComicSourceViewModel> PanelChoices => [Empty, .. Sources];

    public string PanelText => Tool is { Selected: >= 0 } tool ? $"Panel {tool.Selected + 1}:" : "";

    public bool PanelHasPhoto => Tool is { Selected: >= 0 } tool && tool.Contents[tool.Selected] is not null;

    /// <summary>The selected panel's photo.</summary>
    public ComicSourceViewModel? SelectedPanelChoice
    {
        get
        {
            if (Tool is not { Selected: >= 0 } tool || tool.Contents[tool.Selected] is not { } content)
                return Empty;
            return Sources.FirstOrDefault(s => ReferenceEquals(s.Source.Photo, content.Photo)) ?? Empty;
        }
        set
        {
            if (_syncing || Tool is not { Selected: >= 0 } tool || value is null || value == SelectedPanelChoice)
                return;
            tool.SetPhoto(tool.Selected, value == Empty ? null : value.Source.Photo);
        }
    }

    /// <summary>The selected panel's photo is stretched to the panel instead of cropped to fill it.</summary>
    public bool PanelStretch
    {
        get => Tool is { Selected: >= 0 } tool && tool.Contents[tool.Selected] is { Stretch: true };
        set
        {
            if (!_syncing && Tool is { Selected: >= 0 } tool)
                tool.SetStretch(tool.Selected, value);
        }
    }

    /// <summary>Zoom only applies to a cropped photo.</summary>
    public bool PanelCanZoom => PanelHasPhoto && !PanelStretch;

    /// <summary>The selected panel's zoom, 100-400 %.</summary>
    public double PanelZoom
    {
        get => Tool is { Selected: >= 0 } tool && tool.Contents[tool.Selected] is { } content ? content.Zoom * 100 : 100;
        set
        {
            if (!_syncing && Tool is { Selected: >= 0 } tool)
                tool.SetZoom(tool.Selected, value / 100);
        }
    }

    private void RefreshPanel()
    {
        _syncing = true;
        try
        {
            OnPropertyChanged(nameof(PanelText));
            OnPropertyChanged(nameof(PanelHasPhoto));
            OnPropertyChanged(nameof(SelectedPanelChoice));
            OnPropertyChanged(nameof(PanelZoom));
            OnPropertyChanged(nameof(PanelStretch));
            OnPropertyChanged(nameof(PanelCanZoom));
        }
        finally
        {
            _syncing = false;
        }
    }

    partial void OnSelectedFormatChanged(ComicPageFormat value)
    {
        foreach (var layout in Layouts)
            layout.SetPage(value.Size);
    }

    partial void OnSelectedLayoutChanged(ComicLayoutViewModel value)
    {
        OnPropertyChanged(nameof(HasOverlay));
        Tool?.SetLayout(value.Layout);
    }

    partial void OnOverlapPercentChanged(double value) => Tool?.SetOptions(Options);
    partial void OnGutterChanged(int value) => Tool?.SetOptions(Options);
    partial void OnBorderWidthChanged(int value) => Tool?.SetOptions(Options);
    partial void OnSelectedBackgroundChanged(Option<bool> value) => Tool?.SetOptions(Options);
}
