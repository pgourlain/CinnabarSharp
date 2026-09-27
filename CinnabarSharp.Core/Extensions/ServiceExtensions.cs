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
			services.AddSingleton<IImageImporter, OraFormat>();

			services.AddTransient<ImageDocument>();

			return services;
		}

		private static void AddFormat(IServiceCollection services, string name, string displayName,
			string[] extensions, MagickFormat[] magickFormats)
		{
			services.AddSingleton<IImageImporter>(sp => new MagickImageFormat(name, displayName, extensions,
				magickFormats, sp.GetRequiredService<IWorkspaceService>()));
		}
	}
}
