namespace CinnabarSharp.Vector;

/// <summary>What the rasterizer may use besides the SVG itself.</summary>
public sealed class RenderOptions
{
    /// <summary>Decodes <c>&lt;image&gt;</c> data; without one images are drawn as placeholders.</summary>
    public IImageDecoder? ImageDecoder { get; init; }

    /// <summary>Glyph outlines for <c>&lt;text&gt;</c>; without one text is drawn as placeholder boxes.</summary>
    public IGlyphOutlineProvider? GlyphProvider { get; init; }

    /// <summary>Folder of the SVG file: linked images are read from it and below. Null refuses every link.</summary>
    public string? BaseFolder { get; init; }

    public CancellationToken Cancellation { get; init; }

    /// <summary>Painted behind the drawing (the output is transparent where nothing is drawn when null).</summary>
    public VColor? Background { get; init; }
}

/// <summary>
/// Renders an SVG tree to straight-alpha BGRA pixels in pure C#, identical on every platform (the coverage rasterizer,
/// the stroker and the compositor use integer or plain double arithmetic only). Not supported, and drawn as nothing:
/// filters, markers, patterns, foreignObject, switch, animation. Unknown elements are skipped without failing.
/// </summary>
public static class VectorRasterizer
{
    /// <summary>
    /// Renders the part <paramref name="region"/> (device pixels of the picture shown at <paramref name="scale"/>, 1 = the
    /// picture's size at 96 dpi) into <paramref name="bgra"/> (region width × height × 4 bytes, overwritten).
    /// </summary>
    public static void Render(SvgRoot root, VRectI region, double scale, Span<byte> bgra, RenderOptions? options = null)
    {
        var pixels = Render(root, region, scale, options);
        pixels.CopyTo(bgra);
    }

    public static byte[] Render(SvgRoot root, VRectI region, double scale, RenderOptions? options = null)
    {
        if (region.IsEmpty)
            return [];
        var target = new RenderTarget(region.Width, region.Height);
        new Renderer(root, region, scale, options ?? new RenderOptions()).Run(target);
        return target.Pixels;
    }

    /// <summary>The whole picture at <paramref name="scale"/>; its size is the document's pixel size times the scale, rounded up.</summary>
    public static (byte[] Bgra, int Width, int Height) RenderAll(SvgRoot root, double scale, RenderOptions? options = null)
    {
        var (w, h) = root.PixelSize;
        var width = Math.Max(1, (int)Math.Ceiling(w * scale - 1e-9));
        var height = Math.Max(1, (int)Math.Ceiling(h * scale - 1e-9));
        return (Render(root, new VRectI(0, 0, width, height), scale, options), width, height);
    }

    private readonly record struct Context(Matrix2D Ctm, ComputedStyle ParentStyle, bool ClipMode);

    private sealed class Renderer
    {
        private const double Tolerance = 0.1;
        private const int MaxDepth = 24;

        private readonly SvgRoot _root;
        private readonly VRectI _region;
        private readonly double _scale;
        private readonly RenderOptions _options;
        private readonly int _width, _height;
        private readonly Dictionary<string, DecodedImage?> _images = [];
        private readonly HashSet<SvgElement> _active = [];
        private int _depth;

        public Renderer(SvgRoot root, VRectI region, double scale, RenderOptions options)
        {
            _root = root;
            _region = region;
            _scale = scale;
            _options = options;
            _width = region.Width;
            _height = region.Height;
        }

        public void Run(RenderTarget target)
        {
            if (_options.Background is { } background)
            {
                var p = target.Pixels;
                for (var i = 0; i < p.Length; i += 4)
                    (p[i], p[i + 1], p[i + 2], p[i + 3]) = (background.B, background.G, background.R, background.A);
            }
            var rootStyle = StyleResolver.Compute(_root, ComputedStyle.Initial);
            var ctm = Matrix2D.Translate(-_region.X, -_region.Y) * Matrix2D.Scale(_scale) * _root.UserToPixel;
            if (rootStyle.DisplayNone)
                return;
            RenderChildren(_root, new Context(ctm, rootStyle, false), target);
        }

        private void RenderChildren(SvgContainer container, Context context, RenderTarget target)
        {
            foreach (var child in container.Children)
                if (child is SvgElement element)
                    RenderElement(element, context, target);
        }

        private void RenderElement(SvgElement element, Context context, RenderTarget target)
        {
            _options.Cancellation.ThrowIfCancellationRequested();
            if (element is not (SvgShape or SvgGroup or SvgRoot or SvgUse or SvgImage or SvgText))
                return;
            if (_depth > MaxDepth)
                return;
            var style = StyleResolver.Compute(element, context.ParentStyle);
            if (style.DisplayNone)
                return;
            var ctm = context.Ctm * element.Transform;
            if (!ctm.IsInvertible && element is not SvgGroup)
                return;

            SvgClipPath? clip = style.ClipPathId is { } clipId ? _root.FindById(clipId) as SvgClipPath : null;
            SvgMask? mask = !context.ClipMode && style.MaskId is { } maskId ? _root.FindById(maskId) as SvgMask : null;
            var opacity = context.ClipMode ? 1 : style.Opacity;
            if (clip is null && mask is null && opacity >= 1)
            {
                DrawContent(element, style, ctm, context.ClipMode, target);
                return;
            }

            var layer = new RenderTarget(_width, _height);
            DrawContent(element, style, ctm, context.ClipMode, layer);
            byte[]? masks = null;
            VRect? bounds = null;
            if (clip is not null)
            {
                bounds = SvgBounds.Object(element, _options.GlyphProvider);
                masks = BuildClip(clip, ctm, bounds);
            }
            if (mask is not null)
            {
                bounds ??= SvgBounds.Object(element, _options.GlyphProvider);
                var m = BuildMask(mask, ctm, bounds);
                masks = masks is null ? m : Multiply(masks, m);
            }
            Compositor.DrawLayer(target, layer, (int)Math.Round(opacity * 255), masks);
        }

        private static byte[] Multiply(byte[] a, byte[] b)
        {
            var result = new byte[a.Length];
            for (var i = 0; i < a.Length; i++)
                result[i] = (byte)Compositor.Mul255(a[i], b[i]);
            return result;
        }

        private void DrawContent(SvgElement element, ComputedStyle style, Matrix2D ctm, bool clipMode, RenderTarget target)
        {
            switch (element)
            {
                case SvgShape shape:
                    PaintPath(shape.CreatePath(), shape, style, ctm, clipMode, target, placeholderOpacity: 1);
                    break;
                case SvgRoot nested:
                    DrawNestedSvg(nested, style, ctm, clipMode, target, null, null);
                    break;
                case SvgGroup group:
                    _depth++;
                    RenderChildren(group, new Context(ctm, style, clipMode), target);
                    _depth--;
                    break;
                case SvgUse use:
                    DrawUse(use, style, ctm, clipMode, target);
                    break;
                case SvgImage image when !clipMode:
                    DrawImage(image, style, ctm, target);
                    break;
                case SvgText text:
                    DrawText(text, style, ctm, clipMode, target);
                    break;
            }
        }

        // ---- Shapes ----

        private void PaintPath(VectorPath path, SvgElement owner, ComputedStyle style, Matrix2D ctm, bool clipMode,
            RenderTarget target, double placeholderOpacity)
        {
            if (path.IsEmpty || !style.Visible)
                return;
            var device = new VRect(0, 0, _width, _height);
            var scaleFactor = Math.Max(ctm.ScaleFactors.X, ctm.ScaleFactors.Y);
            var margin = style.Stroke.Kind != PaintKind.None && !clipMode
                ? style.StrokeWidth * scaleFactor * Math.Max(1.5, style.LineJoin == LineJoin.Miter ? style.MiterLimit : 1) / 2 + 2
                : 2;
            if (!ctm.TransformBounds(path.Bounds).Inflate(margin, margin).IntersectsWith(device))
                return;

            var clip = new VRectI(0, 0, _width, _height);
            if (clipMode)
            {
                var polygons = Flattener.Flatten(path, ctm, Tolerance);
                Compositor.Fill(target, PathCoverage.Fill(polygons, style.ClipRule, clip), new SolidPaint(VColor.White));
                return;
            }

            if (style.Fill.Kind != PaintKind.None)
            {
                if (MakePaint(style.Fill, style.FillOpacity * placeholderOpacity, owner, style, ctm) is { } fill)
                {
                    var polygons = Flattener.Flatten(path, ctm, Tolerance);
                    Compositor.Fill(target, PathCoverage.Fill(polygons, style.FillRule, clip), fill.Source, fill.Opacity);
                }
            }

            if (style.Stroke.Kind != PaintKind.None && style.StrokeWidth > 0
                && MakePaint(style.Stroke, style.StrokeOpacity * placeholderOpacity, owner, style, ctm) is { } stroke)
            {
                var userTolerance = Tolerance / Math.Max(scaleFactor, 1e-6);
                var subpaths = Flattener.Flatten(path, Matrix2D.Identity, userTolerance);
                var outline = Stroker.Stroke(subpaths, new StrokeStyle(style.StrokeWidth, style.LineCap, style.LineJoin,
                    style.MiterLimit, style.DashArray, style.DashOffset), userTolerance);
                foreach (var polygon in outline)
                    for (var i = 0; i < polygon.Points.Count; i++)
                        polygon.Points[i] = ctm.Transform(polygon.Points[i]);
                Compositor.Fill(target, PathCoverage.Fill(outline, FillRule.NonZero, clip), stroke.Source, stroke.Opacity);
            }
        }

        private readonly record struct PaintChoice(PaintSource Source, int Opacity);

        private PaintChoice? MakePaint(SvgPaint paint, double opacity, SvgElement owner, ComputedStyle style, Matrix2D ctm)
        {
            var alpha = (int)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
            switch (paint.Kind)
            {
                case PaintKind.Color:
                    return new PaintChoice(new SolidPaint(paint.Color), alpha);
                case PaintKind.CurrentColor:
                    return new PaintChoice(new SolidPaint(style.Color), alpha);
                case PaintKind.Url:
                {
                    if (_root.FindById(paint.Id) is SvgGradient gradient)
                        return GradientFor(gradient, owner, ctm) is { } source ? new PaintChoice(source, alpha) : null;
                    // A missing reference (or a pattern, which is not drawn) falls back to the alternative paint, if any.
                    return paint.Fallback is { } fallback ? MakePaint(fallback, opacity, owner, style, ctm) : null;
                }
                default:
                    return null;
            }
        }

        private PaintSource? GradientFor(SvgGradient gradient, SvgElement owner, Matrix2D ctm)
        {
            var stops = gradient.ResolvedStops().Select(s => (s.Offset, s.Color)).ToList();
            if (stops.Count == 0)
                return null;
            if (stops.Count == 1)
                return new SolidPaint(stops[0].Color);

            var toUser = Matrix2D.Identity;
            if (gradient.Units == GradientUnits.ObjectBoundingBox)
            {
                if (SvgBounds.Object(owner, _options.GlyphProvider) is not { } box || box.Width <= 0 || box.Height <= 0)
                    return null;
                toUser = Matrix2D.Translate(box.X, box.Y) * Matrix2D.Scale(box.Width, box.Height);
            }
            var toDevice = ctm * toUser * gradient.GradientTransform;
            if (toDevice.Invert() is not { } inverse)
                return null;
            var spread = gradient.Spread;
            switch (gradient)
            {
                case SvgLinearGradient linear:
                    return GradientPaint.Linear(stops, spread, inverse, new VPoint(linear.X1, linear.Y1), new VPoint(linear.X2, linear.Y2));
                case SvgRadialGradient radial:
                    if (radial.R <= 0)
                        return new SolidPaint(stops[^1].Color);
                    return GradientPaint.Radial(stops, spread, inverse, new VPoint(radial.Cx, radial.Cy), radial.R, new VPoint(radial.Fx, radial.Fy));
                default:
                    return null;
            }
        }

        // ---- Containers, use, nested svg ----

        private void DrawNestedSvg(SvgRoot svg, ComputedStyle style, Matrix2D ctm, bool clipMode, RenderTarget target,
            double? overrideWidth, double? overrideHeight)
        {
            var (pw, ph) = (_root.PercentBase(LengthAxis.X), _root.PercentBase(LengthAxis.Y));
            double Length(string name, double fallback, double percentBase) =>
                SvgLength.TryParse(svg.GetAttribute(name), out var l) ? l.ToUser(percentBase) : fallback;
            var x = Length("x", 0, pw);
            var y = Length("y", 0, ph);
            var width = overrideWidth ?? Length("width", pw, pw);
            var height = overrideHeight ?? Length("height", ph, ph);
            if (width <= 0 || height <= 0)
                return;
            var inner = Matrix2D.Translate(x, y);
            if (svg.ViewBox is { } box)
                inner *= svg.AspectRatio.ViewBoxTransform(box, width, height);
            var visible = svg.Style.Get("overflow") is "visible" or "auto";
            _depth++;
            if (visible)
                RenderChildren(svg, new Context(ctm * inner, style, clipMode), target);
            else
                DrawClipped(new VRect(x, y, width, height), ctm, target,
                    layer => RenderChildren(svg, new Context(ctm * inner, style, clipMode), layer));
            _depth--;
        }

        private void DrawClipped(VRect rect, Matrix2D ctm, RenderTarget target, Action<RenderTarget> draw)
        {
            var layer = new RenderTarget(_width, _height);
            draw(layer);
            var coverage = PathCoverage.Fill(Flattener.Flatten(VectorPath.FromRect(rect.X, rect.Y, rect.Width, rect.Height), ctm, Tolerance),
                FillRule.NonZero, new VRectI(0, 0, _width, _height));
            Compositor.DrawLayer(target, layer, 255, ToFullMask(coverage));
        }

        private byte[] ToFullMask(CoverageMask coverage)
        {
            var result = new byte[_width * _height];
            if (coverage.IsEmpty)
                return result;
            for (var y = 0; y < coverage.Area.Height; y++)
                Array.Copy(coverage.Data, y * coverage.Area.Width, result, (coverage.Area.Y + y) * _width + coverage.Area.X, coverage.Area.Width);
            return result;
        }

        private void DrawUse(SvgUse use, ComputedStyle style, Matrix2D ctm, bool clipMode, RenderTarget target)
        {
            if (use.Target is not { } referenced || !_active.Add(use))
                return;
            try
            {
                if (referenced == use || use.Ancestors().Contains(referenced))
                    return;
                _depth++;
                var moved = ctm * Matrix2D.Translate(use.X, use.Y);
                switch (referenced)
                {
                    case SvgSymbol symbol:
                        DrawSymbol(symbol, use, style, moved, clipMode, target);
                        break;
                    case SvgRoot svg:
                        DrawNestedSvg(svg, StyleResolver.Compute(svg, style), moved, clipMode, target, use.Width, use.Height);
                        break;
                    default:
                        RenderElement(referenced, new Context(moved, style, clipMode), target);
                        break;
                }
                _depth--;
            }
            finally
            {
                _active.Remove(use);
            }
        }

        private void DrawSymbol(SvgSymbol symbol, SvgUse use, ComputedStyle useStyle, Matrix2D ctm, bool clipMode, RenderTarget target)
        {
            var symbolStyle = StyleResolver.Compute(symbol, useStyle);
            var (pw, ph) = (_root.PercentBase(LengthAxis.X), _root.PercentBase(LengthAxis.Y));
            var width = use.Width ?? pw;
            var height = use.Height ?? ph;
            var inner = Matrix2D.Identity;
            if (symbol.ViewBox is { } box && width > 0 && height > 0)
                inner = symbol.AspectRatio.ViewBoxTransform(box, width, height);
            DrawClipped(new VRect(0, 0, width, height), ctm, target,
                layer => RenderChildren(symbol, new Context(ctm * inner, symbolStyle, clipMode), layer));
        }

        // ---- Clip paths and masks ----

        private byte[] BuildClip(SvgClipPath clip, Matrix2D elementCtm, VRect? bounds)
        {
            var empty = new byte[_width * _height];
            if (!_active.Add(clip))
                return empty;
            try
            {
                var matrix = elementCtm * clip.Transform;
                if (clip.UsesBoundingBox)
                {
                    if (bounds is not { } box || box.Width <= 0 || box.Height <= 0)
                        return empty;
                    matrix *= Matrix2D.Translate(box.X, box.Y) * Matrix2D.Scale(box.Width, box.Height);
                }
                var layer = new RenderTarget(_width, _height);
                var parentStyle = StyleResolver.ComputeFor(clip);
                _depth++;
                foreach (var child in clip.Elements)
                    if (child is SvgShape or SvgText or SvgUse)
                        RenderElement(child, new Context(matrix, parentStyle, true), layer);
                _depth--;
                var result = new byte[_width * _height];
                for (var i = 0; i < result.Length; i++)
                    result[i] = layer.Pixels[i * 4 + 3];
                // A clip path can itself be clipped.
                if (parentStyleClip(clip) is { } nested)
                    result = Multiply(result, BuildClip(nested, elementCtm, bounds));
                return result;
            }
            finally
            {
                _active.Remove(clip);
            }

            SvgClipPath? parentStyleClip(SvgClipPath c) =>
                StyleResolver.ComputeFor(c).ClipPathId is { } id && _root.FindById(id) is SvgClipPath other ? other : null;
        }

        private byte[] BuildMask(SvgMask mask, Matrix2D elementCtm, VRect? bounds)
        {
            var empty = new byte[_width * _height];
            if (!_active.Add(mask))
                return empty;
            try
            {
                VRect region;
                var (pw, ph) = (_root.PercentBase(LengthAxis.X), _root.PercentBase(LengthAxis.Y));
                if (mask.RegionUsesBoundingBox)
                {
                    if (bounds is not { } box || box.Width <= 0 || box.Height <= 0)
                        return empty;
                    double Fraction(string name, double fallback) =>
                        SvgLength.TryParse(mask.GetAttribute(name), out var l) ? (l.Unit == LengthUnit.Percent ? l.Value / 100 : l.Value) : fallback;
                    region = new VRect(box.X + Fraction("x", -0.1) * box.Width, box.Y + Fraction("y", -0.1) * box.Height,
                        Fraction("width", 1.2) * box.Width, Fraction("height", 1.2) * box.Height);
                }
                else
                {
                    double Length(string name, double fallback, double percentBase) =>
                        SvgLength.TryParse(mask.GetAttribute(name), out var l) ? l.ToUser(percentBase) : fallback;
                    region = new VRect(Length("x", -0.1 * pw, pw), Length("y", -0.1 * ph, ph), Length("width", 1.2 * pw, pw), Length("height", 1.2 * ph, ph));
                }
                var contentMatrix = elementCtm;
                if (mask.ContentUsesBoundingBox)
                {
                    if (bounds is not { } box || box.Width <= 0 || box.Height <= 0)
                        return empty;
                    contentMatrix *= Matrix2D.Translate(box.X, box.Y) * Matrix2D.Scale(box.Width, box.Height);
                }

                var layer = new RenderTarget(_width, _height);
                var parentStyle = StyleResolver.ComputeFor(mask);
                _depth++;
                RenderChildren(mask, new Context(contentMatrix, parentStyle, false), layer);
                _depth--;

                var result = new byte[_width * _height];
                var p = layer.Pixels;
                for (var i = 0; i < result.Length; i++)
                {
                    var a = p[i * 4 + 3];
                    if (a == 0)
                        continue;
                    // Luminance of the color (sRGB values, the SVG coefficients) times its alpha.
                    var luminance = (2125 * p[i * 4 + 2] + 7154 * p[i * 4 + 1] + 721 * p[i * 4] + 5000) / 10000;
                    result[i] = (byte)Compositor.Mul255(luminance, a);
                }
                var area = PathCoverage.Fill(
                    Flattener.Flatten(VectorPath.FromRect(region.X, region.Y, region.Width, region.Height), elementCtm, Tolerance),
                    FillRule.NonZero, new VRectI(0, 0, _width, _height));
                return Multiply(result, ToFullMask(area));
            }
            finally
            {
                _active.Remove(mask);
            }
        }

        // ---- Text ----

        private void DrawText(SvgText text, ComputedStyle style, Matrix2D ctm, bool clipMode, RenderTarget target)
        {
            var provider = _options.GlyphProvider;
            foreach (var run in TextLayout.Layout(text, style, provider))
                PaintPath(run.Outline, text, run.Style, ctm, clipMode, target, placeholderOpacity: provider is null ? 0.35 : 1);
        }

        // ---- Images ----

        private void DrawImage(SvgImage image, ComputedStyle style, Matrix2D ctm, RenderTarget target)
        {
            if (!style.Visible || image.Width <= 0 || image.Height <= 0)
                return;
            var bounds = image.Bounds;
            var decoded = LoadImage(image);
            if (decoded is null)
            {
                DrawImagePlaceholder(bounds, ctm, target);
                return;
            }

            // Fit the picture into the box (preserveAspectRatio), clip to the box.
            var fit = image.AspectRatio.ViewBoxTransform(new VRect(0, 0, decoded.Width, decoded.Height), bounds.Width, bounds.Height);
            var toDevice = ctm * Matrix2D.Translate(bounds.X, bounds.Y) * fit;
            if (toDevice.Invert() is not { } inverse)
                return;
            var covered = Flattener.Flatten(VectorPath.FromRect(0, 0, decoded.Width, decoded.Height), toDevice, Tolerance);
            var clipPolygons = Flattener.Flatten(VectorPath.FromRect(bounds.X, bounds.Y, bounds.Width, bounds.Height), ctm, Tolerance);
            var imageCoverage = PathCoverage.Fill(covered, FillRule.NonZero, new VRectI(0, 0, _width, _height));
            var boxCoverage = PathCoverage.Fill(clipPolygons, FillRule.NonZero, new VRectI(0, 0, _width, _height));
            if (imageCoverage.IsEmpty || boxCoverage.IsEmpty)
                return;
            const int opacity = 255; // the element's opacity is applied by its layer
            var area = imageCoverage.Area.Intersect(boxCoverage.Area);
            for (var y = area.Y; y < area.Bottom; y++)
            {
                for (var x = area.X; x < area.Right; x++)
                {
                    var coverage = Compositor.Mul255(imageCoverage.At(x, y), boxCoverage.At(x, y));
                    if (coverage == 0)
                        continue;
                    var source = inverse.Transform(new VPoint(x + 0.5, y + 0.5));
                    var color = Sample(decoded, source.X - 0.5, source.Y - 0.5);
                    var alpha = Compositor.Mul255(Compositor.Mul255(coverage, color.A), opacity);
                    Compositor.Blend(target.Pixels, (y * _width + x) * 4, color.B, color.G, color.R, alpha);
                }
            }
        }

        private DecodedImage? LoadImage(SvgImage image)
        {
            var href = image.Href;
            if (href is null)
                return null;
            if (_images.TryGetValue(href, out var cached))
                return cached;
            DecodedImage? decoded = null;
            if (_options.ImageDecoder is { } decoder && ImageResolver.Resolve(href, _options.BaseFolder) is { } bytes)
            {
                try
                {
                    decoded = decoder.Decode(bytes);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    decoded = null;
                }
                if (decoded is { Width: <= 0 } or { Height: <= 0 } || decoded is not null && decoded.Bgra.Length < decoded.Width * decoded.Height * 4)
                    decoded = null;
            }
            _images[href] = decoded;
            return decoded;
        }

        // Bilinear sampling of a straight-alpha picture, interpolating premultiplied values so edges do not fringe.
        private static VColor Sample(DecodedImage image, double x, double y)
        {
            var x0 = (int)Math.Floor(x);
            var y0 = (int)Math.Floor(y);
            var fx = x - x0;
            var fy = y - y0;
            double a = 0, r = 0, g = 0, b = 0;
            for (var j = 0; j < 2; j++)
            {
                for (var i = 0; i < 2; i++)
                {
                    var px = Math.Clamp(x0 + i, 0, image.Width - 1);
                    var py = Math.Clamp(y0 + j, 0, image.Height - 1);
                    var weight = (i == 0 ? 1 - fx : fx) * (j == 0 ? 1 - fy : fy);
                    var o = (py * image.Width + px) * 4;
                    var alpha = image.Bgra[o + 3] / 255.0;
                    a += weight * alpha;
                    b += weight * image.Bgra[o] * alpha;
                    g += weight * image.Bgra[o + 1] * alpha;
                    r += weight * image.Bgra[o + 2] * alpha;
                }
            }
            if (a <= 0)
                return VColor.Transparent;
            return new VColor((byte)Math.Clamp(b / a + 0.5, 0, 255), (byte)Math.Clamp(g / a + 0.5, 0, 255),
                (byte)Math.Clamp(r / a + 0.5, 0, 255), (byte)Math.Clamp(a * 255 + 0.5, 0, 255));
        }

        // A grey box with a cross: a missing, refused or undecodable image.
        private void DrawImagePlaceholder(VRect bounds, Matrix2D ctm, RenderTarget target)
        {
            var clip = new VRectI(0, 0, _width, _height);
            var grey = new SolidPaint(new VColor(190, 190, 190, 255));
            var line = new SolidPaint(new VColor(120, 120, 120, 255));
            Compositor.Fill(target, PathCoverage.Fill(Flattener.Flatten(
                VectorPath.FromRect(bounds.X, bounds.Y, bounds.Width, bounds.Height), ctm, Tolerance), FillRule.NonZero, clip), grey, 90);
            var cross = new VectorPath().MoveTo(bounds.X, bounds.Y).LineTo(bounds.Right, bounds.Bottom)
                .MoveTo(bounds.Right, bounds.Y).LineTo(bounds.X, bounds.Bottom);
            var outline = new VectorPath().MoveTo(bounds.X, bounds.Y).LineTo(bounds.Right, bounds.Y)
                .LineTo(bounds.Right, bounds.Bottom).LineTo(bounds.X, bounds.Bottom).Close();
            var scaleFactor = Math.Max(ctm.ScaleFactors.X, ctm.ScaleFactors.Y);
            var width = 1 / Math.Max(scaleFactor, 1e-6);
            foreach (var path in new[] { cross, outline })
            {
                var strokes = Stroker.Stroke(Flattener.Flatten(path, Matrix2D.Identity, 0.05 * width),
                    new StrokeStyle(width), 0.05 * width);
                foreach (var polygon in strokes)
                    for (var i = 0; i < polygon.Points.Count; i++)
                        polygon.Points[i] = ctm.Transform(polygon.Points[i]);
                Compositor.Fill(target, PathCoverage.Fill(strokes, FillRule.NonZero, clip), line);
            }
        }
    }
}
