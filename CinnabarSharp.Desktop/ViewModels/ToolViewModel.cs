using CinnabarSharp.Core.Tools;

namespace CinnabarSharp.Desktop.ViewModels;

/// <param name="Tool">Core implementation, or null for tools not implemented yet (clicking the canvas does nothing).</param>
public record ToolViewModel(string Name, string Label, string Shortcut, ITool? Tool = null)
{
    public Avalonia.Media.Geometry? Icon { get; } = ToolIcons.For(Name);
    public Avalonia.Collections.AvaloniaList<double>? IconDashes { get; } = ToolIcons.IsDashed(Name) ? [2, 1.5] : null;

    /// <summary>Tools not implemented yet are shown dimmed.</summary>
    public double IconOpacity => Tool is null && Name is not ("Pan" or "Zoom") ? 0.35 : 1;

    public string ToolTip => IconOpacity < 1 ? $"{Name} ({Shortcut}) — coming soon" : $"{Name} ({Shortcut})";

    public bool IsSelectionTool => Tool is ShapeSelectionTool or MagicWandTool;
    public bool HasTolerance => Tool is MagicWandTool or PaintBucketTool or RecolorTool;
    public bool HasBrushWidth => Tool is PaintbrushTool and not PencilTool or LineTool or ShapesTool;

    /// <summary>Round brushes: hardness option and a brush-size outline under the pointer.</summary>
    public bool IsBrush => Tool is PaintbrushTool and not PencilTool;

    public bool IsShapes => Tool is ShapesTool;
    public bool IsGradient => Tool is GradientTool;
    public bool IsColorPicker => Tool is ColorPickerTool;
    public bool IsText => Tool is TextTool;

    /// <summary>Tools that paint get a crosshair cursor.</summary>
    public bool IsPaintingTool => Tool is PaintbrushTool or DragShapeTool or LineTool or PaintBucketTool or ColorPickerTool or CropTool;

    public static ToolViewModel[] CreatePaintDotNetTools(ToolSettings settings, ITextRasterizer textRasterizer) =>
    [
        new("Move Selected Pixels", "Mv", "M", new MoveSelectedPixelsTool()),
        new("Move Selection", "MS", "M", new MoveSelectionTool()),
        new("Zoom", "Zm", "Z"),
        new("Pan", "Pn", "H"),
        new("Rectangle Select", "RS", "S", new RectangleSelectTool(settings)),
        new("Ellipse Select", "ES", "S", new EllipseSelectTool(settings)),
        new("Lasso Select", "LS", "S", new LassoSelectTool(settings)),
        new("Magic Wand", "MW", "S", new MagicWandTool(settings)),
        new("Crop", "Cr", "C", new CropTool(settings)),
        new("Paint Bucket", "Fi", "F", new PaintBucketTool(settings)),
        new("Gradient", "Gr", "G", new GradientTool(settings)),
        new("Paintbrush", "Br", "B", new PaintbrushTool(settings)),
        new("Eraser", "Er", "E", new EraserTool(settings)),
        new("Pencil", "Pe", "P", new PencilTool(settings)),
        new("Color Picker", "CP", "K", new ColorPickerTool(settings)),
        new("Clone Stamp", "CS", "L", new CloneStampTool(settings)),
        new("Recolor", "Rc", "R", new RecolorTool(settings)),
        new("Text", "Tx", "T", new TextTool(settings, textRasterizer)),
        new("Line / Curve", "Ln", "O", new LineTool(settings)),
        new("Shapes", "Sh", "O", new ShapesTool(settings)),
    ];
}
