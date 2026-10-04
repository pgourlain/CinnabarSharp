//
// Based on Pinta's GdkPixbufFormat.cs
//
// Author:
//       Maia Kozheva <sikon@ubuntu.com>
//
// Copyright (c) 2010 Maia Kozheva <sikon@ubuntu.com>
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using ImageMagick;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Services;

/// <summary>
/// A single-layer image file format read and written with Magick.NET.
/// </summary>
public class MagickImageFormat : ImageFormat
{
    private readonly IWorkspaceService _workspaceService;
    private readonly MagickFormat[] _magickFormats;

    private readonly bool _canSave;

    public MagickImageFormat(string name, string displayName, string[] extensions,
        MagickFormat[] magickFormats, IWorkspaceService workspaceService, bool canSave = true)
        : base(name, displayName, extensions)
    {
        _magickFormats = magickFormats;
        _workspaceService = workspaceService;
        _canSave = canSave;
    }

    public override bool SupportsSaving => _canSave;

    public override bool MatchesContent(ImageFile file)
    {
        try
        {
            return _magickFormats.Contains(new MagickImageInfo(file).Format);
        }
        catch (MagickException)
        {
            return false;
        }
    }

    public override ImageSize? PeekSize(ImageFile file)
    {
        try
        {
            var info = new MagickImageInfo(file);
            return new ImageSize((int)info.Width, (int)info.Height);
        }
        catch (MagickException)
        {
            return null;
        }
    }

    public override void Import(ImageFile file)
    {
        var img = Utility.OpenImage(file);
        img.AutoOrient();
        // Photos often carry a Display P3 or CMYK profile; the canvas and compositing assume sRGB.
        if (img.GetColorProfile() is not null)
            img.TransformColorSpace(ColorProfiles.SRGB);
        var imagesize = new ImageSize((int)img.Width, (int)img.Height);

        var doc = _workspaceService.CreateAndActivateDocument(file, SupportedExtensions[0], imagesize);

        doc.Workspace.ViewSize = imagesize;
        var layer = doc.Layers.AddNewLayer(file.GetDisplayName());
        var placeholder = layer.Surface;
        layer.Surface = img;
        placeholder.Dispose();
        // The layer was added empty: without this the canvas shows it transparent until something else redraws it.
        doc.Workspace.Invalidate();
    }

    public override void Export(ImageDocument document, ImageFile file)
    {
        if (!SupportsSaving)
            throw new NotSupportedException($"{DisplayName} files can be opened but not saved.");
        using var image = document.GetFlattenedImage();
        PrepareForSave(image);
        image.Format = _magickFormats[0];
        image.Write(file);
    }

    protected virtual void PrepareForSave(IMagickImage<byte> image)
    {
    }
}

public class JpegFormat : MagickImageFormat
{
    public const int DefaultQuality = 90;

    public JpegFormat(IWorkspaceService workspaceService)
        : base(nameof(JpegFormat), "JPEG", ["jpg", "jpeg", "jpe", "jfif"],
            [MagickFormat.Jpeg, MagickFormat.Jpg, MagickFormat.Jpe], workspaceService)
    {
    }

    public int Quality { get; set; } = DefaultQuality;

    public override bool SupportsTransparency => false;

    // JPEG has no alpha channel: flatten onto white like Paint.NET instead of letting transparent pixels turn black.
    protected override void PrepareForSave(IMagickImage<byte> image)
    {
        image.BackgroundColor = MagickColors.White;
        image.Alpha(AlphaOption.Remove);
        image.Quality = (uint)Quality;
        // TVs and browsers assume sRGB when there is no profile; say it explicitly (pixels are sRGB already).
        image.SetProfile(ColorProfiles.SRGB);
    }
}
