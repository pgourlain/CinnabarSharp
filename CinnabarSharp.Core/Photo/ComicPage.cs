using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Photo;

/// <summary>A comic page layout: the panels as rectangles of the page, from 0 to 1 (gutters are added later).</summary>
public sealed record ComicLayout(string Name, IReadOnlyList<RectangleD> Panels);

/// <summary>A page size for comic pages.</summary>
public sealed record ComicPageFormat(string Name, ImageSize Size);

/// <summary>
/// Page, gutter between panels (also the outer margin), panel border and page background. Sizes are in pixels of the
/// page.
/// </summary>
public sealed record ComicPageOptions(ImageSize Page, int Gutter, int BorderWidth, ColorBgra Border, ColorBgra Background);

/// <summary>
/// A photo in a panel: <paramref name="Zoom"/> 1 shows the largest part of the photo with the panel's shape, higher
/// values show less of it; <paramref name="Center"/> is the center of the visible part, from 0 to 1 of the photo.
/// </summary>
public sealed record ComicPanelContent(BgraImage Photo, double Zoom, PointD Center)
{
    public ComicPanelContent(BgraImage photo) : this(photo, 1, new PointD(0.5, 0.5))
    {
    }

    /// <summary>The whole photo is stretched to the panel (its proportions change); zoom and center are ignored.</summary>
    public bool Stretch { get; init; }
}

/// <summary>Assembles photos into one image like a comic page: panels, gutters, borders.</summary>
public static class ComicPage
{
    private static RectangleD R(double x, double y, double w, double h) => new(x, y, w, h);

    public static IReadOnlyList<ComicLayout> Layouts { get; } =
    [
        new("1 panel", [R(0, 0, 1, 1)]),
        new("2 rows", [R(0, 0, 1, 0.5), R(0, 0.5, 1, 0.5)]),
        new("2 columns", [R(0, 0, 0.5, 1), R(0.5, 0, 0.5, 1)]),
        new("2 × 2 grid", [R(0, 0, 0.5, 0.5), R(0.5, 0, 0.5, 0.5), R(0, 0.5, 0.5, 0.5), R(0.5, 0.5, 0.5, 0.5)]),
        new("3 rows", [R(0, 0, 1, 1 / 3.0), R(0, 1 / 3.0, 1, 1 / 3.0), R(0, 2 / 3.0, 1, 1 / 3.0)]),
        new("1 large + 2 small", [R(0, 0, 1, 0.6), R(0, 0.6, 0.5, 0.4), R(0.5, 0.6, 0.5, 0.4)]),
        new("2 small + 1 large", [R(0, 0, 0.5, 0.4), R(0.5, 0, 0.5, 0.4), R(0, 0.4, 1, 0.6)]),
        new("1 tall + 2 stacked", [R(0, 0, 0.5, 1), R(0.5, 0, 0.5, 0.5), R(0.5, 0.5, 0.5, 0.5)]),
        new("Classic (2 + 1 + 2)",
        [
            R(0, 0, 0.5, 1 / 3.0), R(0.5, 0, 0.5, 1 / 3.0),
            R(0, 1 / 3.0, 1, 1 / 3.0),
            R(0, 2 / 3.0, 0.5, 1 / 3.0), R(0.5, 2 / 3.0, 0.5, 1 / 3.0),
        ]),
        new("3 × 3 grid",
        [
            .. Enumerable.Range(0, 9).Select(i => R(i % 3 / 3.0, i / 3 / 3.0, 1 / 3.0, 1 / 3.0)),
        ]),
    ];

    public static IReadOnlyList<ComicPageFormat> Formats { get; } =
    [
        new("A4 portrait (2480 × 3508)", new ImageSize(2480, 3508)),
        new("A4 landscape (3508 × 2480)", new ImageSize(3508, 2480)),
        new("Square (3000 × 3000)", new ImageSize(3000, 3000)),
        new("16:9 TV 4K (3840 × 2160)", new ImageSize(3840, 2160)),
    ];

    /// <summary>
    /// The panels in pixels of the page: the gutter separates the panels and is also the margin around them.
    /// </summary>
    public static IReadOnlyList<RectangleI> PanelRects(ComicLayout layout, ImageSize page, int gutter)
    {
        const double eps = 1e-6;
        var g = Math.Max(0, gutter);
        var (cw, ch) = (page.Width - 2.0 * g, page.Height - 2.0 * g);
        return layout.Panels.Select(p =>
        {
            var left = g + p.X * cw + (p.X > eps ? g / 2.0 : 0);
            var top = g + p.Y * ch + (p.Y > eps ? g / 2.0 : 0);
            var right = g + (p.X + p.Width) * cw - (p.X + p.Width < 1 - eps ? g / 2.0 : 0);
            var bottom = g + (p.Y + p.Height) * ch - (p.Y + p.Height < 1 - eps ? g / 2.0 : 0);
            var (x0, y0) = ((int)Math.Round(left), (int)Math.Round(top));
            return new RectangleI(x0, y0, Math.Max(1, (int)Math.Round(right) - x0), Math.Max(1, (int)Math.Round(bottom) - y0));
        }).ToList();
    }

    /// <summary>
    /// The part of a photo shown in a panel: the largest area with the panel's shape, divided by the zoom, centered on
    /// <paramref name="center"/> (0-1 of the photo) and kept inside the photo.
    /// </summary>
    public static RectangleD VisibleArea(int photoWidth, int photoHeight, int panelWidth, int panelHeight, double zoom, PointD center)
    {
        var ratio = (double)panelWidth / panelHeight;
        var (w, h) = (double)photoWidth / photoHeight > ratio
            ? (photoHeight * ratio, (double)photoHeight)
            : ((double)photoWidth, photoWidth / ratio);
        zoom = Math.Max(1, zoom);
        (w, h) = (w / zoom, h / zoom);
        var x = Math.Clamp(center.X * photoWidth - w / 2, 0, photoWidth - w);
        var y = Math.Clamp(center.Y * photoHeight - h / 2, 0, photoHeight - h);
        return new RectangleD(x, y, w, h);
    }

    /// <summary>The part of the content's photo drawn in a panel: the whole photo when stretched, else <see cref="VisibleArea"/>.</summary>
    public static RectangleD SourceArea(ComicPanelContent content, int panelWidth, int panelHeight) =>
        content.Stretch
            ? new RectangleD(0, 0, content.Photo.Width, content.Photo.Height)
            : VisibleArea(content.Photo.Width, content.Photo.Height, panelWidth, panelHeight, content.Zoom, content.Center);

    /// <summary>
    /// The page: background, each photo cropped to fill its panel or stretched to it (Lanczos), and panel borders. Panels without a
    /// photo stay empty with their border. <paramref name="size"/> renders the same page smaller (a preview).
    /// </summary>
    public static BgraImage Compose(ComicLayout layout, ComicPageOptions options, IReadOnlyList<ComicPanelContent?> contents,
        ImageSize? size = null, CancellationToken cancellation = default)
    {
        var page = options.Page;
        var target = size ?? page;
        var scale = (double)target.Width / page.Width;
        var result = new byte[target.Width * target.Height * 4];
        Fill(result, target.Width, new RectangleI(0, 0, target.Width, target.Height), options.Background);

        var rects = PanelRects(layout, page, options.Gutter);
        var border = options.BorderWidth <= 0 ? 0 : Math.Max(1, (int)Math.Round(options.BorderWidth * scale));
        for (var i = 0; i < rects.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var r = Scale(rects[i], scale);
            if (i < contents.Count && contents[i] is { } content)
            {
                var photo = content.Photo;
                var area = SourceArea(content, rects[i].Width, rects[i].Height);
                var crop = new RectangleI((int)Math.Round(area.X), (int)Math.Round(area.Y),
                    Math.Max(1, (int)Math.Round(area.Width)), Math.Max(1, (int)Math.Round(area.Height)));
                crop = crop with
                {
                    Width = Math.Min(crop.Width, photo.Width - crop.X),
                    Height = Math.Min(crop.Height, photo.Height - crop.Y),
                };
                var part = new BgraImage(PixelRegion.Extract(photo.Pixels, photo.Width, crop), crop.Width, crop.Height);
                var pixels = TvExport.Resize(part, r.Width, r.Height);
                PixelRegion.Place(result, target.Width, target.Height, pixels, r.Width, r.Height, r.X, r.Y, composite: true);
            }
            if (border > 0)
            {
                Fill(result, target.Width, r with { Height = Math.Min(border, r.Height) }, options.Border);
                Fill(result, target.Width, r with { Y = r.Y + r.Height - Math.Min(border, r.Height), Height = Math.Min(border, r.Height) }, options.Border);
                Fill(result, target.Width, r with { Width = Math.Min(border, r.Width) }, options.Border);
                Fill(result, target.Width, r with { X = r.X + r.Width - Math.Min(border, r.Width), Width = Math.Min(border, r.Width) }, options.Border);
            }
        }
        return new BgraImage(result, target.Width, target.Height);
    }

    private static RectangleI Scale(RectangleI r, double scale)
    {
        if (scale == 1)
            return r;
        var (x0, y0) = ((int)Math.Round(r.X * scale), (int)Math.Round(r.Y * scale));
        return new RectangleI(x0, y0, Math.Max(1, (int)Math.Round((r.X + r.Width) * scale) - x0),
            Math.Max(1, (int)Math.Round((r.Y + r.Height) * scale) - y0));
    }

    private static void Fill(byte[] bgra, int width, RectangleI r, ColorBgra color)
    {
        for (var y = r.Y; y < r.Y + r.Height; y++)
            for (var x = r.X; x < r.X + r.Width; x++)
            {
                var i = (y * width + x) * 4;
                (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (color.B, color.G, color.R, color.A);
            }
    }
}
