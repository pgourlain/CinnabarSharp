using System.Linq;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Photo;

namespace CinnabarSharp.Core.Tools;

/// <summary>
/// Edits a comic page on its document: click a panel to select it, drag inside it to move the photo in the panel, drag
/// the gutter between panels to resize them.
/// The page itself is drawn from <see cref="Preview"/> (computed by the caller with <see cref="ComicPage.Compose"/>);
/// the document's pixels only change when the caller applies it. Not in the toolbox: the Page de BD mode uses it.
/// </summary>
public sealed class ComicPageTool(ComicLayout layout, ComicPageOptions options, IEnumerable<ComicPanelContent?> contents) : IOverlayTool
{
    private readonly List<ComicPanelContent?> _contents = [.. contents];
    private int _dragged = -1;
    private PointD _dragStart;
    private PointD _centerAtStart;
    private Divider? _divider;

    /// <summary>The smallest width or height a panel keeps when its divider is dragged, as a fraction of the page.</summary>
    public const double MinPanelSize = 0.05;

    /// <summary>A line shared by panels on both sides, in layout units (0-1): <paramref name="Before"/> end on it, <paramref name="After"/> start on it.</summary>
    private sealed record Divider(bool Vertical, double Position, int[] Before, int[] After);

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

    /// <summary>Raised on a double click on a panel (the caller lets the user pick another photo for it).</summary>
    public event Action<int>? PanelActivated;

    public IReadOnlyList<RectangleI> PanelRects => ComicPage.PanelRects(Layout, Options.Page, Options.Gutter, Options.Overlap);

    public int PanelAt(PointD point)
    {
        var rects = PanelRects;
        // The overlay panel is drawn last, so it is the first to get the click.
        foreach (var i in Enumerable.Range(0, rects.Count).OrderBy(i => i == Layout.OverlayPanel ? 0 : 1))
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

    /// <summary>Stretches a panel's whole photo to the panel (proportions change), or crops it to fill (the default).</summary>
    public void SetStretch(int panel, bool stretch)
    {
        if (panel < 0 || panel >= _contents.Count || _contents[panel] is not { } content || content.Stretch == stretch)
            return;
        _contents[panel] = content with { Stretch = stretch };
        Changed?.Invoke();
    }

    public void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        if (pointer.ClickCount < 2 && DividerAt(pointer.Position) is { } divider)
        {
            _divider = divider;
            _dragged = -1;
            return;
        }
        var panel = PanelAt(pointer.Position);
        if (panel < 0)
            return;
        Select(panel);
        if (pointer.ClickCount >= 2)
        {
            _dragged = -1;
            PanelActivated?.Invoke(panel);
            return;
        }
        // A stretched photo shows all of itself: nothing to move.
        if (panel < _contents.Count && _contents[panel] is { Stretch: false } content)
        {
            _dragged = panel;
            _dragStart = pointer.Position;
            _centerAtStart = content.Center;
        }
    }

    public void OnPointerMove(ImageDocument document, ToolPointer pointer)
    {
        if (_divider is { } divider)
        {
            MoveDivider(divider, pointer.Position);
            return;
        }
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
        _divider = null;
    }

    // ---- Dividers: the lines between panels ----

    private const double Eps = 1e-6;

    /// <summary>The page area the panels share, in pixels: the page minus the margin (the gutter).</summary>
    private (double Left, double Top, double Width, double Height) Content
    {
        get
        {
            var g = Math.Max(0, Options.Gutter);
            return (g, g, Options.Page.Width - 2.0 * g, Options.Page.Height - 2.0 * g);
        }
    }

    /// <summary>The panels' ranges along the other axis touch or overlap: their sides on a line form one stretch.</summary>
    private static bool Overlaps(RectangleD a, RectangleD b, bool vertical) => vertical
        ? a.Y <= b.Y + b.Height + Eps && b.Y <= a.Y + a.Height + Eps
        : a.X <= b.X + b.Width + Eps && b.X <= a.X + a.Width + Eps;

    /// <summary>The divider under a point (image coordinates): between panels, never over the overlay panel.</summary>
    private Divider? DividerAt(PointD point)
    {
        var panels = Layout.Panels;
        var (left, top, width, height) = Content;
        var tolerance = Math.Max(Math.Max(0, Options.Gutter) / 2.0, Options.Page.Width * 0.003);
        if (Layout.OverlayPanel is { } overlay && overlay < panels.Count && PanelRects[overlay] is var o
            && point.X >= o.X && point.X < o.X + o.Width && point.Y >= o.Y && point.Y < o.Y + o.Height)
            return null;
        foreach (var vertical in new[] { true, false })
        {
            double Start(RectangleD r) => vertical ? r.X : r.Y;
            double End(RectangleD r) => vertical ? r.X + r.Width : r.Y + r.Height;
            var (origin, extent) = vertical ? (left, width) : (top, height);
            var along = vertical ? (point.Y - top) / height : (point.X - left) / width;
            var across = vertical ? point.X : point.Y;
            foreach (var position in panels.Select(End).Where(e => e > Eps && e < 1 - Eps).Distinct())
            {
                if (Math.Abs(across - (origin + position * extent)) > tolerance)
                    continue;
                // Panels ending on the line and panels starting on it, around the pointer.
                bool Around(RectangleD r) => along >= (vertical ? r.Y : r.X) - Eps && along <= (vertical ? r.Y + r.Height : r.X + r.Width) + Eps;
                var beforeHit = Enumerable.Range(0, panels.Count).Where(i => i != Layout.OverlayPanel && Math.Abs(End(panels[i]) - position) < Eps && Around(panels[i])).ToList();
                var afterHit = Enumerable.Range(0, panels.Count).Where(i => i != Layout.OverlayPanel && Math.Abs(Start(panels[i]) - position) < Eps && Around(panels[i])).ToList();
                if (beforeHit.Count == 0 || afterHit.Count == 0)
                    continue;
                // Follow the line: every panel touching it along the same stretch moves with it.
                var before = new HashSet<int>(beforeHit);
                var after = new HashSet<int>(afterHit);
                for (var changed = true; changed;)
                {
                    changed = false;
                    for (var i = 0; i < panels.Count; i++)
                    {
                        var touchesBefore = Math.Abs(End(panels[i]) - position) < Eps;
                        var touchesAfter = Math.Abs(Start(panels[i]) - position) < Eps;
                        if (!touchesBefore && !touchesAfter)
                            continue;
                        var linked = before.Concat(after).Any(j => j != i && Overlaps(panels[i], panels[j], vertical));
                        if (linked && (touchesBefore ? before.Add(i) : after.Add(i)))
                            changed = true;
                    }
                }
                return new Divider(vertical, position, [.. before], [.. after]);
            }
        }
        return null;
    }

    private void MoveDivider(Divider divider, PointD point)
    {
        var (left, top, width, height) = Content;
        var (origin, extent) = divider.Vertical ? (left, width) : (top, height);
        var wanted = ((divider.Vertical ? point.X : point.Y) - origin) / extent;
        var panels = Layout.Panels;
        double Start(RectangleD r) => divider.Vertical ? r.X : r.Y;
        double End(RectangleD r) => divider.Vertical ? r.X + r.Width : r.Y + r.Height;
        // Every panel keeps a minimum size on both sides.
        var min = divider.Before.Max(i => Start(panels[i])) + MinPanelSize;
        var max = divider.After.Min(i => End(panels[i])) - MinPanelSize;
        if (min > max)
            return;
        var position = Math.Clamp(wanted, min, max);
        if (Math.Abs(position - divider.Position) < Eps && panels.Count > 0 && divider.Before.All(i => Math.Abs(End(panels[i]) - position) < Eps))
            return;
        var moved = panels.ToList();
        foreach (var i in divider.Before)
            moved[i] = divider.Vertical ? moved[i] with { Width = position - moved[i].X } : moved[i] with { Height = position - moved[i].Y };
        foreach (var i in divider.After)
            moved[i] = divider.Vertical
                ? moved[i] with { X = position, Width = End(moved[i]) - position }
                : moved[i] with { Y = position, Height = End(moved[i]) - position };
        Layout = Layout with { Panels = moved };
        _divider = divider with { Position = position };
        Changed?.Invoke();
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
            EmphasizeFrame = true,
        };
    }

    public ToolCursor CursorAt(ImageDocument document, PointD point) =>
        DividerAt(point) is { } divider
            ? divider.Vertical ? ToolCursor.ResizeHorizontal : ToolCursor.ResizeVertical
            : PanelAt(point) is var panel and >= 0 && panel < _contents.Count && _contents[panel] is { Stretch: false }
                ? ToolCursor.Move
                : ToolCursor.Default;
}
