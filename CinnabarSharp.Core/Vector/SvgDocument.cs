using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Vector;
using Microsoft.Extensions.Logging;

namespace CinnabarSharp.Core.Vector;

/// <summary>Event for a change to one node: <see cref="DocumentEventEnum.VectorNodeChanged"/>.</summary>
public record VectorNodeEventItem : EventItem<DocumentEventEnum>
{
    public VectorNodeEventItem(SvgDocument document, DocumentEventEnum state, SvgNode node, VRect? dirtyBounds)
        : base(document, state)
    {
        Node = node;
        DirtyBounds = dirtyBounds;
    }

    public SvgNode Node { get; }

    /// <summary>Area of the user space that needs redrawing (node bounds before and after the change), or null for "unknown".</summary>
    public VRect? DirtyBounds { get; }
}

/// <summary>
/// A vector drawing: the <see cref="SvgRoot"/> tree plus what a tab needs (file, dirty state, zoom, history, selection).
/// Edits go through <see cref="Actions"/>; the user space is the root's viewBox, shown at 96 dpi times zoom.
/// </summary>
public sealed class SvgDocument : IDocument
{
    private readonly IDocumentEventsService _events;
    private SvgRoot _root;
    private bool _isDirty;
    private string _displayName = string.Empty;
    private ImageFile? _file;

    public SvgDocument(IDocumentEventsService events, ILogger<SvgDocument> logger, IHistoryStorage? historyStorage = null,
        IGlyphOutlineProvider? glyphProvider = null)
    {
        GlyphProvider = glyphProvider;
        _events = events;
        _root = new SvgRoot();
        Selection = new SvgSelection(this);
        Actions = new SvgActions(this);
        Workspace = new ImageDocumentWorkspace(this, new ImageDocumentHistory(this, events, storage: historyStorage), events, logger);
    }

    public Guid Id { get; } = Guid.NewGuid();

    public DocumentKind Kind => DocumentKind.Svg;

    public SvgRoot Root => _root;

    /// <summary>User-level edits that are recorded in the undo history: the only way to change the drawing.</summary>
    public SvgActions Actions { get; }

    /// <summary>The selected objects.</summary>
    public SvgSelection Selection { get; }

    public ImageDocumentWorkspace Workspace { get; }

    public IImageDocumentHistory History => Workspace.History;

    /// <summary>Replaces the drawing (opening a file); the size and the tree events follow.</summary>
    public void Attach(SvgRoot root)
    {
        _root = root;
        Selection.Clear();
        Workspace.UpdateViewSize();
        NotifyTreeChanged();
    }

    // ---- IDocument ----

    public string DisplayName
    {
        get => _displayName;
        set
        {
            _displayName = value;
            _events.PushEvent(new DocumentEventItem(this, DocumentEventEnum.DocumentRenamed));
        }
    }

    public ImageFile? File
    {
        get => _file;
        set
        {
            _file = value;
            DisplayName = value?.Name ?? string.Empty;
        }
    }

    public string? FileType { get; set; }

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty == value)
                return;
            _isDirty = value;
            _events.PushEvent(new DocumentEventItem(this, DocumentEventEnum.DirtyChanged));
        }
    }

    /// <summary>Pixel size of the picture at 100 % zoom (96 dpi): the root's width and height, rounded up.</summary>
    public ImageSize ImageSize
    {
        get
        {
            var (w, h) = _root.PixelSize;
            return new ImageSize(Math.Max(1, (int)Math.Ceiling(w - 1e-9)), Math.Max(1, (int)Math.Ceiling(h - 1e-9)));
        }
        set
        {
            // Changing the size keeps the drawing: the viewBox is set first so the content scales with the page.
            if (_root.ViewBox is null)
            {
                var (w, h) = _root.UserSize;
                _root.ViewBox = new VRect(0, 0, w, h);
            }
            _root.Width = SvgLength.Px(value.Width);
            _root.Height = SvgLength.Px(value.Height);
            Workspace.UpdateViewSize();
            Workspace.Invalidate();
            NotifyTreeChanged();
        }
    }

    public void Close()
    {
        Selection.Clear();
        Workspace.History.Clear();
    }

    /// <summary>Options for the rasterizer: the glyph provider and the folder for linked images (set by the desktop app).</summary>
    public RenderOptions RenderOptions => new()
    {
        ImageDecoder = ImageDecoder,
        GlyphProvider = GlyphProvider,
        BaseFolder = _file?.DirectoryName,
    };

    /// <summary>Decodes embedded and linked pictures; Magick.NET by default.</summary>
    public IImageDecoder ImageDecoder { get; set; } = new MagickImageDecoder();

    /// <summary>Fonts for text; null in headless mode, where text is drawn as boxes.</summary>
    public IGlyphOutlineProvider? GlyphProvider { get; set; }

    public (byte[] Bgra, int Width, int Height) GetThumbnail(int maxSide)
    {
        var size = ImageSize;
        var scale = Math.Min(1.0, maxSide / (double)Math.Max(size.Width, size.Height));
        return VectorRasterizer.RenderAll(_root, scale, RenderOptions);
    }

    // ---- Coordinates ----

    /// <summary>Maps user-space coordinates (the viewBox) to picture pixels at 100 %.</summary>
    public Matrix2D UserToImage => _root.UserToPixel;

    /// <summary>The picture pixel (image coordinates) under a point of the user space.</summary>
    public VPoint UserToImagePoint(VPoint p) => UserToImage.Transform(p);

    /// <summary>The user-space point under a picture pixel (what tools receive pointer positions as).</summary>
    public VPoint ImageToUserPoint(VPoint p) => (UserToImage.Invert() ?? Matrix2D.Identity).Transform(p);

    /// <summary>How many user units one picture pixel at 100 % is (so a 5 pixel handle is <c>PixelsToUser(5 / zoom)</c>).</summary>
    public double PixelsToUser(double pixels) => pixels / Math.Max(UserToImage.MeanScale, 1e-9);

    /// <summary>User units that are <paramref name="screenPixels"/> long on the screen at the current zoom (hit tolerances, handle sizes).</summary>
    public double ScreenToUser(double screenPixels) => PixelsToUser(screenPixels / Math.Max(Workspace.Scale, 1e-9));

    // ---- Events ----

    internal void NotifyTreeChanged() =>
        _events.PushEvent(new DocumentEventItem(this, DocumentEventEnum.VectorTreeChanged));

    internal void NotifySelectionChanged() =>
        _events.PushEvent(new DocumentEventItem(this, DocumentEventEnum.VectorSelectionChanged));

    internal void NotifyNodeChanged(SvgNode node, VRect? dirtyBounds) =>
        _events.PushEvent(new VectorNodeEventItem(this, DocumentEventEnum.VectorNodeChanged, node, dirtyBounds));
}
