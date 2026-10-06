using System.ComponentModel;
using System.Text.Json;
using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Photo;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Tools;
using ImageMagick;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PointD = CinnabarSharp.Core.Models.PointD;

namespace CinnabarSharp.Mcp;

/// <summary>
/// The MCP tools. Every edit goes through <see cref="DocumentActions"/> (or an <see cref="EffectSession"/>), so it is
/// one undoable history step, like the same command in the window.
/// </summary>
[McpServerToolType]
public sealed class ImageTools(McpContext context)
{
    private const string DocumentHelp = "Id or name of an open image (see list_documents); the active image when omitted.";
    private const string LayerHelp = "Layer index, 0 = bottom layer; the current layer when omitted.";
    private const string ModeHelp = "How it combines with the current selection: Replace (default), Union, Exclude, Xor, Intersect.";

    // ---------------------------------------------------------------- Files

    [McpServerTool(Name = "open_image"), Description(
        "Opens an image file (PNG, JPEG, BMP, GIF, TIFF, WebP, HEIC, ORA) as a new document and makes it active. " +
        "Returns the document's id, size and layers. An image that is already open is just activated.")]
    public Task<DocumentInfo> OpenImage([Description("Path of the file, absolute or relative to the first allowed folder.")] string path) =>
        context.Run(() =>
        {
            var file = new FileInfo(context.Files.Resolve(path));
            if (!file.Exists)
                throw new McpException($"'{path}' does not exist.");
            return Describe.Document(context, context.Formats.Open(file));
        });

    [McpServerTool(Name = "new_image"), Description("Creates a new single-layer image and makes it active.")]
    public Task<DocumentInfo> NewImage(
        [Description("Width in pixels.")] int width,
        [Description("Height in pixels.")] int height,
        [Description("Background: #RRGGBB, #RRGGBBAA, white (default), black or transparent.")] string? background = null) =>
        context.Run(() =>
        {
            CheckSize(width, height);
            return Describe.Document(context, context.Workspace.NewDocument(new ImageSize(width, height),
                Parse.Color(background, ColorBgra.White)));
        });

    [McpServerTool(Name = "save_image"), Description(
        "Saves the image to a file, which becomes the image's file (like File › Save / Save As). The format comes from " +
        "'format' or the extension; only ORA keeps layers, other formats save the flattened image. Refuses to replace an " +
        "existing file unless overwrite is true.")]
    public Task<SavedFile> SaveImage(
        [Description(DocumentHelp)] string? document = null,
        [Description("Destination path; the image's own file when omitted.")] string? path = null,
        [Description("Format name or extension (png, jpg, bmp, gif, tiff, webp, ora); from the extension when omitted.")] string? format = null,
        [Description("JPEG quality 1-100 (default 90).")] int? jpegQuality = null,
        [Description("Must be true to replace an existing file, including the image's own file.")] bool overwrite = false) =>
        context.Run(() =>
        {
            var doc = context.Document(document);
            var target = path ?? doc.File?.FullName
                ?? throw new McpException("This image has never been saved: give a path.");
            var (file, imageFormat) = Destination(target, format, overwrite);
            WithJpegQuality(imageFormat, jpegQuality, () => context.Formats.Save(doc, file, imageFormat));
            return Saved(file, imageFormat);
        });

    [McpServerTool(Name = "export_image"), Description(
        "Writes a copy of the image (flattened, except ORA) to a file without changing the image's own file or its " +
        "unsaved-changes state. Refuses to replace an existing file unless overwrite is true.")]
    public Task<SavedFile> ExportImage(
        [Description("Destination path.")] string path,
        [Description(DocumentHelp)] string? document = null,
        [Description("Format name or extension (png, jpg, bmp, gif, tiff, webp, ora); from the extension when omitted.")] string? format = null,
        [Description("JPEG quality 1-100 (default 90).")] int? jpegQuality = null,
        [Description("Must be true to replace an existing file.")] bool overwrite = false) =>
        context.Run(() =>
        {
            var doc = context.Document(document);
            var (file, imageFormat) = Destination(path, format, overwrite);
            WithJpegQuality(imageFormat, jpegQuality, () => imageFormat.Export(doc, file));
            file.Refresh();
            return Saved(file, imageFormat);
        });

    [McpServerTool(Name = "close_image"), Description(
        "Closes an image. Refuses if it has unsaved changes unless discardChanges is true.")]
    public Task<IReadOnlyList<DocumentInfo>> CloseImage(
        [Description(DocumentHelp)] string? document = null,
        [Description("Must be true to close an image with unsaved changes (they are lost).")] bool discardChanges = false) =>
        context.Run(() =>
        {
            var doc = context.AnyDocument(document);
            if (doc.IsDirty && !discardChanges)
                throw new McpException($"'{doc.DisplayName}' has unsaved changes. Save it, or pass discardChanges=true to lose them.");
            context.Workspace.CloseDocument(doc);
            return Documents();
        });

    // ---------------------------------------------------------------- Inspecting

    [McpServerTool(Name = "list_documents", ReadOnly = true), Description("Lists the open images with their ids, sizes, layers and selection.")]
    public Task<IReadOnlyList<DocumentInfo>> ListDocuments() => context.Run(Documents);

    [McpServerTool(Name = "get_image_info", ReadOnly = true), Description(
        "Size, file, layers, selection and undo state of an image, optionally with a histogram of the flattened image " +
        "(red, green, blue and luminosity counts, mean luminosity, clipped shadows/highlights).")]
    public Task<ImageInfo> GetImageInfo(
        [Description(DocumentHelp)] string? document = null,
        [Description("Include the histogram.")] bool includeHistogram = false,
        [Description("Number of histogram bins, 1-256 (default 16).")] int histogramBins = 16) =>
        context.Run(() =>
        {
            var doc = context.Document(document);
            return new ImageInfo(Describe.Document(context, doc), includeHistogram ? Describe.Histogram(doc, histogramBins) : null);
        });

    [McpServerTool(Name = "render_preview", ReadOnly = true), Description(
        "Returns a PNG preview of the image as it looks (all visible layers), scaled down to fit maxSize, so you can " +
        "check the result of an edit.")]
    public async Task<IEnumerable<ContentBlock>> RenderPreview(
        [Description(DocumentHelp)] string? document = null,
        [Description("Longest side of the preview in pixels, 16-2048 (default 768).")] int maxSize = 768)
    {
        var (bgra, width, height, name) = await context.Run(() =>
        {
            var doc = context.Document(document);
            return (doc.Layers.GetFlattenedBgra(includeToolLayer: false), doc.ImageSize.Width, doc.ImageSize.Height, doc.DisplayName);
        });
        var (png, w, h) = Preview(bgra, width, height, Math.Clamp(maxSize, 16, 2048));
        return
        [
            ImageContentBlock.FromBytes(png, "image/png"),
            new TextContentBlock { Text = $"{name}: {width}×{height}, preview {w}×{h}." },
        ];
    }

    [McpServerTool(Name = "get_history", ReadOnly = true), Description("The image's undo history; 'current' marks the state the image is in.")]
    public Task<IReadOnlyList<HistoryStep>> GetHistory([Description(DocumentHelp)] string? document = null) =>
        context.Run(() => Describe.History(context.Document(document)));

    // ---------------------------------------------------------------- History

    [McpServerTool(Name = "undo"), Description("Undoes the last steps of an image.")]
    public Task<DocumentInfo> Undo([Description(DocumentHelp)] string? document = null,
        [Description("Number of steps (default 1).")] int steps = 1) =>
        Edit(document, doc =>
        {
            for (var i = 0; i < steps && doc.Workspace.History.CanUndo; i++)
                doc.Workspace.History.Undo();
        });

    [McpServerTool(Name = "redo"), Description("Redoes steps that were undone.")]
    public Task<DocumentInfo> Redo([Description(DocumentHelp)] string? document = null,
        [Description("Number of steps (default 1).")] int steps = 1) =>
        Edit(document, doc =>
        {
            for (var i = 0; i < steps && doc.Workspace.History.CanRedo; i++)
                doc.Workspace.History.Redo();
        });

    // ---------------------------------------------------------------- Layers

    [McpServerTool(Name = "add_layer"), Description("Adds a transparent layer above the current one and makes it current.")]
    public Task<DocumentInfo> AddLayer([Description(DocumentHelp)] string? document = null,
        [Description("Name of the new layer.")] string? name = null) =>
        Edit(document, doc =>
        {
            var layer = doc.Actions.AddNewLayer();
            if (!string.IsNullOrEmpty(name))
                Rename(doc, layer, name);
        });

    [McpServerTool(Name = "import_layer"), Description("Adds an image file as a new layer above the current one.")]
    public Task<DocumentInfo> ImportLayer([Description("Path of the image file.")] string path,
        [Description(DocumentHelp)] string? document = null) =>
        Edit(document, doc =>
        {
            var file = new FileInfo(context.Files.Resolve(path));
            if (!file.Exists)
                throw new McpException($"'{path}' does not exist.");
            doc.Actions.ImportFromFile(file);
        });

    [McpServerTool(Name = "delete_layer"), Description("Deletes a layer (an image keeps at least one layer).")]
    public Task<DocumentInfo> DeleteLayer([Description(DocumentHelp)] string? document = null,
        [Description(LayerHelp)] int? layer = null) =>
        Edit(document, doc =>
        {
            if (doc.Layers.Count() < 2)
                throw new McpException("The image has only one layer.");
            SelectLayer(doc, layer);
            doc.Actions.DeleteCurrentLayer();
        });

    [McpServerTool(Name = "duplicate_layer"), Description("Duplicates a layer; the copy goes above it and becomes current.")]
    public Task<DocumentInfo> DuplicateLayer([Description(DocumentHelp)] string? document = null,
        [Description(LayerHelp)] int? layer = null) =>
        Edit(document, doc =>
        {
            SelectLayer(doc, layer);
            doc.Actions.DuplicateCurrentLayer();
        });

    [McpServerTool(Name = "merge_layer_down"), Description("Merges a layer into the one below it.")]
    public Task<DocumentInfo> MergeLayerDown([Description(DocumentHelp)] string? document = null,
        [Description(LayerHelp)] int? layer = null) =>
        Edit(document, doc =>
        {
            SelectLayer(doc, layer);
            if (doc.Layers.CurrentUserLayerIndex == 0)
                throw new McpException("The bottom layer has no layer below it.");
            doc.Actions.MergeCurrentLayerDown();
        });

    [McpServerTool(Name = "flatten"), Description("Merges all layers into one.")]
    public Task<DocumentInfo> Flatten([Description(DocumentHelp)] string? document = null) =>
        Edit(document, doc =>
        {
            if (doc.Layers.Count() > 1)
                doc.Actions.Flatten();
        });

    [McpServerTool(Name = "move_layer"), Description("Moves a layer to another position in the stack (0 = bottom).")]
    public Task<DocumentInfo> MoveLayer(
        [Description("New index of the layer.")] int toIndex,
        [Description(DocumentHelp)] string? document = null,
        [Description(LayerHelp)] int? layer = null) =>
        Edit(document, doc =>
        {
            SelectLayer(doc, layer);
            if (toIndex < 0 || toIndex >= doc.Layers.Count())
                throw new McpException($"toIndex must be between 0 and {doc.Layers.Count() - 1}.");
            while (doc.Layers.CurrentUserLayerIndex < toIndex)
                doc.Actions.MoveCurrentLayerUp();
            while (doc.Layers.CurrentUserLayerIndex > toIndex)
                doc.Actions.MoveCurrentLayerDown();
        });

    [McpServerTool(Name = "select_layer"), Description("Makes a layer current: effects, fills and pastes apply to it.")]
    public Task<DocumentInfo> SelectLayerTool([Description(LayerHelp)] int layer,
        [Description(DocumentHelp)] string? document = null) =>
        Edit(document, doc => SelectLayer(doc, layer));

    [McpServerTool(Name = "set_layer_properties"), Description("Changes a layer's name, visibility, opacity or blend mode (one undo step).")]
    public Task<DocumentInfo> SetLayerProperties(
        [Description(DocumentHelp)] string? document = null,
        [Description(LayerHelp)] int? layer = null,
        [Description("New name.")] string? name = null,
        [Description("Show or hide the layer.")] bool? visible = null,
        [Description("Opacity in percent, 0-100.")] double? opacity = null,
        [Description("Normal, Multiply, Additive, ColorBurn, ColorDodge, Reflect, Glow, Overlay, Difference, Negation, " +
                     "Lighten, Darken, Screen, Xor, HardLight, SoftLight.")] string? blendMode = null) =>
        Edit(document, doc =>
        {
            SelectLayer(doc, layer);
            var target = doc.Layers.CurrentUserLayer;
            var before = LayerProperties.From(target);
            if (opacity is < 0 or > 100)
                throw new McpException("opacity must be between 0 and 100.");
            var blend = Parse.Enum(blendMode, target.BlendMode, "blend mode");
            if (name is not null)
                target.Name = name;
            if (visible is { } v)
                target.Hidden = !v;
            if (opacity is { } o)
                target.Opacity = o / 100;
            target.BlendMode = blend;
            doc.Actions.CommitLayerProperties(target, before);
            doc.Workspace.Invalidate();
        });

    [McpServerTool(Name = "flip_layer"), Description("Flips the current layer (or the given one) horizontally or vertically.")]
    public Task<DocumentInfo> FlipLayer([Description("horizontal or vertical.")] string direction,
        [Description(DocumentHelp)] string? document = null, [Description(LayerHelp)] int? layer = null) =>
        Edit(document, doc =>
        {
            SelectLayer(doc, layer);
            if (Horizontal(direction))
                doc.Actions.FlipCurrentLayerHorizontal();
            else
                doc.Actions.FlipCurrentLayerVertical();
        });

    // ---------------------------------------------------------------- Selection

    [McpServerTool(Name = "select_rectangle"), Description("Selects a rectangle (image pixels, origin top-left).")]
    public Task<DocumentInfo> SelectRectangle(int x, int y, int width, int height,
        [Description(ModeHelp)] string? mode = null, [Description(DocumentHelp)] string? document = null) =>
        Select(document, mode, "Rectangle Select", (w, h) =>
            SelectionMask.Rectangle(w, h, new PointD(x, y), new PointD(x + width, y + height)));

    [McpServerTool(Name = "select_ellipse"), Description("Selects the ellipse inscribed in a rectangle.")]
    public Task<DocumentInfo> SelectEllipse(int x, int y, int width, int height,
        [Description(ModeHelp)] string? mode = null, [Description(DocumentHelp)] string? document = null) =>
        Select(document, mode, "Ellipse Select", (w, h) =>
            SelectionMask.Ellipse(w, h, new PointD(x, y), new PointD(x + width, y + height)));

    [McpServerTool(Name = "magic_wand"), Description("Selects pixels of a color similar to the pixel at (x, y), like the Magic Wand tool.")]
    public Task<DocumentInfo> MagicWand(int x, int y,
        [Description("Color tolerance in percent, 0-100 (default 50).")] int tolerance = 50,
        [Description("Select similar colors anywhere in the image instead of only the connected area.")] bool global = false,
        [Description("Sample the whole image as it looks instead of the current layer.")] bool sampleImage = false,
        [Description(ModeHelp)] string? mode = null, [Description(DocumentHelp)] string? document = null) =>
        Select(document, mode, "Magic Wand", (w, h) =>
        {
            var doc = context.Document(document);
            if (x < 0 || y < 0 || x >= w || y >= h)
                throw new McpException($"({x}, {y}) is outside the image ({w}×{h}).");
            var pixels = sampleImage
                ? doc.Layers.GetFlattenedBgra(includeToolLayer: false)
                : doc.Layers.CurrentUserLayer.Surface.ToBgra();
            return SelectionMask.MagicWand(pixels, w, h, new PointI(x, y), Math.Clamp(tolerance, 0, 100), global);
        });

    [McpServerTool(Name = "select_all"), Description("Selects the whole image.")]
    public Task<DocumentInfo> SelectAll([Description(DocumentHelp)] string? document = null) =>
        Edit(document, doc => doc.Actions.SelectAll());

    [McpServerTool(Name = "deselect"), Description("Removes the selection (edits then apply to the whole layer).")]
    public Task<DocumentInfo> Deselect([Description(DocumentHelp)] string? document = null) =>
        Edit(document, doc => doc.Actions.DeselectAll());

    [McpServerTool(Name = "invert_selection"), Description("Selects what is not selected.")]
    public Task<DocumentInfo> InvertSelection([Description(DocumentHelp)] string? document = null) =>
        Edit(document, doc => doc.Actions.InvertSelection());

    [McpServerTool(Name = "fill_selection"), Description("Fills the selection (or the whole layer) of the current layer with a color.")]
    public Task<DocumentInfo> FillSelection([Description("#RRGGBB, #RRGGBBAA, white, black or transparent.")] string color,
        [Description(DocumentHelp)] string? document = null) =>
        Edit(document, doc => doc.Actions.FillSelection(Parse.Color(color, ColorBgra.Black)));

    [McpServerTool(Name = "erase_selection"), Description("Makes the selected pixels (or the whole layer) of the current layer transparent.")]
    public Task<DocumentInfo> EraseSelection([Description(DocumentHelp)] string? document = null) =>
        Edit(document, doc => doc.Actions.EraseSelection());

    [McpServerTool(Name = "add_speech_bubble"), Description(
        "Adds a comic speech bubble with text whose tail points at (x, y), like the Speech Bubble tool: outline and text " +
        "in 'color', inside in 'fill'. Use one per thing to explain on a photo. By default bubbles go to a \"Bubbles\" " +
        "layer at the top (created when needed). One undoable step. Needs fonts, so only in attached mode (the app running).")]
    public Task<DocumentInfo> AddSpeechBubble(
        [Description("Text; \\n for line breaks.")] string text,
        [Description("X of the tail's tip: the point the bubble is about.")] double x,
        [Description("Y of the tail's tip.")] double y,
        [Description("X of the bubble's center; above-right of the tip when omitted.")] double? bubbleX = null,
        [Description("Y of the bubble's center.")] double? bubbleY = null,
        [Description("Square, Rounded (default), Oval or Thought (cloud with a trail of circles).")] string? style = null,
        [Description("Bubble width in pixels (text wraps); fitted to the text when omitted.")] double? width = null,
        [Description("Font size in pixels (default 24).")] double? fontSize = null,
        bool bold = false,
        [Description("Outline and text color (default black).")] string? color = null,
        [Description("Inside color (default white).")] string? fill = null,
        [Description("Outline width in pixels (default 2).")] int? outlineWidth = null,
        [Description("Number shown in a badge on the bubble's corner (for numbered explanations).")] int? number = null,
        [Description("False to draw on the current layer instead of the \"Bubbles\" layer.")] bool ownLayer = true,
        [Description(DocumentHelp)] string? document = null) =>
        Edit(document, doc =>
        {
            var rasterizer = context.TextRasterizer ?? throw new McpException(
                "Speech bubbles need the app's fonts: run CinnabarSharp, turn on File › Allow AI Agents and connect with --mcp --attach.");
            if (bubbleX.HasValue != bubbleY.HasValue)
                throw new McpException("Give both bubbleX and bubbleY, or neither.");
            var settings = new ToolSettings
            {
                PrimaryColor = Parse.Color(color, ColorBgra.Black),
                SecondaryColor = Parse.Color(fill, ColorBgra.White),
                BubbleStyle = Parse.Enum(style, BubbleStyle.Rounded, "bubble style"),
                FontSize = Math.Clamp(fontSize ?? 24, 1, 1000),
                Bold = bold,
                BrushWidth = Math.Clamp(outlineWidth ?? 2, 1, 50),
                BubbleNumbered = number.HasValue,
                BubbleNextNumber = number ?? 1,
                BubbleOwnLayer = ownLayer,
            };
            var center = bubbleX is { } bx && bubbleY is { } by ? new PointD(bx, by) : (PointD?)null;
            new SpeechBubbleTool(settings, rasterizer).Place(doc, new PointD(x, y), center, text, width);
        });

    // ---------------------------------------------------------------- Adjustments and effects

    [McpServerTool(Name = "list_effects", ReadOnly = true), Description(
        "Lists every adjustment and effect with its menu and parameters (name, range, default, list choices). " +
        "Use the names with apply_effect.")]
    public IReadOnlyList<EffectInfo> ListEffects() => EffectCatalogInfo.All;

    [McpServerTool(Name = "apply_effect"), Description(
        "Runs an adjustment or effect (see list_effects) on the current layer, inside the selection if there is one. " +
        "Parameters not given keep their default. One undo step.")]
    public Task<DocumentInfo> ApplyEffect(
        [Description("Effect name, e.g. \"Auto-Enhance\", \"Sepia\", \"Gaussian Blur\", \"Adjust Photo\".")] string effect,
        [Description("Parameter values by name, e.g. {\"Radius\": 4}; list parameters take the choice name, e.g. {\"Filter\": \"Noir\"}.")]
        Dictionary<string, JsonElement>? parameters = null,
        [Description("All values in order, for effects whose values are encoded (Curves, per-channel Levels).")] double[]? values = null,
        [Description("Primary color for effects that use it (e.g. Clouds); default black.")] string? primaryColor = null,
        [Description("Secondary color; default white.")] string? secondaryColor = null,
        [Description(DocumentHelp)] string? document = null) =>
        Edit(document, doc =>
        {
            var fx = Parse.Effect(effect);
            var list = Parse.EffectValues(fx, parameters, values);
            EffectSession.ApplyNow(doc, fx, list, Parse.Color(primaryColor, ColorBgra.Black),
                Parse.Color(secondaryColor, ColorBgra.White));
        });

    [McpServerTool(Name = "suggest_effect_values", ReadOnly = true), Description(
        "Values an effect suggests for this image (the Auto button of its dialog, e.g. Adjust Photo, Levels), without " +
        "applying them. Pass them to apply_effect, adjusted if you like.")]
    public Task<SuggestedValues> SuggestEffectValues([Description("Effect name.")] string effect,
        [Description(DocumentHelp)] string? document = null) =>
        context.Run(() =>
        {
            var doc = context.Document(document);
            var fx = Parse.Effect(effect);
            var ctx = new EffectContext(doc.Layers.CurrentUserLayer.Surface.ToBgra(), doc.ImageSize.Width,
                doc.ImageSize.Height, ColorBgra.Black, ColorBgra.White);
            var suggested = fx.SuggestValues(ctx)
                ?? throw new McpException($"'{fx.Name}' has no suggested values.");
            return suggested.Count == fx.Parameters.Count
                ? new SuggestedValues(fx.Name, fx.Parameters.Select((p, i) => (p.Name, Value: suggested[i]))
                    .ToDictionary(p => p.Name, p => Math.Round(p.Value, 3)), null)
                : new SuggestedValues(fx.Name, null, suggested);
        });

    // ---------------------------------------------------------------- Image

    [McpServerTool(Name = "resize_image"), Description(
        "Resizes the image (all layers). Give width and/or height (the other keeps the aspect ratio), or percent.")]
    public Task<DocumentInfo> ResizeImage(
        [Description("New width in pixels.")] int? width = null,
        [Description("New height in pixels.")] int? height = null,
        [Description("Scale in percent, instead of width/height.")] double? percent = null,
        [Description("BestQuality (Lanczos, default), Bicubic, Bilinear or NearestNeighbor.")] string? resampling = null,
        [Description(DocumentHelp)] string? document = null) =>
        Edit(document, doc =>
        {
            var (w0, h0) = (doc.ImageSize.Width, doc.ImageSize.Height);
            var (w, h) = (width, height, percent) switch
            {
                (null, null, { } p) => ((int)Math.Round(w0 * p / 100), (int)Math.Round(h0 * p / 100)),
                ({ } a, { } b, null) => (a, b),
                ({ } a, null, null) => (a, (int)Math.Round((double)h0 * a / w0)),
                (null, { } b, null) => ((int)Math.Round((double)w0 * b / h0), b),
                _ => throw new McpException("Give width and/or height, or percent."),
            };
            CheckSize(w, h);
            doc.Actions.ResizeImage(new ImageSize(w, h), Parse.Enum(resampling, ResamplingMode.BestQuality, "resampling"));
        });

    [McpServerTool(Name = "resize_canvas"), Description(
        "Changes the canvas size without scaling the pixels; new area of the bottom layer gets the background color.")]
    public Task<DocumentInfo> ResizeCanvas(int width, int height,
        [Description("Where the image stays: Center (default), NW, N, NE, E, SE, S, SW, W.")] string? anchor = null,
        [Description("Color of the new area of the bottom layer (default white).")] string? background = null,
        [Description(DocumentHelp)] string? document = null) =>
        Edit(document, doc =>
        {
            CheckSize(width, height);
            doc.Actions.ResizeCanvas(new ImageSize(width, height), Parse.Enum(anchor, Anchor.Center, "anchor"),
                Parse.Color(background, ColorBgra.White));
        });

    [McpServerTool(Name = "place_beside"), Description(
        "Puts another open image next to this one (left, right, above or below), growing the canvas to fit both, in a " +
        "new layer. Neither image is scaled; the shorter one is aligned along the shared edge, and the new area of the " +
        "bottom layer gets the background color. Use it to stitch photos side by side or one above the other.")]
    public Task<DocumentInfo> PlaceBeside(
        [Description("Id or name of the open image to place (flattened).")] string source,
        [Description("Left, Right (default), Top or Bottom.")] string? side = null,
        [Description("Start (top or left), Middle (default) or End (bottom or right).")] string? alignment = null,
        [Description("Color of the new area of the bottom layer (default white).")] string? background = null,
        [Description(DocumentHelp)] string? document = null) =>
        Edit(document, doc =>
        {
            var from = context.Document(source);
            var (w, h) = (from.ImageSize.Width, from.ImageSize.Height);
            var pasteSide = Parse.Enum(side, PasteSide.Right, "side");
            var align = Parse.Enum(alignment, EdgeAlignment.Middle, "alignment");
            var size = PasteBesideLayout.For(doc.ImageSize, from.ImageSize, pasteSide, align).Size;
            CheckSize(size.Width, size.Height);
            var image = new ClipboardImage(from.Layers.GetFlattenedBgra(includeToolLayer: false), w, h);
            doc.Actions.PasteBeside(image, pasteSide, align, Parse.Color(background, ColorBgra.White));
        });

    [McpServerTool(Name = "crop"), Description(
        "Crops the image. Give a rectangle; or ratio (e.g. \"16:9\") for the largest centered area of that ratio; or " +
        "neither to crop to the selection.")]
    public Task<DocumentInfo> Crop(int? x = null, int? y = null, int? width = null, int? height = null,
        [Description("Aspect ratio such as 16:9, 4:3, 1:1; the kept area is centered in the selection, or in the image.")] string? ratio = null,
        [Description(DocumentHelp)] string? document = null) =>
        Edit(document, doc =>
        {
            if (x is { } rx && y is { } ry && width is { } rw && height is { } rh)
                doc.Actions.CropToRectangle(new RectangleI(rx, ry, rw, rh));
            else if (ratio is not null)
            {
                var area = doc.Selection?.Bounds ?? new RectangleI(0, 0, doc.ImageSize.Width, doc.ImageSize.Height);
                var inner = TvExport.CenteredCrop(area.Width, area.Height, Parse.Ratio(ratio));
                doc.Actions.CropToRectangle(new RectangleI(area.X + inner.X, area.Y + inner.Y, inner.Width, inner.Height));
            }
            else if (doc.Selection is not null)
                doc.Actions.CropToSelection();
            else
                throw new McpException("Give x, y, width and height, or a ratio, or select an area first.");
        });

    [McpServerTool(Name = "rotate_image"), Description("Rotates the whole image by 90, 180 or 270 degrees clockwise (-90 = counter-clockwise).")]
    public Task<DocumentInfo> RotateImage([Description("90, 180, 270 or -90.")] int degrees,
        [Description(DocumentHelp)] string? document = null) =>
        Edit(document, doc =>
        {
            switch (((degrees % 360) + 360) % 360)
            {
                case 90: doc.Actions.RotateImage90(clockwise: true); break;
                case 180: doc.Actions.RotateImage180(); break;
                case 270: doc.Actions.RotateImage90(clockwise: false); break;
                default: throw new McpException("degrees must be 90, 180, 270 or -90. Use the Straighten effect for small angles.");
            }
        });

    [McpServerTool(Name = "flip_image"), Description("Flips the whole image (all layers).")]
    public Task<DocumentInfo> FlipImage([Description("horizontal or vertical.")] string direction,
        [Description(DocumentHelp)] string? document = null) =>
        Edit(document, doc =>
        {
            if (Horizontal(direction))
                doc.Actions.FlipImageHorizontal();
            else
                doc.Actions.FlipImageVertical();
        });

    // ---------------------------------------------------------------- TV

    [McpServerTool(Name = "prepare_for_tv"), Description(
        "Makes a 16:9 TV image (e.g. Samsung The Frame) from the image as it looks, and opens it as a new image named " +
        "'<name>_4K'. Crop to fill keeps the selection's area if there is one, else the center. Save it with save_image as JPEG.")]
    public Task<DocumentInfo> PrepareForTv(
        [Description("2K (1920×1080), 4K (3840×2160, default) or 8K (7680×4320).")] string? resolution = null,
        [Description("CropToFill (default), FitWithBorders or Stretch.")] string? fit = null,
        [Description("Borders for FitWithBorders: Black (default), White or Blurred.")] string? background = null,
        [Description(DocumentHelp)] string? document = null) =>
        context.Run(() =>
        {
            var doc = context.Document(document);
            var options = TvOptionsFrom(resolution, fit, background);
            var photo = new BgraImage(doc.Layers.GetFlattenedBgra(includeToolLayer: false), doc.ImageSize.Width, doc.ImageSize.Height);
            var result = TvExport.Compose(photo, options, doc.Selection?.Bounds);
            var tv = context.Workspace.NewDocumentFromImage(new ClipboardImage(result.Pixels, result.Width, result.Height));
            tv.DisplayName = Path.GetFileNameWithoutExtension(doc.DisplayName) + TvExport.Suffix(options.Resolution);
            tv.FileType = "jpg";
            return Describe.Document(context, tv);
        });

    [McpServerTool(Name = "prepare_folder_for_tv"), Description(
        "Prepares every image of a folder (not subfolders) for a 16:9 TV and saves each as '<name>_4K.jpg' in a " +
        "'TV 4K' subfolder. Refuses if that subfolder already has files unless overwrite is true.")]
    public async Task<FolderExportResult> PrepareFolderForTv(
        [Description("Folder of photos.")] string folder,
        [Description("2K, 4K (default) or 8K.")] string? resolution = null,
        [Description("CropToFill (default), FitWithBorders or Stretch.")] string? fit = null,
        [Description("Borders for FitWithBorders: Black (default), White or Blurred.")] string? background = null,
        [Description("JPEG quality 1-100 (default 90).")] int jpegQuality = JpegFormat.DefaultQuality,
        [Description("Must be true to replace files in an existing output folder.")] bool overwrite = false,
        CancellationToken cancellation = default)
    {
        var directory = new DirectoryInfo(context.Files.Resolve(folder));
        if (!directory.Exists)
            throw new McpException($"'{folder}' is not a folder.");
        var options = TvOptionsFrom(resolution, fit, background);
        var output = new DirectoryInfo(Path.Combine(directory.FullName, "TV" + TvExport.Suffix(options.Resolution).Replace('_', ' ')));
        if (output.Exists && output.EnumerateFiles().Any() && !overwrite)
            throw new McpException($"'{output.FullName}' already has files. Pass overwrite=true to replace them.");
        try
        {
            var (result, count) = await Task.Run(() => TvExport.ExportFolder(directory, options, jpegQuality, cancellation: cancellation),
                cancellation);
            return new FolderExportResult(result.FullName, count);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or MagickException)
        {
            throw new McpException(e.Message, e);
        }
    }

    // ---------------------------------------------------------------- Comic page

    [McpServerTool(Name = "compose_comic_page"), Description(
        "Assembles open images into one image like a comic page (panels, gutters, borders) and opens it as a new " +
        "image named 'Comic page'. Each image fills its panel: its selection if it has one, else its center (or, with " +
        "stretch, the whole image stretched to the panel). Layouts: " +
        "1 panel, 2 rows, 2 columns, 2x2 grid, 3 rows, 1 large + 2 small, 2 small + 1 large, 1 tall + 2 stacked, " +
        "Classic (2 + 1 + 2), 3x3 grid, Large centre + surround, 3x3 + overlapping centre. Use apply_effect with \"Cartoon\" first for a comic look.")]
    public Task<DocumentInfo> ComposeComicPage(
        [Description("Ids or names of the images, in panel order; all open images when omitted.")] string[]? documents = null,
        [Description("Layout name; chosen from the number of images when omitted.")] string? layout = null,
        [Description("Page: A4 portrait (default), A4 landscape, Square, 16:9.")] string? format = null,
        [Description("Space between panels and around them, in pixels (default 40).")] int gutter = 40,
        [Description("Panel border width in pixels, 0 for none (default 8).")] int borderWidth = 8,
        [Description("white (default: black borders) or black (white borders).")] string? background = null,
        [Description("Stretch each whole image to its panel (proportions change) instead of cropping it (default false).")] bool stretch = false) =>
        context.Run(() =>
        {
            var docs = documents is { Length: > 0 }
                ? documents.Select(context.Document).ToList()
                : context.Workspace.OpenDocuments.OfType<ImageDocument>().ToList();
            if (docs.Count == 0)
                throw new McpException("No image is open. Use open_image first.");
            var panels = Math.Min(docs.Count, 9);
            var comicLayout = layout is null
                ? ComicPage.Layouts.FirstOrDefault(l => l.Panels.Count == panels) ?? ComicPage.Layouts.First(l => l.Panels.Count >= panels)
                : ComicPage.Layouts.FirstOrDefault(l => LayoutKey(l.Name) == LayoutKey(layout))
                  ?? throw new McpException($"Unknown layout '{layout}'. Layouts: {string.Join(", ", ComicPage.Layouts.Select(l => l.Name))}.");
            var page = (format is null ? ComicPage.Formats[0]
                : ComicPage.Formats.FirstOrDefault(f => Parse.Key(f.Name).StartsWith(Parse.Key(format), StringComparison.Ordinal)))
                ?? throw new McpException($"Unknown format '{format}'. Formats: {string.Join(", ", ComicPage.Formats.Select(f => f.Name))}.");
            var dark = Parse.Key(background ?? "white") switch
            {
                "white" => false,
                "black" => true,
                _ => throw new McpException("background must be white or black."),
            };
            var options = new ComicPageOptions(page.Size, Math.Clamp(gutter, 0, 400), Math.Clamp(borderWidth, 0, 60),
                dark ? ColorBgra.White : ColorBgra.Black, dark ? ColorBgra.Black : ColorBgra.White);

            var rects = ComicPage.PanelRects(comicLayout, page.Size, options.Gutter);
            var contents = docs.Take(rects.Count)
                .Select((doc, i) => (ComicPanelContent?)(Framing(doc, rects[i]) with { Stretch = stretch })).ToList();
            var result = ComicPage.Compose(comicLayout, options, contents);
            var comic = context.Workspace.NewDocumentFromImage(new ClipboardImage(result.Pixels, result.Width, result.Height));
            comic.DisplayName = "Comic page";
            return Describe.Document(context, comic);
        });

    private static string LayoutKey(string name) => Parse.Key(name.Replace('×', 'x'));

    /// <summary>The image in a panel: its selection, as large as the panel shape allows, else its center.</summary>
    private static ComicPanelContent Framing(ImageDocument doc, RectangleI panel)
    {
        var (w, h) = (doc.ImageSize.Width, doc.ImageSize.Height);
        var photo = new BgraImage(doc.Layers.GetFlattenedBgra(includeToolLayer: false), w, h);
        if (doc.Selection?.Bounds is not { Width: > 0, Height: > 0 } keep)
            return new ComicPanelContent(photo);
        var full = ComicPage.VisibleArea(w, h, panel.Width, panel.Height, 1, new PointD(0.5, 0.5));
        var zoom = Math.Clamp(Math.Min(full.Width / keep.Width, full.Height / keep.Height), 1, 4);
        return new ComicPanelContent(photo, zoom,
            new PointD((keep.X + keep.Width / 2.0) / w, (keep.Y + keep.Height / 2.0) / h));
    }

    // ---------------------------------------------------------------- Helpers

    private IReadOnlyList<DocumentInfo> Documents() =>
        context.Workspace.OpenDocuments.Select(d => Describe.Document(context, d)).ToList();

    private Task<DocumentInfo> Edit(string? document, Action<ImageDocument> edit) => context.Run(() =>
    {
        var doc = context.Document(document);
        edit(doc);
        return Describe.Document(context, doc);
    });

    private Task<DocumentInfo> Select(string? document, string? mode, string text, Func<int, int, SelectionMask> shape) =>
        Edit(document, doc =>
        {
            var (w, h) = (doc.ImageSize.Width, doc.ImageSize.Height);
            var combine = Parse.Enum(mode, SelectionMode.Replace, "selection mode");
            var before = doc.Selection;
            var selection = (before ?? SelectionMask.Empty(w, h)).Combine(shape(w, h), combine);
            doc.SetSelection(selection.IsEmpty ? null : selection);
            doc.Actions.RecordSelectionChange(before, text);
        });

    private static void SelectLayer(ImageDocument doc, int? index)
    {
        if (index is not { } i)
            return;
        if (i < 0 || i >= doc.Layers.Count())
            throw new McpException($"Layer {i} doesn't exist; the image has {doc.Layers.Count()} layers (0 = bottom).");
        doc.Layers.SetCurrentUserLayer(i);
    }

    private static void Rename(ImageDocument doc, UserLayer layer, string name)
    {
        var before = LayerProperties.From(layer);
        layer.Name = name;
        doc.Actions.CommitLayerProperties(layer, before);
    }

    private static bool Horizontal(string direction) => Parse.Key(direction) switch
    {
        "horizontal" or "h" => true,
        "vertical" or "v" => false,
        _ => throw new McpException("direction must be horizontal or vertical."),
    };

    private static void CheckSize(int width, int height)
    {
        const int max = 32768;
        if (width < 1 || height < 1 || width > max || height > max)
            throw new McpException($"Width and height must be between 1 and {max} pixels (got {width}×{height}).");
    }

    private (FileInfo, ImageFormat) Destination(string path, string? format, bool overwrite)
    {
        var file = new FileInfo(context.Files.Resolve(path));
        var imageFormat = (format is null ? context.Formats.GetFormatByExtension(file.Extension) : FindFormat(format))
            ?? throw new McpException($"Can't tell the format of '{file.Name}': give 'format' or a known extension " +
                                      $"({string.Join(", ", context.Formats.SaveFormats.Select(f => f.SupportedExtensions[0]))}).");
        if (!imageFormat.SupportsSaving)
            throw new McpException($"{imageFormat.DisplayName} files can be opened but not saved.");
        if (file.Exists && !overwrite)
            throw new McpException($"'{file.FullName}' already exists. Pass overwrite=true to replace it.");
        file.Directory?.Create();
        return (file, imageFormat);
    }

    private ImageFormat? FindFormat(string name) =>
        context.Formats.GetFormatByExtension(name)
        ?? context.Formats.Formats.FirstOrDefault(f => Parse.Key(f.DisplayName) == Parse.Key(name))
        ?? throw new McpException($"Unknown format '{name}'.");

    private static void WithJpegQuality(ImageFormat format, int? quality, Action save)
    {
        if (format is not JpegFormat jpeg || quality is not { } q)
        {
            save();
            return;
        }
        var previous = jpeg.Quality;
        jpeg.Quality = Math.Clamp(q, 1, 100);
        try
        {
            save();
        }
        finally
        {
            jpeg.Quality = previous;
        }
    }

    private static SavedFile Saved(FileInfo file, ImageFormat format) => new(file.FullName, format.DisplayName, file.Length);

    private static TvOptions TvOptionsFrom(string? resolution, string? fit, string? background) => new(
        Parse.Key(resolution ?? "4k") switch
        {
            "2k" or "fullhd" or "1080p" or "hd" => TvResolution.FullHd,
            "4k" or "uhd4k" or "uhd" or "2160p" => TvResolution.Uhd4K,
            "8k" or "uhd8k" or "4320p" => TvResolution.Uhd8K,
            _ => throw new McpException($"Unknown resolution '{resolution}'. Use 2K, 4K or 8K."),
        },
        Parse.Enum(fit, TvFit.CropToFill, "fit"),
        Parse.Enum(background, TvBackground.Black, "background"));

    /// <summary>PNG of the image scaled down (never up) so its longest side is at most <paramref name="maxSize"/>.</summary>
    public static (byte[] Png, int Width, int Height) Preview(byte[] bgra, int width, int height, int maxSize)
    {
        using var image = Utility.FromBgra(bgra, width, height);
        var scale = Math.Min(1.0, (double)maxSize / Math.Max(width, height));
        if (scale < 1)
        {
            image.FilterType = FilterType.Triangle;
            image.Resize(new MagickGeometry((uint)Math.Max(1, Math.Round(width * scale)), (uint)Math.Max(1, Math.Round(height * scale)))
                { IgnoreAspectRatio = true });
        }
        image.Format = MagickFormat.Png;
        return (image.ToByteArray(), (int)image.Width, (int)image.Height);
    }
}
