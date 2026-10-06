using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using ImageMagick;

namespace CinnabarSharp.Core.Tests;

public class ImageDocumentTests : BaseTests
{
    [Fact]
    public void Test_createdocument_NotFail()
    {
        var sp = CinnabarSharpService();

        var doc = sp.GetService<ImageDocument>();
        Assert.Null(doc.File);
        Assert.NotNull(doc.Layers);
        Assert.Null(doc.Selection);
        Assert.NotNull(doc.Workspace);
    }
    
    [Fact]
    public void Test_importFile()
    {
        var sp = CinnabarSharpService();
        var doc = sp.GetService<ImageDocument>();
        Assert.Null(doc.File);
        Assert.NotNull(doc.Layers);
        Assert.Null(doc.Selection);
        Assert.NotNull(doc.Workspace);

        var importer = sp.GetServices<IImageImporter>().SingleOrDefault(x => x.Name == nameof(JpegFormat));


        importer?.Import(ImageSample1());
        var workspace = sp.GetService<IWorkspaceService>();
        Assert.NotNull(workspace);
        Assert.Single(workspace.OpenDocuments);
    }

    [Fact]
    public void Test_import_crop_exportFile()
    {
        var sp = CinnabarSharpService();
        var importer = sp.GetServices<IImageImporter>().SingleOrDefault(x => x.Name == nameof(JpegFormat));


        importer?.Import(ImageSample1());
        var workspace = sp.GetRequiredService<IWorkspaceService>();

        var sampleDir = SampleFilesDirectory();
        var original = workspace.ActiveImageDocument!.Layers[0].Surface;
        var oh = original.Height;
        var ow = original.Width;
        using var flatten = workspace.ActiveImageDocument!.GetFlattenedImage();
        
        flatten.Crop(100,100);
        // flatten.Format = MagickFormat.Png;
        // flatten.Density = new Density(300);
        Assert.Equal(100u, flatten.Height);
        Assert.Equal(100u, flatten.Width);
        original = workspace.ActiveImageDocument!.Layers[0].Surface;
        
        Assert.Equal(oh, original.Height);
        Assert.Equal(ow, original.Width);
    }

}
