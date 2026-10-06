using ImageMagick;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Extensions
{
	public static class ServiceExtensions
	{
		public static IServiceCollection AddCinnabarSharpServices(this IServiceCollection services)
		{
			ConfigureMagickResourceLimits();

			services.TryAddSingleton<IDocumentEventsService, DocumentEventsService>();
			services.TryAddSingleton<IWorkspaceService, WorkspaceManager>();
			services.TryAddSingleton<IFormatManager, FormatManager>();

			AddFormat(services, "PngFormat", "PNG", ["png"],
				[MagickFormat.Png, MagickFormat.Png8, MagickFormat.Png24, MagickFormat.Png32, MagickFormat.Png48, MagickFormat.Png64, MagickFormat.Png00]);
			services.AddSingleton<IImageImporter, JpegFormat>();
			AddFormat(services, "BmpFormat", "BMP", ["bmp"],
				[MagickFormat.Bmp, MagickFormat.Bmp2, MagickFormat.Bmp3]);
			AddFormat(services, "GifFormat", "GIF", ["gif"],
				[MagickFormat.Gif, MagickFormat.Gif87]);
			AddFormat(services, "TiffFormat", "TIFF", ["tif", "tiff"],
				[MagickFormat.Tiff, MagickFormat.Tif, MagickFormat.Tiff64]);
			AddFormat(services, "WebPFormat", "WebP", ["webp"],
				[MagickFormat.WebP]);
			AddFormat(services, "HeicFormat", "HEIC", ["heic", "heif"],
				[MagickFormat.Heic, MagickFormat.Heif], canSave: false);
			AddFormat(services, "SvgFormat", "SVG", ["svg", "svgz"],
				[MagickFormat.Svg, MagickFormat.Svgz, MagickFormat.Msvg], canSave: false);
			services.AddSingleton<IImageImporter, OraFormat>();

			services.AddTransient<ImageDocument>();
			services.AddTransient<CinnabarSharp.Core.Vector.SvgDocument>();

			return services;
		}

		/// <summary>
		/// Caps Magick.NET's own unmanaged memory use so a very large image spills to its disk cache instead of
		/// growing process memory unbounded or failing (performance-tasks.md P2). Global to the process (Magick.NET
		/// has no per-instance limit) and idempotent, so calling this more than once (every DI container built,
		/// e.g. once per test) is harmless.
		/// </summary>
		private static void ConfigureMagickResourceLimits()
		{
			try
			{
				var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
				if (available > 0)
					ResourceLimits.Memory = (ulong)(available / 4);
			}
			catch
			{
				// Leave Magick.NET's own defaults if this isn't available/supported on the current runtime.
			}
		}

		private static void AddFormat(IServiceCollection services, string name, string displayName,
			string[] extensions, MagickFormat[] magickFormats, bool canSave = true)
		{
			services.AddSingleton<IImageImporter>(sp => new MagickImageFormat(name, displayName, extensions,
				magickFormats, sp.GetRequiredService<IWorkspaceService>(), canSave));
		}
	}
}
