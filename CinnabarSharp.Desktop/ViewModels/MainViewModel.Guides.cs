using System;
using System.Collections.Generic;
using System.Linq;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CinnabarSharp.Desktop.ViewModels;

/// <summary>A guide as drawn: its direction and its place in picture pixels.</summary>
public sealed record GuideLine(GuideOrientation Orientation, double Position);

// Rulers around the picture, and the guide lines dragged out of them.
public partial class MainViewModel
{
    private GuideSet? _watchedGuides;

    public bool ShowRulers
    {
        get => ToolSettings.ShowRulers;
        set { ToolSettings.ShowRulers = value; OnPropertyChanged(); RefreshGuides(); }
    }

    public bool SnapToGuides
    {
        get => ToolSettings.SnapToGuides;
        set { ToolSettings.SnapToGuides = value; OnPropertyChanged(); }
    }

    /// <summary>The guides to draw over the picture: those of the active document while the rulers are shown.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<GuideLine>? Guides { get; set; }

    public GuideSet? ActiveGuides => ActiveDocument?.Document.Workspace.Guides;

    public bool HasGuides => ActiveGuides is { Count: > 0 };

    [RelayCommand]
    private void ToggleRulers() => ShowRulers = !ShowRulers;

    [RelayCommand]
    private void ToggleSnapToGuides() => SnapToGuides = !SnapToGuides;

    [RelayCommand(CanExecute = nameof(HasGuides))]
    private void ClearGuides() => ActiveGuides?.Clear();

    /// <summary>Follows the guides of the active document (called when the document, the rulers or the guides change).</summary>
    private void RefreshGuides()
    {
        var guides = ActiveGuides;
        if (!ReferenceEquals(guides, _watchedGuides))
        {
            if (_watchedGuides is not null)
                _watchedGuides.Changed -= RefreshGuides;
            _watchedGuides = guides;
            if (guides is not null)
                guides.Changed += RefreshGuides;
        }
        Guides = ToolSettings.ShowRulers && guides is { Count: > 0 }
            ? guides.Items.Select(g => new GuideLine(g.Orientation, g.Position)).ToList()
            : null;
        OnPropertyChanged(nameof(HasGuides));
        ClearGuidesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Moves a point (picture pixels) onto the guides that are within a few screen pixels, when guides are on;
    /// tells on which axes it did, so the grid leaves those alone.
    /// </summary>
    private (PointD Point, bool X, bool Y) SnapToGuidesImage(PointD point)
    {
        if (!ToolSettings.SnapToGuides || ActiveGuides is not { Count: > 0 } guides || ActiveDocument is null)
            return (point, false, false);
        var reach = 6 / Math.Max(ActiveDocument.Document.Workspace.Scale, 1e-6);
        var x = guides.Snap(GuideOrientation.Vertical, point.X, reach);
        var y = guides.Snap(GuideOrientation.Horizontal, point.Y, reach);
        return (new PointD(x ?? point.X, y ?? point.Y), x is not null, y is not null);
    }
}
