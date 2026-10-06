using System.Text;

namespace CinnabarSharp.Vector;

/// <summary>A decoded bitmap: straight-alpha BGRA.</summary>
public sealed record DecodedImage(byte[] Bgra, int Width, int Height);

/// <summary>Decodes image files for <c>&lt;image&gt;</c> elements (PNG, JPEG…). Implemented outside the engine (Core uses Magick.NET).</summary>
public interface IImageDecoder
{
    /// <summary>The decoded picture, or null when the data is not an image this decoder reads.</summary>
    DecodedImage? Decode(ReadOnlySpan<byte> data);
}

/// <summary>Finds the bytes an <c>&lt;image&gt;</c> refers to: a data: URI, or a file inside the SVG's folder. Never a URL.</summary>
public static class ImageResolver
{
    /// <summary>Largest image data accepted, embedded or linked.</summary>
    public const long MaxBytes = 64L * 1024 * 1024;

    /// <summary>
    /// The image bytes, or null when the reference is missing, refused or unreadable. A linked file must be below
    /// <paramref name="baseFolder"/> (symbolic links resolved); with no base folder every link is refused.
    /// </summary>
    public static byte[]? Resolve(string? href, string? baseFolder)
    {
        if (string.IsNullOrWhiteSpace(href))
            return null;
        href = href.Trim();
        if (href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return ResolveDataUri(href);
        return ResolveFile(href, baseFolder);
    }

    private static byte[]? ResolveDataUri(string uri)
    {
        var comma = uri.IndexOf(',');
        if (comma < 0)
            return null;
        var header = uri[5..comma];
        var payload = uri[(comma + 1)..];
        try
        {
            if (header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            {
                var cleaned = string.Concat(payload.Where(c => !char.IsWhiteSpace(c)));
                var bytes = Convert.FromBase64String(cleaned);
                return bytes.Length <= MaxBytes ? bytes : null;
            }
            return Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static byte[]? ResolveFile(string href, string? baseFolder)
    {
        if (string.IsNullOrEmpty(baseFolder))
            return null;
        // No URLs (http:, file:, //host/…), no absolute paths.
        if (href.Contains("://", StringComparison.Ordinal) || href.StartsWith("//", StringComparison.Ordinal)
            || href.StartsWith('/') || href.StartsWith('\\') || (href.Length > 1 && href[1] == ':'))
            return null;
        try
        {
            var relative = Uri.UnescapeDataString(href).Replace('\\', '/');
            var baseFull = Path.GetFullPath(baseFolder);
            var full = Path.GetFullPath(Path.Combine(baseFull, relative));
            if (!IsInside(baseFull, full))
                return null;
            var info = new FileInfo(full);
            if (!info.Exists)
                return null;
            // A symbolic link must not lead out of the folder.
            if (info.LinkTarget is not null)
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target is null || !IsInside(RealPath(baseFull), target.FullName))
                    return null;
            }
            if (info.Length > MaxBytes)
                return null;
            return File.ReadAllBytes(full);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }

    private static string RealPath(string folder)
    {
        var info = new DirectoryInfo(folder);
        return info.LinkTarget is not null ? info.ResolveLinkTarget(true)?.FullName ?? folder : folder;
    }

    private static bool IsInside(string folder, string path)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, comparison);
    }
}
