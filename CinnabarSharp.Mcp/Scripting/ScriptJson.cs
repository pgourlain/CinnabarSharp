using System.Text.Json.Serialization;

namespace CinnabarSharp.Mcp.Scripting;

/// <summary>Source-generated JSON for the few primitive values a script argument becomes (Native AOT).</summary>
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string))]
internal sealed partial class ScriptJson : JsonSerializerContext;
