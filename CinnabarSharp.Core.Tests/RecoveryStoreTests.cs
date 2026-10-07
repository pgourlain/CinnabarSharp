using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Vector;
using Microsoft.Extensions.DependencyInjection;

namespace CinnabarSharp.Core.Tests;

public sealed class RecoveryStoreTests : BaseTests, IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("cinnabar-recovery");
    private readonly IServiceProvider _services;

    public RecoveryStoreTests() => _services = CinnabarSharpService();

    public void Dispose() => _root.Delete(recursive: true);

    private IFormatManager Formats => _services.GetRequiredService<IFormatManager>();

    private IWorkspaceService Workspace => _services.GetRequiredService<IWorkspaceService>();

    private RecoveryStore Store(int pid, Func<int, bool>? alive = null) => new(_root.FullName, Formats, alive ?? (_ => false), pid);

    [Fact]
    public void A_crashed_session_leaves_copies_that_open_with_their_content()
    {
        var drawing = Workspace.NewSvgDocument(200, 100, SvgUnit.Px);
        var rect = new SvgRect { Id = "r" };
        rect.Width = 50;
        rect.Height = 20;
        drawing.Actions.AddNode(rect);
        var image = Workspace.NewDocument(new ImageSize(30, 20), ColorBgra.White);
        image.Actions.AddNewLayer();
        image.DisplayName = "photo.png";

        var crashed = Store(1111);
        crashed.Begin();
        crashed.Save(drawing);
        crashed.Save(image);
        Assert.True(crashed.Has(drawing));
        // The process is gone and never called End: the next session finds the copies.
        var next = Store(2222);
        next.Begin();
        var found = next.FindRecoverable();

        Assert.Equal(2, found.Count);
        var svg = Assert.Single(found, d => d.Kind == DocumentKind.Svg);
        var ora = Assert.Single(found, d => d.Kind == DocumentKind.Image);
        Assert.Equal("photo.png", ora.Name);
        var reopenedDrawing = Assert.IsType<SvgDocument>(Formats.Open(new FileInfo(svg.DataFile)));
        Assert.NotNull(reopenedDrawing.Root.FindById("r"));
        var reopenedImage = Assert.IsType<ImageDocument>(Formats.Open(new FileInfo(ora.DataFile)));
        Assert.Equal(2, reopenedImage.Layers.Count());

        next.Discard(found);
        Assert.Empty(next.FindRecoverable());
        Assert.Empty(Directory.GetDirectories(_root.FullName).Where(d => d.Contains("1111-")));
    }

    [Fact]
    public void Saving_a_copy_does_not_touch_the_document_and_a_normal_exit_leaves_nothing()
    {
        var drawing = Workspace.NewSvgDocument(100, 100, SvgUnit.Px);
        drawing.Actions.AddNode(new SvgRect { Id = "r" }.Also(r => { r.Width = 5; r.Height = 5; }));
        var dirty = drawing.IsDirty;
        var file = drawing.File;
        var store = Store(3333);
        store.Begin();
        store.Save(drawing);
        Assert.Equal(dirty, drawing.IsDirty);
        Assert.Equal(file, drawing.File);

        store.End();
        Assert.Empty(Directory.GetDirectories(_root.FullName));
        Assert.Empty(Store(4444).FindRecoverable());
    }

    [Fact]
    public void A_session_that_is_still_running_and_the_current_one_are_left_alone()
    {
        var drawing = Workspace.NewSvgDocument(100, 100, SvgUnit.Px);
        var running = Store(5555);
        running.Begin();
        running.Save(drawing);
        var other = Store(6666, alive: pid => pid == 5555);
        other.Begin();
        other.Save(drawing);
        Assert.Empty(other.FindRecoverable());                       // 5555 is alive, 6666 is this one
        Assert.NotEmpty(Store(7777).FindRecoverable());              // seen from a third session where 5555 is dead
    }

    [Fact]
    public void A_saved_or_closed_document_loses_its_copy()
    {
        var drawing = Workspace.NewSvgDocument(100, 100, SvgUnit.Px);
        var crashed = Store(8888);
        crashed.Begin();
        crashed.Save(drawing);
        crashed.Remove(drawing.Id);
        Assert.False(crashed.Has(drawing));
        Assert.Empty(Store(9999).FindRecoverable());
    }

    [Fact]
    public void The_original_path_is_kept_and_a_broken_copy_is_skipped()
    {
        var drawing = Workspace.NewSvgDocument(100, 100, SvgUnit.Px);
        drawing.File = new FileInfo(Path.Combine(_root.FullName, "logo.svg"));
        var crashed = Store(1212);
        crashed.Begin();
        crashed.Save(drawing);
        var next = Store(1313);
        var found = Assert.Single(next.FindRecoverable());
        Assert.Equal(drawing.File.FullName, found.OriginalPath);
        File.WriteAllText(Path.ChangeExtension(found.DataFile, "json"), "not json");
        Assert.Empty(next.FindRecoverable());
    }
}

internal static class AlsoExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
