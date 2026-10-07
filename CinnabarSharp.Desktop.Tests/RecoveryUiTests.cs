using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Desktop.Services;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Vector;
using Microsoft.Extensions.DependencyInjection;

namespace CinnabarSharp.Desktop.Tests;

public sealed class RecoveryUiTests : IDisposable
{
    private readonly TestHarness _h = new();
    private readonly string _root;

    public RecoveryUiTests()
    {
        _root = _h.TempPath("recovery");
    }

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    private RecoveryStore Store(int pid) => new(_root, _h.Services.GetRequiredService<IFormatManager>(), _ => false, pid);

    private SvgDocument NewDrawing()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(200, 100), ColorBgra.Transparent, new SvgDrawingOptions(200, 100, SvgUnit.Px)));
        var svg = Assert.IsType<SvgDocument>(Vm.ActiveDocument!.Document);
        var rect = new SvgRect { Id = "r" };
        rect.Width = 40;
        rect.Height = 30;
        svg.Actions.AddNode(rect);
        return svg;
    }

    [AvaloniaFact]
    public async Task Dirty_documents_get_a_copy_that_follows_them_and_goes_when_they_are_saved_or_closed()
    {
        Vm.Recovery = Store(100);
        Vm.StartRecoverySession(timer: false);
        var svg = NewDrawing();

        Vm.AutosaveNow();
        Assert.True(Vm.Recovery.Has(svg));
        var data = Directory.GetFiles(_root, "*.svg", SearchOption.AllDirectories).Single();
        var written = File.GetLastWriteTimeUtc(data);

        Thread.Sleep(30);
        Vm.AutosaveNow();                                        // nothing changed: nothing is written again
        Assert.Equal(written, File.GetLastWriteTimeUtc(data));
        svg.Actions.MoveBy([(SvgElement)svg.Root.FindById("r")!], 5, 5);
        Vm.AutosaveNow();
        Assert.True(File.GetLastWriteTimeUtc(data) > written);   // the change was kept

        svg.History.SetClean();                                   // as after a save
        Vm.AutosaveNow();
        Assert.False(Vm.Recovery.Has(svg));
        Assert.Empty(Directory.GetFiles(_root, "*.svg", SearchOption.AllDirectories));

        svg.Actions.MoveBy([(SvgElement)svg.Root.FindById("r")!], 5, 5);
        Vm.AutosaveNow();
        Assert.True(Vm.Recovery.Has(svg));
        _h.Dialogs.SaveChangesAnswers.Enqueue(SaveChangesChoice.DontSave);
        await Vm.CloseCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Vm.AutosaveNow();
        Assert.Empty(Directory.GetFiles(_root, "*.svg", SearchOption.AllDirectories));

        Vm.EndRecoverySession();
        Assert.Empty(Directory.GetDirectories(_root));
    }

    [AvaloniaFact]
    public async Task After_a_crash_the_documents_come_back_unsaved_and_the_copies_are_removed()
    {
        var photo = _h.TempPath("photo.png");
        File.Copy(TestHarness.SampleImage, photo);
        Vm.Recovery = Store(200);
        Vm.StartRecoverySession(timer: false);
        NewDrawing();
        await Vm.OpenFileAsync(photo);
        Vm.AddNewLayerCommand.Execute(null);
        Vm.AutosaveNow();
        // The process dies here: no End. A new run starts with nothing open.
        while (Vm.Documents.Count > 0)
        {
            Vm.ActiveDocument = Vm.Documents[0];
            _h.Dialogs.SaveChangesAnswers.Enqueue(SaveChangesChoice.DontSave);
            await Vm.CloseCommand.ExecuteAsync(null);
        }
        Vm.Recovery = Store(300);
        Vm.StartRecoverySession(timer: false);
        _h.Dialogs.ConfirmAnswers.Enqueue(true);

        await Vm.OfferRecoveryAsync();

        Assert.Equal(2, Vm.Documents.Count);
        Assert.Contains("Restore unsaved work?", _h.Dialogs.Confirmations);
        var drawing = Assert.IsType<SvgDocument>(Vm.Documents.Single(d => d.Document.Kind == DocumentKind.Svg).Document);
        Assert.NotNull(drawing.Root.FindById("r"));
        Assert.True(drawing.IsDirty);
        Assert.EndsWith("(recovered)", drawing.DisplayName);
        Assert.Null(drawing.File);
        var image = Assert.IsType<ImageDocument>(Vm.Documents.Single(d => d.Document.Kind == DocumentKind.Image).Document);
        Assert.Equal(2, image.Layers.Count());
        Assert.True(image.IsDirty);
        Assert.Equal(Path.GetFullPath(photo), image.File!.FullName);      // back under its own name
        // The old session's copies are gone; the new session keeps its own for what was restored.
        Assert.DoesNotContain(Directory.GetDirectories(_root), d => Path.GetFileName(d).StartsWith("200-"));
        Assert.Equal(2, Vm.Recovery.Written.Count);
        Vm.EndRecoverySession();
    }

    [AvaloniaFact]
    public async Task Declining_deletes_the_copies_and_a_copy_that_cannot_be_opened_is_kept()
    {
        var crashed = Store(400);
        crashed.Begin();
        var svg = Assert.IsType<SvgDocument>(_h.Services.GetRequiredService<IWorkspaceService>().NewSvgDocument(50, 50, SvgUnit.Px));
        crashed.Save(svg);
        var count = Vm.Documents.Count;

        Vm.Recovery = Store(500);
        Vm.StartRecoverySession(timer: false);
        _h.Dialogs.ConfirmAnswers.Enqueue(false);
        await Vm.OfferRecoveryAsync();
        Assert.Equal(count, Vm.Documents.Count);                  // nothing was opened
        Assert.Empty(Vm.Recovery.FindRecoverable());              // and the copies are gone

        var broken = Store(600);
        broken.Begin();
        broken.Save(svg);
        File.WriteAllText(Directory.GetFiles(_root, "*.svg", SearchOption.AllDirectories).Single(f => f.Contains("600-")), "this is not an svg");
        _h.Dialogs.ConfirmAnswers.Enqueue(true);
        await Vm.OfferRecoveryAsync();
        Assert.NotEmpty(_h.Dialogs.Errors);
        Assert.NotEmpty(Vm.Recovery.FindRecoverable());           // kept: nothing is lost for good
        Vm.EndRecoverySession();
    }
}
