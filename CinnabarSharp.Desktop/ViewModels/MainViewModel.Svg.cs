using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Vector.Tools;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Desktop.ViewModels;

// Editing SVG drawings: the Objects panel, the Edit commands for objects, clipboard.
public partial class MainViewModel
{
    private readonly HashSet<long> _collapsedObjects = [];
    private bool _syncingObjects;

    /// <summary>The active drawing, or null when the active tab is not an SVG drawing.</summary>
    public SvgDocument? ActiveSvg => ActiveSvgTab?.Svg;

    /// <summary>Top-most object first, like the Layers panel; nested groups follow their group, indented.</summary>
    public ObservableCollection<ObjectViewModel> Objects { get; } = [];

    /// <summary>True when the active tab is an image or a drawing: the CanExecute of Edit commands that work on both.</summary>
    public bool HasContent => HasImage || HasSvg;

    public bool IsNotSvg => !HasSvg;

    public bool HasObjectSelection => ActiveSvg is { Selection.IsEmpty: false };

    public string ObjectSelectionText => ActiveSvg?.Selection switch
    {
        null or { Count: 0 } => "Nothing selected",
        { Count: 1 } selection => selection.Nodes[0].Label,
        { Count: var count } => $"{count} objects",
    };

    /// <summary>What is drawn over a drawing: the selected tool's overlay, or the frame and handles of the selection.</summary>
    private ToolOverlay? SvgOverlay(SvgDocument drawing) => SvgSelectionOverlay.For(drawing);

    private static bool IsObject(SvgElement e) => e is SvgShape or SvgGroup or SvgText or SvgImage or SvgUse or SvgRoot;

    private void RefreshObjects()
    {
        var drawing = ActiveSvg;
        var rows = new List<ObjectViewModel>();
        if (drawing is not null)
            AddRows(drawing, drawing.Root, 0, rows);

        _syncingObjects = true;
        var selected = drawing?.Selection.Nodes.ToHashSet() ?? [];
        Objects.Clear();
        foreach (var row in rows)
        {
            row.SetSelectedQuietly(selected.Contains(row.Node));
            Objects.Add(row);
        }
        _syncingObjects = false;
        OnPropertyChanged(nameof(HasObjectSelection));
        OnPropertyChanged(nameof(ObjectSelectionText));
        foreach (var command in new IRelayCommand[]
                 {
                     DeselectObjectsCommand, DeleteObjectsCommand, DuplicateObjectsCommand, GroupObjectsCommand, UngroupObjectsCommand,
                     RaiseObjectsCommand, LowerObjectsCommand, RaiseToTopCommand, LowerToBottomCommand,
                 })
            command.NotifyCanExecuteChanged();
    }

    private void AddRows(SvgDocument drawing, SvgContainer container, int depth, List<ObjectViewModel> rows)
    {
        foreach (var child in container.Elements.Reverse())
        {
            if (!IsObject(child))
                continue;
            var hasChildren = child is SvgContainer c && c.Elements.Any(IsObject);
            var row = new ObjectViewModel(child, depth, drawing.Actions, OnObjectRowSelected, ToggleObjectExpanded, RenameObject)
            {
                HasChildren = hasChildren,
                InitiallyExpanded = !_collapsedObjects.Contains(child.InternalId),
            };
            rows.Add(row);
            if (child is SvgContainer group && hasChildren && row.IsExpanded)
                AddRows(drawing, group, depth + 1, rows);
        }
    }

    private void ToggleObjectExpanded(ObjectViewModel row)
    {
        if (row.IsExpanded)
            _collapsedObjects.Remove(row.Node.InternalId);
        else
            _collapsedObjects.Add(row.Node.InternalId);
        RefreshObjects();
    }

    /// <summary>A row was selected or deselected in the panel: the drawing's selection follows.</summary>
    private void OnObjectRowSelected(ObjectViewModel row)
    {
        if (_syncingObjects || ActiveSvg is not { } drawing)
            return;
        drawing.Selection.Set(Objects.Where(o => o.IsSelected).Select(o => o.Node));
    }

    /// <summary>Selects objects from outside the panel (a tool, the canvas); the panel's rows follow.</summary>
    public void SelectObjects(IEnumerable<SvgElement> nodes) => ActiveSvg?.Selection.Set(nodes);

    private void SyncObjectSelection()
    {
        var selected = ActiveSvg?.Selection.Nodes.ToHashSet() ?? [];
        _syncingObjects = true;
        foreach (var row in Objects)
            row.SetSelectedQuietly(selected.Contains(row.Node));
        _syncingObjects = false;
        OnPropertyChanged(nameof(HasObjectSelection));
        OnPropertyChanged(nameof(ObjectSelectionText));
        foreach (var command in new IRelayCommand[]
                 {
                     DeselectObjectsCommand, DeleteObjectsCommand, DuplicateObjectsCommand, GroupObjectsCommand, UngroupObjectsCommand,
                     RaiseObjectsCommand, LowerObjectsCommand, RaiseToTopCommand, LowerToBottomCommand,
                 })
            command.NotifyCanExecuteChanged();
    }

    // ---- Commands on the selected objects ----

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private void DeselectObjects() => ActiveSvg?.Selection.Clear();

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private void DeleteObjects() => ActiveSvg?.Actions.DeleteSelection();

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private void DuplicateObjects() => ActiveSvg?.Actions.Duplicate();

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private void GroupObjects() => ActiveSvg?.Actions.Group();

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private async Task UngroupObjects()
    {
        if (ActiveSvg is not { } drawing)
            return;
        try
        {
            drawing.Actions.Ungroup();
        }
        catch (NotSupportedException e)
        {
            await (Dialogs?.ShowErrorAsync("Could not ungroup", e.Message) ?? Task.CompletedTask);
        }
    }

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private void RaiseObjects() => ActiveSvg?.Actions.Raise();

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private void LowerObjects() => ActiveSvg?.Actions.Lower();

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private void RaiseToTop() => ActiveSvg?.Actions.RaiseToTop();

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private void LowerToBottom() => ActiveSvg?.Actions.LowerToBottom();

    /// <summary>The label of an object (Objects panel: rename). An empty label goes back to the id.</summary>
    public void RenameObject(ObjectViewModel row, string label)
    {
        if (ActiveSvg is { } drawing)
            drawing.Actions.SetLabel(row.Node, label.Trim());
    }

    /// <summary>The XML id of an object; returns the error text when it is not valid or already used, null on success.</summary>
    public string? SetObjectId(ObjectViewModel row, string id)
    {
        if (ActiveSvg is not { } drawing)
            return null;
        try
        {
            drawing.Actions.SetId(row.Node, id);
            return null;
        }
        catch (ArgumentException e)
        {
            return e.Message;
        }
    }

    public enum DropPosition
    {
        /// <summary>Visually above the target: just in front of it in z-order.</summary>
        Above,
        /// <summary>Visually below the target.</summary>
        Below,
        /// <summary>Into the target group, on top.</summary>
        Into,
    }

    /// <summary>Drag and drop in the Objects panel: reorders, or moves into or out of a group, as one undoable step.</summary>
    public void MoveObject(ObjectViewModel dragged, ObjectViewModel target, DropPosition position)
    {
        if (ActiveSvg is not { } drawing || dragged == target)
            return;
        var moving = drawing.Selection.Contains(dragged.Node) ? drawing.Selection.Nodes.ToList() : [dragged.Node];
        if (moving.Contains(target.Node) || moving.Any(m => target.Node.Ancestors().Contains(m)))
            return;
        SvgContainer parent;
        int index;
        if (position == DropPosition.Into && target.Node is SvgContainer group)
        {
            (parent, index) = (group, group.Children.Count);
        }
        else
        {
            parent = target.Node.Parent!;
            var at = parent.IndexOf(target.Node);
            index = position == DropPosition.Above ? at + 1 : at;
        }
        drawing.Actions.MoveToParent(moving, parent, index);
    }

    /// <summary>
    /// On a drawing the palette colors are the stroke (primary) and the fill (secondary) of what is drawn next, like the shape tools use them,
    /// and picking one in the palette applies it to the selected objects. Raster images are not affected.
    /// </summary>
    public void ApplyPaletteColor(bool stroke)
    {
        if (ActiveSvg is not { Selection.IsEmpty: false } drawing)
            return;
        var color = stroke ? ToolSettings.PrimaryColor : ToolSettings.SecondaryColor;
        var properties = new Dictionary<string, string?>
        {
            [stroke ? "stroke" : "fill"] = SvgPaint.FromColor(VColor.FromRgb(color.R, color.G, color.B)).ToText(),
            [stroke ? "stroke-opacity" : "fill-opacity"] = color.A == 255 ? null : NumberFormat.Format(color.A / 255.0, 3),
        };
        drawing.Actions.SetStyle(null, properties, stroke ? "Set Stroke" : "Set Fill");
    }

    // ---- Clipboard ----

    /// <summary>Copy or cut: the objects as SVG text, plus a picture of them for applications that only take bitmaps.</summary>
    private async Task CopyObjectsAsync(SvgDocument drawing, bool cut)
    {
        if (Clipboard is null || drawing.Selection.IsEmpty)
            return;
        var text = drawing.Actions.Copy();
        if (text.Length == 0)
            return;
        ClipboardImage? picture = null;
        try
        {
            var rendered = SvgParser.Parse(text).Root;
            var (bgra, width, height) = VectorRasterizer.RenderAll(rendered, 1, drawing.RenderOptions);
            picture = new ClipboardImage(bgra, width, height);
        }
        catch (SvgParseException)
        {
        }
        await Clipboard.SetSvgAsync(text, picture);
        if (cut)
            drawing.Actions.DeleteSelection();
    }

    private async Task PasteIntoDrawingAsync(SvgDocument drawing)
    {
        if (Clipboard is null)
            return;
        if (await Clipboard.GetSvgAsync() is { } svg)
        {
            try
            {
                drawing.Actions.PasteSvg(svg);
                return;
            }
            catch (ArgumentException)
            {
                // Not a drawing after all: try a picture.
            }
        }
        if (await Clipboard.GetImageAsync() is { } picture)
        {
            drawing.Actions.PasteImage(picture);
            return;
        }
        if (Dialogs is not null)
            await Dialogs.ShowErrorAsync("Nothing to paste", "The clipboard doesn't contain an SVG drawing or an image.");
    }
}
