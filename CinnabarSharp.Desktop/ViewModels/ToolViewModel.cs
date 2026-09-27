using CinnabarSharp.Core.Tools;

namespace CinnabarSharp.Desktop.ViewModels;

/// <param name="Tool">Core implementation, or null for tools not implemented yet (clicking the canvas does nothing).</param>
public record ToolViewModel(string Name, string Label, string Shortcut, ITool? Tool = null)
{
    public string ToolTip => Tool is null ? $"{Name} ({Shortcut}) — coming soon" : $"{Name} ({Shortcut})";

    public bool IsSelectionTool => Tool is ShapeSelectionTool or MagicWandTool;
    public bool HasTolerance => Tool is MagicWandTool or PaintBucketTool;
    public bool HasBrushWidth => Tool is PaintbrushTool and not PencilTool or LineTool or ShapesTool;
    public bool IsShapes => Tool is ShapesTool;
    public bool IsGradient => Tool is GradientTool;
    public bool IsColorPicker => Tool is ColorPickerTool;

    /// <summary>Tools that paint get a crosshair cursor.</summary>
    public bool IsPaintingTool => Tool is PaintbrushTool or DragShapeTool or PaintBucketTool or ColorPickerTool;

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
        new("Paint Bucket", "Fi", "F", new PaintBucketTool(settings)),
        new("Gradient", "Gr", "G", new GradientTool(settings)),
        new("Paintbrush", "Br", "B", new PaintbrushTool(settings)),
        new("Eraser", "Er", "E", new EraserTool(settings)),
        new("Pencil", "Pe", "P", new PencilTool(settings)),
        new("Color Picker", "CP", "K", new ColorPickerTool(settings)),
        new("Clone Stamp", "CS", "L"),
        new("Recolor", "Rc", "R"),
        new("Text", "Tx", "T"),
        new("Line / Curve", "Ln", "O", new LineTool(settings)),
        new("Shapes", "Sh", "O", new ShapesTool(settings)),
    ];
}
