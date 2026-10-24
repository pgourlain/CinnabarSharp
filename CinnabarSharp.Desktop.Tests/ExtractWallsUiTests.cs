using Avalonia.Headless.XUnit;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Tests;

public sealed class ExtractWallsUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    /// <summary>A 120 × 80 plan: white paper, a room with 8-pixel walls and a 1-pixel line inside it.</summary>
    private ImageDocument NewPlan()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(120, 80), ColorBgra.White));
        var doc = Vm.ActiveDocument!.Image;
        var bgra = new byte[120 * 80 * 4];
        Array.Fill(bgra, (byte)255);
        void Black(int x0, int y0, int w, int h)
        {
            for (var y = y0; y < y0 + h; y++)
                for (var x = x0; x < x0 + w; x++)
                    (bgra[(y * 120 + x) * 4], bgra[(y * 120 + x) * 4 + 1], bgra[(y * 120 + x) * 4 + 2]) = (0, 0, 0);
        }
        Black(10, 10, 100, 8);
        Black(10, 62, 100, 8);
        Black(10, 10, 8, 60);
        Black(102, 10, 8, 60);
        Black(30, 40, 60, 1);
        doc.Layers.CurrentUserLayer.Surface = Utility.FromBgra(bgra, 120, 80);
        return doc;
    }

    [AvaloniaFact]
    public async Task Extract_walls_adds_a_layer_and_leaves_the_plan_untouched()
    {
        var doc = NewPlan();
        var before = doc.Layers.CurrentUserLayer.Surface.ToBgra();
        var plan = doc.Layers.CurrentUserLayer;

        await Vm.ExtractWallsCommand.ExecuteAsync(null);

        Assert.Equal(["Extract Walls"], _h.Dialogs.EffectsShown);
        Assert.Equal(["Background", "Walls"], doc.Layers.UserLayers.Select(l => l.Name));
        Assert.Equal(before, plan.Surface.ToBgra()); // the red preview is gone
        var walls = doc.Layers.CurrentUserLayer.Surface.ToBgra();
        Assert.Equal([40, 40, 230, 255], walls[((40 * 120 + 12) * 4)..((40 * 120 + 12) * 4 + 4)]); // wall, red by default
        Assert.Equal(0, walls[(40 * 120 + 60) * 4 + 3]); // the thin line is not a wall
        Assert.Equal("Extract Walls", doc.Workspace.History.Items[^1].Text);
    }

    [AvaloniaFact]
    public void The_commands_follow_the_active_document()
    {
        Assert.False(Vm.ExtractWallsCommand.CanExecute(null));

        NewPlan();

        Assert.True(Vm.ExtractWallsCommand.CanExecute(null));
        Assert.True(Vm.TraceLayerToSvgCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task The_preview_shows_the_walls_in_red_while_the_dialog_is_open()
    {
        var doc = NewPlan();
        byte[]? shown = null;
        _h.Dialogs.EffectAnswer = dialog =>
        {
            for (var i = 0; i < 500 && !dialog.PreviewTask.IsCompleted; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            shown = doc.Layers.CurrentUserLayer.Surface.ToBgra();
            return false;
        };

        await Vm.ExtractWallsCommand.ExecuteAsync(null);

        var wall = (40 * 120 + 12) * 4;
        Assert.Equal([40, 40, 230, 255], shown![wall..(wall + 4)]);
    }

    [AvaloniaFact]
    public async Task Cancelling_the_dialog_changes_nothing()
    {
        var doc = NewPlan();
        var before = doc.Layers.CurrentUserLayer.Surface.ToBgra();
        var steps = doc.Workspace.History.Items.Count;
        _h.Dialogs.EffectAnswer = _ => false;

        await Vm.ExtractWallsCommand.ExecuteAsync(null);

        Assert.Equal(steps, doc.Workspace.History.Items.Count);
        Assert.Equal(before, doc.Layers.CurrentUserLayer.Surface.ToBgra());
        Assert.Single(doc.Layers.UserLayers);
    }

    [AvaloniaFact]
    public async Task The_svg_output_opens_a_new_drawing_tab()
    {
        var doc = NewPlan();
        var tabs = Vm.Documents.Count;
        _h.Dialogs.EffectAnswer = dialog =>
        {
            dialog.Parameters.Single(p => p.Name == "Output").Value = 1;
            return true;
        };

        await Vm.ExtractWallsCommand.ExecuteAsync(null);

        Assert.Equal(tabs + 1, Vm.Documents.Count);
        Assert.True(Vm.ActiveDocument!.IsSvg);
        Assert.Single(doc.Layers.UserLayers); // the plan itself is not changed
    }

    [AvaloniaFact]
    public async Task Trace_layer_to_svg_traces_the_current_layer()
    {
        var doc = NewPlan();
        await Vm.ExtractWallsCommand.ExecuteAsync(null);
        var tabs = Vm.Documents.Count;

        await Vm.TraceLayerToSvgCommand.ExecuteAsync(null);

        Assert.Equal(tabs + 1, Vm.Documents.Count);
        Assert.True(Vm.ActiveDocument!.IsSvg);
        Assert.Equal(2, doc.Layers.UserLayers.Count);
    }
}
