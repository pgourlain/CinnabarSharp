using System;
using System.Reflection;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Desktop.Services;

/// <summary>The version of the running app (from the assembly, e.g. "0.9.0+abc123" → 0.9.0).</summary>
public static class AppVersion
{
    public static Version Current { get; } =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is { } text
        && UpdateChecker.TryParseVersion(text, out var version)
            ? version
            : new Version(0, 0, 0);
}
