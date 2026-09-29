using BenchmarkDotNet.Attributes;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;

namespace CinnabarSharp.Benchmarks;

/// <summary>A 500-point freehand stroke on a 24 MP layer (performance-tasks.md's own scenario: "Brush and
/// selection tools: 60 fps while dragging").</summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
public class BrushStrokeBenchmarks
{
    private const int PointCount = 500;

    private ImageDocument _doc = null!;
    private ToolSettings _settings = null!;
    private ToolPointer[] _points = null!;

    [GlobalSetup]
    public void Setup()
    {
        var (width, height) = BenchmarkSizes.Large;
        var sp = BenchmarkHelpers.BuildServices();
        _doc = BenchmarkHelpers.CreateDocument(sp, width, height);
        _settings = new ToolSettings { BrushWidth = 20 };

        // A wandering path across the middle of the image, like a real drag.
        var rnd = new Random(1);
        var x = width / 2.0;
        var y = height / 2.0;
        _points = new ToolPointer[PointCount];
        for (var i = 0; i < PointCount; i++)
        {
            x = Math.Clamp(x + rnd.Next(-4, 5), 0, width - 1);
            y = Math.Clamp(y + rnd.Next(-4, 5), 0, height - 1);
            _points[i] = new ToolPointer(new PointD(x, y), ToolButton.Left, ToolModifiers.None);
        }
    }

    [Benchmark]
    public void Stroke()
    {
        var tool = new PaintbrushTool(_settings);
        tool.OnPointerDown(_doc, _points[0]);
        for (var i = 1; i < _points.Length; i++)
            tool.OnPointerMove(_doc, _points[i]);
        tool.OnPointerUp(_doc, _points[^1]);
    }

    [IterationCleanup]
    public void Cleanup() => _doc.Workspace.History.Undo(); // undoes the stroke's one PixelRegionHistoryItem
}
