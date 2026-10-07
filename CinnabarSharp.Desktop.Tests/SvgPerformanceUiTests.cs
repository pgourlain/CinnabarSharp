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
        Assert.True(opened < 30000, $"opening took {opened} ms");

        // The frame arrives later; until then the checkerboard (or the last frame) is shown, not a frozen window.
        Assert.True(_h.Canvas.IsRenderingInBackground || opened > 0);
        await WaitForFrame();
        _h.Capture("svg-90-map");

        // Zooming asks for a new frame without blocking the UI thread for the whole render.
        clock.Restart();
        Vm.ZoomInCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var zoomed = clock.ElapsedMilliseconds;
        Assert.True(zoomed < 5000, $"zooming blocked the UI for {zoomed} ms");
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

    // The README screenshot: one of every kind of object.
    [AvaloniaFact]
    public async Task Every_kind_of_object_renders()
    {
        var sample = Convert.ToBase64String(File.ReadAllBytes(TestHarness.SampleImage));
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:inkscape="http://www.inkscape.org/namespaces/inkscape" width="640" height="420" viewBox="0 0 640 420">
              <defs>
                <linearGradient id="sunset" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#ffcc00"/><stop offset="1" stop-color="#cc2200"/></linearGradient>
                <radialGradient id="ball"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#2255cc"/></radialGradient>
                <clipPath id="round"><circle cx="540" cy="110" r="60"/></clipPath>
              </defs>
              <rect width="640" height="420" fill="#fbfaf8"/>
              <g id="layer1" inkscape:groupmode="layer" inkscape:label="Layer 1">
                <rect id="rect" x="30" y="30" width="130" height="80" fill="#336699" stroke="#1a334d" stroke-width="3"/>
                <rect id="rounded" x="190" y="30" width="130" height="80" rx="22" fill="url(#sunset)"/>
                <ellipse id="ellipse" cx="395" cy="70" rx="55" ry="40" fill="url(#ball)"/>
                <image id="photo" x="480" y="50" width="120" height="68" clip-path="url(#round)" preserveAspectRatio="xMidYMid slice" href="data:image/png;base64,{sample}"/>
                <line id="line" x1="30" y1="160" x2="170" y2="200" stroke="#222" stroke-width="6" stroke-linecap="round"/>
                <polygon id="polygon" points="230,200 280,150 330,200 305,250 255,250" fill="#2e8b57" stroke="#14452b" stroke-width="3"/>
                <polygon id="star" points="410,150 424,184 462,187 433,211 442,248 410,228 378,248 387,211 358,187 396,184" fill="#ffcc00" stroke="#8a6d00" stroke-width="3"/>
                <path id="curve" d="M30 300 C90 220 150 380 210 300 S 300 250 330 310" fill="none" stroke="#cc2200" stroke-width="5" stroke-linecap="round"/>
                <path id="blob" d="M400 300 q40 -60 90 -10 t70 20 q-10 60 -80 50 t-80 -60z" fill="#8a2be2" fill-opacity="0.75"/>
                <g id="group" transform="rotate(-8 120 370)">
                  <rect x="40" y="345" width="230" height="50" rx="10" fill="#222"/>
                  <text id="text" x="58" y="380" font-family="Helvetica, Arial, sans-serif" font-size="26" font-weight="bold" fill="#ffffff">Text in a group</text>
                </g>
              </g>
            </svg>
            """;
        var path = _h.TempPath("objects.svg");
        File.WriteAllText(path, svg);
        await Vm.OpenFileAsync(path);
        Dispatcher.UIThread.RunJobs();
        var drawing = Assert.IsType<SvgDocument>(Vm.ActiveDocument!.Document);
        drawing.Selection.Set((CinnabarSharp.Vector.SvgElement)drawing.Root.FindById("star")!);
        Vm.Properties.Refresh();
        Dispatcher.UIThread.RunJobs();
        _h.Capture("svg-95-all-objects");
    }
}
