using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace CinnabarSharp.Vector.Tests;

public class RoundTripTests
{
    public static IEnumerable<object[]> Samples() => SvgTestFiles.AllAsData();

    /// <summary>Elements, attributes (in order) and non-blank text; indentation and attribute quoting do not matter.</summary>
    private static string Normalize(XNode node)
    {
        var sb = new StringBuilder();
        Append(sb, node);
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, XNode node)
    {
        switch (node)
        {
            case XElement e:
                sb.Append('<').Append(e.Name);
                foreach (var a in e.Attributes())
                    sb.Append(' ').Append(a.Name).Append("=\"").Append(a.Value).Append('"');
                sb.Append('>');
                foreach (var child in e.Nodes())
                    Append(sb, child);
                sb.Append("</").Append(e.Name).Append('>');
                break;
            case XComment c:
                sb.Append("<!--").Append(c.Value).Append("-->");
                break;
            case XProcessingInstruction p:
                sb.Append("<?").Append(p.Target).Append(' ').Append(p.Data).Append("?>");
                break;
            case XText t when string.IsNullOrWhiteSpace(t.Value):
                break;
            case XText t:
                sb.Append(t.Value.Trim());
                break;
        }
    }

    private static string NormalizeDocument(XDocument doc)
    {
        var sb = new StringBuilder();
        foreach (var node in doc.Nodes())
            if (node is not XDocumentType)
                Append(sb, node);
        return sb.ToString();
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Parse_write_parse_gives_the_same_tree(string name)
    {
        var original = XDocument.Parse(SvgTestFiles.ReadText(name), LoadOptions.PreserveWhitespace);
        var first = SvgParser.Parse(SvgTestFiles.ReadText(name)).Root;
        var written = SvgWriter.ToText(first);
        var second = SvgParser.Parse(written).Root;

        Assert.Equal(NormalizeDocument(original), NormalizeDocument(XDocument.Parse(written, LoadOptions.PreserveWhitespace)));
        Assert.Equal(SvgWriter.ToText(first), SvgWriter.ToText(second));
        Assert.Equal(first.SelfAndDescendants().Count(), second.SelfAndDescendants().Count());
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Untouched_documents_are_written_from_their_source(string name)
    {
        var root = SvgParser.Parse(SvgTestFiles.ReadText(name)).Root;
        Assert.DoesNotContain(root.SelfAndDescendants(), n => n.IsDirty || n.SubtreeDirty);
        var original = XDocument.Parse(SvgTestFiles.ReadText(name), LoadOptions.PreserveWhitespace);
        // Even whitespace survives: the written elements equal the originals node for node.
        Assert.True(XNode.DeepEquals(original.Root!, XDocument.Parse(SvgWriter.ToText(root), LoadOptions.PreserveWhitespace).Root!));
    }

    [Fact]
    public void Unknown_content_is_preserved_byte_for_byte_after_normalizing_white_space()
    {
        var text = SvgTestFiles.ReadText("unknown");
        var root = SvgParser.Parse(text).Root;
        // Touch something else so the document is rebuilt from the model.
        ((SvgRect)root.FindById("r1")!).X = 12;
        var written = XDocument.Parse(SvgWriter.ToText(root));
        var original = XDocument.Parse(text);

        foreach (var name in new[] { "metadata", "namedview", "title", "desc" })
            Assert.Equal(Normalize(original.Descendants().First(e => e.Name.LocalName == name)),
                Normalize(written.Descendants().First(e => e.Name.LocalName == name)));
        Assert.Equal(Normalize(original.Descendants().First(e => e.Name.LocalName == "filter")),
            Normalize(written.Descendants().First(e => e.Name.LocalName == "filter")));
        Assert.Contains(written.DescendantNodes().OfType<XComment>(), c => c.Value == " a comment kept in place ");
        var circle = written.Descendants().First(e => (string?)e.Attribute("id") == "c1");
        Assert.Equal("url(#blur1)", (string?)circle.Attribute("filter"));
        Assert.Equal("1:2", (string?)circle.Attribute("data-figma-id"));
        var rect = written.Descendants().First(e => (string?)e.Attribute("id") == "r1");
        Assert.Equal("Red box", (string?)rect.Attribute(SvgElement.Inkscape + "label"));
        Assert.Equal("true", (string?)rect.Attribute(SvgElement.Sodipodi + "insensitive"));
        Assert.Equal("12", (string?)rect.Attribute("x"));
        Assert.Equal("1.3", (string?)written.Root!.Attribute(SvgElement.Inkscape + "version"));
    }

    [Fact]
    public void Editing_one_node_changes_only_that_node()
    {
        var text = SvgTestFiles.ReadText("shapes");
        var root = SvgParser.Parse(text).Root;
        ((SvgRect)root.FindById("r1")!).Width = 77;
        var original = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        var written = XDocument.Parse(SvgWriter.ToText(root), LoadOptions.PreserveWhitespace);

        var changed = original.Root!.Elements().Zip(written.Root!.Elements())
            .Where(p => !XNode.DeepEquals(p.First, p.Second)).ToList();
        var pair = Assert.Single(changed);
        Assert.Equal("r1", (string?)pair.First.Attribute("id"));
        Assert.Equal("77", (string?)pair.Second.Attribute("width"));
        Assert.Equal("8", (string?)pair.Second.Attribute("rx"));
        // Attribute order is kept.
        Assert.Equal(pair.First.Attributes().Select(a => a.Name), pair.Second.Attributes().Select(a => a.Name));
    }

    [Fact]
    public void Added_and_removed_nodes_are_written_in_place()
    {
        var root = SvgParser.Parse(SvgTestFiles.ReadText("shapes")).Root;
        var added = new SvgCircle { Id = "new" };
        added.Cx = 5;
        added.Cy = 5;
        added.R = 3;
        root.InsertChild(1, added);
        root.RemoveChild(root.FindById("e1")!);
        var written = XDocument.Parse(SvgWriter.ToText(root));
        var ids = written.Root!.Elements().Select(e => (string?)e.Attribute("id")).ToList();
        Assert.Equal(["r1", "new", "c1", "l1", "pl1", "pg1"], ids);
        Assert.Equal(SvgElement.Ns, written.Root.Elements().ElementAt(1).Name.Namespace);
    }

    [Fact]
    public void Edited_text_keeps_spans_and_unknown_attributes()
    {
        var root = SvgParser.Parse(SvgTestFiles.ReadText("text")).Root;
        var span = root.Descendants().OfType<SvgTextSpan>().First();
        ((SvgTextRun)span.Children[0]).SetText("BOLD");
        var written = XDocument.Parse(SvgWriter.ToText(root));
        Assert.Contains("Hello <tspan", written.Descendants().First(e => (string?)e.Attribute("id") == "t1").ToString().Replace(" xmlns=\"http://www.w3.org/2000/svg\"", ""));
        Assert.Equal("BOLD", written.Descendants().First(e => e.Name.LocalName == "tspan").Value);
        Assert.Equal("bold", (string?)written.Descendants().First(e => e.Name.LocalName == "tspan").Attribute("font-weight"));
    }

    [Fact]
    public void Written_numbers_use_the_invariant_culture()
    {
        var current = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
            var rect = new SvgRect();
            rect.X = 1.5;
            rect.Width = 2.25;
            Assert.Equal("1.5", rect.GetAttribute("x"));
            var root = SvgParser.Parse(SvgTestFiles.ReadText("shapes")).Root;
            ((SvgCircle)root.FindById("c1")!).R = 2.5;
            Assert.Contains("r=\"2.5\"", SvgWriter.ToText(root));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = current;
        }
    }

    [Fact]
    public void Document_level_nodes_and_the_declaration_survive()
    {
        const string text = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!-- before -->\n<?xml-stylesheet href=\"a.css\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\"><rect id=\"r\" width=\"1\" height=\"1\"/></svg>\n<!-- after -->";
        var root = SvgParser.Parse(text).Root;
        ((SvgRect)root.FindById("r")!).Width = 5;
        var written = SvgWriter.ToText(root);
        Assert.StartsWith("<?xml", written);
        Assert.Contains("<!-- before -->", written);
        Assert.Contains("<?xml-stylesheet href=\"a.css\"?>", written);
        Assert.Contains("<!-- after -->", written);
        Assert.True(written.IndexOf("before", StringComparison.Ordinal) < written.IndexOf("<svg", StringComparison.Ordinal));
        Assert.True(written.IndexOf("after", StringComparison.Ordinal) > written.IndexOf("</svg>", StringComparison.Ordinal));
        Assert.False(written.StartsWith('\uFEFF'));
    }

    [Fact]
    public void A_file_without_a_declaration_is_written_without_one()
    {
        var root = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect id='r'/></svg>").Root;
        Assert.DoesNotContain("<?xml", SvgWriter.ToText(root));
    }

    [Fact]
    public void An_indented_file_stays_indented_for_new_nodes()
    {
        var root = SvgParser.Parse("<svg xmlns=\"http://www.w3.org/2000/svg\">\n\t<g id=\"g\">\n\t\t<rect id=\"a\"/>\n\t</g>\n</svg>").Root;
        ((SvgGroup)root.FindById("g")!).AddChild(new SvgRect { Id = "b" });
        var written = SvgWriter.ToText(root);
        Assert.Contains("\n\t\t<rect id=\"b\"", written);
    }

    [Fact]
    public void Svgz_round_trip()
    {
        var text = SvgTestFiles.ReadText("shapes");
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, true))
            gzip.Write(Encoding.UTF8.GetBytes(text));
        compressed.Position = 0;
        var root = SvgParser.Parse(compressed).Root;
        Assert.NotNull(root.FindById("r1"));

        var bytes = SvgWriter.ToBytes(root, gzip: true);
        Assert.Equal((byte)0x1F, bytes[0]);
        Assert.Equal((byte)0x8B, bytes[1]);
        var again = SvgParser.Parse(new MemoryStream(bytes)).Root;
        Assert.Equal(root.SelfAndDescendants().Count(), again.SelfAndDescendants().Count());
    }

    [Fact]
    public void External_entities_are_never_read()
    {
        var secret = Path.Combine(Path.GetTempPath(), $"secret-{Guid.NewGuid():N}.txt");
        File.WriteAllText(secret, "TOP-SECRET-CONTENT");
        try
        {
            var uri = new Uri(secret).AbsoluteUri;
            var svg = $"<?xml version=\"1.0\"?>\n<!DOCTYPE svg [<!ENTITY xxe SYSTEM \"{uri}\">]>\n<svg xmlns=\"http://www.w3.org/2000/svg\"><text id=\"t\">&xxe;</text></svg>";
            string written;
            try
            {
                written = SvgWriter.ToText(SvgParser.Parse(svg).Root);
            }
            catch (SvgParseException)
            {
                return; // refusing the file is fine too
            }
            Assert.DoesNotContain("TOP-SECRET-CONTENT", written);
        }
        finally
        {
            File.Delete(secret);
        }
    }

    [Fact]
    public void Entity_bombs_do_not_expand()
    {
        const string svg = "<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\"><!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\">]><svg xmlns=\"http://www.w3.org/2000/svg\"><text>&c;</text></svg>";
        try
        {
            var text = SvgWriter.ToText(SvgParser.Parse(svg).Root);
            Assert.True(text.Length < 5000);
        }
        catch (SvgParseException)
        {
            // fine
        }
    }

    [Fact]
    public void Files_over_the_limit_are_refused_with_a_clear_error()
    {
        var big = new MemoryStream(new byte[2000]);
        var e = Assert.Throws<SvgParseException>(() => SvgParser.Parse(big, new SvgParseOptions { MaxBytes = 1000 }));
        Assert.Contains("larger than the limit", e.Message);
    }

    [Fact]
    public void A_gzip_bomb_is_stopped_by_the_limit()
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, true))
            gzip.Write(new byte[5000]);
        compressed.Position = 0;
        Assert.Throws<SvgParseException>(() => SvgParser.Parse(compressed, new SvgParseOptions { MaxBytes = 1000 }));
    }

    [Theory]
    [InlineData("not xml")]
    [InlineData("<svg")]
    [InlineData("<html xmlns='http://www.w3.org/1999/xhtml'/>")]
    [InlineData("")]
    public void Invalid_files_throw_a_parse_exception(string text) =>
        Assert.Throws<SvgParseException>(() => SvgParser.Parse(text));

    [Fact]
    public void Writing_does_not_change_the_document()
    {
        var root = SvgParser.Parse(SvgTestFiles.ReadText("shapes")).Root;
        ((SvgRect)root.FindById("r1")!).X = 1;
        var version = root.Version;
        var first = SvgWriter.ToText(root);
        Assert.Equal(first, SvgWriter.ToText(root));
        Assert.Equal(version, root.Version);
        Assert.True(root.FindById("r1")!.IsDirty);
    }
}
