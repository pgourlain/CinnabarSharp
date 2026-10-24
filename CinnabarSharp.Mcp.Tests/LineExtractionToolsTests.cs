namespace CinnabarSharp.Mcp.Tests;

/// <summary>Drives extract_walls and trace_layer_to_svg of "CinnabarSharp --mcp".</summary>
public class LineExtractionToolsTests
{
    /// <summary>A white 300 × 200 plan with a room (10-pixel walls) and a 1-pixel dimension line.</summary>
    private static async Task NewPlan(McpTestServer server)
    {
        await server.Call("new_image", new { width = 300, height = 200, background = "white" });
        foreach (var (x, y, w, h) in new[] { (20, 20, 260, 10), (20, 170, 260, 10), (20, 20, 10, 160), (270, 20, 10, 160), (60, 100, 120, 1) })
        {
            await server.Call("select_rectangle", new { x, y, width = w, height = h });
            await server.Call("fill_selection", new { color = "#000000" });
        }
        await server.Call("deselect");
    }

    [Fact]
    public async Task The_tools_are_listed()
    {
        await using var server = await McpTestServer.StartAsync();

        var tools = (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).Select(t => t.Name).ToHashSet();

        Assert.Contains("extract_walls", tools);
        Assert.Contains("trace_layer_to_svg", tools);
    }

    [Fact]
    public async Task Walls_become_a_layer_in_one_undo_step()
    {
        await using var server = await McpTestServer.StartAsync();
        await NewPlan(server);

        var info = await server.Call("extract_walls", new { color = "#FF0000" });

        Assert.Equal("image", info.Str("kind"));
        Assert.Equal(["Background", "Walls"], info.Get("layers").EnumerateArray().Select(l => l.Str("name")!).ToArray());
        var history = (await server.Call("get_history")).EnumerateArray().Select(h => h.Str("text")!).ToArray();
        Assert.Equal("Extract Walls", history[^1]);

        info = await server.Call("undo");
        Assert.Equal(1, info.Get("layers").GetArrayLength());
    }

    [Fact]
    public async Task Walls_become_a_drawing_with_one_outline_and_a_hole()
    {
        await using var server = await McpTestServer.StartAsync();
        await NewPlan(server);

        var drawing = await server.Call("extract_walls", new { output = "svg", includePlan = false });

        Assert.Equal("svg", drawing.Str("kind"));
        var tree = await server.Call("svg_tree");
        Assert.Equal(["g", "path"], tree.EnumerateArray().Select(n => n.Str("element")!).ToArray());
    }

    [Fact]
    public async Task A_scale_gives_the_drawing_real_units()
    {
        await using var server = await McpTestServer.StartAsync();
        await NewPlan(server);

        var drawing = await server.Call("extract_walls", new { output = "svg", includePlan = false, pixelsPerMeter = 100 });

        // 300 px at 100 px per meter = 3 m = 3000 mm, reported in CSS pixels (96 per inch).
        Assert.Equal((int)Math.Round(3000 / 25.4 * 96), drawing.Int("width"));
    }

    [Fact]
    public async Task A_layer_can_be_traced_after_extraction()
    {
        await using var server = await McpTestServer.StartAsync();
        await NewPlan(server);
        await server.Call("extract_walls");

        var drawing = await server.Call("trace_layer_to_svg");

        Assert.Equal("svg", drawing.Str("kind"));
        Assert.Contains("path", (await server.Call("svg_tree")).EnumerateArray().Select(n => n.Str("element")));
    }

    [Fact]
    public async Task Bad_arguments_say_what_is_valid()
    {
        await using var server = await McpTestServer.StartAsync();
        await NewPlan(server);

        Assert.Contains("Valid values", await server.CallError("extract_walls", new { output = "pdf" }));
        Assert.Contains("pixelsPerMeter", await server.CallError("extract_walls", new { output = "svg", pixelsPerMeter = -1 }));
    }
}
