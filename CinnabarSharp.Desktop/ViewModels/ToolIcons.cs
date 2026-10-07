using System.Collections.Generic;
using Avalonia.Media;

namespace CinnabarSharp.Desktop.ViewModels;

/// <summary>
/// Line icons for the tools, drawn on a 24×24 grid (stroked, not filled). Selection tools are dashed,
/// like marching ants.
/// </summary>
public static class ToolIcons
{
    private static readonly Dictionary<string, (string Path, bool Dashed)> Paths = new()
    {
        ["Move Selected Pixels"] = ("M12 3v18 M3 12h18 M12 3l-3 3 M12 3l3 3 M12 21l-3-3 M12 21l3-3 M3 12l3-3 M3 12l3 3 M21 12l-3-3 M21 12l-3 3", false),
        ["Move Selection"] = ("M12 1v6 M12 17v6 M1 12h6 M17 12h6 M12 1l-2 2 M12 1l2 2 M12 23l-2-2 M12 23l2-2 M1 12l2-2 M1 12l2 2 M23 12l-2-2 M23 12l-2 2 M8 8h8v8h-8z", false),
        ["Zoom"] = ("M10 3a7 7 0 1 0 0 14a7 7 0 1 0 0-14 M15 15l6 6 M7 10h6 M10 7v6", false),
        ["Pan"] = ("M8 13V6a1.5 1.5 0 0 1 3 0v6 M11 11V4a1.5 1.5 0 0 1 3 0v7 M14 11V6a1.5 1.5 0 0 1 3 0v6 M17 12V9a1.5 1.5 0 0 1 3 0v5c0 4-3 7-7 7h-1c-3 0-5-2-6-4l-3-5a1.5 1.5 0 0 1 2.5-1.5L8 15", false),
        ["Rectangle Select"] = ("M4 4h16v16H4z", true),
        ["Ellipse Select"] = ("M3 12a9 7 0 1 0 18 0a9 7 0 1 0-18 0", true),
        ["Lasso Select"] = ("M7 17c-3-1-4-4-3-7 1-4 6-6 10-5s7 4 5 7-6 5-10 5 M7 17c1 1 1 3-2 4", true),
        ["Crop"] = ("M6 2v16h16 M2 6h16v16", false),
        ["Magic Wand"] = ("M4 20L14 10 M16 2v4 M14 4h4 M20 8v2 M19 9h2 M10 3v2 M9 4h2", false),
        ["Paint Bucket"] = ("M4 11l7-7 7 7-7 7z M4 11h14 M20 14c0 2 1 3 1 4a1 1 0 0 1-2 0c0-1 1-2 1-4z", false),
        ["Gradient"] = ("M3 3h18v18H3z M3 21L21 3 M3 14L14 3 M10 21L21 10", false),
        ["Paintbrush"] = ("M18 3l3 3-9 9-3-3z M9 12c-3 0-5 2-5 5 0 2-1 3-2 4 4 0 8-1 9-4", false),
        ["Eraser"] = ("M7 21h13 M4 15l9-9 6 6-9 9H8z M8 11l6 6", false),
        ["Pencil"] = ("M4 20l1-5L16 4l4 4L9 19z M14 6l4 4 M5 15l4 4", false),
        ["Color Picker"] = ("M19 5a2 2 0 0 0-3-1l-3 3-1-1-2 2 1 1-7 7v3h3l7-7 1 1 2-2-1-1 3-3a2 2 0 0 0 0-2z", false),
        ["Clone Stamp"] = ("M9 13V8a3 3 0 1 1 6 0v5 M5 13h14v4H5z M4 21h16", false),
        ["Recolor"] = ("M4 12a8 8 0 0 1 14-5 M20 12a8 8 0 0 1-14 5 M18 3v4h-4 M6 21v-4h4", false),
        ["Text"] = ("M5 5h14 M12 5v14 M9 19h6", false),
        ["Speech Bubble"] = ("M5 4h14a3 3 0 0 1 3 3v7a3 3 0 0 1-3 3h-8l-5 4v-4H5a3 3 0 0 1-3-3V7a3 3 0 0 1 3-3z", false),
        ["Line / Curve"] = ("M4 20L20 4 M4 20a1 1 0 1 0 0.1 0 M20 4a1 1 0 1 0 0.1 0", false),
        ["Select"] = ("M5 3l14 8-6 2 4 7-3 1-4-7-5 4z", false),
        ["Node"] = ("M5 3l14 8-6 2 4 7-3 1-4-7-5 4z M17 3h4v4h-4z", false),
        ["Pen"] = ("M12 3l7 9-7 9-7-9z M12 3v10 M12 13a1 1 0 1 0 0.1 0", false),
        ["Rectangle"] = ("M4 6h16v12H4z", false),
        ["Ellipse"] = ("M3 12a9 7 0 1 0 18 0a9 7 0 1 0-18 0", false),
        ["Line"] = ("M4 20L20 4", false),
        ["Polygon / Star"] = ("M12 3l2.6 6 6.4.6-4.8 4.3 1.5 6.3L12 17l-5.7 3.2 1.5-6.3L3 9.6 9.4 9z", false),
        ["Shape"] = ("M3 20 L9 8 L15 20Z M19 7 a4 4 0 1 0 0.01 0 M14 21 L21 21", false),
        ["Eyedropper"] = ("M19 5a2 2 0 0 0-3-1l-3 3-1-1-2 2 1 1-7 7v3h3l7-7 1 1 2-2-1-1 3-3a2 2 0 0 0 0-2z", false),
        ["Scissors"] = ("M6 6a3 3 0 1 0 0.01 0 M6 18a3 3 0 1 0 0.01 0 M8.5 7.5L20 18 M8.5 16.5L20 6", false),
        ["Shapes"] = ("M3 3h10v10H3z M17 21a5 5 0 1 0 0-10a5 5 0 1 0 0 10", false),
    };

    public static Geometry? For(string toolName) =>
        Paths.TryGetValue(toolName, out var icon) ? Geometry.Parse(icon.Path) : null;

    public static bool IsDashed(string toolName) => Paths.TryGetValue(toolName, out var icon) && icon.Dashed;
}
