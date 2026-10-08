using System;
using System.Linq;
using System.Reflection;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Desktop.Services;

/// <summary>
/// Where usage statistics go, decided at build time: <c>dotnet publish -p:TelemetryEndpoint=https://…</c> (packaging/package.sh
/// passes <c>CINNABARSHARP_TELEMETRY_URL</c>, the release workflow sets it from the repository variable
/// <c>TELEMETRY_ENDPOINT</c>). Without one the app never asks and never sends. At run time
/// <c>CINNABARSHARP_TELEMETRY_URL</c> replaces it (a local relay while testing) and <c>CINNABARSHARP_TELEMETRY=0</c> turns
/// it off.
/// </summary>
public static class TelemetryEndpoint
{
    public static Uri? Resolve() => Resolve(
        Environment.GetEnvironmentVariable("CINNABARSHARP_TELEMETRY"),
        Environment.GetEnvironmentVariable("CINNABARSHARP_TELEMETRY_URL"),
        Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "TelemetryEndpoint")?.Value);

    public static Uri? Resolve(string? switchValue, string? overrideUrl, string? builtIn)
    {
        if (switchValue is "0" or "false" or "off")
            return null;
        var text = string.IsNullOrWhiteSpace(overrideUrl) ? builtIn : overrideUrl;
        return Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) && TelemetryClient.IsAllowedEndpoint(uri) ? uri : null;
    }
}
