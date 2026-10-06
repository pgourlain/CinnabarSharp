using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace CinnabarSharp.Vector;

public sealed class SvgParseException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class SvgParseOptions
{
    /// <summary>Largest accepted file, in bytes; for .svgz the limit applies to the decompressed data too.</summary>
    public long MaxBytes { get; init; } = 64L * 1024 * 1024;
}

public sealed record SvgParseResult(SvgRoot Root)
{
    /// <summary>What was read but ignored (unsupported CSS…); the file still opened.</summary>
    public IReadOnlyList<string> Warnings => Root.Warnings;
}

/// <summary>Reads an SVG (plain or gzip-compressed) into the node tree.</summary>
public static class SvgParser
{
    public static SvgParseResult Parse(Stream stream, SvgParseOptions? options = null)
    {
        options ??= new SvgParseOptions();
        var bytes = ReadLimited(stream, options.MaxBytes);
        if (bytes.Length > 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
        {
            using var compressed = new MemoryStream(bytes);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            bytes = ReadLimited(gzip, options.MaxBytes);
        }
        return ParseBytes(bytes);
    }

    public static SvgParseResult Parse(string svgText, SvgParseOptions? options = null) =>
        Parse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgText)), options);

    public static SvgParseResult ParseFile(string path, SvgParseOptions? options = null)
    {
        using var stream = File.OpenRead(path);
        return Parse(stream, options);
    }

    private static byte[] ReadLimited(Stream stream, long limit)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > limit)
                throw new SvgParseException($"The SVG file is larger than the limit of {limit / (1024 * 1024)} MB.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static SvgParseResult ParseBytes(byte[] bytes)
    {
        XDocument document;
        try
        {
            var settings = new XmlReaderSettings
            {
                // A DOCTYPE is skipped, never processed: no entity expansion, nothing is fetched (XXE and entity bombs).
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                IgnoreWhitespace = false,
                IgnoreComments = false,
                IgnoreProcessingInstructions = false,
            };
            using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
            document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException e)
        {
            throw new SvgParseException("The file is not valid XML: " + e.Message, e);
        }

        if (document.Root is not { } rootElement || rootElement.Name.LocalName != "svg")
            throw new SvgParseException("The file is not an SVG document (no svg element at the root).");

        var root = (SvgRoot)SvgNodeFactory.Build(rootElement, inText: false)!;
        root.Declaration = document.Declaration;
        var before = true;
        foreach (var node in document.Nodes())
        {
            if (ReferenceEquals(node, rootElement))
            {
                before = false;
                continue;
            }
            if (node is XText)
                continue;
            (before ? root.LeadingNodes : root.TrailingNodes).Add(node);
        }
        root.IndentText = DetectIndent(rootElement);
        // Collect warnings of the stylesheet now, so they come with the parse.
        _ = root.Stylesheet;
        return new SvgParseResult(root);
    }

    private static string? DetectIndent(XElement root)
    {
        foreach (var child in root.Nodes())
        {
            if (child is not XText { Value: var text } || !string.IsNullOrWhiteSpace(text) || !text.Contains('\n'))
                continue;
            var indent = text[(text.LastIndexOf('\n') + 1)..];
            if (indent.Length > 0)
                return indent;
        }
        return null;
    }
}
