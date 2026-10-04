using System.Text.Json;
using CinnabarSharp.Mcp.Scripting;

namespace CinnabarSharp.Mcp.Tests;

public class ScriptParserTests
{
    [Fact]
    public void Lines_become_commands_with_arguments_and_comments_and_blanks_are_ignored()
    {
        var commands = ScriptParser.Parse("""
            # a comment
            open_image path=/photos/a.heic   # trailing comment

            apply_effect effect="Black and White" document=2
            resize_image width=1920
            close_image
            """);

        Assert.Equal(["open_image", "apply_effect", "resize_image", "close_image"], commands.Select(c => c.Name));
        Assert.Equal([2, 4, 5, 6], commands.Select(c => c.Line));
        Assert.Equal([("path", "/photos/a.heic")], commands[0].Arguments.Select(a => (a.Name, a.Value)));
        Assert.Equal([("effect", "Black and White"), ("document", "2")], commands[1].Arguments.Select(a => (a.Name, a.Value)));
        Assert.Empty(commands[3].Arguments);
    }

    [Fact]
    public void Quotes_escapes_json_and_hash_inside_values_are_kept()
    {
        var command = Assert.Single(ScriptParser.Parse(
            """compose name="a \"b\" c" other='x # y' color=#FF0000 list=["a b","c"] obj={"Radius":4,"s":"}"} tail=1"""));

        Assert.Equal(
            [("name", "a \"b\" c"), ("other", "x # y"), ("color", "#FF0000"), ("list", "[\"a b\",\"c\"]"), ("obj", "{\"Radius\":4,\"s\":\"}\"}"), ("tail", "1")],
            command.Arguments.Select(a => (a.Name, a.Value)));
    }

    [Theory]
    [InlineData("path=a.png", "should be a command name")]
    [InlineData("open_image path", "should be name=value")]
    [InlineData("open_image =x", "should be name=value")]
    [InlineData("open_image path=\"unclosed", "never closed")]
    [InlineData("open_image list=[1,2", "never closed")]
    public void A_bad_line_is_reported_with_its_number(string line, string message)
    {
        var e = Assert.Throws<ScriptException>(() => ScriptParser.Parse("# ok\n" + line));

        Assert.Equal(2, e.Line);
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void Variables_and_the_home_folder_are_expanded_and_unknown_variables_fail()
    {
        var variables = new Dictionary<string, string> { ["name"] = "photo", ["dir"] = "/in" };

        Assert.Equal("/in/photo.png", ScriptParser.Expand("$dir/$name.png", variables, 3));
        Assert.Equal("/in/photo_x", ScriptParser.Expand("${dir}/${name}_x", variables, 3));
        Assert.Equal("$name costs 5$", ScriptParser.Expand("$$name costs 5$$", variables, 3));
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/x", ScriptParser.Expand("~/x", variables, 3));
        Assert.Equal("a~b", ScriptParser.Expand("a~b", variables, 3));
        var e = Assert.Throws<ScriptException>(() => ScriptParser.Expand("$nope/x", variables, 3));
        Assert.Equal(3, e.Line);
        Assert.Contains("$nope", e.Message);
    }

    [Theory]
    [InlineData("""{"type":"integer"}""", "1920", """1920""")]
    [InlineData("""{"type":["integer","null"]}""", "-5", """-5""")]
    [InlineData("""{"type":"number"}""", "0.5", """0.5""")]
    [InlineData("""{"type":"boolean"}""", "TRUE", """true""")]
    [InlineData("""{"type":["string","null"]}""", "1920", "\"1920\"")]
    [InlineData("""{"type":"string"}""", "true", "\"true\"")]
    [InlineData("""{"type":["array","null"]}""", """["a","b"]""", """["a","b"]""")]
    [InlineData("""{"type":["object","null"]}""", """{"Radius":4}""", """{"Radius":4}""")]
    [InlineData("""{"anyOf":[{"type":"integer"},{"type":"string"}]}""", "7", "7")]
    [InlineData("""{"anyOf":[{"type":"integer"},{"type":"string"}]}""", "seven", "\"seven\"")]
    [InlineData("""{}""", "[1,2]", "[1,2]")]
    [InlineData("""{}""", "text", "\"text\"")]
    public void Values_become_the_json_type_the_schema_asks_for(string schema, string text, string expected)
    {
        using var document = JsonDocument.Parse(schema);

        Assert.Equal(expected, ScriptArguments.Convert("p", text, document.RootElement).GetRawText());
    }

    [Theory]
    [InlineData("""{"type":"integer"}""", "abc")]
    [InlineData("""{"type":"integer"}""", "1.5")]
    [InlineData("""{"type":"boolean"}""", "yes")]
    [InlineData("""{"type":"array"}""", "not json")]
    [InlineData("""{"type":"array"}""", """{"a":1}""")]
    public void A_value_of_the_wrong_type_says_what_is_expected(string schema, string text)
    {
        using var document = JsonDocument.Parse(schema);

        var e = Assert.Throws<ScriptException>(() => ScriptArguments.Convert("width", text, document.RootElement));

        Assert.Contains("width expects", e.Message);
    }
}
