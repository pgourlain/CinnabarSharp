using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Tests;

public sealed class ShapeLibraryUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    [AvaloniaFact]
    public void Contact_sheet_of_the_library()
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(8) };
        foreach (var category in ShapeLibrary.Categories)
        {
            panel.Children.Add(new TextBlock { Text = category, FontWeight = FontWeight.Bold });
            var wrap = new WrapPanel { Width = 1000 };
            foreach (var shape in ShapeLibrary.In(category))
                wrap.Children.Add(new Border
                {
                    Width = 62, Height = 62, Margin = new Thickness(2), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1),
                    Child = new Viewbox { Width = 44, Height = 44, Child = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(shape.PreviewData), Fill = Brushes.Black, Width = 24, Height = 24 } },
                });
            panel.Children.Add(wrap);
        }
        var window = new Window { Width = 1040, Height = 1200, Content = new ScrollViewer { Content = panel }, Background = Brushes.White };
        window.Show();
        TestHarness.CaptureWindow(window, "svg-98-shape-library");
        window.Close();
    }

    [AvaloniaFact]
    public void Picking_a_shape_selects_the_tool_and_draws_it()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(200, 150), ColorBgra.Transparent, new SvgDrawingOptions(200, 150, SvgUnit.Px)));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Vm.VectorTools, t => t.Name == "Shape");
        Assert.Equal(ShapeLibrary.Categories, Vm.LibraryCategories);
        Vm.SelectedLibraryCategory = "Arrows";
        Assert.All(Vm.LibraryShapes, s => Assert.StartsWith("arrows/", s.Id));

        Vm.PickLibraryShape(Vm.LibraryShapes.First(s => s.Name == "Double arrow"));
        Assert.Equal("Shape", Vm.SelectedTool.Name);
        Assert.True(Vm.ShowLibraryShapeOptions);
        Assert.Equal("Double arrow", Vm.CurrentLibraryShape.Name);

        Vm.ShapeStyle = CinnabarSharp.Core.Tools.ShapeStyle.Fill;
        Vm.ToolPointerDown(new CinnabarSharp.Core.Tools.ToolPointer(new PointD(20, 30), CinnabarSharp.Core.Tools.ToolButton.Left, CinnabarSharp.Core.Tools.ToolModifiers.None));
        Vm.ToolPointerMove(new CinnabarSharp.Core.Tools.ToolPointer(new PointD(100, 70), CinnabarSharp.Core.Tools.ToolButton.Left, CinnabarSharp.Core.Tools.ToolModifiers.None));
        Vm.ToolPointerUp(new CinnabarSharp.Core.Tools.ToolPointer(new PointD(160, 90), CinnabarSharp.Core.Tools.ToolButton.Left, CinnabarSharp.Core.Tools.ToolModifiers.None));
        var svg = Assert.IsType<SvgDocument>(Vm.ActiveDocument!.Document);
        Assert.Single(svg.Root.Descendants().OfType<CinnabarSharp.Vector.SvgPath>());
        _h.Capture("svg-99-library-shape");
        Assert.Equal("double-arrow", Vm.CaptureSettings(new CinnabarSharp.Desktop.Services.AppSettings()).LibraryShape?.Split('/')[1]);
    }
}
