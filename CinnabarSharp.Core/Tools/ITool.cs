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
/// <param name="ClickCount">1 for a single click, 2 for a double click (on press).</param>
public readonly record struct ToolPointer(PointD Position, ToolButton Button, ToolModifiers Modifiers, double Pressure = 1, int ClickCount = 1);

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

/// <summary>Body of a speech bubble.</summary>
public enum BubbleStyle
{
    Square,
    Rounded,
    Oval,

    /// <summary>A cloud with a trail of small circles instead of a pointed tail.</summary>
    Thought,
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

    public BubbleStyle BubbleStyle { get; set; } = BubbleStyle.Rounded;

    /// <summary>Speech bubbles get a numbered badge (1, 2, 3...), for step-by-step explanations.</summary>
    public bool BubbleNumbered { get; set; }

    private int _bubbleNextNumber = 1;

    /// <summary>Raised when a speech bubble takes a number (<see cref="BubbleNextNumber"/> changed).</summary>
    public event Action? BubbleNumberChanged;

    /// <summary>Number of the next numbered speech bubble.</summary>
    public int BubbleNextNumber
    {
        get => _bubbleNextNumber;
        set { _bubbleNextNumber = Math.Max(1, value); BubbleNumberChanged?.Invoke(); }
    }

    /// <summary>Speech bubbles are drawn on a "Bubbles" layer at the top, created when needed.</summary>
    public bool BubbleOwnLayer { get; set; } = true;

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

    /// <summary>The ready-made shape the Shape tool draws (see <c>ShapeLibrary</c>), by id such as "basic/star".</summary>
    public string LibraryShape { get; set; } = "basic/star";

    // ---- Grid ----

    /// <summary>Draws a grid over the picture.</summary>
    public bool ShowGrid { get; set; }

    /// <summary>The drawing tools snap what they place to the grid (hold Alt to place freely).</summary>
    public bool SnapToGrid { get; set; }

    /// <summary>Distance between grid lines: pixels on an image, user units on a drawing.</summary>
    public double GridSize { get; set; } = 10;

    // ---- Vector (SVG) tools ----

    /// <summary>Corner radius of rectangles drawn with the vector Rectangle tool, in user units.</summary>
    public double VectorCornerRadius { get; set; }

    /// <summary>Number of corners of polygons and stars.</summary>
    public int PolygonCorners { get; set; } = 5;

    /// <summary>The Polygon tool draws stars: the inner points sit at this fraction of the outer radius (0 = a plain polygon).</summary>
    public double StarRatio { get; set; }

    /// <summary>Rounds the corners of polygons and stars by this fraction (0 = sharp, 1 = as round as the sides allow).</summary>
    public double PolygonRounding { get; set; }

    /// <summary>The Select tool snaps moves to the page and to the bounding boxes of other objects.</summary>
    public bool SnapToObjects { get; set; } = true;

    /// <summary>The Pencil tool's smoothing, 0 to 100: how far the fitted curve may stray from the drawn line.</summary>
    public int PencilSmoothing { get; set; } = 50;

    /// <summary>The Gradient tool works on the stroke instead of the fill.</summary>
    public bool GradientOnStroke { get; set; }
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

/// <summary>A tool editing text: the Edit menu's Select All and clipboard commands go to its text.</summary>
public interface ITextEditingTool : IKeyboardTool
{
    void SelectAll(ImageDocument document);
    Task Copy(Services.IClipboardService clipboard);
    Task Cut(ImageDocument document, Services.IClipboardService clipboard);
    Task Paste(ImageDocument document, Services.IClipboardService clipboard);
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

    /// <summary>Draws <see cref="Frame"/> as a thick accent-colored outline (screen pixels) instead of the thin dashed one,
    /// for a selection that must be easy to see.</summary>
    public bool EmphasizeFrame { get; init; }

    /// <summary>Highlighted areas, e.g. selected text.</summary>
    public IReadOnlyList<RectangleD> Highlights { get; init; } = [];

    /// <summary>Everything outside this rectangle is shaded (what a crop will cut away).</summary>
    public RectangleD? Shade { get; init; }

    /// <summary>A picture drawn over the canvas, e.g. a preview of what a TV will show.</summary>
    public OverlayPicture? Picture { get; init; }

    /// <summary>The rotate grip's position, drawn with a distinct two-arrow rotate icon instead of a plain
    /// handle (e.g. the Text tool's). In the same unrotated coordinates as everything else in this overlay.</summary>
    public PointD? RotateHandle { get; init; }

    /// <summary>Rotates every other part of this overlay (<see cref="Frame"/>, <see cref="Lines"/>,
    /// <see cref="Highlights"/>, <see cref="Handles"/>, <see cref="RotateHandle"/>) by <c>Angle</c> radians
    /// around <c>Pivot</c> when drawn — the canvas pixels themselves are rotated separately by the tool.</summary>
    public (double Angle, PointD Pivot)? Rotation { get; init; }
}

/// <summary>Straight-alpha BGRA pixels drawn stretched over <paramref name="Area"/> (image coordinates).</summary>
public sealed record OverlayPicture(byte[] Bgra, int Width, int Height, RectangleD Area);

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

    /// <summary>Over a rotate handle.</summary>
    Rotate,

    /// <summary>Over editable text (I-beam).</summary>
    Text,
}

public interface IOverlayTool : ITool
{
    ToolOverlay? GetOverlay(ImageDocument document);

    /// <summary>The cursor over <paramref name="point"/> (image coordinates) when no button is pressed.</summary>
    ToolCursor CursorAt(ImageDocument document, PointD point) => ToolCursor.Default;
}
