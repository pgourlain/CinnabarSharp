using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CinnabarSharp.Desktop.Services;

/// <summary>
/// Source-generated JSON for the settings and recent files: no reflection, so it also works in a Native AOT build
/// (performance-tasks.md P6).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class DesktopJson : JsonSerializerContext;
