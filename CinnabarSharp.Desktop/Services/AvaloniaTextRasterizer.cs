using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CinnabarSharp.Core.Tools;

namespace CinnabarSharp.Desktop.Services;

/// <summary>Renders text for the Text tool with Avalonia's text stack (system fonts, Skia rasterizer).</summary>
public sealed class AvaloniaTextRasterizer : ITextRasterizer
{
    /// <summary>Installed font families, sorted, for the Text tool's font list.</summary>
    public static IReadOnlyList<string> FontFamilies =>
        FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().Order(StringComparer.CurrentCultureIgnoreCase).ToList();

    public static string DefaultFontFamily => FontManager.Current.DefaultFontFamily.Name;

    public TextRaster RenderLine(string text, TextStyle style)
    {
        var formatted = Format(text, style);
        // Glyphs can reach outside the line box (italics, accents, descenders of large fonts).
        var margin = (int)Math.Ceiling(style.Size / 2) + 2;
        var width = (int)Math.Ceiling(formatted.WidthIncludingTrailingWhitespace) + 2 * margin;
        var height = (int)Math.Ceiling(formatted.Height) + 2 * margin;

        using var target = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var context = target.CreateDrawingContext())
        using (context.PushRenderOptions(new RenderOptions
               {
                   TextRenderingMode = style.Antialias ? TextRenderingMode.Antialias : TextRenderingMode.Alias,
               }))
        {
            context.DrawText(formatted, new Point(margin, margin));
        }

        // White text: the alpha channel is the coverage, whatever the channel order.
        var stride = width * 4;
        var pixels = new byte[stride * height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            target.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }
        var coverage = new byte[width * height];
        for (var i = 0; i < coverage.Length; i++)
            coverage[i] = pixels[i * 4 + 3];
        return new TextRaster(coverage, width, height, margin, margin);
    }

    public double MeasureWidth(string text, TextStyle style) =>
        text.Length == 0 ? 0 : Format(text, style).WidthIncludingTrailingWhitespace;

    public double LineHeight(TextStyle style) => Format("Ag", style).Height;

    private static FormattedText Format(string text, TextStyle style)
    {
        var family = string.IsNullOrEmpty(style.FontFamily) ? FontFamily.Default : new FontFamily(style.FontFamily);
        var typeface = new Typeface(family,
            style.Italic ? FontStyle.Italic : FontStyle.Normal,
            style.Bold ? FontWeight.Bold : FontWeight.Normal);
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface,
            Math.Max(1, style.Size), Brushes.White);
        if (style.Underline)
            formatted.SetTextDecorations(TextDecorations.Underline);
        return formatted;
    }
}
