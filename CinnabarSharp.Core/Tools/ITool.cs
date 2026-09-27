using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Tools;

public enum ToolButton
{
    Left,
    Right,
    Middle,
}

[Flags]
public enum ToolModifiers
{
    None = 0,
    Shift = 1,

    /// <summary>The platform command key: ⌘ on macOS, Ctrl elsewhere.</summary>
    Command = 2,
    Alt = 4,
}

/// <summary>Pointer input in image coordinates (pixels, fractional); independent of any UI framework.</summary>
public readonly record struct ToolPointer(PointD Position, ToolButton Button, ToolModifiers Modifiers);

public enum ShapeKind
{
    Rectangle,
    Ellipse,
}

public enum ShapeStyle
{
    Outline,
    Fill,

    /// <summary>Outline in the primary color, filled with the secondary color (Paint.NET).</summary>
    OutlineAndFill,
}

public enum GradientKind
{
    Linear,
    Radial,
    Diamond,
    Conical,
}

/// <summary>Options shared by tools, edited in the tool options bar and the palette.</summary>
public class ToolSettings
{
    private ColorBgra _primary = ColorBgra.Black;
    private ColorBgra _secondary = ColorBgra.White;

    /// <summary>Raised when a tool (the Color Picker) changes a color.</summary>
    public event Action? ColorsChanged;

    /// <summary>Left button paints with it.</summary>
    public ColorBgra PrimaryColor
    {
        get => _primary;
        set { _primary = value; ColorsChanged?.Invoke(); }
    }

    /// <summary>Right button paints with it.</summary>
    public ColorBgra SecondaryColor
    {
        get => _secondary;
        set { _secondary = value; ColorsChanged?.Invoke(); }
    }

    public ColorBgra ColorFor(ToolButton button) => button == ToolButton.Right ? SecondaryColor : PrimaryColor;

    /// <summary>Brush, eraser, line and shape outline width, in pixels.</summary>
    public int BrushWidth { get; set; } = 2;

    public bool Antialiasing { get; set; } = true;

    public ShapeKind ShapeKind { get; set; } = ShapeKind.Rectangle;
    public ShapeStyle ShapeStyle { get; set; } = ShapeStyle.Outline;
    public GradientKind GradientKind { get; set; } = GradientKind.Linear;

    /// <summary>Color Picker samples the merged image instead of the current layer.</summary>
    public bool SampleImage { get; set; }

    public SelectionMode SelectionMode { get; set; } = SelectionMode.Replace;

    /// <summary>Magic wand tolerance, 0–100 %.</summary>
    public int Tolerance { get; set; } = 50;

    /// <summary>Magic wand selects matching pixels anywhere, not only connected ones.</summary>
    public bool GlobalFill { get; set; }
}

/// <summary>
/// A canvas tool. Pointer-down starts an operation on the document, moves update it (with live preview),
/// pointer-up finishes it and records one history step.
/// </summary>
public interface ITool
{
    string Name { get; }
    void OnPointerDown(ImageDocument document, ToolPointer pointer);
    void OnPointerMove(ImageDocument document, ToolPointer pointer);
    void OnPointerUp(ImageDocument document, ToolPointer pointer);
}
