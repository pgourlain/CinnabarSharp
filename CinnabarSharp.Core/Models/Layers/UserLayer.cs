using ImageMagick;

namespace CinnabarSharp.Core.Models
{
    public class UserLayer : Layer
    {
        //Special layers to be drawn on to keep things editable by drawing them separately from the UserLayers.
        public List<ReEditableLayer> ReEditableLayers = new List<ReEditableLayer>();
        public ReEditableLayer TextLayer;

        //Call the base class constructor and setup the engines.
        public UserLayer(IImageBuf surface) : this(surface, false, 1f, "")
        {
        }

        //Call the base class constructor and setup the engines.
        public UserLayer(IImageBuf surface, bool hidden, double opacity, string name) : base(surface, hidden, opacity, name)
        {
            tEngine = new TextEngine();
            TextLayer = new ReEditableLayer(this);
        }

        //Stores most of the editable text's data, including the text itself.
        public TextEngine tEngine;

        //Rectangular boundary surrounding the editable text.
        public RectangleI textBounds = RectangleI.Zero;
        public RectangleI previousTextBounds = RectangleI.Zero;

        public override void ApplyTransform(object xform, ImageSize old_size, ImageSize new_size)
        {
            base.ApplyTransform(xform, old_size, new_size);

            foreach (ReEditableLayer rel in ReEditableLayers)
            {
                if (rel.IsLayerSetup)
                    rel.Layer.ApplyTransform(xform, old_size, new_size);
            }
        }

        public void Rotate(double angle, ImageSize old_size, ImageSize new_size)
        {
            double radians = (angle / 180d) * Math.PI;

            // var xform = CairoExtensions.CreateIdentityMatrix();
            // xform.Translate(new_size.Width / 2.0, new_size.Height / 2.0);
            // xform.Rotate(radians);
            // xform.Translate(-old_size.Width / 2.0, -old_size.Height / 2.0);

            ApplyTransform(null, old_size, new_size);
        }

        public override void Crop(RectangleI rect, object selection)
        {
            base.Crop(rect, selection);

            foreach (ReEditableLayer rel in ReEditableLayers)
            {
                if (rel.IsLayerSetup)
                    rel.Layer.Crop(rect, selection);
            }
        }

        public override void ResizeCanvas(int width, int height, Anchor anchor)
        {
            base.ResizeCanvas(width, height, anchor);

            foreach (ReEditableLayer rel in ReEditableLayers)
            {
                if (rel.IsLayerSetup)
                {
                    rel.Layer.ResizeCanvas(width, height, anchor);
                }
            }
        }

        public override void Resize(int width, int height)
        {
            base.Resize(width, height);

            foreach (ReEditableLayer rel in ReEditableLayers)
            {
                if (rel.IsLayerSetup)
                {
                    rel.Layer.Resize(width, height);
                }
            }
        }
    }

}

