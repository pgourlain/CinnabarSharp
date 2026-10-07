using System.Text.Json;
using System.Text.Json.Serialization;

namespace CinnabarSharp.Mcp;

/// <summary>
/// Source-generated JSON for the resources and for the tools' parameters and results (no reflection, so the server
/// also works in a Native AOT build: performance-tasks.md P6). Add a type here when a tool takes or returns a new one.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(DocumentInfo))]
[JsonSerializable(typeof(IEnumerable<DocumentInfo>))]
[JsonSerializable(typeof(IReadOnlyList<DocumentInfo>))]
[JsonSerializable(typeof(ImageInfo))]
[JsonSerializable(typeof(IReadOnlyList<HistoryStep>))]
[JsonSerializable(typeof(IReadOnlyList<EffectInfo>))]
[JsonSerializable(typeof(SuggestedValues))]
[JsonSerializable(typeof(FolderExportResult))]
[JsonSerializable(typeof(SavedFile))]
[JsonSerializable(typeof(SvgNodeInfo))]
[JsonSerializable(typeof(IReadOnlyList<SvgNodeInfo>))]
[JsonSerializable(typeof(SvgNodeDetails))]
[JsonSerializable(typeof(SvgEditResult))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(double[]))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(double?))]
[JsonSerializable(typeof(bool?))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public sealed partial class McpJson : JsonSerializerContext
{
    /// <summary>The SDK's options plus the metadata of our types, for the tools.</summary>
    // Lazy: the generated Default isn't set yet while this class's static fields are initialized.
    public static JsonSerializerOptions ToolOptions => ToolOptionsLazy.Value;

    private static readonly Lazy<JsonSerializerOptions> ToolOptionsLazy = new(CreateToolOptions);

    private static JsonSerializerOptions CreateToolOptions()
    {
        var options = new JsonSerializerOptions(ModelContextProtocol.McpJsonUtilities.DefaultOptions);
        options.TypeInfoResolverChain.Insert(0, Default);
        options.MakeReadOnly();
        return options;
    }
}
