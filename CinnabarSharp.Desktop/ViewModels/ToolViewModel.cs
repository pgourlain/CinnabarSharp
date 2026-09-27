using CinnabarSharp.Core.Tools;

namespace CinnabarSharp.Desktop.ViewModels;

/// <param name="Tool">Core implementation, or null for tools not implemented yet (clicking the canvas does nothing).</param>
public record ToolViewModel(string Name, string Label, string Shortcut, ITool? Tool = null)
{
    public string ToolTip => Tool is null ? $"{Name} ({Shortcut}) — coming soon" : $"{Name} ({Shortcut})";

    public bool IsSelectionTool => Tool is ShapeSelectionTool or MagicWandTool;
    public bool IsMagicWand => Tool is MagicWandTool;

    public static ToolViewModel[] CreatePaintDotNetTools(ToolSettings settings) =>
    [
        new("Move Selected Pixels", "Mv", "M", new MoveSelectedPixelsTool()),
        new("Move Selection", "MS", "M", new MoveSelectionTool()),
        new("Zoom", "Zm", "Z"),
        new("Pan", "Pn", "H"),
        new("Rectangle Select", "RS", "S", new RectangleSelectTool(settings)),
        new("Ellipse Select", "ES", "S", new EllipseSelectTool(settings)),
        new("Lasso Select", "LS", "S", new LassoSelectTool(settings)),
        new("Magic Wand", "MW", "S", new MagicWandTool(settings)),
        new("Paint Bucket", "Fi", "F"),
        new("Gradient", "Gr", "G"),
        new("Paintbrush", "Br", "B"),
        new("Eraser", "Er", "E"),
        new("Pencil", "Pe", "P"),
        new("Color Picker", "CP", "K"),
        new("Clone Stamp", "CS", "L"),
        new("Recolor", "Rc", "R"),
        new("Text", "Tx", "T"),
        new("Line / Curve", "Ln", "O"),
        new("Shapes", "Sh", "O"),
    ];
}
