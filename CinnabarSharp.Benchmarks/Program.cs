using System.Reflection;
using BenchmarkDotNet.Running;

// Run everything: `dotnet run -c Release`. Run one class or filter by name: `dotnet run -c Release -- --filter *Flatten*`.
// Not part of CI (performance-tasks.md P0): CI only builds this project to catch compile errors.
BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
