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
/// <param name="Pressure">Pen pressure, 0–1; always 1 for a mouse.</param>
public readonly record struct ToolPointer(PointD Position, ToolButton Button, ToolModifiers Modifiers, double Pressure = 1);

public enum ShapeKind
{
    Rectangle,
    RoundedRectangle,
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

    /// <summary>Brush edge hardness, 0–100 %: 100 is a hard edge, lower values fade out towards the edge.</summary>
    public int Hardness { get; set; } = 100;

    public ShapeKind ShapeKind { get; set; } = ShapeKind.Rectangle;
    public ShapeStyle ShapeStyle { get; set; } = ShapeStyle.Outline;

    /// <summary>Corner radius of rounded rectangles, in pixels.</summary>
    public int CornerRadius { get; set; } = 20;

    public GradientKind GradientKind { get; set; } = GradientKind.Linear;

    /// <summary>The gradient fades the layer's alpha (opaque to transparent) instead of painting colors.</summary>
    public bool GradientTransparency { get; set; }

    /// <summary>Font family name; empty means the platform's default font.</summary>
    public string FontFamily { get; set; } = "";
    public double FontSize { get; set; } = 24;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public TextAlignment TextAlignment { get; set; } = TextAlignment.Left;

    /// <summary>Aspect ratio the crop frame is locked to.</summary>
    public CropAspect CropAspect { get; set; } = CropAspect.Wide;

    public TextStyle TextStyle => new(FontFamily, FontSize, Bold, Italic, Underline, Antialiasing);

    /// <summary>Color Picker samples the merged image instead of the current layer.</summary>
    public bool SampleImage { get; set; }

    public SelectionMode SelectionMode { get; set; } = SelectionMode.Replace;

    /// <summary>Magic wand tolerance, 0–100 %.</summary>
    public int Tolerance { get; set; } = 50;

    /// <summary>Magic wand selects matching pixels anywhere, not only connected ones.</summary>
    public bool GlobalFill { get; set; }
}

/// <summary>Font and options for the Text tool.</summary>
public sealed record TextStyle(string FontFamily, double Size, bool Bold, bool Italic, bool Underline, bool Antialias);

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

/// <summary>
/// A tool whose last result stays editable (curve handles, text) until it is finished: when another tool is
/// selected, another document becomes active, or anything else changes the history.
/// </summary>
public interface IEditingTool : ITool
{
    bool IsEditing(ImageDocument document);

    /// <summary>Keeps the result as it is and stops editing it.</summary>
    void Finish(ImageDocument document);

    /// <summary>Redraws the result being edited with the current settings (colors, width, font...).</summary>
    void Refresh(ImageDocument document);
}

public enum ToolKey
{
    Enter,
    Escape,
    Backspace,
    Delete,
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
}

/// <summary>A tool that reacts to keys while it is editing.</summary>
public interface IKeyboardTool : IEditingTool
{
    /// <summary>Returns true if the key was used.</summary>
    bool OnKeyDown(ImageDocument document, ToolKey key, ToolModifiers modifiers);

    /// <summary>True while typed characters belong to the tool (so single-letter shortcuts must not fire).</summary>
    bool IsTyping(ImageDocument document) => false;

    void OnTextInput(ImageDocument document, string text) { }
}

/// <summary>What a tool draws over the canvas (in image coordinates); the canvas renders it at the current zoom.</summary>
public sealed record ToolOverlay
{
    /// <summary>Draggable points, drawn as small circles (squares when <see cref="SquareHandles"/>).</summary>
    public IReadOnlyList<PointD> Handles { get; init; } = [];

    /// <summary>Handles resize a shape (selection), drawn as squares like Paint.NET's.</summary>
    public bool SquareHandles { get; init; }

    /// <summary>Thin lines: text caret, lines from curve ends to their control points.</summary>
    public IReadOnlyList<(PointD From, PointD To)> Lines { get; init; } = [];

    /// <summary>Dashed frame, e.g. around the text being edited.</summary>
    public RectangleD? Frame { get; init; }

    /// <summary>Highlighted areas, e.g. selected text.</summary>
    public IReadOnlyList<RectangleD> Highlights { get; init; } = [];

    /// <summary>Everything outside this rectangle is shaded (what a crop will cut away).</summary>
    public RectangleD? Shade { get; init; }
}

/// <summary>Mouse cursor a tool asks for over a point (e.g. resize arrows over a handle).</summary>
public enum ToolCursor
{
    Default,
    Move,

    /// <summary>Left or right edge.</summary>
    ResizeHorizontal,

    /// <summary>Top or bottom edge.</summary>
    ResizeVertical,

    /// <summary>Top-left or bottom-right corner.</summary>
    ResizeDiagonal,

    /// <summary>Top-right or bottom-left corner.</summary>
    ResizeAntiDiagonal,
}

public interface IOverlayTool : ITool
{
    ToolOverlay? GetOverlay(ImageDocument document);

    /// <summary>The cursor over <paramref name="point"/> (image coordinates) when no button is pressed.</summary>
    ToolCursor CursorAt(ImageDocument document, PointD point) => ToolCursor.Default;
}
