using System;
using System.Collections.Generic;
using System.Linq;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Core.Vector.Tools;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CinnabarSharp.Desktop.ViewModels;

// The tools of SVG drawings: a toolbox of its own, pointer/key routing in user space, options bar.
public partial class MainViewModel
{
    private ToolViewModel? _lastRasterTool;
    private ToolViewModel? _lastVectorTool;
    private bool _toolboxIsVector;

    public ToolViewModel[] VectorTools { get; private set; } = [];

    /// <summary>The toolbox shown: the raster tools, or the vector tools while a drawing is active.</summary>
    public ToolViewModel[] ToolboxTools => _toolboxIsVector ? VectorTools : Tools;

    /// <summary>What the toolbox list selects. Ignores null, which the list writes while it swaps its items.</summary>
    public ToolViewModel? ToolboxSelection
    {
        get => SelectedTool;
        set
        {
            if (value is not null)
                SelectedTool = value;
        }
    }

    private void CreateVectorTools() => VectorTools = ToolViewModel.CreateVectorTools(ToolSettings, Tools);

    /// <summary>Swaps the toolbox when the active tab changes between an image and a drawing.</summary>
    private void SyncToolbox()
    {
        var vector = ActiveSvg is not null;
        if (vector == _toolboxIsVector)
            return;
        if (vector)
            _lastRasterTool = SelectedTool;
        else
            _lastVectorTool = SelectedTool;
        _toolboxIsVector = vector;
        SelectedTool = vector ? _lastVectorTool ?? VectorTools[0] : _lastRasterTool ?? Tools.First(t => t.Name == "Rectangle Select");
        OnPropertyChanged(nameof(ToolboxTools));
        OnPropertyChanged(nameof(ToolboxSelection));
    }

    /// <summary>What the selected vector tool does, for the status bar; the Select tool's hint follows its state.</summary>
    private string? VectorHint(SvgDocument drawing) => ActiveVectorTool switch
    {
        VectorSelectTool { RotateMode: true } => "drag a corner to rotate, the center dot to move the pivot · click again for resize handles",
        VectorSelectTool when drawing.Selection.IsEmpty => "click an object, drag a box to select several, Shift+click to add · Alt+click selects below",
        VectorSelectTool => "drag to move · click the selection again for rotate handles · arrows nudge (Shift = 10) · Delete removes",
        VectorNodeTool => "click a node or drag a box to select nodes · drag nodes and handles · double-click a segment to add a node · Delete removes",
        VectorPenTool => "click for corner nodes, drag for smooth ones · click the first node to close · Enter finishes, Esc cancels, Backspace removes the last node",
        VectorPencilTool => "draw freehand: the line is smoothed into curves (see Smoothing)",
        VectorRectangleTool or VectorEllipseTool => "drag to draw · Shift = square or circle · Alt = from the center",
        VectorLineTool => "drag to draw · Shift = 15° steps · Alt = from the center",
        VectorPolygonTool => "drag from the center · Shift = 15° steps · corners and star depth in the options",
        VectorTextTool => "click to place text and type · click a text to edit it · Esc finishes",
        VectorGradientTool => "select an object, then drag across it to make a gradient",
        VectorEyedropperTool => "click an object to take its colors · Shift = the pixel color",
        _ => null,
    };

    /// <summary>The text under the pointer (image coordinates), if it is a plain one the Text tool can edit.</summary>
    private static CinnabarSharp.Vector.SvgText? TextAt(SvgDocument drawing, ToolPointer pointer)
    {
        var p = ImageToUser(drawing, pointer.Position);
        return SvgHitTester.HitTest(drawing, new CinnabarSharp.Vector.VPoint(p.X, p.Y), drawing.ScreenToUser(3), enterGroups: true)
            is CinnabarSharp.Vector.SvgText text && !text.Children.OfType<CinnabarSharp.Vector.SvgTextSpan>().Any() ? text : null;
    }

    private IVectorTool? ActiveVectorTool => SelectedTool?.VectorTool;

    private void WithVectorTool(SvgDocument drawing, ToolPointer pointer, Action<IVectorTool, SvgDocument, ToolPointer> handler)
    {
        if (ActiveVectorTool is not { } tool)
            return;
        pointer = pointer with { Position = ImageToUser(drawing, pointer.Position) };
        handler(tool, drawing, tool is IGridSnappingTool ? SnapToGridUser(drawing, pointer) : pointer);
    }

    private static PointD ImageToUser(SvgDocument drawing, PointD image)
    {
        var p = drawing.ImageToUserPoint(new CinnabarSharp.Vector.VPoint(image.X, image.Y));
        return new PointD(p.X, p.Y);
    }

    private ToolOverlay? VectorToolOverlay(SvgDocument drawing) =>
        (ActiveVectorTool?.GetOverlay(drawing)) ?? SvgSelectionOverlay.For(drawing);

    private void FinishVectorEditing(IVectorTool? tool, SvgDocument? drawing)
    {
        if (drawing is null || tool is not IVectorEditingTool editing || !editing.IsEditing(drawing))
            return;
        editing.Finish(drawing);
    }

    private IVectorTextTool? EditingVectorText =>
        ActiveSvg is { } d && ActiveVectorTool is IVectorTextTool t && t.IsEditing(d) ? t : null;

    private bool IsVectorTyping => ActiveSvg is { } d && ActiveVectorTool is IVectorKeyboardTool k && k.IsTyping(d);

    private bool VectorToolKeyDown(SvgDocument drawing, ToolKey key, ToolModifiers modifiers)
    {
        var handled = ActiveVectorTool is IVectorKeyboardTool tool && tool.OnKeyDown(drawing, key, modifiers);
        // Escape that the tool doesn't use deselects the objects.
        if (!handled && key == ToolKey.Escape && modifiers == ToolModifiers.None && !drawing.Selection.IsEmpty)
        {
            drawing.Selection.Clear();
            handled = true;
        }
        UpdateOverlay();
        return handled;
    }

    private void RefreshEditingVectorTool()
    {
        if (ActiveSvg is not { } d || ActiveVectorTool is not IVectorEditingTool tool || !tool.IsEditing(d))
            return;
        tool.Refresh(d);
        UpdateOverlay();
    }

    // ---- Options bar ----

    public bool ShowVectorSelectOptions => SelectedTool?.IsVectorSelect == true;
    public bool ShowVectorNodeOptions => SelectedTool?.IsVectorNode == true;
    public bool ShowVectorShapeOptions => SelectedTool?.IsVectorShape == true;
    public bool ShowVectorRectangleOptions => SelectedTool?.IsVectorRectangle == true;
    public bool ShowVectorPolygonOptions => SelectedTool?.IsVectorPolygon == true;
    public bool ShowVectorPencilOptions => SelectedTool?.IsVectorPencil == true;
    public bool ShowVectorGradientOptions => SelectedTool?.IsVectorGradient == true;
    public bool ShowVectorStrokeOptions => SelectedTool?.HasVectorStroke == true;

    private void RaiseVectorOptionFlags()
    {
        foreach (var name in new[]
                 {
                     nameof(ShowVectorSelectOptions), nameof(ShowVectorNodeOptions), nameof(ShowVectorShapeOptions),
                     nameof(ShowVectorRectangleOptions), nameof(ShowVectorPolygonOptions), nameof(ShowVectorPencilOptions),
                     nameof(ShowVectorGradientOptions), nameof(ShowVectorStrokeOptions), nameof(ShowGridOptions), nameof(ShowLibraryShapeOptions),
                 })
            OnPropertyChanged(name);
    }

    public double VectorCornerRadius
    {
        get => ToolSettings.VectorCornerRadius;
        set { ToolSettings.VectorCornerRadius = Math.Max(0, value); OnPropertyChanged(); }
    }

    public int PolygonCorners
    {
        get => ToolSettings.PolygonCorners;
        set { ToolSettings.PolygonCorners = Math.Clamp(value, 3, 100); OnPropertyChanged(); }
    }

    /// <summary>0–100: 0 draws a polygon, higher values dig the star's inner corners deeper.</summary>
    public int StarRatioPercent
    {
        get => (int)Math.Round(ToolSettings.StarRatio * 100);
        set { ToolSettings.StarRatio = Math.Clamp(value, 0, 100) / 100.0; OnPropertyChanged(); }
    }

    public double PolygonRounding
    {
        get => ToolSettings.PolygonRounding;
        set { ToolSettings.PolygonRounding = Math.Max(0, value); OnPropertyChanged(); }
    }

    public bool SnapToObjects
    {
        get => ToolSettings.SnapToObjects;
        set { ToolSettings.SnapToObjects = value; OnPropertyChanged(); }
    }

    public int PencilSmoothing
    {
        get => ToolSettings.PencilSmoothing;
        set { ToolSettings.PencilSmoothing = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
    }

    public bool GradientOnStroke
    {
        get => ToolSettings.GradientOnStroke;
        set { ToolSettings.GradientOnStroke = value; OnPropertyChanged(); }
    }

    // ---- Node tool buttons ----

    private VectorNodeTool? NodeTool => ActiveVectorTool as VectorNodeTool;

    private void WithNodeTool(Action<VectorNodeTool, SvgDocument> action)
    {
        if (NodeTool is { } tool && ActiveSvg is { } drawing)
        {
            action(tool, drawing);
            UpdateOverlay();
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NodesCorner() => WithNodeTool((t, d) => t.SetNodeType(d, CinnabarSharp.Vector.NodeType.Corner));

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NodesSmooth() => WithNodeTool((t, d) => t.SetNodeType(d, CinnabarSharp.Vector.NodeType.Smooth));

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NodesSymmetric() => WithNodeTool((t, d) => t.SetNodeType(d, CinnabarSharp.Vector.NodeType.Symmetric));

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SegmentsToLines() => WithNodeTool((t, d) => t.SetSegments(d, line: true));

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SegmentsToCurves() => WithNodeTool((t, d) => t.SetSegments(d, line: false));

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NodesJoin() => WithNodeTool((t, d) => t.JoinNodes(d));

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NodesBreak() => WithNodeTool((t, d) => t.BreakNodes(d));

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ConvertToPath() => WithNodeTool((t, d) => t.ConvertToPath(d));
}
