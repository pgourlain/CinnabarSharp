using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace CinnabarSharp.Mcp;

/// <summary>Read-only views for clients that browse resources; the same data is also available through tools.</summary>
[McpServerResourceType]
public sealed class ImageResources(McpContext context)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [McpServerResource(UriTemplate = "cinnabar://documents", Name = "documents", MimeType = "application/json")]
    [Description("The open images: id, name, file, size, layers, selection, unsaved changes.")]
    public Task<string> Documents() => context.Run(() => JsonSerializer.Serialize(
        context.Workspace.OpenDocuments.Select(d => Describe.Document(context, d)), Json));

    [McpServerResource(UriTemplate = "cinnabar://effects", Name = "effects", MimeType = "application/json")]
    [Description("Every adjustment and effect with its parameters (name, range, default, choices).")]
    public string Effects() => JsonSerializer.Serialize(EffectCatalogInfo.All, Json);

    [McpServerResource(UriTemplate = "cinnabar://documents/{id}/history", Name = "history", MimeType = "application/json")]
    [Description("Undo history of an open image.")]
    public Task<string> History([Description("Document id from cinnabar://documents.")] string id) =>
        context.Run(() => JsonSerializer.Serialize(Describe.History(context.Document(id)), Json));
}
