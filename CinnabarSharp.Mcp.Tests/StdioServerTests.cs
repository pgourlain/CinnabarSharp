using System.Text.Json;
using ImageMagick;
using ModelContextProtocol.Protocol;

namespace CinnabarSharp.Mcp.Tests;

/// <summary>Drives "CinnabarSharp --mcp" over stdio like Claude Code does.</summary>
public class StdioServerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_the_tools_and_resources()
    {
        await using var server = await McpTestServer.StartAsync();

        var tools = (await server.Client.ListToolsAsync(cancellationToken: Ct)).Select(t => t.Name).ToList();
        Assert.Superset(new HashSet<string>
        {
            "open_image", "new_image", "save_image", "export_image", "close_image", "list_documents", "get_image_info",
            "render_preview", "get_history", "undo", "redo", "add_layer", "delete_layer", "move_layer",
            "set_layer_properties", "select_rectangle", "select_ellipse", "magic_wand", "select_all", "deselect",
            "list_effects", "apply_effect", "resize_image", "resize_canvas", "crop", "rotate_image", "prepare_for_tv",
            "prepare_folder_for_tv",
        }, tools.ToHashSet());

        var resources = (await server.Client.ListResourcesAsync(cancellationToken: Ct)).Select(r => r.Uri).ToList();
        Assert.Contains("cinnabar://documents", resources);
        Assert.Contains("cinnabar://effects", resources);

        var effects = await server.Client.ReadResourceAsync("cinnabar://effects", cancellationToken: Ct);
        var json = JsonDocument.Parse(((TextResourceContents)effects.Contents[0]).Text).RootElement;
        var blur = json.EnumerateArray().First(e => e.Str("name") == "Gaussian Blur");
        Assert.Equal("Radius", blur.Get("parameters")[0].Str("name"));
    }

    [Fact]
    public async Task Photo_for_tv_end_to_end()
    {
        await using var server = await McpTestServer.StartAsync();

        var doc = await server.Call("open_image", new { path = "sample1.png" });
        Assert.Equal(1024, doc.Int("width"));
        Assert.Equal(576, doc.Int("height"));

        await server.Call("apply_effect", new { effect = "Auto-Enhance" });
        var cropped = await server.Call("crop", new { ratio = "1:1" });
        Assert.Equal((576, 576), (cropped.Int("width"), cropped.Int("height")));

        var tv = await server.Call("prepare_for_tv", new { resolution = "2K", fit = "FitWithBorders", background = "Blurred" });
        Assert.Equal((1920, 1080), (tv.Int("width"), tv.Int("height")));
        Assert.Equal("sample1_2K", tv.Str("name"));

        var saved = await server.Call("save_image", new { path = "out/sample1_2K.jpg", jpegQuality = 85 });
        Assert.Equal("JPEG", saved.Str("format"));
        using var written = new MagickImage(Path.Combine(server.Folder, "out", "sample1_2K.jpg"));
        Assert.Equal((1920u, 1080u), (written.Width, written.Height));
        Assert.Equal(MagickFormat.Jpeg, written.Format);

        var docs = await server.Call("list_documents");
        Assert.Equal(2, docs.GetArrayLength());
        Assert.False(docs[1].Get("hasUnsavedChanges").GetBoolean());
        Assert.True(docs[0].Get("hasUnsavedChanges").GetBoolean());
    }

    [Fact]
    public async Task Files_outside_the_allowed_folders_are_refused()
    {
        await using var server = await McpTestServer.StartAsync();
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.png");

        Assert.Contains("outside the allowed folders", await server.CallError("open_image", new { path = outside }));
        Assert.Contains("outside the allowed folders", await server.CallError("open_image", new { path = "../x.png" }));

        await server.Call("new_image", new { width = 8, height = 8 });
        Assert.Contains("outside the allowed folders", await server.CallError("save_image", new { path = outside }));
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public async Task Symbolic_links_do_not_escape_the_allowed_folders()
    {
        if (OperatingSystem.IsWindows())
            return; // creating symbolic links needs developer mode on Windows
        await using var server = await McpTestServer.StartAsync();
        var outside = Directory.CreateTempSubdirectory("cinnabar-outside-").FullName;
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(server.Folder, "link"), outside);
            await server.Call("new_image", new { width = 8, height = 8 });
            Assert.Contains("outside the allowed folders", await server.CallError("save_image", new { path = "link/escape.png" }));
            Assert.Empty(Directory.GetFiles(outside));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task Destructive_operations_need_explicit_parameters()
    {
        await using var server = await McpTestServer.StartAsync();
        var before = await File.ReadAllBytesAsync(server.SamplePath, Ct);

        await server.Call("open_image", new { path = "sample1.png" });
        await server.Call("apply_effect", new { effect = "Invert Colors" });

        Assert.Contains("overwrite=true", await server.CallError("save_image"));
        Assert.Equal(before, await File.ReadAllBytesAsync(server.SamplePath, Ct));
        Assert.Contains("overwrite=true", await server.CallError("export_image", new { path = "sample1.png" }));
        Assert.Contains("discardChanges=true", await server.CallError("close_image"));

        await server.Call("save_image", new { overwrite = true });
        Assert.NotEqual(before, await File.ReadAllBytesAsync(server.SamplePath, Ct));
        var docs = await server.Call("close_image");
        Assert.Equal(0, docs.GetArrayLength());

        await server.Call("new_image", new { width = 4, height = 4 });
        await server.Call("fill_selection", new { color = "#FF0000" });
        await server.Call("close_image", new { discardChanges = true });
    }

    [Fact]
    public async Task Effects_take_named_parameters_and_choices()
    {
        await using var server = await McpTestServer.StartAsync();
        await server.Call("open_image", new { path = "sample1.png" });

        await server.Call("apply_effect", new { effect = "gaussian_blur", parameters = new { radius = 3 } });
        await server.Call("apply_effect", new { effect = "Photo Filter", parameters = new { Filter = "Noir", Intensity = 50 } });
        var history = await server.Call("get_history");
        Assert.Equal(["Open Image", "Gaussian Blur", "Photo Filter"],
            history.EnumerateArray().Select(h => h.Str("text")!).ToArray());

        Assert.Contains("Use list_effects", await server.CallError("apply_effect", new { effect = "Sparkles" }));
        Assert.Contains("no parameter 'Size'", await server.CallError("apply_effect", new { effect = "Gaussian Blur", parameters = new { Size = 2 } }));
        Assert.Contains("between", await server.CallError("apply_effect", new { effect = "Gaussian Blur", parameters = new { Radius = 100000 } }));

        var suggested = await server.Call("suggest_effect_values", new { effect = "Adjust Photo" });
        Assert.Equal(JsonValueKind.Object, suggested.Get("parameters").ValueKind);
    }

    [Fact]
    public async Task Undo_and_redo_restore_the_image()
    {
        await using var server = await McpTestServer.StartAsync();
        await server.Call("open_image", new { path = "sample1.png" });
        await server.Call("resize_image", new { width = 512 });
        await server.Call("rotate_image", new { degrees = 90 });

        var info = await server.Call("undo", new { steps = 2 });
        Assert.Equal((1024, 576), (info.Int("width"), info.Int("height")));
        Assert.False(info.Get("hasUnsavedChanges").GetBoolean());

        info = await server.Call("redo");
        Assert.Equal((512, 288), (info.Int("width"), info.Int("height")));
    }

    [Fact]
    public async Task Layers_selection_and_properties()
    {
        await using var server = await McpTestServer.StartAsync();
        await server.Call("new_image", new { width = 100, height = 50, background = "#102030" });
        await server.Call("add_layer", new { name = "Top" });
        await server.Call("select_rectangle", new { x = 10, y = 10, width = 20, height = 10 });
        await server.Call("fill_selection", new { color = "#FF8000" });

        var info = await server.Call("set_layer_properties", new { opacity = 50, blendMode = "Multiply", name = "Orange" });
        var layers = info.Get("layers");
        Assert.Equal(2, layers.GetArrayLength());
        Assert.Equal("Orange", layers[1].Str("name"));
        Assert.Equal(0.5, layers[1].Get("opacity").GetDouble());
        Assert.Equal("Multiply", layers[1].Str("blendMode"));
        Assert.Equal((10, 10, 20, 10), (info.Get("selection").Int("x"), info.Get("selection").Int("y"),
            info.Get("selection").Int("width"), info.Get("selection").Int("height")));

        info = await server.Call("move_layer", new { toIndex = 0 });
        Assert.Equal("Orange", info.Get("layers")[0].Str("name"));

        info = await server.Call("magic_wand", new { x = 0, y = 0, sampleImage = true });
        Assert.Equal(100, info.Get("selection").Int("width"));

        Assert.Contains("unknown blend mode", (await server.CallError("set_layer_properties", new { blendMode = "Sparkle" })).ToLowerInvariant());

        info = await server.Call("flatten");
        Assert.Equal(1, info.Get("layers").GetArrayLength());

        var withHistogram = await server.Call("get_image_info", new { includeHistogram = true, histogramBins = 4 });
        Assert.Equal(4, withHistogram.Get("histogram").Get("luminosity").GetArrayLength());
    }

    [Fact]
    public async Task Preview_is_a_scaled_down_png()
    {
        await using var server = await McpTestServer.StartAsync();
        await server.Call("open_image", new { path = "sample1.png" });

        var result = await server.CallRaw("render_preview", new { maxSize = 256 });
        var image = Assert.Single(result.Content.OfType<ImageContentBlock>());
        Assert.Equal("image/png", image.MimeType);
        using var png = new MagickImage(image.DecodedData.ToArray());
        Assert.Equal((256u, 144u), (png.Width, png.Height));
    }

    [Fact]
    public async Task Prepares_a_folder_for_tv()
    {
        await using var server = await McpTestServer.StartAsync();
        File.Copy(server.SamplePath, Path.Combine(server.Folder, "second.png"));

        var result = await server.Call("prepare_folder_for_tv", new { folder = ".", resolution = "2K" });
        Assert.Equal(2, result.Int("count"));
        var output = Path.Combine(server.Folder, "TV 2K");
        Assert.True(File.Exists(Path.Combine(output, "second_2K.jpg")));

        Assert.Contains("overwrite=true", await server.CallError("prepare_folder_for_tv", new { folder = ".", resolution = "2K" }));
    }
}
