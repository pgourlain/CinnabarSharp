using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Tests;

public sealed class LayersPanelTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    private string[] Names() => Vm.Layers.Select(l => l.Name).ToArray();

    private void NewImage() => Vm.CreateImage(new NewImageOptions(new ImageSize(300, 200), ColorBgra.White));

    /// <summary>Fills the current layer with an opaque color.</summary>
    private void FillCurrentLayer(byte b, byte g, byte r)
    {
        var layers = Vm.ActiveDocument!.Image.Layers;
        var surface = layers.CurrentUserLayer.Surface;
        var px = new byte[surface.Width * surface.Height * 4];
        for (var i = 0; i < px.Length; i += 4)
            (px[i], px[i + 1], px[i + 2], px[i + 3]) = (b, g, r, 255);
        layers.CurrentUserLayer.Surface = Utility.FromBgra(px, (int)surface.Width, (int)surface.Height);
        surface.Dispose();
        Vm.ActiveDocument.Image.Workspace.Invalidate();
    }

    [AvaloniaFact]
    public void Commands_are_enabled_only_when_they_apply()
    {
        NewImage();
        Assert.False(Vm.DeleteLayerCommand.CanExecute(null));
        Assert.False(Vm.MergeLayerDownCommand.CanExecute(null));
        Assert.False(Vm.MoveLayerUpCommand.CanExecute(null));
        Assert.False(Vm.FlattenCommand.CanExecute(null));

        Vm.AddNewLayerCommand.Execute(null);

        Assert.True(Vm.DeleteLayerCommand.CanExecute(null));
        Assert.True(Vm.MergeLayerDownCommand.CanExecute(null));
        Assert.False(Vm.MoveLayerUpCommand.CanExecute(null));
        Assert.True(Vm.MoveLayerDownCommand.CanExecute(null));
        Assert.True(Vm.FlattenCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Duplicate_move_merge_delete_update_the_panel()
    {
        NewImage();
        Vm.AddNewLayerCommand.Execute(null);

        Vm.DuplicateLayerCommand.Execute(null);
        Assert.Equal(["Layer 2 copy", "Layer 2", "Background"], Names());
        Assert.Equal("Layer 2 copy", Vm.SelectedLayer!.Name);

        Vm.MoveLayerDownCommand.Execute(null);
        Assert.Equal(["Layer 2", "Layer 2 copy", "Background"], Names());
        Assert.Equal("Layer 2 copy", Vm.SelectedLayer!.Name);

        Vm.MergeLayerDownCommand.Execute(null);
        Assert.Equal(["Layer 2", "Background"], Names());
        Assert.Equal("Background", Vm.SelectedLayer!.Name);

        Vm.SelectedLayer = Vm.Layers[0];
        Vm.DeleteLayerCommand.Execute(null);
        Assert.Equal(["Background"], Names());
        Assert.True(Vm.ActiveDocument!.Image.IsDirty);
    }

    [AvaloniaFact]
    public void Blend_mode_and_opacity_render_on_canvas()
    {
        NewImage();
        Vm.AddNewLayerCommand.Execute(null);
        FillCurrentLayer(0, 0, 255);
        var top = Vm.ActiveDocument!.Image.Layers.CurrentUserLayer;

        top.BlendMode = BlendMode.Difference;
        var frame = _h.Capture("20-difference-blend");
        Assert.Equal((0, 255, 255), TestHarness.PixelAt(frame, _h.CanvasToWindow(150, 100)));
        Assert.Equal("Difference", Vm.Layers[0].Details);

        top.BlendMode = BlendMode.Normal;
        top.Opacity = 0.5;
        frame = _h.Capture("21-half-opacity");
        Assert.Equal((255, 127, 127), TestHarness.PixelAt(frame, _h.CanvasToWindow(150, 100)));
        Assert.Equal("50%", Vm.Layers[0].Details);
    }

    [AvaloniaFact]
    public async Task Layer_properties_ok_applies_changes()
    {
        NewImage();
        _h.Dialogs.LayerPropertiesAnswer = p =>
        {
            p.Name = "Shadow";
            p.Opacity = 40;
            p.BlendMode = LayerPropertiesViewModel.BlendModes.First(b => b.Mode == BlendMode.Multiply);
            return true;
        };

        await Vm.LayerPropertiesCommand.ExecuteAsync(null);

        var layer = Vm.ActiveDocument!.Image.Layers.CurrentUserLayer;
        Assert.Equal("Shadow", layer.Name);
        Assert.Equal(0.4, layer.Opacity, 3);
        Assert.Equal(BlendMode.Multiply, layer.BlendMode);
        Assert.Equal("Shadow", Vm.Layers[0].Name);
        Assert.Equal("Multiply · 40%", Vm.Layers[0].Details);
        Assert.True(Vm.ActiveDocument.Image.IsDirty);
    }

    [AvaloniaFact]
    public async Task Layer_properties_cancel_reverts_preview()
    {
        NewImage();
        _h.Dialogs.LayerPropertiesAnswer = p =>
        {
            p.Name = "Temp";
            p.IsVisible = false;
            p.Opacity = 10;
            return false;
        };

        await Vm.LayerPropertiesCommand.ExecuteAsync(null);

        var layer = Vm.ActiveDocument!.Image.Layers.CurrentUserLayer;
        Assert.Equal("Background", layer.Name);
        Assert.False(layer.Hidden);
        Assert.Equal(1, layer.Opacity);
        Assert.False(Vm.ActiveDocument.Image.IsDirty);
    }

    [AvaloniaFact]
    public void Blend_mode_names_are_readable()
    {
        Assert.Contains(LayerPropertiesViewModel.BlendModes, b => b.Name == "Color Burn");
        Assert.Contains(LayerPropertiesViewModel.BlendModes, b => b.Name == "Hard Light");
        Assert.Equal(16, LayerPropertiesViewModel.BlendModes.Count);
    }

    [AvaloniaFact]
    public void Thumbnails_fit_in_the_panel()
    {
        NewImage();
        Vm.AddNewLayerCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        foreach (var layer in Vm.Layers)
        {
            Assert.Equal(LayerViewModel.ThumbnailSize, layer.Thumbnail.PixelSize.Width);
            Assert.True(layer.Thumbnail.PixelSize.Height <= LayerViewModel.ThumbnailSize);
        }
        _h.Capture("22-layers-panel");
    }

    [AvaloniaFact]
    public async Task Saving_as_ora_keeps_layers_without_flatten_warning()
    {
        NewImage();
        Vm.AddNewLayerCommand.Execute(null);
        Vm.AddNewLayerCommand.Execute(null);
        var path = _h.TempPath("art.ora");
        _h.Dialogs.SavePaths.Enqueue(path);

        await Vm.SaveCommand.ExecuteAsync(null);
        await Vm.CloseCommand.ExecuteAsync(null);
        await Vm.OpenFileAsync(path);

        Assert.Empty(_h.Dialogs.Confirmations);
        Assert.Equal(["Layer 3", "Layer 2", "Background"], Names());
        Assert.Equal("art.ora - CinnabarSharp", _h.Window.Title);
    }

    [AvaloniaFact]
    public void Flatten_merges_into_one_layer()
    {
        NewImage();
        Vm.AddNewLayerCommand.Execute(null);
        Vm.AddNewLayerCommand.Execute(null);

        Vm.FlattenCommand.Execute(null);

        Assert.Equal(["Background"], Names());
        Assert.False(Vm.FlattenCommand.CanExecute(null));
    }
}
