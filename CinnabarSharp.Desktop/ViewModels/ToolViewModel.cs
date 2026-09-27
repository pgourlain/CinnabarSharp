namespace CinnabarSharp.Desktop.ViewModels;

public record ToolViewModel(string Name, string Label, string Shortcut)
{
    public string ToolTip => $"{Name} ({Shortcut})";

    public static ToolViewModel[] PaintDotNetTools { get; } =
    [
        new("Move Selected Pixels", "Mv", "M"),
        new("Move Selection", "MS", "M"),
        new("Zoom", "Zm", "Z"),
        new("Pan", "Pn", "H"),
        new("Rectangle Select", "RS", "S"),
        new("Ellipse Select", "ES", "S"),
        new("Lasso Select", "LS", "S"),
        new("Magic Wand", "MW", "S"),
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
