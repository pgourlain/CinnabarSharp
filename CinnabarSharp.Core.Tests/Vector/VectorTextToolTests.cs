using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Vector.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Tests.Vector;

public sealed class VectorTextToolTests : VectorToolTestBase
{
    [Fact]
    public void Typing_after_a_click_creates_one_text_in_one_step()
    {
        var doc = Open("<rect id='r' x='0' y='0' width='10' height='10'/>");
        var tool = new VectorTextTool(Settings);
        var steps = Steps(doc);
        Click(tool, doc, 50, 80);
        tool.OnTextInput(doc, "Hi");
        tool.OnTextInput(doc, "!");
        Assert.Equal(steps, Steps(doc));
        Assert.True(tool.IsTyping(doc));
        tool.OnKeyDown(doc, ToolKey.Escape, ToolModifiers.None);
        Assert.Equal(steps + 1, Steps(doc));
        var text = Assert.Single(doc.Root.Descendants().OfType<SvgText>());
        Assert.Equal("Hi!", text.Content);
        Assert.Equal((50, 80), (text.X, text.Y));
        doc.History.Undo();
        Assert.Empty(doc.Root.Descendants().OfType<SvgText>());
    }

    [Fact]
    public void Nothing_typed_creates_nothing()
    {
        var doc = Open("<rect id='r' x='0' y='0' width='10' height='10'/>");
        var tool = new VectorTextTool(Settings);
        var steps = Steps(doc);
        Click(tool, doc, 50, 80);
        tool.Finish(doc);
        Assert.Equal(steps, Steps(doc));
    }

    [Fact]
    public void Clicking_a_text_edits_it_and_backspace_erases()
    {
        var doc = Open("<text id='t' x='10' y='40' font-size='20'>abc</text>");
        var tool = new VectorTextTool(Settings);
        var steps = Steps(doc);
        Click(tool, doc, 12, 35);
        Assert.True(tool.IsTyping(doc));
        tool.OnKeyDown(doc, ToolKey.End, ToolModifiers.None);
        tool.OnKeyDown(doc, ToolKey.Backspace, ToolModifiers.None);
        tool.OnTextInput(doc, "Z");
        tool.Finish(doc);
        Assert.Equal("abZ", ((SvgText)El(doc, "t")).Content);
        Assert.Equal(steps + 1, Steps(doc));
        doc.History.Undo();
        Assert.Equal("abc", ((SvgText)El(doc, "t")).Content);
    }
}
