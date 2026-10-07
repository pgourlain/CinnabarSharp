using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using CinnabarSharp.Core.Vector;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CinnabarSharp.Desktop.ViewModels;

/// <summary>A shape of the picker: its name and its outline as an icon.</summary>
public sealed class LibraryShapeItem(LibraryShape shape)
{
    public string Id { get; } = shape.Id;

    public string Name { get; } = shape.Name;

    public Geometry Icon { get; } = Geometry.Parse(shape.PreviewData);
}

// The Shape tool's picker: categories on the left, the shapes of the chosen one on the right.
public partial class MainViewModel
{
    private readonly Dictionary<string, LibraryShapeItem> _libraryItems = [];

    public IReadOnlyList<string> LibraryCategories => ShapeLibrary.Categories;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LibraryShapes))]
    public partial string SelectedLibraryCategory { get; set; } = ShapeLibrary.Categories[0];

    private LibraryShapeItem Item(LibraryShape shape)
    {
        if (!_libraryItems.TryGetValue(shape.Id, out var item))
            _libraryItems[shape.Id] = item = new LibraryShapeItem(shape);
        return item;
    }

    /// <summary>The shapes of the chosen category (their previews are built when the category is first shown).</summary>
    public IReadOnlyList<LibraryShapeItem> LibraryShapes => ShapeLibrary.In(SelectedLibraryCategory ?? ShapeLibrary.Categories[0]).Select(Item).ToList();

    public LibraryShapeItem CurrentLibraryShape => Item(ShapeLibrary.Find(ToolSettings.LibraryShape) ?? ShapeLibrary.Default);

    public bool ShowLibraryShapeOptions => SelectedTool?.IsVectorLibraryShape == true;

    /// <summary>Makes <paramref name="item"/> the shape the Shape tool draws.</summary>
    public void PickLibraryShape(LibraryShapeItem item)
    {
        ToolSettings.LibraryShape = item.Id;
        OnPropertyChanged(nameof(CurrentLibraryShape));
        if (SelectedTool?.IsVectorLibraryShape != true && VectorTools.FirstOrDefault(t => t.IsVectorLibraryShape) is { } tool && ActiveSvg is not null)
            SelectedTool = tool;
    }
}
