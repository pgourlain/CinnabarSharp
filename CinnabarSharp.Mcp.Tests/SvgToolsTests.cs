using System.Text.Json;
using ImageMagick;

namespace CinnabarSharp.Mcp.Tests;

/// <summary>Drives the svg_ tools of "CinnabarSharp --mcp" like Claude Code does.</summary>
public class SvgToolsTests
{
    private static string[] Nodes(JsonElement result) =>
        result.Get("nodes").EnumerateArray().Select(n => n.Str("node")!).ToArray();

    private static (byte R, byte G, byte B, byte A) Pixel(string png, int x, int y)
    {
        using var image = new MagickImage(png);
        using var pixels = image.GetPixels();
        var p = pixels.ToByteArray(x, y, 1, 1, PixelMapping.RGBA)!;
        return (p[0], p[1], p[2], p[3]);
    }

    [Fact]
    public async Task Lists_the_svg_tools_and_the_new_documents_are_svg()
    {
        await using var server = await McpTestServer.StartAsync();
        var tools = (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).Select(t => t.Name).ToHashSet();
        Assert.Superset(new HashSet<string>
        {
            "new_svg", "svg_tree", "svg_get_node", "svg_add_shape", "svg_add_path", "svg_add_text", "svg_add_image", "svg_set_style",
            "svg_set_attributes", "svg_transform", "svg_delete", "svg_duplicate", "svg_group", "svg_ungroup", "svg_reorder",
            "svg_align", "svg_path_operation", "svg_select", "svg_set_gradient", "svg_rename", "svg_clip",
        }, tools);

        var document = await server.Call("new_svg", new { width = 200, height = 100, units = "px" });
        Assert.Equal("svg", document.Str("kind"));
        Assert.Equal(200, document.Int("width"));
    }

    [Fact]
    public async Task A_badge_from_shapes_and_a_union_saved_exported_and_reopened()
    {
        await using var server = await McpTestServer.StartAsync();
        await server.Call("new_svg", new { width = 200, height = 200 });

        var square = Nodes(await server.Call("svg_add_shape", new { kind = "rect", x = 20, y = 20, width = 100, height = 100, fill = "#cc2200" }))[0];
        var circle = Nodes(await server.Call("svg_add_shape", new { kind = "ellipse", x = 80, y = 80, width = 100, height = 100, fill = "#cc2200" }))[0];
        var union = await server.Call("svg_path_operation", new { operation = "union", nodes = new[] { square, circle } });
        var badge = Nodes(union).Single();
        Assert.Equal("path", union.Get("nodes")[0].Str("element"));
        await server.Call("svg_set_style", new { nodes = new[] { badge }, properties = new Dictionary<string, string> { ["fill"] = "#2255cc", ["stroke"] = "#ffffff", ["stroke-width"] = "4" } });

        var tree = await server.Call("svg_tree");
        // The layer the shapes live in, then the badge.
        Assert.Equal(["g", "path"], tree.EnumerateArray().Select(n => n.Str("element")!).ToArray());
        Assert.Equal("#2255cc", tree[1].Str("fill"));

        var svg = Path.Combine(server.Folder, "badge.svg");
        await server.Call("save_image", new { path = svg });
        var png = Path.Combine(server.Folder, "badge.png");
        await server.Call("export_image", new { path = png, width = 100, background = "#ffffff" });

        Assert.Equal((255, 255, 255, 255), Pixel(png, 2, 2));                 // the white background
        Assert.Equal((0x22, 0x55, 0xcc, 255), Pixel(png, 50, 50));            // inside the union
        Assert.Equal((255, 255, 255, 255), Pixel(png, 5, 95));                // outside it

        // The saved file is a drawing again when opened.
        await server.Call("close_image", new { discardChanges = true });
        var reopened = await server.Call("open_image", new { path = svg });
        Assert.Equal("svg", reopened.Str("kind"));
        Assert.Equal(2, (await server.Call("svg_tree")).GetArrayLength());
        var details = await server.Call("svg_get_node", new { node = (await server.Call("svg_tree"))[1].Str("node") });
        Assert.Equal("path", details.Get("info").Str("element"));
    }

    [Fact]
    public async Task Edits_are_undoable_and_the_preview_shows_the_drawing()
    {
        await using var server = await McpTestServer.StartAsync();
        await server.Call("new_svg", new { width = 100, height = 100 });
        var node = Nodes(await server.Call("svg_add_shape", new { kind = "rect", x = 10, y = 10, width = 50, height = 50, fill = "#00aa00" }))[0];
        await server.Call("svg_transform", new { action = "move", dx = 20, dy = 0, nodes = new[] { node } });

        var history = (await server.Call("get_history")).EnumerateArray().Select(h => h.Str("text")!).ToArray();
        Assert.Equal(["Add rect", "Move"], history[^2..]);
        var bounds = (await server.Call("svg_get_node", new { node })).Get("info").Get("bounds");
        Assert.Equal(30, bounds.Get("x").GetDouble());

        await server.Call("undo");
        Assert.Equal(10, (await server.Call("svg_get_node", new { node })).Get("info").Get("bounds").Get("x").GetDouble());
        await server.Call("redo");
        Assert.Equal(30, (await server.Call("svg_get_node", new { node })).Get("info").Get("bounds").Get("x").GetDouble());

        var preview = await server.CallRaw("render_preview");
        Assert.True(preview.IsError != true);
        Assert.Contains(preview.Content, c => c is ModelContextProtocol.Protocol.ImageContentBlock);
    }

    [Fact]
    public async Task Gradients_groups_alignment_and_clipping()
    {
        await using var server = await McpTestServer.StartAsync();
        await server.Call("new_svg", new { width = 200, height = 200 });
        var a = Nodes(await server.Call("svg_add_shape", new { kind = "rect", x = 10, y = 10, width = 40, height = 40, fill = "#ff0000" }))[0];
        var b = Nodes(await server.Call("svg_add_shape", new { kind = "star", x = 100, y = 60, width = 60, height = 60, corners = 5, fill = "#00ff00" }))[0];
        var c = Nodes(await server.Call("svg_add_shape", new { kind = "line", x = 5, y = 150, x2 = 190, y2 = 170, stroke = "#0000ff", strokeWidth = 6 }))[0];

        await server.Call("svg_set_gradient", new { stops = new[] { "0:#ff0000", "1:#0000ff" }, nodes = new[] { a } });
        Assert.StartsWith("url(#", (await server.Call("svg_get_node", new { node = a })).Get("info").Str("fill"));

        await server.Call("svg_align", new { align = "right", relativeTo = "page", nodes = new[] { a, b } });
        var box = (await server.Call("svg_get_node", new { node = a })).Get("info").Get("bounds");
        Assert.Equal(200, box.Get("x").GetDouble() + box.Get("width").GetDouble(), 1);

        var group = Nodes(await server.Call("svg_group", new { nodes = new[] { a, b } }))[0];
        var tree = await server.Call("svg_tree");
        Assert.Equal(["g", "g", "rect", "polygon", "line"], tree.EnumerateArray().Select(n => n.Str("element")!).ToArray());
        Assert.Equal(2, tree[2].Int("depth"));
        await server.Call("svg_ungroup", new { nodes = new[] { group } });
        await server.Call("svg_reorder", new { move = "to_bottom", nodes = new[] { c } });
        Assert.Equal("line", (await server.Call("svg_tree"))[1].Str("element"));
    }

    [Fact]
    public async Task A_drawing_can_be_resized_and_its_canvas_changed()
    {
        await using var server = await McpTestServer.StartAsync();
        await server.Call("new_svg", new { width = 100, height = 50 });
        var resized = await server.Call("resize_image", new { width = 200 });
        Assert.Equal((200, 100), (resized.Int("width"), resized.Int("height")));
        var canvas = await server.Call("resize_canvas", new { width = 300, height = 100, anchor = "NW" });
        Assert.Equal((300, 100), (canvas.Int("width"), canvas.Int("height")));
        await server.Call("undo", new { steps = 2 });
        Assert.Equal(100, (await server.Call("list_documents"))[0].Int("width"));
    }

    [Fact]
    public async Task Refuses_misuse_with_clear_messages()
    {
        await using var server = await McpTestServer.StartAsync();
        await server.Call("new_image", new { width = 20, height = 20 });
        var raster = await server.CallError("svg_tree");
        Assert.Contains("image", raster);
        Assert.Contains("SVG", raster);

        await server.Call("new_svg", new { width = 50, height = 50 });
        Assert.Contains("SVG", await server.CallError("fill_selection", new { color = "#ff0000", document = "2" }));
        Assert.Contains("svg_tree", await server.CallError("svg_get_node", new { node = "nope" }));
        Assert.Contains("fill", await server.CallError("svg_add_shape", new { kind = "rect", x = 0, y = 0, width = 5, height = 5, fill = "url(http://evil/x)" }));
        var node = Nodes(await server.Call("svg_add_shape", new { kind = "rect", x = 0, y = 0, width = 5, height = 5 }))[0];
        Assert.Contains("not a style property", await server.CallError("svg_set_style", new { nodes = new[] { node }, properties = new Dictionary<string, string> { ["onclick"] = "x" } }));
        Assert.Contains("can't be set", await server.CallError("svg_set_attributes", new { node, attributes = new Dictionary<string, string> { ["href"] = "x" } }));
        Assert.Contains("can't contain references", await server.CallError("svg_set_attributes", new { node, attributes = new Dictionary<string, string> { ["x"] = "url(#a)" } }));
        Assert.Contains("export_image", await server.CallError("save_image", new { path = Path.Combine(server.Folder, "x.png") }));
        Assert.Contains("outside the allowed", await server.CallError("svg_add_image", new { path = "/etc/hosts" }));
    }

    [Fact]
    public async Task A_linked_picture_outside_the_folder_is_never_read()
    {
        await using var server = await McpTestServer.StartAsync();
        var secret = Path.Combine(Path.GetDirectoryName(server.Folder)!, "cinnabar-secret.png");
        File.Copy(server.SamplePath, secret, overwrite: true);
        try
        {
            var svg = Path.Combine(server.Folder, "link.svg");
            await File.WriteAllTextAsync(svg, "<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='50' height='50'>" +
                "<rect width='50' height='50' fill='#ffffff'/><image width='50' height='50' xlink:href='../cinnabar-secret.png'/></svg>", TestContext.Current.CancellationToken);
            await server.Call("open_image", new { path = svg });
            var png = Path.Combine(server.Folder, "link.png");
            await server.Call("export_image", new { path = png });
            // The picture is not drawn: no pixel of the photo, only the white rect (the placeholder is a flat gray box).
            var (r, g, b, _) = Pixel(png, 25, 25);
            Assert.True(r == g && g == b, $"expected a flat color, got {r},{g},{b}");
        }
        finally
        {
            File.Delete(secret);
        }
    }
}
