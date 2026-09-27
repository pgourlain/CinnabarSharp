using Avalonia.Headless.XUnit;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Tests;

public sealed class HistoryPanelTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    private string[] Steps() => Vm.History.Select(h => h.Text).ToArray();
    private string[] LayerNames() => Vm.Layers.Select(l => l.Name).ToArray();

    private void NewImage() => Vm.CreateImage(new NewImageOptions(new ImageSize(200, 120), ColorBgra.White));

    [AvaloniaFact]
    public void Panel_lists_steps_and_selects_the_current_one()
    {
        NewImage();
        Assert.Equal(["New Image"], Steps());
        Assert.False(Vm.UndoCommand.CanExecute(null));

        Vm.AddNewLayerCommand.Execute(null);
        Vm.DuplicateLayerCommand.Execute(null);

        Assert.Equal(["New Image", "Add New Layer", "Duplicate Layer"], Steps());
        Assert.Equal("Duplicate Layer", Vm.SelectedHistoryItem!.Text);
        Assert.True(Vm.UndoCommand.CanExecute(null));
        Assert.False(Vm.RedoCommand.CanExecute(null));
        _h.Capture("30-history-panel");
    }

    [AvaloniaFact]
    public void Undo_and_redo_restore_layers_and_modified_flag()
    {
        NewImage();
        Vm.AddNewLayerCommand.Execute(null);
        Assert.Equal("Unsaved Image 1 *", Vm.Documents[0].Title);

        Vm.UndoCommand.Execute(null);

        Assert.Equal(["Background"], LayerNames());
        Assert.Equal("Unsaved Image 1", Vm.Documents[0].Title);
        Assert.True(Vm.History[1].IsUndone);
        Assert.True(Vm.RedoCommand.CanExecute(null));

        Vm.RedoCommand.Execute(null);

        Assert.Equal(["Layer 2", "Background"], LayerNames());
        Assert.Equal("Unsaved Image 1 *", Vm.Documents[0].Title);
    }

    [AvaloniaFact]
    public void Clicking_a_step_jumps_to_it()
    {
        NewImage();
        Vm.AddNewLayerCommand.Execute(null);
        Vm.AddNewLayerCommand.Execute(null);
        Vm.AddNewLayerCommand.Execute(null);

        Vm.SelectedHistoryItem = Vm.History[1];

        Assert.Equal(["Layer 2", "Background"], LayerNames());
        Assert.Equal([false, false, true, true], Vm.History.Select(h => h.IsUndone));
        _h.Capture("31-history-jump");

        Vm.SelectedHistoryItem = Vm.History[3];
        Assert.Equal(["Layer 4", "Layer 3", "Layer 2", "Background"], LayerNames());
    }

    [AvaloniaFact]
    public void Visibility_checkbox_and_properties_dialog_are_undoable()
    {
        NewImage();
        Vm.Layers[0].IsVisible = false;
        _h.Dialogs.LayerPropertiesAnswer = p =>
        {
            p.Name = "Paper";
            return true;
        };
        Vm.LayerPropertiesCommand.Execute(null);

        Assert.Equal(["New Image", "Hide Layer", "Layer Properties"], Steps());

        Vm.UndoCommand.Execute(null);
        Vm.UndoCommand.Execute(null);

        Assert.Equal("Background", Vm.Layers[0].Name);
        Assert.True(Vm.Layers[0].IsVisible);
        Assert.False(Vm.ActiveDocument!.Document.IsDirty);
    }

    [AvaloniaFact]
    public void Each_document_has_its_own_history()
    {
        NewImage();
        Vm.AddNewLayerCommand.Execute(null);
        NewImage();

        Assert.Equal(["New Image"], Steps());

        Vm.ActiveDocument = Vm.Documents[0];
        Assert.Equal(["New Image", "Add New Layer"], Steps());
    }

    [AvaloniaFact]
    public async Task Saving_then_undoing_marks_modified_again()
    {
        NewImage();
        Vm.AddNewLayerCommand.Execute(null);
        _h.Dialogs.SavePaths.Enqueue(_h.TempPath("h.ora"));
        await Vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("h.ora", Vm.Documents[0].Title);

        Vm.UndoCommand.Execute(null);
        Assert.Equal("h.ora *", Vm.Documents[0].Title);

        Vm.RedoCommand.Execute(null);
        Assert.Equal("h.ora", Vm.Documents[0].Title);
    }
}
