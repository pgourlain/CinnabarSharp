using System;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Desktop.ViewModels;

/// <summary>One row of the Objects panel: an element of the drawing, indented by its depth.</summary>
public partial class ObjectViewModel(SvgElement node, int depth, SvgActions actions, Action<ObjectViewModel> selectionChanged,
    Action<ObjectViewModel> toggleExpanded, Action<ObjectViewModel, string>? rename = null) : ViewModelBase
{
    private bool _syncing;

    public SvgElement Node { get; } = node;

    public int Depth { get; } = depth;

    public Thickness Indent => new(Depth * 14, 0, 0, 0);

    /// <summary>The label, else the id, else the element name.</summary>
    public string Name => Node.Label;

    /// <summary>The kind of object, shown next to the name ("rect", "group", "text"…).</summary>
    public string Kind => Node switch
    {
        SvgGroup { IsLayer: true } => "layer",
        SvgGroup => "group",
        SvgRoot => "svg",
        SvgPath => "path",
        SvgRect => "rectangle",
        SvgCircle => "circle",
        SvgEllipse => "ellipse",
        SvgLine => "line",
        SvgPolyline => "polyline",
        SvgPolygon => "polygon",
        SvgText => "text",
        SvgImage => "image",
        SvgUse => "clone",
        _ => Node.ElementName,
    };

    public bool IsContainer => Node is SvgGroup or SvgRoot;

    public bool HasChildren { get; init; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    /// <summary>Sets <see cref="IsExpanded"/> when the row is made, without telling the panel to rebuild.</summary>
    public bool InitiallyExpanded
    {
        init
        {
            _syncing = true;
            IsExpanded = value;
            _syncing = false;
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (!_syncing)
            toggleExpanded(this);
    }

    /// <summary>Hidden through <c>display:none</c>, like Inkscape's eye.</summary>
    public bool IsVisible
    {
        get => !StyleResolver.ComputeFor(Node).DisplayNone;
        set
        {
            if (!_syncing && value != IsVisible)
                actions.SetVisible(Node, value);
        }
    }

    /// <summary>Locked objects cannot be picked on the canvas (<c>sodipodi:insensitive</c>).</summary>
    public bool IsLocked
    {
        get => Node.IsLocked;
        set
        {
            if (!_syncing && value != Node.IsLocked)
                actions.SetLocked(Node, value);
        }
    }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (!_syncing)
            selectionChanged(this);
    }

    /// <summary>Sets the selected state from the document without telling the document back.</summary>
    public void SetSelectedQuietly(bool value)
    {
        _syncing = true;
        IsSelected = value;
        _syncing = false;
    }

    /// <summary>Renaming in place (double click in the panel).</summary>
    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditText { get; set; } = "";

    public void BeginEdit()
    {
        EditText = Name;
        IsEditing = true;
    }

    public void CommitEdit()
    {
        if (!IsEditing)
            return;
        IsEditing = false;
        if (EditText.Trim() != Name)
            rename?.Invoke(this, EditText.Trim());
    }

    public void CancelEdit() => IsEditing = false;

    public void Refresh()
    {
        _syncing = true;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsLocked));
        _syncing = false;
    }
}
