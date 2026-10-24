using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Photo;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Tests;

public sealed class WallExtractionTests : BaseTests
{
    private const int W = 200, H = 140;

    private static void Fill(byte[] bgra, int x, int y, int w, int h, byte gray = 0)
    {
        for (var j = y; j < y + h; j++)
            for (var i = x; i < x + w; i++)
                (bgra[(j * W + i) * 4], bgra[(j * W + i) * 4 + 1], bgra[(j * W + i) * 4 + 2], bgra[(j * W + i) * 4 + 3]) = (gray, gray, gray, 255);
    }

    /// <summary>A plan on white paper: a room with 8-pixel walls, a 1-pixel dimension line above it, and a small "text" blob.</summary>
    private static byte[] Plan(bool lightLines = false)
    {
        var bgra = new byte[W * H * 4];
        Array.Fill(bgra, (byte)255);
        Fill(bgra, 30, 30, 120, 8);
        Fill(bgra, 30, 102, 120, 8);
        Fill(bgra, 30, 30, 8, 80);
        Fill(bgra, 142, 30, 8, 80);
        Fill(bgra, 30, 15, 120, 1); // dimension line
        Fill(bgra, 30, 10, 1, 12); // its end tick
        Fill(bgra, 60, 60, 2, 9); // text-like marks inside the room
        Fill(bgra, 65, 60, 2, 9);
        if (lightLines)
            for (var i = 0; i < bgra.Length; i++)
                if (i % 4 != 3)
                    bgra[i] = (byte)(255 - bgra[i]);
        return bgra;
    }

    private static bool On(byte[] mask, int x, int y) => mask[y * W + x] != 0;

    private static LineExtractionOptions Options(LineMode mode = LineMode.Walls, int thickness = 5, int bridge = 0, bool light = false) =>
        new(mode, 0, thickness, bridge, 20, light);

    [Fact]
    public void Walls_stay_and_thin_strokes_go()
    {
        var mask = LineExtraction.Extract(Plan(), W, H, Options());

        Assert.True(On(mask, 34, 70)); // left wall
        Assert.True(On(mask, 80, 34)); // top wall
        Assert.True(On(mask, 146, 70)); // right wall
        Assert.True(On(mask, 80, 106)); // bottom wall
        Assert.False(On(mask, 80, 15)); // dimension line
        Assert.False(On(mask, 30, 12)); // its tick
        Assert.False(On(mask, 61, 64)); // text mark
        Assert.False(On(mask, 80, 70)); // empty room
    }

    [Fact]
    public void Walls_keep_their_full_thickness_and_ends()
    {
        var mask = LineExtraction.Extract(Plan(), W, H, Options());

        Assert.Equal(2 * 120 * 8 + 2 * 8 * 64, mask.Count(v => v != 0));
    }

    [Fact]
    public void Light_lines_on_a_dark_background()
    {
        var mask = LineExtraction.Extract(Plan(lightLines: true), W, H, Options(light: true));

        Assert.True(On(mask, 34, 70));
        Assert.False(On(mask, 80, 15));
        Assert.Equal(2 * 120 * 8 + 2 * 8 * 64, mask.Count(v => v != 0));
    }

    [Fact]
    public void Thin_lines_mode_keeps_the_dimension_line_and_not_the_walls()
    {
        var mask = LineExtraction.Extract(Plan(), W, H, Options(LineMode.ThinLines) with { MinArea = 5 });

        Assert.True(On(mask, 80, 15));
        Assert.True(On(mask, 61, 64));
        Assert.False(On(mask, 34, 70));
        Assert.False(On(mask, 80, 34));
    }

    [Fact]
    public void A_break_in_a_wall_is_bridged_up_to_the_gap()
    {
        var plan = Plan();
        Fill(plan, 80, 30, 4, 8, 255); // 4-pixel break in the top wall

        var open = LineExtraction.Extract(plan, W, H, Options());
        var bridged = LineExtraction.Extract(plan, W, H, Options(bridge: 4));

        Assert.False(On(open, 81, 34));
        Assert.True(On(bridged, 81, 34));
        Assert.True(On(bridged, 40, 34));
    }

    [Fact]
    public void Small_specks_are_removed()
    {
        var plan = Plan();
        Fill(plan, 100, 60, 6, 6); // thick enough to survive the opening, but only 36 pixels

        var kept = LineExtraction.Extract(plan, W, H, Options() with { MinArea = 20 });
        var dropped = LineExtraction.Extract(plan, W, H, Options() with { MinArea = 40 });

        Assert.True(On(kept, 102, 62));
        Assert.False(On(dropped, 102, 62));
    }

    [Fact]
    public void The_selection_limits_the_extraction_without_thinning_the_walls()
    {
        var selection = SelectionMask.Rectangle(W, H, new PointD(0, 0), new PointD(90, H));

        var mask = LineExtraction.Extract(Plan(), W, H, Options(), selection);

        Assert.True(On(mask, 34, 70));
        Assert.True(On(mask, 80, 34));
        Assert.False(On(mask, 146, 70));
    }

    [Fact]
    public void Suggested_thickness_separates_walls_from_thin_lines()
    {
        var thickness = LineExtraction.SuggestThickness(Plan(), W, H);

        Assert.InRange(thickness, 3, 8);
        var mask = LineExtraction.Extract(Plan(), W, H, Options(thickness: thickness));
        Assert.True(On(mask, 34, 70));
        Assert.False(On(mask, 80, 15));
    }

    [Fact]
    public void Extraction_is_deterministic_and_pinned()
    {
        var first = LineExtraction.Extract(Plan(), W, H, Options());
        var second = LineExtraction.Extract(Plan(), W, H, Options());

        Assert.Equal(first, second);
        Assert.Equal("D01156166D1523EE849AFADE15779D1E276AA420A868A73BDDECA2958691D6A8",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(first)));
    }

    [Fact]
    public void Cancelling_stops_the_extraction()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => LineExtraction.Extract(Plan(), W, H, Options(), cancellation: cancelled.Token));
    }

    // ---- The effect (preview) ----

    [Fact]
    public void The_preview_marks_the_walls_in_red_over_a_faded_plan()
    {
        var effect = new ExtractWallsEffect();
        var context = new EffectContext(Plan(), W, H, ColorBgra.Black, ColorBgra.White);
        var destination = new byte[W * H * 4];

        effect.Render(context, new RectangleI(0, 0, W, H), destination, effect.Defaults, CancellationToken.None);

        var wall = (70 * W + 34) * 4;
        Assert.Equal([40, 40, 230, 255], destination[wall..(wall + 4)]);
        var paper = (70 * W + 100) * 4;
        Assert.Equal([255, 255, 255, 255], destination[paper..(paper + 4)]);
    }

    [Fact]
    public void The_preview_is_the_same_in_strips_as_in_one_block()
    {
        var effect = new ExtractWallsEffect();
        var context = new EffectContext(Plan(), W, H, ColorBgra.Black, ColorBgra.White);
        var whole = new byte[W * H * 4];
        effect.Render(context, new RectangleI(0, 0, W, H), whole, effect.Defaults, CancellationToken.None);
        var strip = new byte[W * 50 * 4];

        effect.Render(context, new RectangleI(0, 30, W, 50), strip, effect.Defaults, CancellationToken.None);

        Assert.Equal(whole[(30 * W * 4)..(80 * W * 4)], strip);
    }

    [Fact]
    public void Suggested_values_set_the_thickness()
    {
        var effect = new ExtractWallsEffect();
        var context = new EffectContext(Plan(), W, H, ColorBgra.Black, ColorBgra.White);

        var values = effect.SuggestValues(context)!;

        Assert.InRange(values[3], 3, 8);
        Assert.Equal(effect.Parameters.Count, values.Count);
    }

    [Fact]
    public void Settings_follow_the_parameters()
    {
        var values = new ExtractWallsEffect().Defaults.ToArray();
        values[0] = 1;
        values[1] = 1;
        values[3] = 9;
        values[6] = 1;
        values[9] = 2000;
        values[10] = 1;

        var settings = ExtractWallsEffect.Settings(values);

        Assert.Equal(LineMode.ThinLines, settings.Lines.Mode);
        Assert.True(settings.Lines.LightLines);
        Assert.Equal(9, settings.Lines.MinThickness);
        Assert.Equal(WallsOutput.Svg, settings.Output);
        Assert.Equal(2000, settings.PixelsPerMeter);
        Assert.False(settings.IncludePlan);
        Assert.Equal(0.5, WallExtraction.MillimetresPerPixel(settings));
    }

    // ---- The document ----

    private (IWorkspaceService Workspace, ImageDocument Document) PlanDocument()
    {
        var workspace = CinnabarSharpService().GetRequiredService<IWorkspaceService>();
        var document = workspace.NewDocument(new ImageSize(W, H), ColorBgra.White);
        document.Layers.CurrentUserLayer.Surface = Utility.FromBgra(Plan(), W, H);
        return (workspace, document);
    }

    [Fact]
    public void Layer_output_adds_one_undoable_layer_with_the_walls()
    {
        var (workspace, document) = PlanDocument();
        var layers = document.Layers.UserLayers.Count;
        var settings = WallExtractionSettings.Default;

        var data = WallExtraction.Compute(document, settings);
        var result = WallExtraction.Apply(document, workspace, data, settings, ColorBgra.FromBgra(10, 20, 200, 255));

        Assert.Equal(layers + 1, document.Layers.UserLayers.Count);
        Assert.Equal(WallExtraction.LayerName, result.Layer!.Name);
        Assert.Same(result.Layer, document.Layers.CurrentUserLayer);
        var pixels = result.Layer.Surface.ToBgra();
        Assert.Equal([10, 20, 200, 255], pixels[((70 * W + 34) * 4)..((70 * W + 34) * 4 + 4)]);
        Assert.Equal(0, pixels[(70 * W + 100) * 4 + 3]);
        Assert.Equal("Extract Walls", document.Workspace.History.Items[^1].Text);

        document.Workspace.History.Undo();
        Assert.Equal(layers, document.Layers.UserLayers.Count);
    }

    [Fact]
    public void Svg_output_opens_a_drawing_with_the_plan_and_the_walls()
    {
        var (workspace, document) = PlanDocument();
        var settings = WallExtractionSettings.Default with { Output = WallsOutput.Svg };

        var data = WallExtraction.Compute(document, settings);
        var result = WallExtraction.Apply(document, workspace, data, settings, ColorBgra.FromBgra(0, 0, 0, 255));

        var drawing = Assert.IsType<SvgDocument>(result.Drawing);
        Assert.Equal(1, result.OutlineCount); // the room is one connected piece with a hole
        var layers = drawing.Root.Elements.OfType<SvgGroup>().Where(g => g.IsLayer).ToList();
        Assert.Equal(["Plan", "Walls"], layers.Select(l => l.Label));
        Assert.True(layers[0].IsLocked);
        var wall = Assert.Single(layers[1].Elements.OfType<SvgPath>());
        Assert.Equal(2, wall.CreatePath().Figures.Count()); // outline and hole
        Assert.Single(layers[0].Elements.OfType<SvgImage>());
    }

    [Fact]
    public void Svg_output_without_the_plan_or_with_a_scale()
    {
        var (workspace, document) = PlanDocument();
        var settings = WallExtractionSettings.Default with { Output = WallsOutput.Svg, IncludePlan = false, PixelsPerMeter = 100 };

        var data = WallExtraction.Compute(document, settings);
        var drawing = WallExtraction.Apply(document, workspace, data, settings, ColorBgra.Black).Drawing!;

        Assert.Single(drawing.Root.Elements.OfType<SvgGroup>());
        Assert.Equal("2000mm", drawing.Root.GetAttribute("width")); // 200 px at 100 px per meter
        var box = drawing.Root.Elements.OfType<SvgGroup>().Single().Elements.OfType<SvgPath>().Single().CreatePath().Segments;
        Assert.Contains(box, s => s.Kind == SegmentKind.MoveTo && Math.Abs(s.End.X - 30 * 10) < 1e-6); // 30 px = 300 mm
    }

    [Fact]
    public void A_walls_layer_edited_by_hand_can_be_traced()
    {
        var (workspace, document) = PlanDocument();
        var settings = WallExtractionSettings.Default with { Output = WallsOutput.Svg, IncludePlan = false };
        var layerData = WallExtraction.Compute(document, WallExtractionSettings.Default);
        WallExtraction.Apply(document, workspace, layerData, WallExtractionSettings.Default, ColorBgra.Black);

        var data = WallExtraction.Compute(document, settings, fromLayerAlpha: true);

        Assert.Equal(layerData.Mask, data.Mask);
        Assert.Single(data.Outlines!);
    }
}
