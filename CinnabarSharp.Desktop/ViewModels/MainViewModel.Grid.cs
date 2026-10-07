using System;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Core.Vector.Tools;
using CommunityToolkit.Mvvm.Input;

namespace CinnabarSharp.Desktop.ViewModels;

/// <summary>The grid to draw over the picture: line spacing and where the lines start, in image pixels.</summary>
public sealed record GridInfo(double Spacing, double OriginX, double OriginY);

// The grid: shown over the picture, and what the drawing tools snap to.
public partial class MainViewModel
{
    public bool ShowGrid
    {
        get => ToolSettings.ShowGrid;
        set { ToolSettings.ShowGrid = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowGridOptions)); UpdateGrid(); }
    }

    public bool SnapToGrid
    {
        get => ToolSettings.SnapToGrid;
        set { ToolSettings.SnapToGrid = value; OnPropertyChanged(); }
    }

    /// <summary>Distance between lines: pixels on an image, user units on a drawing.</summary>
    public double GridSize
    {
        get => ToolSettings.GridSize;
        set
        {
            ToolSettings.GridSize = Math.Clamp(double.IsFinite(value) ? value : 10, GridSnapping.MinSize, GridSnapping.MaxSize);
            OnPropertyChanged();
            UpdateGrid();
        }
    }

    /// <summary>The grid options sit in the options bar for the tools that snap, and while the grid is shown.</summary>
    public bool ShowGridOptions => HasContent && (ToolSettings.ShowGrid
        || SelectedTool is { Tool: IGridSnappingTool } or { VectorTool: IGridSnappingTool or VectorSelectTool });

    [RelayCommand]
    private void ToggleGrid() => ShowGrid = !ShowGrid;

    [RelayCommand]
    private void ToggleSnapToGrid() => SnapToGrid = !SnapToGrid;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial GridInfo? Grid { get; set; }

    private void UpdateGrid()
    {
        Grid = !ToolSettings.ShowGrid || !HasContent ? null
            : ActiveSvg is { } drawing
                ? SvgGrid(drawing)
                : new GridInfo(ToolSettings.GridSize, 0, 0);
    }

    private GridInfo SvgGrid(SvgDocument drawing)
    {
        var toImage = drawing.UserToImage;
        var origin = drawing.Root.ViewBox is { } box ? toImage.Transform(new CinnabarSharp.Vector.VPoint(box.X, box.Y)) : default;
        return new GridInfo(ToolSettings.GridSize * toImage.MeanScale, origin.X, origin.Y);
    }

    /// <summary>A drawing tool's pointer, moved to the grid when snapping is on (positions in user space of the drawing).</summary>
    private ToolPointer SnapToGridUser(SvgDocument drawing, ToolPointer pointer, bool keepX = false, bool keepY = false)
    {
        if (!ToolSettings.SnapToGrid)
            return pointer;
        var origin = drawing.Root.ViewBox is { } box ? new PointD(box.X, box.Y) : default;
        var snapped = GridSnapping.Snap(pointer.Position, ToolSettings.GridSize, origin);
        // An axis that already snapped to a guide stays where the guide put it.
        return pointer with { Position = new PointD(keepX ? pointer.Position.X : snapped.X, keepY ? pointer.Position.Y : snapped.Y) };
    }
}
