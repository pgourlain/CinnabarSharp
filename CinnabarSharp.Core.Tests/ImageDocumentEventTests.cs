using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using System.Reactive.Linq;

namespace CinnabarSharp.Core.Tests;

public class ImageDocumentEventTests : BaseTests
{

    [Fact]
    public void Test_importFile_fireevents()
    {
        List<DocumentEventEnum> received = new List<DocumentEventEnum>();
        var sp = CinnabarSharpService();
        var svc = sp.GetRequiredService<IDocumentEventsService>();
        using var subscriber = svc.DocumentEvents.Subscribe(eventItem =>
        {
            received.Add(eventItem.State);
        });
        var importer = sp.GetServices<IImageImporter>().SingleOrDefault(x => x.Name == nameof(JpegFormat));


        importer?.Import(ImageSample1());
        var expectedStates = new []
        {
            DocumentEventEnum.DocumentRenamed,
            DocumentEventEnum.HistoryChanged,
            DocumentEventEnum.DocumentCreated,
            DocumentEventEnum.ActiveDocumentChanged,
            DocumentEventEnum.ViewSizeChanged,
            DocumentEventEnum.LayerAdded,
            DocumentEventEnum.CanvasInvalidated
        };
        Assert.Equal(expectedStates, received);
    }
}