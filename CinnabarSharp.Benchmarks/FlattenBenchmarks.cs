using BenchmarkDotNet.Attributes;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Benchmarks;

/// <summary>24 MP, 10 layers (performance-tasks.md's own memory target scenario).</summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
public class FlattenBenchmarks
{
    private ImageDocument _doc = null!;
    private RectangleI _region;

    [GlobalSetup]
    public void Setup()
    {
        var (width, height) = BenchmarkSizes.Large;
        var sp = BenchmarkHelpers.BuildServices();
        _doc = BenchmarkHelpers.CreateDocument(sp, width, height, layerCount: 10);
        _region = new RectangleI(width / 4, height / 4, width / 2, height / 2); // a quarter of the image
    }

    [Benchmark]
    public byte[] FlattenFull() => _doc.Layers.GetFlattenedBgra();

    [Benchmark]
    public byte[] FlattenRegion() => _doc.Layers.GetFlattenedBgra(_region);
}
