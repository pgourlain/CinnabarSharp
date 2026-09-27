using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using System.Reactive.Linq;

namespace CinnabarSharp.Core.Tests;

public class NewDocumentTests : BaseTests
{
    [Fact]
    public void NewDocument_creates_active_document_with_background_layer()
    {
        var workspace = CinnabarSharpService().GetRequiredService<IWorkspaceService>();

        var doc = workspace.NewDocument(new ImageSize(320, 200), ColorBgra.White);

        Assert.Same(doc, workspace.ActiveDocument);
        Assert.Equal(new ImageSize(320, 200), doc.ImageSize);
        Assert.Equal(new ImageSize(320, 200), doc.Workspace.ViewSize);
        Assert.Null(doc.File);
        Assert.Equal("Unsaved Image 1", doc.DisplayName);
        Assert.Single(doc.Layers.UserLayers);
        Assert.Equal("Background", doc.Layers[0].Name);
    }

    [Theory]
    [InlineData(255)]
    [InlineData(0)]
    public void NewDocument_fills_background(byte alpha)
    {
        var workspace = CinnabarSharpService().GetRequiredService<IWorkspaceService>();
        var background = ColorBgra.FromBgra(10, 20, 30, alpha);

        var doc = workspace.NewDocument(new ImageSize(4, 3), background);

        var surface = doc.Layers[0].Surface;
        Assert.Equal(4u, surface.Width);
        Assert.Equal(3u, surface.Height);
        var pixel = surface.GetPixels().GetPixel(2, 1).ToColor()!;
        Assert.Equal(30, pixel.R);
        Assert.Equal(20, pixel.G);
        Assert.Equal(10, pixel.B);
        Assert.Equal(alpha, pixel.A);
    }

    [Fact]
    public void Flattened_transparent_document_stays_transparent()
    {
        var workspace = CinnabarSharpService().GetRequiredService<IWorkspaceService>();
        var doc = workspace.NewDocument(new ImageSize(8, 8), ColorBgra.Transparent);

        using var flat = doc.GetFlattenedImage();

        Assert.Equal(8u, flat.Width);
        Assert.Equal(8u, flat.Height);
        Assert.Equal(0, flat.GetPixels().GetPixel(3, 3).ToColor()!.A);
    }

    [Fact]
    public void Flattening_with_all_layers_hidden_gives_transparent_image()
    {
        var workspace = CinnabarSharpService().GetRequiredService<IWorkspaceService>();
        var doc = workspace.NewDocument(new ImageSize(8, 6), ColorBgra.White);
        doc.Layers[0].Hidden = true;

        using var flat = doc.GetFlattenedImage();

        Assert.Equal(8u, flat.Width);
        Assert.Equal(6u, flat.Height);
        Assert.Equal(0, flat.GetPixels().GetPixel(3, 3).ToColor()!.A);
    }

    [Fact]
    public void NewDocument_numbers_unsaved_images()
    {
        var workspace = CinnabarSharpService().GetRequiredService<IWorkspaceService>();

        workspace.NewDocument(new ImageSize(10, 10), ColorBgra.White);
        var second = workspace.NewDocument(new ImageSize(10, 10), ColorBgra.White);

        Assert.Equal("Unsaved Image 2", second.DisplayName);
        Assert.Equal(2, workspace.OpenDocuments.Count);
        Assert.Same(second, workspace.ActiveDocument);
    }

    [Fact]
    public void SetActiveDocument_switches_active_document_and_fires_event()
    {
        var sp = CinnabarSharpService();
        var workspace = sp.GetRequiredService<IWorkspaceService>();
        var first = workspace.NewDocument(new ImageSize(10, 10), ColorBgra.White);
        workspace.NewDocument(new ImageSize(10, 10), ColorBgra.White);
        var received = new List<EventItem<DocumentEventEnum>>();
        using var sub = sp.GetRequiredService<IDocumentEventsService>().DocumentEvents
            .Subscribe(received.Add);

        workspace.SetActiveDocument(first);
        workspace.SetActiveDocument(first);

        Assert.Same(first, workspace.ActiveDocument);
        var evt = Assert.Single(received);
        Assert.Equal(DocumentEventEnum.ActiveDocumentChanged, evt.State);
        Assert.Same(first, evt.Document);
    }
}
