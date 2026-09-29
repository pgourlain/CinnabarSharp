namespace CinnabarSharp.Benchmarks;

/// <summary>
/// Representative image sizes for benchmarks (see performance-tasks.md P0). Not every benchmark uses the
/// literal 24 MP target from the doc: sweeps over many operations (every effect, every history item type) use
/// <see cref="Small"/> instead, so a full run's wall-clock time stays reasonable — the point of these numbers is
/// tracking relative change across phases, not a single absolute number. Each benchmark class says which it uses.
/// </summary>
public static class BenchmarkSizes
{
    /// <summary>~2 MP: cheap enough to sweep over many operations (every effect, every history item type).</summary>
    public static readonly (int Width, int Height) Small = (1600, 1200);

    /// <summary>12 MP.</summary>
    public static readonly (int Width, int Height) Medium = (3000, 4000);

    /// <summary>24 MP: the doc's own target size (4000 x 6000).</summary>
    public static readonly (int Width, int Height) Large = (4000, 6000);
}
