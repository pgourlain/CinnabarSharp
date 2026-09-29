using BenchmarkDotNet.Attributes;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CinnabarSharp.Benchmarks;

/// <summary>Open/save a 24 MP single-layer PNG and JPEG (performance-tasks.md's own target: "no more than twice
/// the time Magick.NET alone needs to decode/encode the file"; these numbers are CinnabarSharp's side only, not
/// compared against raw Magick.NET here).</summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
public class OpenSaveBenchmarks
{
    private IWorkspaceService _workspace = null!;
    private IFormatManager _formats = null!;
    private DirectoryInfo _dir = null!;

    // Open benchmarks read these; the document that wrote them is closed right after, so every Open() call
    // below is a real decode, not a hit on FormatManager's "already open" cache.
    private FileInfo _pngFile = null!;
    private FileInfo _jpgFile = null!;

    // Save benchmarks write to their own document/paths, kept separate from the ones above so saving never
    // makes an Open() call return a cached document instead of decoding.
    private ImageDocument _saveDoc = null!;
    private FileInfo _savePngFile = null!;
    private FileInfo _saveJpgFile = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dir = Directory.CreateTempSubdirectory("cinnabarsharp-benchmarks-");
        var sp = BenchmarkHelpers.BuildServices();
        _workspace = sp.GetRequiredService<IWorkspaceService>();
        _formats = sp.GetRequiredService<IFormatManager>();
        var (width, height) = BenchmarkSizes.Large;

        var seed = BenchmarkHelpers.CreateDocument(sp, width, height);
        _pngFile = new FileInfo(Path.Combine(_dir.FullName, "open.png"));
        _jpgFile = new FileInfo(Path.Combine(_dir.FullName, "open.jpg"));
        _formats.Save(seed, _pngFile);
        _formats.Save(seed, _jpgFile);
        _workspace.CloseDocument(seed);

        _saveDoc = BenchmarkHelpers.CreateDocument(sp, width, height);
        _savePngFile = new FileInfo(Path.Combine(_dir.FullName, "save.png"));
        _saveJpgFile = new FileInfo(Path.Combine(_dir.FullName, "save.jpg"));
    }

    [GlobalCleanup]
    public void Cleanup() => _dir.Delete(recursive: true);

    [Benchmark]
    public void OpenPng()
    {
        var doc = _formats.Open(_pngFile);
        _workspace.CloseDocument(doc);
    }

    [Benchmark]
    public void OpenJpeg()
    {
        var doc = _formats.Open(_jpgFile);
        _workspace.CloseDocument(doc);
    }

    [Benchmark]
    public void SavePng() => _formats.Save(_saveDoc, _savePngFile);

    [Benchmark]
    public void SaveJpeg() => _formats.Save(_saveDoc, _saveJpgFile);
}
