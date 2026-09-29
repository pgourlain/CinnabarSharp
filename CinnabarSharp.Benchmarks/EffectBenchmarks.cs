using BenchmarkDotNet.Attributes;
using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Benchmarks;

public readonly record struct EffectCase(string Name, Effect Instance)
{
    public override string ToString() => Name;
}

/// <summary>Every effect, adjustment and photo tool at its defaults, on a 2 MP image — a full 24 MP sweep over
/// ~30 operations would take too long for a routine run; see <see cref="BenchmarkSizes"/>.</summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
public class EffectBenchmarks
{
    private ImageDocument _doc = null!;

    public IEnumerable<EffectCase> Effects() =>
        EffectCatalog.All.Concat(EffectCatalog.Adjustments).Select(e => new EffectCase(e.GetType().Name, e));

    [ParamsSource(nameof(Effects))]
    public EffectCase Case { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var (width, height) = BenchmarkSizes.Small;
        var sp = BenchmarkHelpers.BuildServices();
        _doc = BenchmarkHelpers.CreateDocument(sp, width, height);
    }

    /// <summary>Just the computation (no history push): reads only <see cref="EffectContext.Source"/>, matching
    /// what a background-thread preview does.</summary>
    [Benchmark]
    public byte[] Apply()
    {
        var session = new EffectSession(_doc, Case.Instance);
        return session.Compute(Case.Instance.Defaults);
    }
}
