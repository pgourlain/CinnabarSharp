using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;

namespace CinnabarSharp.Core.Vector.Tools;

/// <summary>
/// A tool of the vector editor. Pointer positions are in the user space of the drawing (the view model converts them from
/// image coordinates); every gesture records one history step through <see cref="SvgActions"/>. Overlays are returned in
/// image coordinates, which is what the canvas draws.
/// </summary>
public interface IVectorTool
{
    string Name { get; }

    void OnPointerDown(SvgDocument document, ToolPointer pointer);

    void OnPointerMove(SvgDocument document, ToolPointer pointer);

    void OnPointerUp(SvgDocument document, ToolPointer pointer);

    /// <summary>What the tool draws over the canvas (handles, frames, guides), in image coordinates; null for nothing.</summary>
    ToolOverlay? GetOverlay(SvgDocument document) => null;

    /// <summary>The cursor over a point of the user space when no button is pressed.</summary>
    ToolCursor CursorAt(SvgDocument document, PointD userPoint) => ToolCursor.Default;
}

/// <summary>A tool whose result stays editable until finished (a pen path, a text): any other history change ends it.</summary>
public interface IVectorEditingTool : IVectorTool
{
    bool IsEditing(SvgDocument document);

    /// <summary>Keeps the result as it is and stops editing it (records its history step).</summary>
    void Finish(SvgDocument document);

    /// <summary>Redraws the result being edited with the current settings (colors, width, font…).</summary>
    void Refresh(SvgDocument document);
}

public interface IVectorKeyboardTool : IVectorEditingTool
{
    /// <summary>Returns true if the key was used.</summary>
    bool OnKeyDown(SvgDocument document, ToolKey key, ToolModifiers modifiers);

    /// <summary>True while typed characters belong to the tool, so single-letter shortcuts must not fire.</summary>
    bool IsTyping(SvgDocument document) => false;

    void OnTextInput(SvgDocument document, string text) { }
}

/// <summary>A text tool: the Edit menu's Select All and clipboard commands go to its text while it edits.</summary>
public interface IVectorTextTool : IVectorKeyboardTool
{
    void SelectAll(SvgDocument document);

    Task Copy(Services.IClipboardService clipboard);

    Task Cut(SvgDocument document, Services.IClipboardService clipboard);

    Task Paste(SvgDocument document, Services.IClipboardService clipboard);
}
