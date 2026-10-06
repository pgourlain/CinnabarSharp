using System.Xml.Linq;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Vector;
using Microsoft.Extensions.DependencyInjection;

namespace CinnabarSharp.Core.Tests.Vector;

internal sealed class FakeBoxProvider : IGlyphOutlineProvider
{
    public VectorPath Outline(string text, TextStyle style, out double advance)
    {
        advance = text.Length * style.Size * 0.6;
        return VectorPath.FromRect(0, -style.Size * 0.7, advance, style.Size * 0.7);
    }
}

/// <summary>Every vector action does, undoes (restoring the XML exactly, the dirty state and the selection) and redoes.</summary>
public sealed class VectorHistoryTests : BaseTests
{
    private const string Sample = """
        <svg xmlns="http://www.w3.org/2000/svg" xmlns:inkscape="http://www.inkscape.org/namespaces/inkscape" width="200" height="200" viewBox="0 0 200 200">
          <defs>
            <linearGradient id="g1"><stop offset="0" stop-color="#ff0000"/><stop offset="1" stop-color="#0000ff"/></linearGradient>
          </defs>
          <g id="layer1" inkscape:groupmode="layer" inkscape:label="Layer 1">
            <rect id="r1" x="10" y="10" width="40" height="30" fill="#ff0000" stroke="#000000" stroke-width="2"/>
            <circle id="c1" cx="100" cy="30" r="20" style="fill:#00ff00"/>
            <g id="grp" transform="translate(10 100)" fill="#0000ff" opacity="0.5">
              <rect id="gr" x="0" y="0" width="30" height="30"/>
              <ellipse id="ge" cx="60" cy="15" rx="20" ry="10"/>
            </g>
            <path id="p1" d="M150 10 L190 10 L170 50 Z" fill="url(#g1)"/>
            <text id="t1" x="10" y="190" font-size="12">Hello</text>
            <rect id="r2" x="100" y="100" width="40" height="40" fill="url(#g1)" transform="rotate(10 120 120)"/>
          </g>
        </svg>
        """;

    private (SvgDocument Doc, IWorkspaceService Workspace) Open(string svg = Sample)
    {
        var sp = CinnabarSharpService();
        var workspace = sp.GetRequiredService<IWorkspaceService>();
        var doc = workspace.OpenSvgDocument(SvgParser.Parse(svg).Root, null, null);
        doc.GlyphProvider = new FakeBoxProvider();
        return (doc, workspace);
    }

    private static SvgElement El(SvgDocument doc, string id) => (SvgElement)doc.Root.FindById(id)!;

    private static string Xml(SvgDocument doc) => SvgWriter.ToText(doc.Root);

    private static byte[] Pixels(SvgDocument doc) => VectorRasterizer.RenderAll(doc.Root, 1, doc.RenderOptions).Bgra;

    private static string State(SvgDocument doc) =>
        Xml(doc) + "|dirty=" + doc.IsDirty + "|selection=" + string.Join(",", doc.Selection.Nodes.Select(n => n.Id ?? n.ElementName));

    public static TheoryData<string, Action<SvgDocument>> Actions => new()
    {
        { "Add Object", d => d.Actions.AddNode(new SvgRect { Id = "new" }.With(x => { x.Width = 5; x.Height = 5; })) },
        { "Delete r1", d => d.Actions.Delete([El(d, "r1"), El(d, "c1")]) },
        { "Duplicate", d => d.Actions.Duplicate([El(d, "r1"), El(d, "grp")]) },
        { "Move", d => d.Actions.MoveBy([El(d, "r1"), El(d, "c1"), El(d, "grp"), El(d, "p1"), El(d, "t1"), El(d, "r2")], 7, -3) },
        { "Resize", d => d.Actions.Resize([El(d, "r1"), El(d, "c1")], new VRect(5, 5, 120, 60)) },
        { "Rotate", d => d.Actions.Rotate([El(d, "r1"), El(d, "p1")], 30) },
        { "Flip Horizontal", d => d.Actions.Flip([El(d, "r1"), El(d, "c1")], horizontal: true) },
        { "Flip Vertical", d => d.Actions.Flip([El(d, "r1"), El(d, "grp")], horizontal: false) },
        { "Set Fill", d => d.Actions.SetFill([El(d, "r1"), El(d, "c1"), El(d, "grp")], SvgPaint.FromColor(VColor.FromRgb(1, 2, 3))) },
        { "Set Stroke", d => d.Actions.SetStroke([El(d, "r1")], SvgPaint.FromUrl("g1")) },
        { "Set opacity", d => d.Actions.SetStyle([El(d, "r1"), El(d, "c1")], "opacity", "0.4") },
        { "Rename", d => d.Actions.SetId(El(d, "g1"), "gradient-one") },
        { "Rename ", d => d.Actions.SetLabel(El(d, "r1"), "The red box") },
        { "Raise", d => d.Actions.Raise([El(d, "r1")]) },
        { "Lower", d => d.Actions.Lower([El(d, "c1"), El(d, "t1")]) },
        { "Raise to Top", d => d.Actions.RaiseToTop([El(d, "r1"), El(d, "c1")]) },
        { "Lower to Bottom", d => d.Actions.LowerToBottom([El(d, "t1"), El(d, "r2")]) },
        { "Group", d => d.Actions.Group([El(d, "r1"), El(d, "p1")]) },
        { "Ungroup", d => d.Actions.Ungroup([El(d, "grp")]) },
        { "Move to Group", d => d.Actions.MoveToParent([El(d, "r1"), El(d, "p1")], (SvgContainer)El(d, "grp")) },
        { "Hide", d => d.Actions.SetVisible(El(d, "r1"), false) },
        { "Lock", d => d.Actions.SetLocked(El(d, "c1"), true) },
        { "Object to Path", d => d.Actions.ObjectToPath([El(d, "r1"), El(d, "c1"), El(d, "ge"), El(d, "t1")]) },
        { "Edit Path", d => d.Actions.SetPathData((SvgPath)El(d, "p1"), new VectorPath().MoveTo(0, 0).LineTo(50, 50)) },
        { "Set Transform", d => d.Actions.SetTransform(El(d, "r1"), Matrix2D.Rotate(15)) },
        { "Edit Text", d => d.Actions.SetText((SvgText)El(d, "t1"), "Changed") },
        { "Paste", d => d.Actions.PasteSvg("<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='pg'><stop offset='0' stop-color='#fff'/></linearGradient></defs><rect id='r1' width='9' height='9' fill='url(#pg)'/></svg>") },
        { "Cut", d => d.Actions.Cut([El(d, "r1"), El(d, "t1")]) },
        { "Paste Image", d => d.Actions.PasteImage(new ClipboardImage(new byte[4 * 4 * 4], 4, 4)) },
        { "Set Attribute", d => d.Actions.SetAttribute(El(d, "r1"), "data-x", "1") },
    };

    [Theory]
    [MemberData(nameof(Actions))]
    public void Every_action_undoes_and_redoes_exactly(string text, Action<SvgDocument> action)
    {
        var (doc, _) = Open();
        doc.Selection.Set(El(doc, "c1"));
        var history = doc.History;
        var before = State(doc);
        var items = history.Items.Count;

        action(doc);
        var after = State(doc);

        Assert.NotEqual(before, after);
        Assert.Equal(items + 1, history.Items.Count);
        Assert.True(doc.IsDirty);
        Assert.False(string.IsNullOrWhiteSpace(history.Items[^1].Text), text);

        history.Undo();
        Assert.Equal(before, State(doc));
        Assert.False(doc.IsDirty);

        history.Redo();
        Assert.Equal(after, State(doc));
        Assert.True(doc.IsDirty);

        history.Undo();
        Assert.Equal(before, State(doc));
    }

    [Fact]
    public void Undo_of_a_long_session_returns_to_the_exact_file_and_a_clean_state()
    {
        var (doc, _) = Open();
        var original = Xml(doc);
        var a = doc.Actions;
        a.MoveBy([El(doc, "r1")], 5, 5);
        a.SetFill([El(doc, "c1")], SvgPaint.FromColor(VColor.FromRgb(9, 9, 9)));
        a.Duplicate([El(doc, "r2")]);
        a.Group([El(doc, "r1"), El(doc, "c1")]);
        a.Raise([El(doc, "p1")]);
        a.SetLabel(El(doc, "t1"), "Title");
        a.ObjectToPath([El(doc, "ge")]);
        a.Resize([El(doc, "grp")], new VRect(0, 0, 150, 150));
        a.Delete([El(doc, "p1")]);
        Assert.True(doc.IsDirty);

        while (doc.History.CanUndo)
            doc.History.Undo();
        Assert.False(doc.IsDirty);
        Assert.Equal(original, Xml(doc));
        var dirty = doc.Root.SelfAndDescendants().Where(n => n.IsDirty).Select(n => (n as SvgElement)?.Id ?? n.GetType().Name).ToList();
        Assert.True(dirty.Count == 0, "dirty after undo: " + string.Join(",", dirty));

        while (doc.History.CanRedo)
            doc.History.Redo();
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void Clean_state_returns_at_the_save_point_and_not_before()
    {
        var (doc, _) = Open();
        doc.Actions.MoveBy([El(doc, "r1")], 5, 5);
        doc.History.SetClean();
        Assert.False(doc.IsDirty);
        doc.Actions.MoveBy([El(doc, "r1")], 5, 5);
        Assert.True(doc.IsDirty);
        doc.History.Undo();
        Assert.False(doc.IsDirty);
        doc.History.Undo();
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void Nodes_are_found_by_internal_id_even_when_the_xml_ids_are_duplicated_or_missing()
    {
        var (doc, _) = Open("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 50 50'><rect id='x' width='5' height='5'/><rect id='x' x='20' width='5' height='5'/><rect x='30' width='5' height='5'/></svg>");
        var nodes = doc.Root.Elements.ToList();
        doc.Actions.MoveBy([nodes[1], nodes[2]], 3, 3);
        Assert.Equal("3", nodes[1].GetAttribute("y"));
        Assert.Equal("33", nodes[2].GetAttribute("x"));
        doc.History.Undo();
        Assert.Null(nodes[1].GetAttribute("y"));
        Assert.Equal("30", nodes[2].GetAttribute("x"));
        Assert.Equal("0", nodes[0].GetAttribute("x") ?? "0");
    }

    [Fact]
    public void Selection_comes_back_with_undo_and_redo()
    {
        var (doc, _) = Open();
        doc.Selection.Set([El(doc, "r1"), El(doc, "c1")]);
        doc.Actions.DeleteSelection();
        Assert.True(doc.Selection.IsEmpty);
        doc.History.Undo();
        Assert.Equal(["r1", "c1"], doc.Selection.Nodes.Select(n => n.Id));
        doc.History.Redo();
        Assert.True(doc.Selection.IsEmpty);

        doc.History.Undo();
        var copies = doc.Actions.Duplicate();
        Assert.Equal(copies, doc.Selection.Nodes);
        doc.History.Undo();
        Assert.Equal(["r1", "c1"], doc.Selection.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void Selection_changes_at_the_end_of_a_gesture_are_undoable()
    {
        var (doc, _) = Open();
        var before = doc.Selection.Nodes.ToList();
        doc.Selection.Set(El(doc, "r2"));
        doc.Actions.RecordSelectionChange(before, "Select");
        Assert.Equal("Select", doc.History.Items[^1].Text);
        doc.History.Undo();
        Assert.True(doc.Selection.IsEmpty);
        doc.History.Redo();
        Assert.Equal(["r2"], doc.Selection.Nodes.Select(n => n.Id));
        // Nothing changed, nothing recorded.
        var count = doc.History.Items.Count;
        doc.Actions.RecordSelectionChange(doc.Selection.Nodes.ToList());
        Assert.Equal(count, doc.History.Items.Count);
    }

    [Fact]
    public void Actions_raise_events_the_ui_can_use()
    {
        var sp = CinnabarSharpService();
        var events = new List<EventItem<DocumentEventEnum>>();
        sp.GetRequiredService<IDocumentEventsService>().DocumentEvents.Subscribe(new Recorder(events));
        var doc = sp.GetRequiredService<IWorkspaceService>().OpenSvgDocument(SvgParser.Parse(Sample).Root, null, null);
        events.Clear();

        doc.Actions.MoveBy([El(doc, "r1")], 10, 0);
        var node = Assert.IsType<VectorNodeEventItem>(Assert.Single(events, e => e.State == DocumentEventEnum.VectorNodeChanged));
        Assert.Same(El(doc, "r1"), node.Node);
        Assert.NotNull(node.DirtyBounds);
        Assert.True(node.DirtyBounds!.Value.Width >= 40 + 10);        // covers the box before and after
        Assert.Contains(events, e => e.State == DocumentEventEnum.HistoryChanged);

        events.Clear();
        doc.Actions.Delete([El(doc, "r1")]);
        Assert.Contains(events, e => e.State == DocumentEventEnum.VectorTreeChanged);
    }

    private sealed class Recorder(List<EventItem<DocumentEventEnum>> items) : IObserver<EventItem<DocumentEventEnum>>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(EventItem<DocumentEventEnum> value) => items.Add(value);
    }
}

internal static class TestExtensions
{
    public static T With<T>(this T value, Action<T> configure)
    {
        configure(value);
        return value;
    }
}
