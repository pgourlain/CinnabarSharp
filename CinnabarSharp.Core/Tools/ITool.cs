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

/// <summary>Options shared by tools, edited in the tool options bar.</summary>
public class ToolSettings
{
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
