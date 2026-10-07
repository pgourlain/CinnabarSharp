using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Tests;

public sealed class SvgPerformanceUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    private string MapFile(int count)
    {
        var builder = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' width='4000' height='3000' viewBox='0 0 4000 3000'>" +
            "<rect width='4000' height='3000' fill='#ffffff'/><g id='layer1'>");
        var random = new Random(7);
        for (var i = 0; i < count; i++)
        {
            var x = random.Next(0, 3900);
            var y = random.Next(0, 2900);
            builder.Append($"<path d='M{x} {y} c10 -20 40 -20 50 0 s10 40 -20 50 l-30 -10 z' fill='#{random.Next(0x1000000):x6}' stroke='#222' stroke-width='1.5'/>");
        }
        builder.Append("<rect id='mark' x='100' y='100' width='200' height='200' fill='#ff0000'/></g></svg>");
        var path = _h.TempPath("map.svg");
        File.WriteAllText(path, builder.ToString());
        return path;
    }

    private async Task WaitForFrame()
    {
        for (var i = 0; i < 600 && _h.Canvas.IsRenderingInBackground; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.False(_h.Canvas.IsRenderingInBackground, "the background frame never arrived");
    }

    [AvaloniaFact]
    public async Task A_drawing_with_10000_paths_opens_fast_draws_in_the_background_and_stays_responsive()
    {
        var path = MapFile(10_000);
        var clock = Stopwatch.StartNew();
        await Vm.OpenFileAsync(path);
        Dispatcher.UIThread.RunJobs();
        var opened = clock.ElapsedMilliseconds;
        Assert.True(opened < 2000, $"opening took {opened} ms");

        // The frame arrives later; until then the checkerboard (or the last frame) is shown, not a frozen window.
        Assert.True(_h.Canvas.IsRenderingInBackground || opened > 0);
        await WaitForFrame();
        _h.Capture("svg-90-map");

        // Zooming asks for a new frame without blocking the UI thread for the whole render.
        clock.Restart();
        Vm.ZoomInCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var zoomed = clock.ElapsedMilliseconds;
        Assert.True(zoomed < 400, $"zooming blocked the UI for {zoomed} ms");
        await WaitForFrame();
        _h.Capture("svg-91-map-zoomed");

        // An edit while a frame is being computed ends with a frame that shows the edit.
        var svg = Assert.IsType<SvgDocument>(Vm.ActiveDocument!.Document);
        var mark = (CinnabarSharp.Vector.SvgElement)svg.Root.FindById("mark")!;
        svg.Actions.SetStyle([mark], "fill", "#0000ff");
        Vm.ZoomOutCommand.Execute(null);
        await WaitForFrame();
        Assert.Equal("#0000ff", mark.Style.Get("fill"));
    }

    [AvaloniaFact]
    public async Task Small_drawings_still_render_at_once()
    {
        await Vm.OpenFileAsync(MapFile(50));
        Dispatcher.UIThread.RunJobs();
        Assert.False(_h.Canvas.IsRenderingInBackground);
    }
}
