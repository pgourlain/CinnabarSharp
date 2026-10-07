using System;
using System.Collections.Generic;
using System.Linq;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Vector.Tools;

namespace CinnabarSharp.Desktop.ViewModels;

/// <param name="Tool">Core implementation, or null for tools not implemented yet (clicking the canvas does nothing).</param>
public record ToolViewModel(string Name, string Label, string Shortcut, ITool? Tool = null)
{
    /// <summary>Implementation for SVG drawings (the toolbox of a drawing lists these instead of <see cref="Tool"/>).</summary>
    public IVectorTool? VectorTool { get; init; }

    public Avalonia.Media.Geometry? Icon { get; } = ToolIcons.For(Name);
    public Avalonia.Collections.AvaloniaList<double>? IconDashes { get; } = ToolIcons.IsDashed(Name) ? [2, 1.5] : null;

    /// <summary>Tools not implemented yet are shown dimmed.</summary>
    public double IconOpacity => Tool is null && VectorTool is null && Name is not ("Pan" or "Zoom") ? 0.35 : 1;

    public string ToolTip => IconOpacity < 1 ? $"{Name} ({Shortcut}) — coming soon" : $"{Name} ({Shortcut})";

    public bool IsSelectionTool => Tool is ShapeSelectionTool or MagicWandTool;
    public bool HasTolerance => Tool is MagicWandTool or PaintBucketTool or RecolorTool;
    public bool HasBrushWidth => Tool is PaintbrushTool and not PencilTool or LineTool or ShapesTool;

    /// <summary>Round brushes: hardness option and a brush-size outline under the pointer.</summary>
    public bool IsBrush => Tool is PaintbrushTool and not PencilTool;

    public bool IsShapes => Tool is ShapesTool;
    public bool IsGradient => Tool is GradientTool;
    public bool IsColorPicker => Tool is ColorPickerTool;
    public bool IsText => Tool is TextTool || VectorTool is VectorTextTool;
    public bool IsBubble => Tool is SpeechBubbleTool;

    /// <summary>Tools that paint get a crosshair cursor.</summary>
    public bool IsPaintingTool => Tool is PaintbrushTool or DragShapeTool or LineTool or PaintBucketTool or ColorPickerTool or CropTool or SpeechBubbleTool
        || VectorTool is ShapeDrawTool or VectorPenTool or VectorPencilTool or VectorGradientTool or VectorEyedropperTool;

    // Options bar of the vector tools.
    public bool IsVectorSelect => VectorTool is VectorSelectTool;
    public bool IsVectorNode => VectorTool is VectorNodeTool;
    public bool IsVectorShape => VectorTool is ShapeDrawTool;
    public bool IsVectorRectangle => VectorTool is VectorRectangleTool;
    public bool IsVectorPolygon => VectorTool is VectorPolygonTool;
    public bool IsVectorPencil => VectorTool is VectorPencilTool;
    public bool IsVectorGradient => VectorTool is VectorGradientTool;
    public bool HasVectorStroke => VectorTool is ShapeDrawTool or VectorPenTool or VectorPencilTool;

    /// <summary>The toolbox of an SVG drawing; Zoom and Pan are the raster ones (shared).</summary>
    public static ToolViewModel[] CreateVectorTools(ToolSettings settings, IEnumerable<ToolViewModel> shared) =>
    [
        new("Select", "Se", "S") { VectorTool = new VectorSelectTool(settings) },
        new("Node", "No", "N") { VectorTool = new VectorNodeTool(settings) },
        .. shared.Where(t => t.Name is "Zoom" or "Pan"),
        new("Pen", "Pn", "B") { VectorTool = new VectorPenTool(settings) },
        new("Pencil", "Pe", "P") { VectorTool = new VectorPencilTool(settings) },
        new("Rectangle", "Re", "R") { VectorTool = new VectorRectangleTool(settings) },
        new("Ellipse", "El", "E") { VectorTool = new VectorEllipseTool(settings) },
        new("Line", "Li", "L") { VectorTool = new VectorLineTool(settings) },
        new("Polygon / Star", "Po", "Y") { VectorTool = new VectorPolygonTool(settings) },
        new("Text", "Tx", "T") { VectorTool = new VectorTextTool(settings) },
        new("Gradient", "Gr", "G") { VectorTool = new VectorGradientTool(settings) },
        new("Eyedropper", "Ey", "K") { VectorTool = new VectorEyedropperTool(settings) },
    ];

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
        new("Speech Bubble", "Bu", "U", new SpeechBubbleTool(settings, textRasterizer)),
        new("Line / Curve", "Ln", "O", new LineTool(settings)),
        new("Shapes", "Sh", "O", new ShapesTool(settings)),
    ];
}
