namespace CinnabarSharp.Vector.Tests;

/// <summary>Sample SVGs copied next to the test assembly (Data/svg).</summary>
public static class SvgTestFiles
{
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Data", "svg");

    public static string PathOf(string name) =>
        Path.Combine(Folder, name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? name : name + ".svg");

    public static string ReadText(string name) => File.ReadAllText(PathOf(name));

    public static Stream OpenRead(string name) => File.OpenRead(PathOf(name));

    public static IEnumerable<string> All() =>
        Directory.GetFiles(Folder, "*.svg").Select(Path.GetFileName).OfType<string>().OrderBy(n => n, StringComparer.Ordinal);

    public static IEnumerable<object[]> AllAsData() => All().Select(n => new object[] { n });
}
