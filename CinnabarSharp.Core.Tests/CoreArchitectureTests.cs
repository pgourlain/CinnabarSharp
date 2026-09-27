using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Tests;

public class CoreArchitectureTests
{
    private static readonly string[] ForbiddenAssemblyPrefixes =
    {
        "Avalonia",
        "Microsoft.Maui",
        "Microsoft.WinUI",
        "Microsoft.iOS",
        "Microsoft.MacCatalyst",
        "Microsoft.macOS",
        "Microsoft.Android",
        "Mono.Android",
        "PresentationCore",
        "PresentationFramework",
        "WindowsBase",
        "System.Windows",
        "System.Drawing",
        "SkiaSharp",
        "HarfBuzzSharp",
        "GtkSharp",
        "GdkSharp",
        "Gtk",
        "Gdk",
        "Cairo",
        "Eto",
        "Uno",
    };

    [Fact]
    public void Core_does_not_reference_ui_or_rendering_assemblies()
    {
        var violations = typeof(ImageDocument).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => ForbiddenAssemblyPrefixes.Any(p =>
                name.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(p + ".", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(p + "-", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(violations.Count == 0,
            "CinnabarSharp.Core must stay non-visual (see CLAUDE.md). Forbidden references: " + string.Join(", ", violations));
    }
}
