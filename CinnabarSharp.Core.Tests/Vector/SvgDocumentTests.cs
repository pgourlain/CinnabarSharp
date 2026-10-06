using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Vector;
using Microsoft.Extensions.DependencyInjection;

namespace CinnabarSharp.Core.Tests.Vector;

public class SvgDocumentTests : BaseTests
{
    private sealed class Recorder : IObserver<EventItem<DocumentEventEnum>>
    {
        public List<EventItem<DocumentEventEnum>> Items { get; } = [];
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(EventItem<DocumentEventEnum> value) => Items.Add(value);
    }

    private sealed class NoopItem(string text) : HistoryItem(text)
    {
        protected override void OnUndo() { }
        protected override void OnRedo() { }
    }

    private static SvgRoot Root(string sample = "shapes") =>
        SvgParser.ParseFile(Path.Combine(AppContext.BaseDirectory, "Data", "svg", sample + ".svg")).Root;

    private (IServiceProvider Services, IWorkspaceService Workspace, Recorder Recorder) Setup()
    {
        var sp = CinnabarSharpService();
        var recorder = new Recorder();
        sp.GetRequiredService<IDocumentEventsService>().DocumentEvents.Subscribe(recorder);
        return (sp, sp.GetRequiredService<IWorkspaceService>(), recorder);
    }

    [Fact]
    public void Is_created_through_di_and_appears_in_the_workspace()
    {
        var (sp, workspace, recorder) = Setup();
        Assert.NotSame(sp.GetRequiredService<SvgDocument>(), sp.GetRequiredService<SvgDocument>());

        var doc = workspace.OpenSvgDocument(Root(), null, null);

        Assert.Same(doc, workspace.ActiveDocument);
        Assert.Null(workspace.ActiveImageDocument);
        Assert.Equal(DocumentKind.Svg, doc.Kind);
        Assert.Equal("Unsaved Drawing 1", doc.DisplayName);
        Assert.Equal(["New Drawing"], doc.History.Items.Select(i => i.Text));
        Assert.False(doc.IsDirty);
        var states = recorder.Items.Select(e => e.State).ToList();
        Assert.Contains(DocumentEventEnum.VectorTreeChanged, states);
        Assert.True(states.IndexOf(DocumentEventEnum.DocumentCreated) < states.IndexOf(DocumentEventEnum.ActiveDocumentChanged));
        Assert.All(recorder.Items.Where(e => e.State is DocumentEventEnum.DocumentCreated or DocumentEventEnum.ActiveDocumentChanged),
            e => Assert.Same(doc, e.Document));
    }

    [Fact]
    public void Size_comes_from_the_viewbox_at_96_dpi()
    {
        var (_, workspace, _) = Setup();
        var doc = workspace.OpenSvgDocument(Root("units"), null, null);
        Assert.Equal(new ImageSize(189, 114), doc.ImageSize); // 50 mm x 30 mm, rounded up
        Assert.Equal(doc.ImageSize, doc.Workspace.ViewSize);

        var shapes = workspace.OpenSvgDocument(Root("shapes"), null, null);
        Assert.Equal(new ImageSize(200, 120), shapes.ImageSize);
    }

    [Fact]
    public void Opened_file_names_the_tab_and_sets_the_type()
    {
        var (_, workspace, _) = Setup();
        var file = new FileInfo(Path.Combine(AppContext.BaseDirectory, "Data", "svg", "shapes.svg"));
        var doc = workspace.OpenSvgDocument(Root(), file, null);
        Assert.Equal("shapes.svg", doc.DisplayName);
        Assert.Equal("svg", doc.FileType);
        Assert.Equal(["Open Drawing"], doc.History.Items.Select(i => i.Text));
    }

    [Fact]
    public void Dirty_state_follows_history_and_raises_events()
    {
        var (_, workspace, recorder) = Setup();
        var doc = workspace.OpenSvgDocument(Root(), null, null);
        recorder.Items.Clear();

        doc.History.PushNewItem(new NoopItem("Edit"));
        Assert.True(doc.IsDirty);
        Assert.Contains(recorder.Items, e => e.State == DocumentEventEnum.DirtyChanged && ReferenceEquals(e.Document, doc));
        doc.History.SetClean();
        Assert.False(doc.IsDirty);
        doc.History.Undo();
        Assert.True(doc.IsDirty);
        doc.History.Redo();
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Selection_holds_nodes_and_reports_changes_once()
    {
        var (_, workspace, recorder) = Setup();
        var doc = workspace.OpenSvgDocument(Root(), null, null);
        var rect = (SvgElement)doc.Root.FindById("r1")!;
        var circle = (SvgElement)doc.Root.FindById("c1")!;
        recorder.Items.Clear();

        doc.Selection.Set(rect);
        doc.Selection.Set(rect); // unchanged: no event
        doc.Selection.Add(circle);
        doc.Selection.Toggle(rect);

        Assert.Equal([circle], doc.Selection.Nodes);
        Assert.Same(circle, doc.Selection.Primary);
        Assert.Equal(3, recorder.Items.Count(e => e.State == DocumentEventEnum.VectorSelectionChanged));
        doc.Selection.Clear();
        Assert.True(doc.Selection.IsEmpty);
    }

    [Fact]
    public void Selection_drops_nodes_that_left_the_document()
    {
        var (_, workspace, _) = Setup();
        var doc = workspace.OpenSvgDocument(Root(), null, null);
        var rect = (SvgElement)doc.Root.FindById("r1")!;
        doc.Selection.Set(rect);
        doc.Root.RemoveChild(rect);
        doc.Selection.Prune();
        Assert.True(doc.Selection.IsEmpty);
    }

    [Fact]
    public void Coordinates_convert_between_user_space_and_pixels()
    {
        var (_, workspace, _) = Setup();
        var doc = workspace.OpenSvgDocument(Root("units"), null, null);
        var p = doc.UserToImagePoint(new VPoint(25, 15));
        Assert.Equal(25 * 96 / 25.4, p.X, 6);
        var back = doc.ImageToUserPoint(p);
        Assert.Equal(25, back.X, 6);
        Assert.Equal(15, back.Y, 6);
    }

    [Fact]
    public void Resizing_scales_the_drawing_with_the_page()
    {
        var (_, workspace, recorder) = Setup();
        var doc = workspace.OpenSvgDocument(Root("shapes"), null, null);
        recorder.Items.Clear();
        doc.ImageSize = new ImageSize(400, 240);
        Assert.Equal(new ImageSize(400, 240), doc.ImageSize);
        Assert.Equal((200, 120), doc.Root.UserSize);
        Assert.Contains(recorder.Items, e => e.State == DocumentEventEnum.ViewSizeChanged);
    }

    [Fact]
    public void Closing_releases_the_tab_and_clears_history()
    {
        var (_, workspace, recorder) = Setup();
        var image = workspace.NewDocument(new ImageSize(4, 4), ColorBgra.White);
        var doc = workspace.OpenSvgDocument(Root(), null, null);
        doc.Selection.Set((SvgElement)doc.Root.FindById("r1")!);
        workspace.CloseDocument(doc);
        Assert.Same(image, workspace.ActiveDocument);
        Assert.True(doc.Selection.IsEmpty);
        Assert.Empty(doc.History.Items);
        Assert.Contains(recorder.Items, e => e.State == DocumentEventEnum.DocumentClosed && ReferenceEquals(e.Document, doc));
    }

    [Fact]
    public void Thumbnail_is_a_small_render_of_the_drawing()
    {
        var (_, workspace, _) = Setup();
        var doc = workspace.OpenSvgDocument(Root("shapes"), null, null);
        var (bgra, width, height) = doc.GetThumbnail(44);
        Assert.Equal((44, 27), (width, height));
        Assert.Equal(width * height * 4, bgra.Length);
        // The red rounded rectangle at the top left of the sample.
        var i = (8 * width + 12) * 4;
        Assert.Equal((0xe0, 0x30, 0x20), (bgra[i + 2], bgra[i + 1], bgra[i]));
    }

    [Fact]
    public void Node_events_carry_the_node_and_area()
    {
        var (_, workspace, recorder) = Setup();
        var doc = workspace.OpenSvgDocument(Root(), null, null);
        var rect = doc.Root.FindById("r1")!;
        recorder.Items.Clear();
        doc.NotifyNodeChangedForTest(rect);
        var item = Assert.IsType<VectorNodeEventItem>(Assert.Single(recorder.Items));
        Assert.Same(rect, item.Node);
        Assert.Equal(new VRect(1, 2, 3, 4), item.DirtyBounds);
    }
}

internal static class SvgDocumentTestHooks
{
    public static void NotifyNodeChangedForTest(this SvgDocument doc, SvgNode node) =>
        doc.NotifyNodeChanged(node, new VRect(1, 2, 3, 4));
}
