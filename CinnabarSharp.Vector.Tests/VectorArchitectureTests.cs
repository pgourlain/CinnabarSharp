namespace CinnabarSharp.Vector.Tests;

/// <summary>Same idea as CoreArchitectureTests: the SVG engine references the BCL only.</summary>
public class VectorArchitectureTests
{
    private static readonly string[] ForbiddenPrefixes =
    {
        "CinnabarSharp.Core", "Magick", "Avalonia", "SkiaSharp", "HarfBuzzSharp", "Microsoft.Extensions",
        "Microsoft.Maui", "Microsoft.WinUI", "PresentationCore", "PresentationFramework", "WindowsBase",
        "System.Windows", "System.Drawing", "Gtk", "Gdk", "Cairo", "Eto", "Uno",
    };

    private static readonly string[] AllowedPrefixes = { "System", "Microsoft.Win32", "netstandard", "mscorlib" };

    [Fact]
    public void Vector_does_not_reference_anything_but_the_bcl()
    {
        var names = typeof(SvgEngine).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        var forbidden = names.Where(n => ForbiddenPrefixes.Any(p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase)
            && !n.StartsWith("System.Private", StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.True(forbidden.Count == 0,
            "CinnabarSharp.Vector must only reference the BCL. Forbidden references: " + string.Join(", ", forbidden));

        var others = names.Where(n => !AllowedPrefixes.Any(p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.True(others.Count == 0, "Unexpected references: " + string.Join(", ", others));
    }
}
