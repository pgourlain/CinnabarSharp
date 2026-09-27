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
    }
}

