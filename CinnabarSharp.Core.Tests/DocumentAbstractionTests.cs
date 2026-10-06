using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CinnabarSharp.Core.Tests;

/// <summary>A second document kind (like the SVG one) living in the workspace next to raster images.</summary>
public class DocumentAbstractionTests : BaseTests
{
    private sealed class FakeDocument : IDocument
    {
        private bool _dirty;
        private string _name = "Fake";
        private readonly IDocumentEventsService _events;

        public FakeDocument(IDocumentEventsService events)
        {
            _events = events;
            ImageSize = new ImageSize(10, 10);
            Workspace = new ImageDocumentWorkspace(this, new ImageDocumentHistory(this, events), events,
                NullLogger<ImageDocument>.Instance);
        }

        public Guid Id { get; } = Guid.NewGuid();
        public DocumentKind Kind => DocumentKind.Svg;
        public string DisplayName
        {
            get => _name;
            set { _name = value; _events.PushEvent(new DocumentEventItem(this, DocumentEventEnum.DocumentRenamed)); }
        }
        public FileInfo? File { get; set; }
        public string? FileType { get; set; }
        public bool IsDirty
        {
            get => _dirty;
            set
            {
                if (_dirty == value)
                    return;
                _dirty = value;
                _events.PushEvent(new DocumentEventItem(this, DocumentEventEnum.DirtyChanged));
            }
        }
        public ImageSize ImageSize { get; set; }
        public ImageDocumentWorkspace Workspace { get; }
        public bool Closed { get; private set; }
        public void Close() => Closed = true;
        public (byte[] Bgra, int Width, int Height) GetThumbnail(int maxSide) => (new byte[4], 1, 1);
    }

    private sealed class NoopItem(string text) : HistoryItem(text)
    {
        protected override void OnUndo() { }
        protected override void OnRedo() { }
    }

    private sealed class Recorder : IObserver<EventItem<DocumentEventEnum>>
    {
        public List<EventItem<DocumentEventEnum>> Items { get; } = [];
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(EventItem<DocumentEventEnum> value) => Items.Add(value);
    }

    private sealed record Setup(IWorkspaceService Workspace, IDocumentEventsService Events, FakeDocument Fake, Recorder Recorder);

    private Setup Create()
    {
        var sp = CinnabarSharpService();
        var events = sp.GetRequiredService<IDocumentEventsService>();
        var recorder = new Recorder();
        events.DocumentEvents.Subscribe(recorder);
        var workspace = sp.GetRequiredService<IWorkspaceService>();
        var fake = new FakeDocument(events);
        return new Setup(workspace, events, fake, recorder);
    }

    [Fact]
    public void Adding_a_second_kind_creates_and_activates_it()
    {
        var s = Create();
        s.Workspace.AddAndActivate(s.Fake);

        Assert.Same(s.Fake, s.Workspace.ActiveDocument);
        Assert.Null(s.Workspace.ActiveImageDocument);
        Assert.Equal([DocumentEventEnum.DocumentCreated, DocumentEventEnum.ActiveDocumentChanged],
            s.Recorder.Items.Select(e => e.State));
        Assert.All(s.Recorder.Items, e => Assert.Same(s.Fake, e.Document));
    }

    [Fact]
    public void Switching_tabs_between_kinds_reports_the_right_document()
    {
        var s = Create();
        var image = s.Workspace.NewDocument(new ImageSize(4, 4), ColorBgra.White);
        s.Workspace.AddAndActivate(s.Fake);
        Assert.Null(s.Workspace.ActiveImageDocument);

        s.Recorder.Items.Clear();
        s.Workspace.SetActiveDocument(image);

        Assert.Same(image, s.Workspace.ActiveImageDocument);
        var changed = Assert.Single(s.Recorder.Items);
        Assert.Equal(DocumentEventEnum.ActiveDocumentChanged, changed.State);
        Assert.Same(image, changed.Document);
        Assert.Equal(2, s.Workspace.OpenDocuments.Count);
    }

    [Fact]
    public void Dirty_state_follows_the_history_of_a_non_image_document()
    {
        var s = Create();
        s.Workspace.AddAndActivate(s.Fake);
        var history = s.Fake.Workspace.History;
        history.PushNewItem(new BaseHistoryItem("New"));
        Assert.False(s.Fake.IsDirty);

        s.Recorder.Items.Clear();
        history.PushNewItem(new NoopItem("Edit"));
        Assert.True(s.Fake.IsDirty);
        Assert.Contains(s.Recorder.Items, e => e.State == DocumentEventEnum.DirtyChanged && ReferenceEquals(e.Document, s.Fake));
        Assert.Contains(s.Recorder.Items, e => e.State == DocumentEventEnum.HistoryChanged && ReferenceEquals(e.Document, s.Fake));

        history.SetClean();
        Assert.False(s.Fake.IsDirty);
        history.Undo();
        Assert.True(s.Fake.IsDirty);
    }

    [Fact]
    public void Closing_a_non_image_document_closes_it_and_activates_a_neighbour()
    {
        var s = Create();
        var image = s.Workspace.NewDocument(new ImageSize(4, 4), ColorBgra.White);
        s.Workspace.AddAndActivate(s.Fake);

        s.Recorder.Items.Clear();
        s.Workspace.CloseDocument(s.Fake);

        Assert.True(s.Fake.Closed);
        Assert.Same(image, s.Workspace.ActiveDocument);
        Assert.Equal([DocumentEventEnum.DocumentClosed, DocumentEventEnum.ActiveDocumentChanged],
            s.Recorder.Items.Select(e => e.State).Where(st => st is DocumentEventEnum.DocumentClosed or DocumentEventEnum.ActiveDocumentChanged));
        Assert.Same(s.Fake, s.Recorder.Items.First(e => e.State == DocumentEventEnum.DocumentClosed).Document);
    }

    [Fact]
    public void Save_formats_depend_on_the_document_kind()
    {
        var formats = CinnabarSharpService().GetRequiredService<IFormatManager>();
        Assert.Contains(formats.GetSaveFormats(DocumentKind.Image), f => f.SupportedExtensions.Contains("png"));
        var svg = formats.GetSaveFormats(DocumentKind.Svg);
        Assert.Equal(DocumentKind.Svg, svg[0].DocumentKind); // SVG first, then the exports
        Assert.Contains(svg, f => f.SupportedExtensions.Contains("png"));
        Assert.DoesNotContain(formats.GetSaveFormats(DocumentKind.Image), f => f.DocumentKind == DocumentKind.Svg);
    }
}
