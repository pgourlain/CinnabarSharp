using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Core.Vector.Tools;
using CinnabarSharp.Vector;
using Microsoft.Extensions.DependencyInjection;

namespace CinnabarSharp.Core.Tests.Vector;

/// <summary>Drawings of 200 × 200 units shown at 100 %, so a user unit is a screen pixel; pointer helpers for tool tests.</summary>
public abstract class VectorToolTestBase : BaseTests
{
    protected const string Header = "<svg xmlns='http://www.w3.org/2000/svg' width='200' height='200' viewBox='0 0 200 200'>";

    protected readonly ToolSettings Settings = new();

    protected SvgDocument Open(string body)
    {
        var sp = CinnabarSharpService();
        var doc = sp.GetRequiredService<IWorkspaceService>().OpenSvgDocument(SvgParser.Parse(Header + body + "</svg>").Root, null, null);
        doc.GlyphProvider = new FakeBoxProvider();
        return doc;
    }

    protected static SvgElement El(SvgDocument doc, string id) => (SvgElement)doc.Root.FindById(id)!;

    protected static ToolPointer At(double x, double y, ToolModifiers modifiers = ToolModifiers.None, int clicks = 1) =>
        new(new PointD(x, y), ToolButton.Left, modifiers, 1, clicks);

    protected static void Click(IVectorTool tool, SvgDocument doc, double x, double y, ToolModifiers modifiers = ToolModifiers.None)
    {
        tool.OnPointerDown(doc, At(x, y, modifiers));
        tool.OnPointerUp(doc, At(x, y, modifiers));
    }

    protected static void Drag(IVectorTool tool, SvgDocument doc, double x0, double y0, double x1, double y1, ToolModifiers modifiers = ToolModifiers.None)
    {
        tool.OnPointerDown(doc, At(x0, y0, modifiers));
        tool.OnPointerMove(doc, At(x0 + (x1 - x0) / 3, y0 + (y1 - y0) / 3, modifiers));
        tool.OnPointerMove(doc, At(x0 + 2 * (x1 - x0) / 3, y0 + 2 * (y1 - y0) / 3, modifiers));
        tool.OnPointerMove(doc, At(x1, y1, modifiers));
        tool.OnPointerUp(doc, At(x1, y1, modifiers));
    }

    protected static int Steps(SvgDocument doc) => doc.History.Items.Count;

    protected static string Xml(SvgDocument doc) => SvgWriter.ToText(doc.Root);

    protected static VRect Box(SvgDocument doc, string id)
    {
        var b = SvgBounds.InDocument(El(doc, id))!.Value;
        return new VRect(Math.Round(b.X, 4), Math.Round(b.Y, 4), Math.Round(b.Width, 4), Math.Round(b.Height, 4));
    }
}
