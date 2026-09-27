using System;

namespace CinnabarSharp.Core.Models
{
    public record struct RectangleD
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;

        public RectangleD(double x, double y, double width, double height)
        {
            this.X = x;
            this.Y = y;
            this.Width = width;
            this.Height = height;
        }

        public RectangleD(in PointD point, double width, double height)
            : this(point.X, point.Y, width, height)
        {
        }

        public RectangleD(in PointD topleft, in PointD bottomRight)
            : this(topleft.X, topleft.Y, 
                Math.Abs(bottomRight.X - topleft.X), 
                Math.Abs(bottomRight.Y - topleft.Y))
        {
            
        }

        public RectangleI ToInt() => new RectangleI((int)Math.Floor(X), (int)Math.Floor(Y),
                                  (int)Math.Ceiling(Width), (int)Math.Ceiling(Height));

        public double Left => X;
        public double Top => Y;
        public double Right => X + Width - 1;
        public double Bottom => Y + Height - 1;

        public override string ToString() => $"x:{X} y:{Y} w:{Width} h:{Height}";

        public bool ContainsPoint(double x, double y)
        {
            if (x < this.X || x >= this.X + this.Width)
                return false;

            if (y < this.Y || y >= this.Y + this.Height)
                return false;

            return true;
        }

        public bool ContainsPoint(in PointD point) => ContainsPoint(point.X, point.Y);

        public PointD Location() => new PointD(X, Y);
        public PointD GetCenter() => new PointD(X + 0.5 * Width, Y + 0.5 * Height);

        public void Inflate(double width, double height)
        {
            X -= width;
            Y -= height;
            Width += width * 2;
            Height += height * 2;
        }

        public RectangleD Inflated(double width, double height)
        {
            RectangleD copy = this;
            copy.Inflate(width, height);
            return copy;
        }

        public RectangleD Clamp()
        {
            double x = this.X;
            double y = this.Y;
            double w = this.Width;
            double h = this.Height;

            if (x < 0)
            {
                w -= x;
                x = 0;
            }

            if (y < 0)
            {
                h -= y;
                y = 0;
            }

            return new RectangleD(x, y, w, h);
        }
    }

    public record struct RectangleI
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;

        public RectangleI(int x, int y, int width, int height)
        {
            this.X = x;
            this.Y = y;
            this.Width = width;
            this.Height = height;
        }

        public RectangleI(in PointI point, int width, int height)
            : this(point.X, point.Y, width, height)
        {
        }

        public RectangleI(in PointI point, in ImageSize size)
            : this(point.X, point.Y, size.Width, size.Height)
        {
        }

        public static readonly RectangleI Zero;

        public static RectangleI FromLTRB(int left, int top, int right, int bottom)
            => new RectangleI(left, top, right - left + 1, bottom - top + 1);

        public RectangleD ToDouble() => new RectangleD(X, Y, Width, Height);

        public int Left => X;
        public int Top => Y;
        public int Right => X + Width - 1;
        public int Bottom => Y + Height - 1;

        public bool IsEmpty => (Width == 0) || (Height == 0);

        public PointI Location => new PointI(X, Y);
        public ImageSize Size => new ImageSize(Width, Height);

        public override string ToString() => $"x:{X} y:{Y} w:{Width} h:{Height}";

        public bool Contains(int x, int y)
        {
            return x >= Left && x <= Right && y >= Top && y <= Bottom;
        }

        public bool Contains(in PointI pt) => Contains(pt.X, pt.Y);

        public RectangleI Intersect(RectangleI r) => Intersect(this, r);

        public static RectangleI Intersect(in RectangleI a, in RectangleI b)
        {
            int left = Math.Max(a.Left, b.Left);
            int right = Math.Min(a.Right, b.Right);
            int top = Math.Max(a.Top, b.Top);
            int bottom = Math.Min(a.Bottom, b.Bottom);

            if (left > right || top > bottom)
                return Zero;

            return FromLTRB(left, top, right, bottom);
        }

        public RectangleI Union(RectangleI r) => Union(this, r);

        public static RectangleI Union(in RectangleI a, in RectangleI b)
        {
            int left = Math.Min(a.Left, b.Left);
            int right = Math.Max(a.Right, b.Right);
            int top = Math.Min(a.Top, b.Top);
            int bottom = Math.Max(a.Bottom, b.Bottom);
            return FromLTRB(left, top, right, bottom);
        }

        public void Inflate(int width, int height)
        {
            X -= width;
            Y -= height;
            Width += width * 2;
            Height += height * 2;
        }
    }
}

