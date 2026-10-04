using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Desktop.Services;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Tests;

public sealed class FileWorkflowTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    [AvaloniaFact]
    public async Task Open_shows_image_adds_recent_file_and_is_not_dirty()
    {
        _h.Dialogs.FilesToOpen.Enqueue([TestHarness.SampleImage]);

        await Vm.OpenCommand.ExecuteAsync(null);
        var frame = _h.Capture("10-opened-file");

        var doc = Assert.Single(Vm.Documents);
        Assert.Equal("sample1.png - CinnabarSharp", _h.Window.Title);
        Assert.False(doc.Document.IsDirty);
        Assert.Equal([Path.GetFullPath(TestHarness.SampleImage)], Vm.RecentFiles.Files);
        // The real pixel, not just "something drawn": the checkerboard of an empty layer used to pass.
        var image = doc.Document.Layers.GetFlattenedBgra();
        var scale = doc.Document.Workspace.Scale;
        var (x, y) = (doc.Document.ImageSize.Width / 2, doc.Document.ImageSize.Height / 2);
        var i = (y * doc.Document.ImageSize.Width + x) * 4;
        Assert.Equal((image[i + 2], image[i + 1], image[i]),
            TestHarness.PixelAt(frame, _h.CanvasToWindow((x + 0.5) * scale, (y + 0.5) * scale)));
    }

    // performance-tasks.md P5: a large file is decoded in the background; the window stays alive and says so.
    [AvaloniaFact]
    public async Task Opening_a_file_shows_the_busy_status_while_it_is_decoded()
    {
        var opening = Vm.OpenFileAsync(TestHarness.SampleImage);

        Assert.True(Vm.IsBusy);
        Assert.Equal("Opening sample1.png…", Vm.BusyText);
        Assert.True(await opening);
        Assert.False(Vm.IsBusy);
        Assert.Equal("sample1.png", Vm.ActiveDocument!.Document.DisplayName);
    }

    [AvaloniaFact]
    public async Task Opening_same_file_twice_keeps_one_tab()
    {
        await Vm.OpenFileAsync(TestHarness.SampleImage);
        Vm.CreateImage(new NewImageOptions(new ImageSize(10, 10), ColorBgra.White));

        await Vm.OpenFileAsync(TestHarness.SampleImage);

        Assert.Equal(2, Vm.Documents.Count);
        Assert.Equal("sample1.png", Vm.ActiveDocument!.Document.DisplayName);
    }

    [AvaloniaFact]
    public async Task Opening_invalid_file_shows_error()
    {
        var path = _h.TempPath("broken.png");
        await File.WriteAllTextAsync(path, "not a png");

        var ok = await Vm.OpenFileAsync(path);

        Assert.False(ok);
        Assert.Empty(Vm.Documents);
        Assert.Equal(["Could not open \"broken.png\""], _h.Dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task Save_new_image_asks_for_path_then_saves()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(30, 20), ColorBgra.White));
        var path = _h.TempPath("drawing.png");
        _h.Dialogs.SavePaths.Enqueue(path);

        await Vm.SaveCommand.ExecuteAsync(null);

        Assert.True(File.Exists(path));
        Assert.Equal("Unsaved Image 1", _h.Dialogs.LastSuggestedSaveName);
        Assert.Equal("PNG", _h.Dialogs.LastSuggestedSaveFormat!.DisplayName);
        Assert.Equal("drawing.png - CinnabarSharp", _h.Window.Title);
        Assert.Contains(Path.GetFullPath(path), Vm.RecentFiles.Files);
    }

    [AvaloniaFact]
    public async Task Save_existing_file_does_not_ask_for_path()
    {
        var path = _h.TempPath("copy.png");
        File.Copy(TestHarness.SampleImage, path);
        await Vm.OpenFileAsync(path);
        Vm.AddNewLayerCommand.Execute(null);
        Vm.Layers[0].IsVisible = false;
        _h.Dialogs.ConfirmAnswers.Enqueue(true);
        var before = File.GetLastWriteTimeUtc(path);
        await Task.Delay(20);

        await Vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(_h.Dialogs.LastSuggestedSaveName);
        Assert.Equal(["Flatten image?"], _h.Dialogs.Confirmations);
        Assert.True(File.GetLastWriteTimeUtc(path) > before);
        Assert.False(Vm.ActiveDocument!.Document.IsDirty);
    }

    [AvaloniaFact]
    public async Task Heic_photo_opens_and_save_asks_for_a_png()
    {
        var heic = Path.Combine(AppContext.BaseDirectory, "Data", "sample1.heic");
        await Vm.OpenFileAsync(heic);
        Assert.Equal("sample1.heic - CinnabarSharp", _h.Window.Title);
        _h.Dialogs.SavePaths.Enqueue(_h.TempPath("photo.png"));

        await Vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("PNG", _h.Dialogs.LastSuggestedSaveFormat!.DisplayName);
        Assert.True(File.Exists(_h.TempPath("photo.png")));
        Assert.Equal("photo.png - CinnabarSharp", _h.Window.Title);
    }

    [AvaloniaFact]
    public async Task Declining_flatten_does_not_save()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(30, 20), ColorBgra.White));
        Vm.AddNewLayerCommand.Execute(null);
        var path = _h.TempPath("layers.png");
        _h.Dialogs.SavePaths.Enqueue(path);
        _h.Dialogs.ConfirmAnswers.Enqueue(false);

        await Vm.SaveCommand.ExecuteAsync(null);

        Assert.False(File.Exists(path));
        Assert.True(Vm.ActiveDocument!.Document.IsDirty);
    }

    [AvaloniaFact]
    public async Task Save_as_without_extension_uses_suggested_format()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(30, 20), ColorBgra.White));
        _h.Dialogs.SavePaths.Enqueue(_h.TempPath("noext"));

        await Vm.SaveAsCommand.ExecuteAsync(null);

        Assert.True(File.Exists(_h.TempPath("noext.png")));
    }

    [AvaloniaFact]
    public void Editing_marks_document_dirty_in_tab_and_title()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(30, 20), ColorBgra.White));
        Assert.Equal("Unsaved Image 1", Vm.Documents[0].Title);

        Vm.Layers[0].IsVisible = false;

        Assert.Equal("Unsaved Image 1 *", Vm.Documents[0].Title);
        Assert.Equal("Unsaved Image 1 * - CinnabarSharp", _h.Window.Title);
    }

    [AvaloniaTheory]
    [InlineData(SaveChangesChoice.Cancel, 1)]
    [InlineData(SaveChangesChoice.DontSave, 0)]
    public async Task Closing_dirty_document_asks_to_save(SaveChangesChoice answer, int remaining)
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(30, 20), ColorBgra.White));
        Vm.AddNewLayerCommand.Execute(null);
        _h.Dialogs.SaveChangesAnswers.Enqueue(answer);

        await Vm.CloseCommand.ExecuteAsync(null);

        Assert.Equal(["Unsaved Image 1"], _h.Dialogs.SaveChangesAsked);
        Assert.Equal(remaining, Vm.Documents.Count);
    }

    [AvaloniaFact]
    public async Task Closing_dirty_document_with_save_writes_file_then_closes()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(30, 20), ColorBgra.White));
        Vm.Layers[0].IsVisible = false;
        _h.Dialogs.SaveChangesAnswers.Enqueue(SaveChangesChoice.Save);
        _h.Dialogs.SavePaths.Enqueue(_h.TempPath("closed.png"));

        await Vm.CloseCommand.ExecuteAsync(null);

        Assert.True(File.Exists(_h.TempPath("closed.png")));
        Assert.Empty(Vm.Documents);
        Assert.False(Vm.HasDocument);
        Assert.Equal("CinnabarSharp", _h.Window.Title);
    }

    [AvaloniaFact]
    public async Task Tab_close_button_closes_that_tab_and_activates_neighbour()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(10, 10), ColorBgra.White));
        Vm.CreateImage(new NewImageOptions(new ImageSize(20, 20), ColorBgra.White));
        Vm.CreateImage(new NewImageOptions(new ImageSize(30, 30), ColorBgra.White));
        Vm.ActiveDocument = Vm.Documents[1];
        _h.Capture("11-three-tabs");

        await Vm.CloseTabCommand.ExecuteAsync(Vm.Documents[1]);

        Assert.Equal(["Unsaved Image 1", "Unsaved Image 3"], Vm.Documents.Select(d => d.Title));
        Assert.Equal("Unsaved Image 3", Vm.ActiveDocument!.Title);
        Assert.Equal("30 × 30", Vm.ImageSizeText);
    }

    [AvaloniaFact]
    public void Quitting_with_unsaved_changes_can_be_cancelled()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(30, 20), ColorBgra.White));
        Vm.AddNewLayerCommand.Execute(null);
        _h.Dialogs.SaveChangesAnswers.Enqueue(SaveChangesChoice.Cancel);

        _h.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(_h.Window.IsVisible);
        Assert.Single(Vm.Documents);

        _h.Dialogs.SaveChangesAnswers.Enqueue(SaveChangesChoice.DontSave);
        _h.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(_h.Window.IsVisible);
    }

    [AvaloniaFact]
    public async Task Recent_files_appear_in_file_menu()
    {
        await Vm.OpenFileAsync(TestHarness.SampleImage);

        var recent = OpenRecentItems();

        Assert.Contains(Path.GetFullPath(TestHarness.SampleImage), recent);
        Assert.Contains("Clear Recent", recent);

        Vm.ClearRecentCommand.Execute(null);
        Assert.Equal(["No recent files"], OpenRecentItems());
    }

    private List<string> OpenRecentItems()
    {
        if (OperatingSystem.IsMacOS())
        {
            var file = NativeMenu.GetMenu(_h.Window)!.Items.OfType<NativeMenuItem>().First(i => i.Header == "File");
            var recent = file.Menu!.Items.OfType<NativeMenuItem>().First(i => i.Header == "Open Recent");
            return recent.Menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header!).ToList();
        }

        var menu = (Menu)_h.Window.FindControl<ContentControl>("MenuHost")!.Content!;
        var fileItem = menu.ItemsSource!.Cast<MenuItem>().First(i => (string)i.Header! == "_File");
        var recentItem = fileItem.ItemsSource!.OfType<MenuItem>().First(i => (string)i.Header! == "Open _Recent");
        return recentItem.ItemsSource!.OfType<MenuItem>().Select(i => ((string)i.Header!).Replace("__", "_")).ToList();
    }
}
