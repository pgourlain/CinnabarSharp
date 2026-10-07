using CinnabarSharp.Core.Vector;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Tests.Vector;

public sealed class SvgStyleCopyTests : VectorToolTestBase
{
    private const string Body =
        "<rect id='src' x='0' y='0' width='40' height='30' fill='#336699' stroke='#ff0000' stroke-width='5' stroke-linecap='round' stroke-dasharray='4 2' opacity='0.5'/>" +
        "<circle id='dst' cx='100' cy='20' r='10' fill='#00ff00'/>" +
        "<g id='grp'><rect id='in1' x='0' y='60' width='10' height='10'/><ellipse id='in2' cx='60' cy='60' rx='8' ry='5'/></g>" +
        "<text id='t1' x='10' y='120' font-family='Georgia' font-size='30' font-weight='bold' font-style='italic' text-anchor='middle'>Hi</text>" +
        "<text id='t2' x='10' y='160'>Other</text>";

    [Fact]
    public void The_look_of_one_object_is_pasted_on_another_in_one_step()
    {
        var doc = Open(Body);
        var copied = SvgActions.CopyStyle(El(doc, "src"));
        var steps = Steps(doc);
        doc.Actions.PasteStyle([El(doc, "dst")], copied);

        Assert.Equal(steps + 1, Steps(doc));
        Assert.Equal("Paste Style", doc.History.Items[^1].Text);
        var style = StyleResolver.ComputeFor(El(doc, "dst"));
        Assert.Equal(VColor.FromRgb(0x33, 0x66, 0x99), style.Fill.Color);
        Assert.Equal(VColor.FromRgb(255, 0, 0), style.Stroke.Color);
        Assert.Equal((5.0, LineCap.Round, 0.5), (style.StrokeWidth, style.LineCap, style.Opacity));
        Assert.Equal([4.0, 2.0], style.DashArray);
        Assert.Equal(10, ((SvgCircle)El(doc, "dst")).R);               // its geometry is untouched
        doc.History.Undo();
        Assert.Equal(VColor.FromRgb(0, 255, 0), StyleResolver.ComputeFor(El(doc, "dst")).Fill.Color);
    }

    [Fact]
    public void A_group_passes_the_look_to_every_shape_inside()
    {
        var doc = Open(Body);
        doc.Actions.PasteStyle([El(doc, "grp")], SvgActions.CopyStyle(El(doc, "src")));
        foreach (var id in new[] { "in1", "in2" })
            Assert.Equal(VColor.FromRgb(0x33, 0x66, 0x99), StyleResolver.ComputeFor(El(doc, id)).Fill.Color);
    }

    [Fact]
    public void Fonts_go_to_texts_only_and_a_text_copied_onto_a_shape_gives_only_the_paint()
    {
        var doc = Open(Body);
        doc.Actions.PasteStyle([El(doc, "t2")], SvgActions.CopyStyle(El(doc, "t1")));
        var text = StyleResolver.ComputeFor(El(doc, "t2"));
        Assert.Equal(("Georgia", 30.0, 700, true, TextAnchor.Middle), (text.FontFamily, text.FontSize, text.FontWeight, text.Italic, text.TextAnchor));

        doc.Actions.PasteStyle([El(doc, "dst")], SvgActions.CopyStyle(El(doc, "t1")));
        Assert.Null(El(doc, "dst").Style.Get("font-family"));
        // A shape's look onto a text: no font properties are touched.
        doc.Actions.PasteStyle([El(doc, "t1")], SvgActions.CopyStyle(El(doc, "src")));
        Assert.Equal("Georgia", StyleResolver.ComputeFor(El(doc, "t1")).FontFamily);
    }

    [Fact]
    public void A_gradient_is_shared_by_reference()
    {
        var doc = Open("<defs><linearGradient id='g'><stop offset='0' stop-color='#f00'/><stop offset='1' stop-color='#00f'/></linearGradient></defs>" +
            "<rect id='a' width='10' height='10' fill='url(#g)'/><rect id='b' x='20' width='10' height='10' fill='#000'/>");
        doc.Actions.PasteStyle([El(doc, "b")], SvgActions.CopyStyle(El(doc, "a")));
        Assert.Equal("g", StyleResolver.ComputeFor(El(doc, "b")).Fill.Id);
    }
}
