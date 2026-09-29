using System;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Extensions
{
	public static class ImageSizeExtension
	{
        public static ImageSize RotateDimensions(this ImageSize originalSize, double angle)
        {
            double radians = (angle / 180d) * Math.PI;
            double cos = Math.Abs(Math.Cos(radians));
            double sin = Math.Abs(Math.Sin(radians));
            int w = originalSize.Width;
            int h = originalSize.Height;

            return new ImageSize((int)(w * cos + h * sin), (int)(w * sin + h * cos));
        }

        public static RectangleI ToInt(this ImageSize size)
        {
            return new RectangleI(0, 0, size.Width, size.Height);
        }

        /// <summary>
        /// Rough, best-effort check of whether opening an image this size risks running the machine out of
        /// memory: estimates one BGRA copy of it plus headroom for decode/compose intermediates, and flags it
        /// once that would be more than half of what's available (performance-tasks.md P5). Not a hard limit —
        /// the caller decides what to do (warn, offer to open downscaled, let it through anyway); returns false
        /// (don't warn) when available memory can't be determined, rather than guessing.
        /// </summary>
        public static bool IsRiskyToOpen(this ImageSize size, long? availableBytes = null)
        {
            var available = availableBytes ?? SafeAvailableMemory();
            if (available <= 0)
                return false;
            var estimatedBytes = (long)size.Width * size.Height * BytesPerPixelWithHeadroom;
            return estimatedBytes > available / 2;
        }

        // BGRA (4 bytes/pixel) times headroom for the copies decode/compose make along the way (source buffer,
        // straight-alpha conversion, the layer itself, ...) — approximate on purpose; see IsRiskyToOpen.
        private const int BytesPerPixelWithHeadroom = 16;

        private static long SafeAvailableMemory()
        {
            try
            {
                var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                if (available > 0)
                    return available;
            }
            catch
            {
                // Fall through to "unknown" below (e.g. not supported by the current GC/runtime).
            }
            return 0;
        }
    }
}

