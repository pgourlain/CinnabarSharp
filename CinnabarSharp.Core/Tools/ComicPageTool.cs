using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Photo;

namespace CinnabarSharp.Core.Tools;

/// <summary>
/// Edits a comic page on its document: click a panel to select it, drag inside it to move the photo in the panel.
/// The page itself is drawn from <see cref="Preview"/> (computed by the caller with <see cref="ComicPage.Compose"/>);
/// the document's pixels only change when the caller applies it. Not in the toolbox: the Page de BD mode uses it.
/// </summary>
public sealed class ComicPageTool(ComicLayout layout, ComicPageOptions options, IEnumerable<ComicPanelContent?> contents) : IOverlayTool
{
    private readonly List<ComicPanelContent?> _contents = [.. contents];
    private int _dragged = -1;
    private PointD _dragStart;
    private PointD _centerAtStart;

    public string Name => "Comic Page";

    public ComicLayout Layout { get; private set; } = layout;
    public ComicPageOptions Options { get; private set; } = options;

    /// <summary>One entry per panel of the layout (null = empty panel).</summary>
    public IReadOnlyList<ComicPanelContent?> Contents => _contents;

    /// <summary>Selected panel, or -1.</summary>
    public int Selected { get; private set; } = layout.Panels.Count > 0 ? 0 : -1;

    /// <summary>The page as it will look (possibly smaller than the page), drawn over the document.</summary>
    public OverlayPicture? Preview { get; set; }

    /// <summary>Raised when the page changes: a panel's framing, photo or zoom, the layout or the options.</summary>
    public event Action? Changed;

    /// <summary>Raised when another panel is selected.</summary>
    public event Action? SelectionChanged;

    public IReadOnlyList<RectangleI> PanelRects => ComicPage.PanelRects(Layout, Options.Page, Options.Gutter);

    public int PanelAt(PointD point)
    {
        var rects = PanelRects;
        for (var i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            if (point.X >= r.X && point.X < r.X + r.Width && point.Y >= r.Y && point.Y < r.Y + r.Height)
                return i;
        }
        return -1;
    }

    /// <summary>Changes the layout; photos stay in the panels with the same number, extra ones are dropped.</summary>
    public void SetLayout(ComicLayout newLayout)
    {
        Layout = newLayout;
        while (_contents.Count < newLayout.Panels.Count)
            _contents.Add(null);
        Selected = Math.Min(Selected, newLayout.Panels.Count - 1);
        Changed?.Invoke();
        SelectionChanged?.Invoke();
    }

    public void SetOptions(ComicPageOptions newOptions)
    {
        Options = newOptions;
        Changed?.Invoke();
    }

    public void Select(int panel)
    {
        if (panel == Selected || panel < 0 || panel >= Layout.Panels.Count)
            return;
        Selected = panel;
        SelectionChanged?.Invoke();
    }

    /// <summary>Puts a photo (or nothing) in a panel, framed at its center.</summary>
    public void SetPhoto(int panel, BgraImage? photo)
    {
        if (panel < 0 || panel >= Layout.Panels.Count)
            return;
        while (_contents.Count <= panel)
            _contents.Add(null);
        _contents[panel] = photo is null ? null : new ComicPanelContent(photo);
        Changed?.Invoke();
    }

    /// <summary>Zoom of a panel's photo, 1 (the whole panel shape) to 4.</summary>
    public void SetZoom(int panel, double zoom)
    {
        if (panel < 0 || panel >= _contents.Count || _contents[panel] is not { } content)
            return;
        _contents[panel] = content with { Zoom = Math.Clamp(zoom, 1, 4) };
        Changed?.Invoke();
    }

    public void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        var panel = PanelAt(pointer.Position);
        if (panel < 0)
            return;
        Select(panel);
        if (panel < _contents.Count && _contents[panel] is { } content)
        {
            _dragged = panel;
            _dragStart = pointer.Position;
            _centerAtStart = content.Center;
        }
    }

    public void OnPointerMove(ImageDocument document, ToolPointer pointer)
    {
        if (_dragged < 0 || _contents[_dragged] is not { } content)
            return;
        var rect = PanelRects[_dragged];
        var photo = content.Photo;
        var area = ComicPage.VisibleArea(photo.Width, photo.Height, rect.Width, rect.Height, content.Zoom, _centerAtStart);
        // Dragging the photo to the right shows more of its left: the visible area moves the other way.
        var photoPerPage = area.Width / rect.Width;
        var dx = (pointer.Position.X - _dragStart.X) * photoPerPage / photo.Width;
        var dy = (pointer.Position.Y - _dragStart.Y) * photoPerPage / photo.Height;
        // Keep the center where the visible area can still reach, so dragging back responds at once.
        var (hx, hy) = (area.Width / 2 / photo.Width, area.Height / 2 / photo.Height);
        var center = new PointD(Math.Clamp(_centerAtStart.X - dx, hx, 1 - hx), Math.Clamp(_centerAtStart.Y - dy, hy, 1 - hy));
        _contents[_dragged] = content with { Center = center };
        Changed?.Invoke();
    }

    public void OnPointerUp(ImageDocument document, ToolPointer pointer)
    {
        OnPointerMove(document, pointer);
        _dragged = -1;
    }

    public ToolOverlay? GetOverlay(ImageDocument document)
    {
        var page = new RectangleD(0, 0, Options.Page.Width, Options.Page.Height);
        RectangleD? selected = null;
        if (Selected >= 0 && Selected < Layout.Panels.Count)
        {
            var r = PanelRects[Selected];
            selected = new RectangleD(r.X, r.Y, r.Width, r.Height);
        }
        return new ToolOverlay
        {
            Picture = Preview is { } preview && preview.Area == page ? preview : null,
            Frame = selected,
        };
    }

    public ToolCursor CursorAt(ImageDocument document, PointD point) =>
        PanelAt(point) is var panel and >= 0 && panel < _contents.Count && _contents[panel] is not null
            ? ToolCursor.Move
            : ToolCursor.Default;
}
