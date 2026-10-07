using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Vector;
using ImageMagick;
using FillRule = CinnabarSharp.Vector.FillRule;

namespace CinnabarSharp.Core.Vector;

// Bitmaps in drawings: import (embedded or linked), editing the pixels, clipping.
public sealed partial class SvgActions
{
    private static bool IsPng(byte[] d) => d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47;

    private static bool IsJpeg(byte[] d) => d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF;

    /// <summary>
    /// Places a picture file as an <c>&lt;image&gt;</c> at 96 dpi (reduced to fit the page), centered. Embedded: the PNG or JPEG
    /// bytes go into the file as a data URI (other formats are converted to PNG). Linked: the drawing keeps the relative path;
    /// the drawing must be saved and the picture must be in its folder or below it.
    /// </summary>
    public SvgImage ImportImage(FileInfo file, bool linked, VRect? box = null)
    {
        if (!file.Exists)
            throw new FileNotFoundException("The picture does not exist.", file.FullName);
        if (file.Length > ImageResolver.MaxBytes)
            throw new InvalidOperationException("The picture is too large to import.");
        var data = File.ReadAllBytes(file.FullName);
        var decoded = _document.ImageDecoder.Decode(data)
            ?? throw new InvalidOperationException($"\"{file.Name}\" is not a picture that can be placed in a drawing (PNG, JPEG, WebP or GIF).");
        if (linked)
        {
            var folder = _document.File?.Directory?.FullName
                ?? throw new InvalidOperationException("Save the drawing first: a linked picture is stored as a path relative to it.");
            var relative = Path.GetRelativePath(folder, file.FullName);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                throw new InvalidOperationException("A linked picture must be in the folder of the drawing or below it.");
            return AddImageReference(Uri.EscapeDataString(relative.Replace('\\', '/')).Replace("%2F", "/"), decoded.Width, decoded.Height, "Import Linked Picture", box);
        }
        if (IsPng(data))
            return AddImage(data, "image/png", decoded.Width, decoded.Height, "Import Picture", box);
        if (IsJpeg(data))
            return AddImage(data, "image/jpeg", decoded.Width, decoded.Height, "Import Picture", box);
        return AddImage(EncodePng(decoded.Bgra, decoded.Width, decoded.Height), "image/png", decoded.Width, decoded.Height, "Import Picture", box);
    }

    private static byte[] EncodePng(byte[] bgra, int width, int height)
    {
        using var image = Utility.FromBgra(bgra, width, height);
        return image.ToByteArray(MagickFormat.Png);
    }

    /// <summary>The pixels of an <c>&lt;image&gt;</c>, or null when its data is missing, refused (a link outside the drawing's folder) or not a picture.</summary>
    public ClipboardImage? DecodeImage(SvgImage image)
    {
        var bytes = ImageResolver.Resolve(image.Href, _document.File?.DirectoryName);
        if (bytes is null || _document.ImageDecoder.Decode(bytes) is not { } decoded)
            return null;
        return new ClipboardImage(decoded.Bgra, decoded.Width, decoded.Height);
    }

    /// <summary>Replaces the picture of an image (embedded as PNG from now on); its box on the page is unchanged.</summary>
    public void ReplaceImage(SvgImage image, ClipboardImage pixels, string name = "Edit Bitmap")
    {
        var png = EncodePng(pixels.Bgra, pixels.Width, pixels.Height);
        var tx = Begin(name);
        tx.Edit([image], () => image.Href = $"data:image/png;base64,{Convert.ToBase64String(png)}");
        tx.Commit();
    }

    // ---- Clipping ----

    /// <summary>
    /// Clips the selected objects with the top-most selected object, which must be a shape: it is taken out of the drawing and
    /// becomes a <c>clipPath</c> (one per clipped object, in that object's coordinates). An object that was clipped already
    /// is clipped by the new shape instead.
    /// </summary>
    public IReadOnlyList<SvgElement> SetClip(IEnumerable<SvgElement>? nodes = null)
    {
        var targets = TopLevel(nodes).Where(n => n is not SvgDefs).ToList();
        if (targets.Count < 2)
            throw new InvalidOperationException("Select the objects to clip and, above them, the shape to clip with.");
        if (targets[^1] is not SvgShape clipShape)
            throw new InvalidOperationException("The top-most selected object must be a shape or a path: it becomes the clip.");
        var objects = targets.Take(targets.Count - 1).ToList();
        var evenOdd = StyleResolver.ComputeFor(clipShape).FillRule == FillRule.EvenOdd;
        var clip = InDocumentSpace(clipShape);
        var tx = Begin("Set Clip");
        var defs = EnsureDefs(tx);
        var replaced = objects.Select(ClipIdOf).OfType<string>().ToHashSet();
        foreach (var target in objects)
        {
            var local = clip.Transformed(SvgBounds.ToDocument(target).Invert() ?? Matrix2D.Identity);
            var piece = new SvgPath();
            piece.SetPath(local, 4);
            if (evenOdd)
                piece.SetAttribute("clip-rule", "evenodd");
            var clipPath = new SvgClipPath { Id = Root.NewId("clip") };
            clipPath.AddChild(piece);
            tx.Insert(defs, defs.Children.Count, clipPath);
            var reference = $"url(#{clipPath.Id})";
            tx.Edit([target], () =>
            {
                target.Style.Set("clip-path", null);
                target.SetAttribute("clip-path", reference);
            });
        }
        tx.Remove(clipShape);
        PruneClipPaths(tx, replaced);
        tx.Commit(objects);
        return objects;
    }

    /// <summary>Removes the clip of the selected objects (and the clip paths nothing uses any more).</summary>
    public void ReleaseClip(IEnumerable<SvgElement>? nodes = null)
    {
        var targets = TopLevel(nodes).Where(n => n.GetAttribute("clip-path") is not null || n.Style.Get("clip-path") is not null).ToList();
        if (targets.Count == 0)
            return;
        var tx = Begin("Release Clip");
        var released = targets.Select(ClipIdOf).OfType<string>().ToHashSet();
        tx.Edit(targets, () =>
        {
            foreach (var target in targets)
            {
                target.Style.Set("clip-path", null);
                target.SetAttribute("clip-path", null);
            }
        });
        PruneClipPaths(tx, released);
        tx.Commit();
    }

    private static string? ClipIdOf(SvgElement element)
    {
        var reference = element.Style.Get("clip-path") ?? element.GetAttribute("clip-path");
        var at = reference?.IndexOf('#') ?? -1;
        if (reference is null || at < 0)
            return null;
        return reference[(at + 1)..].TrimEnd(')', ' ', '"', '\'');
    }

    /// <summary>Removes, among the clip paths with these ids, the ones no element refers to any more.</summary>
    private void PruneClipPaths(Transaction tx, HashSet<string> candidates)
    {
        if (candidates.Count == 0)
            return;
        var used = Root.SelfAndDescendants().OfType<SvgElement>().Select(ClipIdOf).OfType<string>().ToHashSet();
        foreach (var clipPath in Root.Descendants().OfType<SvgClipPath>().ToList())
            if (clipPath.Id is { } id && candidates.Contains(id) && !used.Contains(id))
                tx.Remove(clipPath);
    }
}
