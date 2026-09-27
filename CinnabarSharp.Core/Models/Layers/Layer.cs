using ImageMagick;
using CinnabarSharp.Core.Extensions;

namespace CinnabarSharp.Core.Models
{
    public class Layer : ObservableObject
    {
        private double opacity;
        private bool hidden;
        private string name;
        private BlendMode blend_mode;

        public Layer(IImageBuf surface) : this(surface, false, 1f, "")
        {
        }

        public Layer(IImageBuf surface, bool hidden, double opacity, string name)
        {
            Surface = surface;

            this.hidden = hidden;
            this.opacity = opacity;
            this.name = name;
            this.blend_mode = BlendMode.Normal;
            
            Transform = new MagickColorMatrix(3);
        }

        public IImageBuf Surface { get; set; }
        public bool Tiled { get; set; }
        public IDoubleMatrix Transform { get; set; }

        public static readonly string OpacityProperty = "Opacity";
        public static readonly string HiddenProperty = "Hidden";
        public static readonly string NameProperty = "Name";
        public static readonly string BlendModeProperty = "BlendMode";

        public double Opacity
        {
            get { return opacity; }
            set { if (opacity != value) SetValue(OpacityProperty, ref opacity, value); }
        }

        public bool Hidden
        {
            get { return hidden; }
            set { if (hidden != value) SetValue(HiddenProperty, ref hidden, value); }
        }

        public string Name
        {
            get { return name; }
            set { if (name != value) SetValue(NameProperty, ref name, value); }
        }

        public BlendMode BlendMode
        {
            get { return blend_mode; }
            set { if (blend_mode != value) SetValue(BlendModeProperty, ref blend_mode, value); }
        }

        public void Clear()
        {
            var old = Surface;
            var w = (int)old.Width;
            var h = (int)old.Height;
            Surface = Utility.CreateImage(w, h);
            old.Dispose();
        }

        public void FlipHorizontal()
        {
            Surface.Flop();
        }

        public void FlipVertical()
        {
            Surface.Flip();
        }

        public void Draw(object ctx)
        {
            Draw(ctx, Surface, Opacity);
        }

        public void Draw(object ctx, object surface, double opacity, bool transform = true)
        {
            throw new NotImplementedException();
            // ctx.Save();
            //
            // if (transform)
            //     ctx.Transform(Transform);
            //
            // ctx.BlendSurface(surface, BlendMode, opacity);
            //
            // ctx.Restore();
        }

        public void DrawWithOperator(object ctx, object surface, object op, double opacity = 1.0, bool transform = true)
        {
            throw new NotImplementedException();
        }

        public virtual void ApplyTransform(object xform, ImageSize old_size, ImageSize new_size)
        {
            throw new NotImplementedException();
            // var dest = CairoExtensions.CreateImageSurface(Format.Argb32, new_size.Width, new_size.Height);
            //
            // var g = new Context(dest);
            // g.Transform(xform);
            // g.SetSourceSurface(Surface, 0, 0);
            // g.Paint();
            //
            // Surface = dest;
        }

        public static ImageSize RotateDimensions(ImageSize originalSize, double angle)
        {
            double radians = (angle / 180d) * Math.PI;
            double cos = Math.Abs(Math.Cos(radians));
            double sin = Math.Abs(Math.Sin(radians));
            int w = originalSize.Width;
            int h = originalSize.Height;

            return new ImageSize((int)(w * cos + h * sin), (int)(w * sin + h * cos));
        }

        public virtual void Resize(int width, int height)
        {
            Surface.Resize((uint)width, (uint)height);
        }

        public virtual void ResizeCanvas(int width, int height, Anchor anchor)
        {
            throw new NotImplementedException();
            /*ImageSurface dest = CairoExtensions.CreateImageSurface(Format.Argb32, width, height);

            int delta_x = Surface.Width - width;
            int delta_y = Surface.Height - height;

            var g = new Context(dest);
            switch (anchor)
            {
                case Anchor.NW:
                    g.SetSourceSurface(Surface, 0, 0);
                    break;
                case Anchor.N:
                    g.SetSourceSurface(Surface, -delta_x / 2, 0);
                    break;
                case Anchor.NE:
                    g.SetSourceSurface(Surface, -delta_x, 0);
                    break;
                case Anchor.E:
                    g.SetSourceSurface(Surface, -delta_x, -delta_y / 2);
                    break;
                case Anchor.SE:
                    g.SetSourceSurface(Surface, -delta_x, -delta_y);
                    break;
                case Anchor.S:
                    g.SetSourceSurface(Surface, -delta_x / 2, -delta_y);
                    break;
                case Anchor.SW:
                    g.SetSourceSurface(Surface, 0, -delta_y);
                    break;
                case Anchor.W:
                    g.SetSourceSurface(Surface, 0, -delta_y / 2);
                    break;
                case Anchor.Center:
                    g.SetSourceSurface(Surface, -delta_x / 2, -delta_y / 2);
                    break;
            }
            g.Paint();

            Surface = dest;
            */
        }

        public virtual void Crop(RectangleI rect, object selection)
        {
            throw new NotImplementedException();
            /*
             ImageSurface dest = CairoExtensions.CreateImageSurface(Format.Argb32, rect.Width, rect.Height);
             
            var g = new Context(dest);
            // Move the selected content to the upper left
            g.Translate(-rect.X, -rect.Y);
            g.Antialias = Antialias.None;

            // Optionally, respect the given path.
            if (selection != null)
            {
                g.AppendPath(selection);
                g.FillRule = Cairo.FillRule.EvenOdd;
                g.Clip();
            }

            g.SetSourceSurface(Surface, 0, 0);
            g.Paint();

            Surface = dest;
            */
        }
    }

}

