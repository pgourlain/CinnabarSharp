using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace CinnabarSharp.Mcp.Tests;

/// <summary>
/// A Native AOT build has no reflection-based JSON: every tool parameter and result must have source-generated
/// metadata (McpJson), or the server fails to start (performance-tasks.md P6).
/// </summary>
public class AotJsonTests
{
    private static readonly Type[] Injected = [typeof(CancellationToken), typeof(McpServer), typeof(IProgress<ModelContextProtocol.ProgressNotificationValue>)];

    public static TheoryData<string> ToolTypes()
    {
        var types = new HashSet<Type>();
        foreach (var method in typeof(ImageTools).GetMethods().Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null))
        {
            foreach (var p in method.GetParameters().Where(p => !Injected.Contains(p.ParameterType)))
                types.Add(p.ParameterType);
            var result = method.ReturnType;
            if (result.IsGenericType && result.GetGenericTypeDefinition() == typeof(Task<>))
                result = result.GetGenericArguments()[0];
            types.Add(result);
        }
        var data = new TheoryData<string>();
        foreach (var t in types)
            data.Add(t.AssemblyQualifiedName!);
        return data;
    }

    [Theory]
    [MemberData(nameof(ToolTypes))]
    public void Tool_type_has_source_generated_json_metadata(string typeName)
    {
        var type = Type.GetType(typeName, throwOnError: true)!;
        var options = McpJson.ToolOptions;
        // Only the source-generated contexts: a JIT build also chains a reflection resolver, absent from an AOT build.
        var info = options.TypeInfoResolverChain
            .OfType<JsonSerializerContext>()
            .Select(r => ((System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver)r).GetTypeInfo(type, options))
            .FirstOrDefault(i => i is not null);
        Assert.True(info is not null, $"{type} needs a [JsonSerializable] in McpJson");
    }
}
