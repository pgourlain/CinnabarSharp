using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CinnabarSharp.Vector;

/// <summary>
/// Writes the node tree back as XML. A node nobody touched is written from the XML it was read from; an edited node is
/// written from the model, keeping its unknown attributes and children in place.
/// </summary>
public static class SvgWriter
{
    public static string ToText(SvgRoot root)
    {
        using var stream = new MemoryStream();
        Write(root, stream);
        return new UTF8Encoding(false).GetString(stream.ToArray());
    }

    public static byte[] ToBytes(SvgRoot root, bool gzip = false)
    {
        using var stream = new MemoryStream();
        Write(root, stream, gzip);
        return stream.ToArray();
    }

    public static void Write(SvgRoot root, Stream output, bool gzip = false)
    {
        var document = ToXDocument(root);
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = root.Declaration is null,
            Indent = root.IndentText is not null,
            IndentChars = root.IndentText ?? "  ",
            NewLineHandling = NewLineHandling.None,
        };
        if (gzip)
        {
            using var zip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
            using var writer = XmlWriter.Create(zip, settings);
            document.Save(writer);
        }
        else
        {
            using var writer = XmlWriter.Create(output, settings);
            document.Save(writer);
        }
    }

    public static XDocument ToXDocument(SvgRoot root)
    {
        var document = new XDocument { Declaration = root.Declaration };
        foreach (var node in root.LeadingNodes)
            document.Add(CloneXml(node));
        document.Add(ToXNode(root));
        foreach (var node in root.TrailingNodes)
            document.Add(CloneXml(node));
        return document;
    }

    public static XNode ToXNode(SvgNode node)
    {
        switch (node)
        {
            case SvgRawContent raw:
                return CloneXml(raw.Content);
            case SvgRawElement rawElement:
                return new XElement(rawElement.Source);
            case SvgTextRun run:
                return new XText(run.Text);
            case SvgElement element:
                if (!element.IsDirty && !element.SubtreeDirty && element.SourceNode is XElement source)
                    return new XElement(source);
                var xe = new XElement(element.XmlName);
                foreach (var attribute in element.Attributes)
                    xe.Add(new XAttribute(attribute.Name, attribute.Value));
                foreach (var child in element.Children)
                    xe.Add(ToXNode(child));
                return xe;
            default:
                throw new InvalidOperationException("Unknown node type " + node.GetType().Name);
        }
    }

    private static XNode CloneXml(XNode node) => node switch
    {
        XElement e => new XElement(e),
        XComment c => new XComment(c),
        XProcessingInstruction p => new XProcessingInstruction(p),
        XCData d => new XCData(d),
        XText t => new XText(t),
        XDocumentType t => new XDocumentType(t),
        _ => throw new InvalidOperationException("Unsupported XML node " + node.NodeType),
    };
}
