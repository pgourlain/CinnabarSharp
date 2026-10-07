using System.ComponentModel;
using System.Globalization;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Core.Vector.Tools;
using CinnabarSharp.Vector;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CinnabarSharp.Mcp;

/// <summary>
/// The MCP tools for SVG drawings. Every edit goes through <see cref="SvgActions"/>, so it is one undoable history step,
/// like the same command in the window. Objects are named by the <c>node</c> id that svg_tree returns, or by their XML id.
/// </summary>
[McpServerToolType]
public sealed class VectorTools(McpContext context)
{
    private const string DrawingHelp = "Id or name of an open SVG drawing (see list_documents); the active document when omitted.";
    private const string NodesHelp = "Objects to act on: node ids from svg_tree (or XML ids). The currently selected objects when omitted.";

    private static readonly string[] AllowedAttributes =
    [
        "x", "y", "width", "height", "rx", "ry", "cx", "cy", "r", "x1", "y1", "x2", "y2", "points", "d", "transform",
        "preserveAspectRatio", "text-anchor", "font-family", "font-size", "font-weight", "font-style", "viewBox",
    ];

    // ---------------------------------------------------------------- Creating and inspecting

    [McpServerTool(Name = "new_svg"), Description(
        "Creates a new empty SVG drawing and makes it active. The size is in the given unit (px, mm or in); coordinates of " +
        "all the svg_ tools are in the drawing's user units, which are pixels for px drawings.")]
    public Task<DocumentInfo> NewSvg(
        [Description("Width in the given unit.")] double width,
        [Description("Height in the given unit.")] double height,
        [Description("px (default), mm or in.")] string? units = null) =>
        context.Run(() =>
        {
            if (!(width > 0 && height > 0 && width <= 100000 && height <= 100000))
                throw new McpException("width and height must be positive numbers (at most 100000).");
            return Describe.Document(context, context.Workspace.NewSvgDocument(width, height, Parse.Enum(units, SvgUnit.Px, "unit")));
        });

    [McpServerTool(Name = "svg_tree", ReadOnly = true), Description(
        "Lists the objects of an SVG drawing, bottom to top, groups before their children (indented by depth): node id, " +
        "element (rect, circle, path, text, g, image…), XML id, label, bounds in user units, fill, stroke, opacity.")]
    public Task<IReadOnlyList<SvgNodeInfo>> SvgTree([Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var list = new List<SvgNodeInfo>();
            void Walk(SvgContainer container, int depth)
            {
                foreach (var child in container.Elements.Where(IsObject))
                {
                    list.Add(Info(drawing, child, depth));
                    if (child is SvgContainer inner)
                        Walk(inner, depth + 1);
                }
            }
            Walk(drawing.Root, 0);
            return (IReadOnlyList<SvgNodeInfo>)list;
        });

    [McpServerTool(Name = "svg_get_node", ReadOnly = true), Description(
        "Everything about one object: its attributes, the style that applies to it (resolved: fill, stroke, opacity…), " +
        "the text of a text object and its children.")]
    public Task<SvgNodeDetails> SvgGetNode(
        [Description("Node id from svg_tree, or an XML id.")] string node,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var element = Find(drawing, node);
            var computed = StyleResolver.ComputeFor(element);
            var attributes = element.Attributes.ToDictionary(a => a.Name.Namespace == System.Xml.Linq.XNamespace.None ? a.Name.LocalName : a.Name.ToString(), a => a.Value);
            var style = new Dictionary<string, string>
            {
                ["fill"] = computed.Fill.ToText(),
                ["fill-opacity"] = Number(computed.FillOpacity),
                ["fill-rule"] = computed.FillRule.ToString().ToLowerInvariant(),
                ["stroke"] = computed.Stroke.ToText(),
                ["stroke-width"] = Number(computed.StrokeWidth),
                ["stroke-opacity"] = Number(computed.StrokeOpacity),
                ["opacity"] = Number(computed.Opacity),
            };
            return new SvgNodeDetails(Info(drawing, element, 0), attributes, style, element is SvgText text ? text.Content : null,
                (element as SvgContainer)?.Elements.Where(IsObject).Select(NodeId).ToList() ?? []);
        });

    // ---------------------------------------------------------------- Adding objects

    [McpServerTool(Name = "svg_add_shape"), Description(
        "Adds a shape: rect (x, y, width, height, optional cornerRadius), ellipse (the ellipse inscribed in that box), " +
        "line (from x, y to x2, y2), polygon or star (inscribed in the box, with 'corners'; a star has 'ratio' > 0 for the " +
        "depth of its inner corners; 'rotation' in degrees). Paint with fill and stroke (colors, none, or url(#gradient)). " +
        "Returns the new object.")]
    public Task<SvgEditResult> SvgAddShape(
        [Description("rect, ellipse, line, polygon or star.")] string kind,
        [Description("Left of the box (or the start of a line).")] double x,
        [Description("Top of the box (or the start of a line).")] double y,
        [Description("Width of the box (not for lines).")] double? width = null,
        [Description("Height of the box (not for lines).")] double? height = null,
        [Description("End of a line.")] double? x2 = null,
        [Description("End of a line.")] double? y2 = null,
        [Description("Corner radius of a rect.")] double? cornerRadius = null,
        [Description("Number of corners of a polygon or star (default 5, at least 3).")] int? corners = null,
        [Description("Star only: inner radius as a fraction of the outer one, 0.1-0.95 (default 0.5).")] double? ratio = null,
        [Description("Polygon or star: rotation in degrees (0 puts a corner on the right; -90 on top).")] double? rotation = null,
        [Description("Fill: #RRGGBB, a color name, none, or url(#id). Default black (SVG default).")] string? fill = null,
        [Description("Stroke paint; none when omitted.")] string? stroke = null,
        [Description("Stroke width in user units.")] double? strokeWidth = null,
        [Description("Opacity 0-1.")] double? opacity = null,
        [Description("A group (node id) to add the shape to; the top layer when omitted.")] string? parent = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var shape = BuildShape(drawing, Parse.Key(kind), x, y, width, height, x2, y2, cornerRadius, corners, ratio, rotation);
            Paint(shape, fill, stroke, strokeWidth, opacity);
            return Added(drawing, shape, "Add " + shape.ElementName, parent);
        });

    [McpServerTool(Name = "svg_add_path"), Description(
        "Adds a path from SVG path data (M, L, H, V, C, S, Q, T, A, Z, absolute or relative), e.g. \"M10 80 C40 10 65 10 95 80 S150 150 180 80\".")]
    public Task<SvgEditResult> SvgAddPath(
        [Description("SVG path data.")] string d,
        [Description("Fill paint; black when omitted (SVG default).")] string? fill = null,
        [Description("Stroke paint; none when omitted.")] string? stroke = null,
        [Description("Stroke width in user units.")] double? strokeWidth = null,
        [Description("Opacity 0-1.")] double? opacity = null,
        [Description("A group (node id) to add the path to; the top layer when omitted.")] string? parent = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var parsed = PathDataParser.ParseWithResult(d);
            if (parsed.Path.IsEmpty)
                throw new McpException("The path data has no drawable segment. Example: \"M10 10 L90 10 L50 80 Z\".");
            var path = new SvgPath { Id = drawing.Root.NewId("path") };
            path.SetPath(parsed.Path, 4);
            Paint(path, fill, stroke, strokeWidth, opacity);
            return Added(drawing, path, "Add Path", parent);
        });

    [McpServerTool(Name = "svg_add_text"), Description(
        "Adds a line of text with its baseline starting at x, y. The app draws it with the installed fonts; a headless " +
        "server has no fonts and shows (and exports) text as a gray block of the estimated size. Use svg_path_operation " +
        "object_to_path in the app to turn it into outlines.")]
    public Task<SvgEditResult> SvgAddText(
        [Description("The text.")] string text,
        [Description("Start of the baseline (end or middle with textAnchor).")] double x,
        [Description("Baseline.")] double y,
        [Description("Font family, e.g. Arial or sans-serif.")] string? fontFamily = null,
        [Description("Font size in user units (default 16).")] double? fontSize = null,
        [Description("Bold.")] bool bold = false,
        [Description("Italic.")] bool italic = false,
        [Description("start (default), middle or end: which part of the text is at x.")] string? textAnchor = null,
        [Description("Fill paint; black when omitted.")] string? fill = null,
        [Description("Stroke paint; none when omitted.")] string? stroke = null,
        [Description("Stroke width in user units.")] double? strokeWidth = null,
        [Description("Opacity 0-1.")] double? opacity = null,
        [Description("A group (node id) to add the text to; the top layer when omitted.")] string? parent = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            if (string.IsNullOrEmpty(text))
                throw new McpException("text must not be empty.");
            var drawing = context.Svg(document);
            var element = new SvgText { Id = drawing.Root.NewId("text") };
            element.X = x;
            element.Y = y;
            element.SetPlainText(text);
            if (!string.IsNullOrWhiteSpace(fontFamily))
                element.SetAttribute("font-family", fontFamily);
            element.SetAttribute("font-size", Number(fontSize is > 0 ? fontSize.Value : 16));
            if (bold)
                element.SetAttribute("font-weight", "bold");
            if (italic)
                element.SetAttribute("font-style", "italic");
            if (textAnchor is not null)
                element.SetAttribute("text-anchor", Parse.Key(textAnchor) switch
                {
                    "start" or "left" => null,
                    "middle" or "center" => "middle",
                    "end" or "right" => "end",
                    _ => throw new McpException("textAnchor must be start, middle or end."),
                });
            Paint(element, fill, stroke, strokeWidth, opacity);
            return Added(drawing, element, "Add Text", parent);
        });

    [McpServerTool(Name = "svg_add_image"), Description(
        "Places a picture file (PNG, JPEG, WebP, GIF) in the drawing at 96 dpi, fitted into the page and centered, or in " +
        "the given box. Embedded in the SVG by default; linked keeps a relative path (the drawing must be saved first, " +
        "and the picture must be in the drawing's folder or below).")]
    public Task<SvgEditResult> SvgAddImage(
        [Description("Path of the picture, inside the allowed folders.")] string path,
        [Description("Keep a link to the file instead of embedding it.")] bool linked = false,
        [Description("Left of the picture's box (with y, width and height: places it exactly).")] double? x = null,
        [Description("Top of the box.")] double? y = null,
        [Description("Width of the box.")] double? width = null,
        [Description("Height of the box.")] double? height = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var file = new FileInfo(context.Files.Resolve(path));
            if (!file.Exists)
                throw new McpException($"'{path}' does not exist.");
            VRect? box = null;
            if (x is not null || y is not null || width is not null || height is not null)
            {
                if (x is null || y is null || width is not > 0 || height is not > 0)
                    throw new McpException("To place the picture give x, y, width and height together.");
                box = new VRect(x.Value, y.Value, width.Value, height.Value);
            }
            var image = drawing.Actions.ImportImage(file, linked, box);
            return Result(drawing, [image]);
        });

    [McpServerTool(Name = "svg_set_gradient"), Description(
        "Paints the objects' fill (or stroke) with a new gradient. stops are \"offset:color\" strings, e.g. " +
        "[\"0:#ff0000\",\"1:#0000ff\"]; colors may have an alpha (#RRGGBBAA). The gradient runs left to right across " +
        "each object (linear) or from its center (radial).")]
    public Task<SvgEditResult> SvgSetGradient(
        [Description("Two or more \"offset:color\" stops, offsets from 0 to 1.")] string[] stops,
        [Description("linear (default) or radial.")] string? kind = null,
        [Description("Paint the stroke instead of the fill.")] bool onStroke = false,
        [Description(NodesHelp)] string[]? nodes = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var targets = Nodes(drawing, nodes);
            if (stops is null || stops.Length < 2)
                throw new McpException("Give at least two stops such as \"0:#ff0000\" and \"1:#0000ff\".");
            var parsed = stops.Select(ParseStop).ToList();
            drawing.Actions.CreateGradient(targets, Parse.Enum(kind, SvgGradientKind.Linear, "gradient kind"), onStroke, parsed);
            return Result(drawing, targets);
        });

    // ---------------------------------------------------------------- Changing objects

    [McpServerTool(Name = "svg_set_style"), Description(
        "Sets (or, with an empty value, removes) presentation properties on the objects: fill, stroke, stroke-width, " +
        "stroke-linecap, stroke-linejoin, stroke-dasharray, fill-opacity, stroke-opacity, opacity, fill-rule, " +
        "font-size, font-family, display… One undo step. References must be local (url(#id)).")]
    public Task<SvgEditResult> SvgSetStyle(
        [Description("Property → value, e.g. {\"fill\":\"#336699\",\"stroke-width\":\"2\"}. An empty value removes the property.")] Dictionary<string, string> properties,
        [Description(NodesHelp)] string[]? nodes = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var targets = Nodes(drawing, nodes);
            if (properties is null || properties.Count == 0)
                throw new McpException("Give at least one property.");
            var values = new Dictionary<string, string?>();
            foreach (var (name, value) in properties)
            {
                if (!SvgStyle.Properties.Contains(name))
                    throw new McpException($"'{name}' is not a style property I can set. Valid: {string.Join(", ", SvgStyle.Properties.Order())}.");
                CheckValue(name, value);
                values[name] = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
            drawing.Actions.SetStyle(targets, values, "Set Style");
            return Result(drawing, targets);
        });

    [McpServerTool(Name = "svg_set_attributes"), Description(
        "Changes geometry attributes of one object in one undo step: x, y, width, height, rx, ry, cx, cy, r, x1, y1, x2, y2, " +
        "points, d (path data), transform, preserveAspectRatio, text-anchor, font-family, font-size, font-weight, font-style " +
        "(an empty value removes the attribute), and 'text' to replace the content of a text object. Other attributes " +
        "(href, style, event handlers) are refused: use svg_set_style, svg_rename or svg_add_image.")]
    public Task<SvgEditResult> SvgSetAttributes(
        [Description("Node id from svg_tree, or an XML id.")] string node,
        [Description("Attribute → value, e.g. {\"x\":\"20\",\"width\":\"80\"}.")] Dictionary<string, string> attributes,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var element = Find(drawing, node);
            if (attributes is null || attributes.Count == 0)
                throw new McpException("Give at least one attribute.");
            foreach (var (name, value) in attributes)
            {
                if (name == "text")
                {
                    if (element is not SvgText)
                        throw new McpException("'text' only applies to text objects.");
                }
                else if (!AllowedAttributes.Contains(name))
                    throw new McpException($"'{name}' can't be set here. Allowed: text, {string.Join(", ", AllowedAttributes)}.");
                else if (value.Contains("url(", StringComparison.OrdinalIgnoreCase))
                    throw new McpException($"'{name}' can't contain references.");
            }
            drawing.Actions.Edit("Set Attributes", [element], () =>
            {
                foreach (var (name, value) in attributes)
                {
                    if (name == "text")
                        ((SvgText)element).SetPlainText(value);
                    else
                        element.SetAttribute(name, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
                }
            });
            return Result(drawing, [element]);
        });

    [McpServerTool(Name = "svg_copy_style"), Description(
        "Gives objects the look of another one: fill, stroke, widths, dashes, opacity (and, between texts, the font). One undo step. " +
        "A group passes it to every shape and text inside; a gradient is shared by reference.")]
    public Task<SvgEditResult> SvgCopyStyle(
        [Description("The object to take the look from: node id from svg_tree, or an XML id.")] string from,
        [Description(NodesHelp)] string[]? nodes = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var source = Find(drawing, from);
            var targets = Nodes(drawing, nodes);
            drawing.Actions.PasteStyle(targets, SvgActions.CopyStyle(source));
            return Result(drawing, targets);
        });

    [McpServerTool(Name = "svg_rename"), Description("Sets the XML id and/or the label (the name shown in the Objects panel) of an object.")]
    public Task<SvgEditResult> SvgRename(
        [Description("Node id from svg_tree, or an XML id.")] string node,
        [Description("New XML id: letters, digits, - _ . only; must be unused.")] string? id = null,
        [Description("New label.")] string? label = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var element = Find(drawing, node);
            if (id is null && label is null)
                throw new McpException("Give an id and/or a label.");
            if (id is not null)
                drawing.Actions.SetId(element, id);
            if (label is not null)
                drawing.Actions.SetLabel(element, label);
            return Result(drawing, [element]);
        });

    [McpServerTool(Name = "svg_transform"), Description(
        "Moves, scales, rotates or flips objects (one undo step). action: move (dx, dy), scale (sx, sy, around cx, cy), " +
        "rotate (degrees clockwise, around cx, cy), resize (x, y, width, height: the new box around all the objects), " +
        "flip_horizontal, flip_vertical, rotate_90_cw, rotate_90_ccw. Around the center of the objects' box when cx, cy are omitted.")]
    public Task<SvgEditResult> SvgTransform(
        [Description("move, scale, rotate, resize, flip_horizontal, flip_vertical, rotate_90_cw or rotate_90_ccw.")] string action,
        [Description("move: horizontal distance in user units.")] double? dx = null,
        [Description("move: vertical distance.")] double? dy = null,
        [Description("scale: horizontal factor (> 0).")] double? sx = null,
        [Description("scale: vertical factor; the same as sx when omitted.")] double? sy = null,
        [Description("rotate: angle in degrees, clockwise.")] double? degrees = null,
        [Description("scale/rotate: center x.")] double? cx = null,
        [Description("scale/rotate: center y.")] double? cy = null,
        [Description("resize: left of the new box.")] double? x = null,
        [Description("resize: top of the new box.")] double? y = null,
        [Description("resize: width of the new box.")] double? width = null,
        [Description("resize: height of the new box.")] double? height = null,
        [Description(NodesHelp)] string[]? nodes = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var targets = Nodes(drawing, nodes);
            var a = drawing.Actions;
            var center = cx is not null && cy is not null ? new VPoint(cx.Value, cy.Value) : (VPoint?)null;
            switch (Parse.Key(action))
            {
                case "move":
                    a.MoveBy(targets, dx ?? 0, dy ?? 0);
                    break;
                case "scale":
                {
                    var fx = sx ?? throw new McpException("scale needs sx.");
                    var fy = sy ?? fx;
                    if (!(fx > 0 && fy > 0))
                        throw new McpException("The scale factors must be positive; use flip_horizontal or flip_vertical to mirror.");
                    var c = center ?? a.BoundsOf(targets)?.Center ?? default;
                    a.Transform(targets, Matrix2D.Translate(c.X, c.Y) * Matrix2D.Scale(fx, fy) * Matrix2D.Translate(-c.X, -c.Y), "Scale");
                    break;
                }
                case "rotate":
                    a.Rotate(targets, degrees ?? throw new McpException("rotate needs degrees."), center);
                    break;
                case "resize":
                    if (x is null || y is null || width is not > 0 || height is not > 0)
                        throw new McpException("resize needs x, y, width and height (positive).");
                    a.Resize(targets, new VRect(x.Value, y.Value, width.Value, height.Value));
                    break;
                case "fliphorizontal":
                    a.Flip(targets, horizontal: true);
                    break;
                case "flipvertical":
                    a.Flip(targets, horizontal: false);
                    break;
                case "rotate90cw":
                    a.Rotate90(targets, clockwise: true);
                    break;
                case "rotate90ccw":
                    a.Rotate90(targets, clockwise: false);
                    break;
                default:
                    throw new McpException("Unknown action. Use move, scale, rotate, resize, flip_horizontal, flip_vertical, rotate_90_cw or rotate_90_ccw.");
            }
            return Result(drawing, targets);
        });

    [McpServerTool(Name = "svg_delete"), Description("Deletes objects.")]
    public Task<SvgEditResult> SvgDelete(
        [Description(NodesHelp)] string[]? nodes = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            drawing.Actions.Delete(Nodes(drawing, nodes));
            return Result(drawing, []);
        });

    [McpServerTool(Name = "svg_duplicate"), Description("Duplicates objects; the copies are placed above and selected. Returns the copies.")]
    public Task<SvgEditResult> SvgDuplicate(
        [Description(NodesHelp)] string[]? nodes = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            return Result(drawing, drawing.Actions.Duplicate(Nodes(drawing, nodes)));
        });

    [McpServerTool(Name = "svg_group"), Description("Wraps the objects in a new group. Returns the group.")]
    public Task<SvgEditResult> SvgGroupObjects(
        [Description(NodesHelp)] string[]? nodes = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var group = drawing.Actions.Group(Nodes(drawing, nodes)) ?? throw new McpException("Nothing to group.");
            return Result(drawing, [group]);
        });

    [McpServerTool(Name = "svg_ungroup"), Description("Dissolves groups: the children take the group's place, transform and style. Returns the children.")]
    public Task<SvgEditResult> SvgUngroup(
        [Description(NodesHelp)] string[]? nodes = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            drawing.Actions.Ungroup(Nodes(drawing, nodes));
            return Result(drawing, drawing.Selection.Nodes.ToList());
        });

    [McpServerTool(Name = "svg_reorder"), Description("Changes the stacking order: raise or lower by one, or to_top / to_bottom.")]
    public Task<SvgEditResult> SvgReorder(
        [Description("raise, lower, to_top or to_bottom.")] string move,
        [Description(NodesHelp)] string[]? nodes = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var targets = Nodes(drawing, nodes);
            switch (Parse.Key(move))
            {
                case "raise": drawing.Actions.Raise(targets); break;
                case "lower": drawing.Actions.Lower(targets); break;
                case "totop": drawing.Actions.RaiseToTop(targets); break;
                case "tobottom": drawing.Actions.LowerToBottom(targets); break;
                default: throw new McpException("move must be raise, lower, to_top or to_bottom.");
            }
            return Result(drawing, targets);
        });

    [McpServerTool(Name = "svg_align"), Description(
        "Aligns objects (align: left, center, right, top, middle, bottom) relative to first_selected, last_selected, " +
        "biggest, page (default) or selection; or spreads them evenly (distribute: left, center, right, gaps for " +
        "horizontal; top, middle, bottom, vgaps for vertical; needs three objects). Give align or distribute.")]
    public Task<SvgEditResult> SvgAlign(
        [Description("left, center, right, top, middle or bottom.")] string? align = null,
        [Description("left, center, right, gaps, top, middle, bottom or vgaps.")] string? distribute = null,
        [Description("first_selected, last_selected, biggest, page (default) or selection; for align only.")] string? relativeTo = null,
        [Description(NodesHelp)] string[]? nodes = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var targets = Nodes(drawing, nodes);
            if ((align is null) == (distribute is null))
                throw new McpException("Give either align or distribute.");
            if (align is not null)
            {
                var edge = Parse.Key(align) switch
                {
                    "left" => AlignEdge.Left,
                    "center" or "centerhorizontal" => AlignEdge.CenterHorizontal,
                    "right" => AlignEdge.Right,
                    "top" => AlignEdge.Top,
                    "middle" or "centervertical" => AlignEdge.CenterVertical,
                    "bottom" => AlignEdge.Bottom,
                    _ => throw new McpException("align must be left, center, right, top, middle or bottom."),
                };
                drawing.Actions.Align(targets, edge, Parse.Enum(relativeTo, AlignRelativeTo.Page, "reference"));
            }
            else
            {
                var mode = Parse.Key(distribute!) switch
                {
                    "left" => DistributeMode.Left,
                    "center" or "centerhorizontal" => DistributeMode.CenterHorizontal,
                    "right" => DistributeMode.Right,
                    "gaps" or "gaphorizontal" => DistributeMode.GapHorizontal,
                    "top" => DistributeMode.Top,
                    "middle" or "centervertical" => DistributeMode.CenterVertical,
                    "bottom" => DistributeMode.Bottom,
                    "vgaps" or "gapvertical" => DistributeMode.GapVertical,
                    _ => throw new McpException("distribute must be left, center, right, gaps, top, middle, bottom or vgaps."),
                };
                if (targets.Count < 3)
                    throw new McpException("Distributing needs at least three objects.");
                drawing.Actions.Distribute(targets, mode);
            }
            return Result(drawing, targets);
        });

    [McpServerTool(Name = "svg_path_operation"), Description(
        "Path operations (one undo step). On two or more shapes (the bottom one gives the style): union, difference " +
        "(bottom minus the others), intersection, exclusion, division (the bottom one cut along the others), combine. " +
        "On each shape or path: object_to_path, stroke_to_path (the stroke becomes a filled outline), break_apart, " +
        "simplify, reverse. Returns the resulting objects.")]
    public Task<SvgEditResult> SvgPathOperation(
        [Description("union, difference, intersection, exclusion, division, combine, object_to_path, stroke_to_path, break_apart, simplify or reverse.")] string operation,
        [Description(NodesHelp)] string[]? nodes = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var targets = Nodes(drawing, nodes);
            var a = drawing.Actions;
            IReadOnlyList<SvgElement> result;
            switch (Parse.Key(operation))
            {
                case "union": result = a.ApplyPathOperation(PathOperation.Union, targets); break;
                case "difference": result = a.ApplyPathOperation(PathOperation.Difference, targets); break;
                case "intersection": result = a.ApplyPathOperation(PathOperation.Intersection, targets); break;
                case "exclusion": result = a.ApplyPathOperation(PathOperation.Exclusion, targets); break;
                case "division": result = a.ApplyPathOperation(PathOperation.Division, targets); break;
                case "combine": result = a.ApplyPathOperation(PathOperation.Combine, targets); break;
                case "objecttopath": result = a.ObjectToPath(targets); break;
                case "stroketopath": result = a.StrokeToPath(targets); break;
                case "breakapart": result = a.BreakApart(targets); break;
                case "simplify": a.Simplify(targets); result = drawing.Selection.Nodes.ToList(); break;
                case "reverse": a.Reverse(targets); result = drawing.Selection.Nodes.ToList(); break;
                default:
                    throw new McpException("Unknown operation. Use union, difference, intersection, exclusion, division, combine, " +
                                           "object_to_path, stroke_to_path, break_apart, simplify or reverse.");
            }
            if (result.Count == 0 && Parse.Key(operation) is "union" or "difference" or "division" or "combine" && targets.Count < 2)
                throw new McpException($"{operation} needs at least two shapes.");
            return Result(drawing, result);
        });

    [McpServerTool(Name = "svg_cut_segment"), Description(
        "Scissors (one undo step): takes out the run of an outline at the point (x, y), between the two places where it crosses " +
        "other shapes of the drawing (for an open path, the run to its end counts). A closed shape becomes an open path, an open " +
        "one is cut in two; a shape that is not a path becomes a path. The curves are kept. Returns the objects that are left. " +
        "Fails when no crossed outline is at the point.")]
    public Task<SvgEditResult> SvgCutSegment(
        [Description("Point on the outline, in user units of the drawing.")] double x,
        [Description("Point on the outline, in user units of the drawing.")] double y,
        [Description("How far from the outline the point may be, in user units (default 2).")] double tolerance = 2,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var hit = SvgScissors.Find(drawing, new VPoint(x, y), Math.Max(tolerance, 0.01))
                ?? throw new McpException("No outline crossed by another shape at that point. Use svg_tree to see the bounds of the objects.");
            return Result(drawing, drawing.Actions.RemoveSegment(hit));
        });

    [McpServerTool(Name = "svg_clip"), Description(
        "Clips objects with the top-most of the given objects (a shape, which becomes the clip and disappears), or releases " +
        "the clip of objects. To clip a picture with a circle: svg_clip nodes=[picture, circle] with the circle above it.")]
    public Task<SvgEditResult> SvgClip(
        [Description("Release the clip instead of setting one.")] bool release = false,
        [Description(NodesHelp)] string[]? nodes = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var targets = Nodes(drawing, nodes);
            if (release)
                drawing.Actions.ReleaseClip(targets);
            else
                targets = drawing.Actions.SetClip(targets).ToList();
            return Result(drawing, targets);
        });

    [McpServerTool(Name = "svg_select"), Description(
        "Selects objects (what the window shows, and what the other svg_ tools act on when you give no nodes). Replaces " +
        "the selection; empty clears it. Selecting is not an undo step.")]
    public Task<SvgEditResult> SvgSelect(
        [Description("Node ids from svg_tree (or XML ids); empty to deselect.")] string[]? nodes = null,
        [Description(DrawingHelp)] string? document = null) =>
        context.Run(() =>
        {
            var drawing = context.Svg(document);
            var targets = nodes is { Length: > 0 } ? nodes.Select(n => Find(drawing, n)).ToList() : [];
            drawing.Selection.Set(targets);
            return Result(drawing, targets);
        });

    // ---------------------------------------------------------------- Helpers

    private static bool IsObject(SvgElement e) => e is SvgShape or SvgGroup or SvgText or SvgImage or SvgUse;

    private static string NodeId(SvgElement e) => e.InternalId.ToString(CultureInfo.InvariantCulture);

    private static string Number(double value) => NumberFormat.Format(value, 4);

    private static SvgElement Find(SvgDocument drawing, string node)
    {
        if (long.TryParse(node, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            && drawing.Root.Descendants().OfType<SvgElement>().FirstOrDefault(n => n.InternalId == id) is { } byId)
            return byId;
        if (drawing.Root.FindById(node) is SvgElement byXmlId && byXmlId != drawing.Root)
            return byXmlId;
        throw new McpException($"No object '{node}' in this drawing. Use svg_tree to list the objects.");
    }

    private static List<SvgElement> Nodes(SvgDocument drawing, string[]? nodes)
    {
        var list = nodes is { Length: > 0 } ? nodes.Select(n => Find(drawing, n)).ToList() : drawing.Selection.Nodes.ToList();
        if (list.Count == 0)
            throw new McpException("No objects given and nothing is selected. Pass node ids from svg_tree, or use svg_select first.");
        return list;
    }

    private static SvgNodeInfo Info(SvgDocument drawing, SvgElement element, int depth)
    {
        var computed = StyleResolver.ComputeFor(element);
        var box = SvgBounds.InDocument(element, drawing.GlyphProvider);
        return new SvgNodeInfo(NodeId(element), element.ElementName, element.Id, element.Label, depth,
            element.Parent is SvgElement parent && parent != drawing.Root ? NodeId(parent) : null,
            box is { } b ? new RectD(Math.Round(b.X, 3), Math.Round(b.Y, 3), Math.Round(b.Width, 3), Math.Round(b.Height, 3)) : null,
            computed.Fill.ToText(), computed.Stroke.ToText(), Math.Round(computed.Opacity, 3),
            element.Style.Get("display") != "none", element.IsLocked, (element as SvgContainer)?.Elements.Count(IsObject) ?? 0);
    }

    private SvgEditResult Result(SvgDocument drawing, IEnumerable<SvgElement> nodes)
    {
        var history = drawing.Workspace.History;
        return new SvgEditResult(context.IdOf(drawing), nodes.Distinct().Select(n => Info(drawing, n, 0)).ToList(), history.CanUndo, history.CanRedo);
    }

    private SvgEditResult Added(SvgDocument drawing, SvgElement element, string name, string? parent)
    {
        var container = parent is null ? null : Find(drawing, parent) as SvgContainer
            ?? throw new McpException($"'{parent}' is not a group.");
        drawing.Actions.AddNode(element, container, name: name);
        return Result(drawing, [element]);
    }

    private static void Paint(SvgElement element, string? fill, string? stroke, double? strokeWidth, double? opacity)
    {
        if (fill is not null)
            element.SetAttribute("fill", Paint(fill, "fill"));
        if (stroke is not null)
            element.SetAttribute("stroke", Paint(stroke, "stroke"));
        if (strokeWidth is { } width)
        {
            if (!(width >= 0 && double.IsFinite(width)))
                throw new McpException("strokeWidth must be 0 or more.");
            element.SetAttribute("stroke-width", Number(width));
        }
        if (opacity is { } o)
        {
            if (!(o >= 0 && o <= 1))
                throw new McpException("opacity must be between 0 and 1.");
            element.SetAttribute("opacity", Number(o));
        }
    }

    private static string Paint(string text, string what) =>
        SvgPaint.TryParse(text)?.ToText()
        ?? throw new McpException($"Invalid {what} '{text}'. Use #RRGGBB, a color name, none, or url(#id) of a gradient in the drawing.");

    private static void CheckValue(string property, string value)
    {
        if (value.Contains("url(", StringComparison.OrdinalIgnoreCase) && !value.Contains("url(#", StringComparison.OrdinalIgnoreCase))
            throw new McpException($"'{property}' can only reference things inside the drawing: url(#id).");
        if (value.Contains("javascript:", StringComparison.OrdinalIgnoreCase) || value.Contains('<') || value.Length > 2000)
            throw new McpException($"Invalid value for '{property}'.");
        if (property is "fill" or "stroke" && !string.IsNullOrWhiteSpace(value) && SvgPaint.TryParse(value) is null)
            throw new McpException($"Invalid {property} '{value}'. Use #RRGGBB, a color name, none, or url(#id).");
    }

    private static GradientStop ParseStop(string text)
    {
        var colon = text.IndexOf(':');
        if (colon < 0 || !double.TryParse(text[..colon], NumberStyles.Float, CultureInfo.InvariantCulture, out var offset) || offset is < 0 or > 1)
            throw new McpException($"Invalid stop '{text}': use \"offset:color\" with an offset from 0 to 1, e.g. \"0.5:#ff8800\".");
        var name = text[(colon + 1)..].Trim();
        if (name.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return new GradientStop(offset, new VColor(0, 0, 0, 0));
        if (!ColorParser.TryParse(name, out var color))
            throw new McpException($"Invalid color in stop '{text}': use #RRGGBB, #RRGGBBAA, a color name or transparent.");
        return new GradientStop(offset, color);

    }

    private static SvgElement BuildShape(SvgDocument drawing, string kind, double x, double y, double? width, double? height,
        double? x2, double? y2, double? cornerRadius, int? corners, double? ratio, double? rotation)
    {
        foreach (var value in new[] { x, y, width ?? 0, height ?? 0, x2 ?? 0, y2 ?? 0 })
            if (!double.IsFinite(value))
                throw new McpException("Coordinates must be finite numbers.");
        double Positive(double? value, string name) =>
            value is > 0 ? value.Value : throw new McpException($"{kind} needs a positive {name}.");
        var id = drawing.Root.NewId(kind);
        switch (kind)
        {
            case "rect" or "rectangle":
            {
                var rect = new SvgRect { Id = drawing.Root.NewId("rect") };
                rect.X = x;
                rect.Y = y;
                rect.Width = Positive(width, "width");
                rect.Height = Positive(height, "height");
                if (cornerRadius is > 0)
                    rect.Rx = cornerRadius.Value;
                return rect;
            }
            case "ellipse" or "circle":
            {
                var w = Positive(width, "width");
                var h = kind == "circle" ? w : Positive(height, "height");
                var ellipse = new SvgEllipse { Id = drawing.Root.NewId("ellipse") };
                ellipse.Cx = x + w / 2;
                ellipse.Cy = y + h / 2;
                ellipse.Rx = w / 2;
                ellipse.Ry = h / 2;
                return ellipse;
            }
            case "line":
            {
                var line = new SvgLine { Id = drawing.Root.NewId("line") };
                line.X1 = x;
                line.Y1 = y;
                line.X2 = x2 ?? throw new McpException("line needs x2 and y2.");
                line.Y2 = y2 ?? throw new McpException("line needs x2 and y2.");
                line.SetAttribute("stroke", "#000000");
                return line;
            }
            case "polygon" or "star":
            {
                var w = Positive(width, "width");
                var h = Positive(height, "height");
                var count = Math.Clamp(corners ?? 5, 3, 100);
                var inner = kind == "star" ? Math.Clamp(ratio ?? 0.5, 0.1, 0.95) : 0;
                var radius = Math.Min(w, h) / 2;
                var points = VectorPolygonTool.Points(new VPoint(x + w / 2, y + h / 2), radius, (rotation ?? -90) * Math.PI / 180, count, inner);
                var polygon = new SvgPolygon { Id = drawing.Root.NewId(kind) };
                polygon.Points = points.Select(p => new VPoint(Math.Round(p.X, 4), Math.Round(p.Y, 4))).ToList();
                return polygon;
            }
            default:
                _ = id;
                throw new McpException("kind must be rect, ellipse, line, polygon or star.");
        }
    }
}
