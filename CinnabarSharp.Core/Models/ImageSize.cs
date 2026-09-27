using System;
using System.Reflection;

namespace CinnabarSharp.Core.Models
{
	public record struct ImageSize
	{
        public ImageSize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width;
        public int Height;

        public static readonly ImageSize Empty;

        public override string ToString() => $"{Width}, {Height}";

        public bool IsEmpty => (Width == 0 && Height == 0);
    }

    public record struct PointI
    {
        public PointI(int x, int y)
        {
            this.X = x;
            this.Y = y;
        }

        public static readonly PointI Zero;

        public int X;
        public int Y;

        public override string ToString() => $"{X}, {Y}";
    }

    public record struct PointD
    {
        public PointD(double x, double y)
        {
            this.X = x;
            this.Y = y;
        }

        public double X;
        public double Y;

        public override string ToString() => $"{X}, {Y}";

        public PointI ToInt() => new((int)X, (int)Y);

        public double Distance(in PointD e)
        {
            return new PointD(X - e.X, Y - e.Y).Magnitude();
        }

        public double Magnitude()
        {
            return Math.Sqrt(X * X + Y * Y);
        }

        /// <summary>
        /// Returns a new point, rounded to the nearest integer coordinates.
        /// </summary>
        public PointD Rounded() => new PointD(Math.Round(X), Math.Round(Y));

        public static PointD operator +(in PointD a, in PointD b)
        {
            return new PointD(a.X + b.X, a.Y + b.Y);
        }
    }

}

