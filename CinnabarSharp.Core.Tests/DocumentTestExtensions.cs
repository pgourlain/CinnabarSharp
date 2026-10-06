using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Tests;

/// <summary>Tests that open raster files get the document as an <see cref="ImageDocument"/>.</summary>
public static class DocumentTestExtensions
{
    public static ImageDocument AsImage(this IDocument document) => (ImageDocument)document;
}
