using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Photo;
using CinnabarSharp.Core.Tools;
using ImageMagick;

namespace CinnabarSharp.Desktop.ViewModels;

/// <summary>A photo that can go in a panel: an open image (flattened) or a file added in the dialog.</summary>
public sealed record ComicSource(string Name, BgraImage Photo);

public partial class ComicSourceViewModel(ComicSource source, bool included) : ViewModelBase
{
    public ComicSource Source => source;
    public string Name => source.Name;

    [ObservableProperty]
    public partial bool IsIncluded { get; set; } = included;
}

/// <summary>A layout with a small drawing of its panels for the dialog.</summary>
public sealed class ComicLayoutViewModel(ComicLayout layout)
{
    public ComicLayout Layout => layout;
    public string Name => layout.Name;

    public Geometry Thumbnail { get; } = new GeometryGroup
    {
        Children = new GeometryCollection(layout.Panels.Select(p =>
            (Geometry)new RectangleGeometry(new Rect(p.X * 36 + 1.5, p.Y * 50 + 1.5, p.Width * 36 - 3, p.Height * 50 - 3)))),
    };
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

    public ComicPageViewModel(IEnumerable<ComicSource> openImages)
    {
        foreach (var source in openImages)
            Sources.Add(new ComicSourceViewModel(source, included: true));
        SelectedLayout = LayoutFor(Sources.Count);
        SelectedFormat = ComicPage.Formats[0];
        SelectedBackground = Backgrounds[0];
    }

    public ObservableCollection<ComicSourceViewModel> Sources { get; } = [];

    public static IReadOnlyList<ComicLayoutViewModel> Layouts { get; } = ComicPage.Layouts.Select(l => new ComicLayoutViewModel(l)).ToList();

    public static IReadOnlyList<ComicPageFormat> Formats => ComicPage.Formats;

    public static IReadOnlyList<Option<bool>> Backgrounds { get; } = [new(false, "White page"), new(true, "Black page")];

    [ObservableProperty]
    public partial ComicLayoutViewModel SelectedLayout { get; set; }

    [ObservableProperty]
    public partial ComicPageFormat SelectedFormat { get; set; }

    [ObservableProperty]
    public partial Option<bool> SelectedBackground { get; set; }

    [ObservableProperty]
    public partial int Gutter { get; set; } = 40;

    [ObservableProperty]
    public partial int BorderWidth { get; set; } = 8;

    /// <summary>The layout that suits this many photos.</summary>
    public static ComicLayoutViewModel LayoutFor(int photos) => Layouts.First(l => l.Name == photos switch
    {
        <= 1 => "1 panel",
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
        SelectedBackground.Value ? ColorBgra.Black : ColorBgra.White);

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
        }
        finally
        {
            _syncing = false;
        }
    }

    partial void OnSelectedLayoutChanged(ComicLayoutViewModel value) => Tool?.SetLayout(value.Layout);
    partial void OnGutterChanged(int value) => Tool?.SetOptions(Options);
    partial void OnBorderWidthChanged(int value) => Tool?.SetOptions(Options);
    partial void OnSelectedBackgroundChanged(Option<bool> value) => Tool?.SetOptions(Options);
}
