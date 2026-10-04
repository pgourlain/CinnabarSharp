using System.Reflection;
using BenchmarkDotNet.Running;

// Run everything: `dotnet run -c Release`. Run one class or filter by name: `dotnet run -c Release -- --filter *Flatten*`.
// Not part of CI (performance-tasks.md P0): CI only builds this project to catch compile errors.
// Memory with many photos open (performance-tasks.md P7): `dotnet run -c Release -- --memory [photos] [megapixels]`.
if (args.Contains("--memory"))
{
    CinnabarSharp.Benchmarks.MemoryReport.Run(args);
    return;
}

BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
