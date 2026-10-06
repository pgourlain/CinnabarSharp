//
// DocumentWorkspace.cs
//  
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
// 
// Copyright (c) 2010 Jonathan Pobst
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
using System;
using CinnabarSharp.Core.Extensions;
using System.Reflection.Metadata;
using CinnabarSharp.Core.Services;
using Microsoft.Extensions.Logging;

namespace CinnabarSharp.Core.Models
{
    public class ImageDocumentWorkspace
    {
        private readonly IDocument document;
        private readonly IDocumentEventsService _documentEventsService;
        private readonly ILogger<ImageDocument> _logger;
        private ImageSize _viewSize;

        // The zoom as requested. Deriving it from ViewSize (whole pixels) loses precision: at 25 % a 1023-pixel-wide
        // image is 255 pixels wide, i.e. 24.93 %, and "zoom in" then goes back to 25 % forever.
        private double _scale = 1;

        private enum ZoomType
        {
            ZoomIn,
            ZoomOut,
            ZoomManually
        }

        internal ImageDocumentWorkspace(IDocument document, IImageDocumentHistory imageDocumentHistory,
            IDocumentEventsService documentEventsService,
            ILogger<ImageDocument> logger)
        {

            this.document = document;
            History = imageDocumentHistory??throw new ArgumentNullException(nameof(imageDocumentHistory));
            this._documentEventsService = documentEventsService;
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Public Events
        //public event EventHandler<CanvasInvalidatedEventArgs>? CanvasInvalidated;
        //public event EventHandler? ViewSizeChanged;
        #endregion

        #region Public Properties
        public ICanvas Canvas { get; set; } = null!; // NRT - This is set soon after creation

        /// <summary>
        /// Returns whether the zoomed image fits in the window without requiring scrolling.
        /// </summary>
        public bool ImageViewFitsInWindow
        {
            get
            {
                var view = Canvas.Viewport!;

                int window_x = view.GetAllocatedWidth();
                int window_y = view.GetAllocatedHeight();

                return ViewSize.Width <= window_x && ViewSize.Height <= window_y;
            }
        }

        /// <summary>
        /// Size of the zoomed image. Setting it directly also sets <see cref="Scale"/> to the width ratio.
        /// </summary>
        public ImageSize ViewSize
        {
            get { return _viewSize; }
            set
            {
                if (document.ImageSize.Width > 0)
                    _scale = (double)value.Width / document.ImageSize.Width;
                SetViewSize(value);
            }
        }

        private bool SetViewSize(ImageSize value)
        {
            if (_viewSize.Width == value.Width && _viewSize.Height == value.Height)
                return false;
            _viewSize = value;
            OnViewSizeChanged();
            return true;
        }

        /// <summary>Recomputes <see cref="ViewSize"/> from <see cref="Scale"/> after the image size changed.</summary>
        public void UpdateViewSize() => SetViewSize(ScaledSize(_scale));

        private ImageSize ScaledSize(double scale) => new(
            Math.Max(1, (int)Math.Round(document.ImageSize.Width * scale)),
            Math.Max(1, (int)Math.Round(document.ImageSize.Height * scale)));

        public IImageDocumentHistory History { get; }

        /// <summary>
        /// Returns whether the image (at 100% zoom) would fit in the window without requiring scrolling.
        /// </summary>
        public bool ImageFitsInWindow
        {
            get
            {
                var view = Canvas.Viewport!;

                int window_x = view.GetAllocatedWidth();
                int window_y = view.GetAllocatedHeight();

                return document.ImageSize.Width <= window_x && document.ImageSize.Height <= window_y;
            }
        }

        /// <summary>
        /// Offset to center the image view in the canvas widget.
        /// (When zoomed out, the widget will have a larger allocated size than the image view size).
        /// </summary>
        public PointD Offset => new PointD(
            (Canvas.GetAllocatedWidth() - _viewSize.Width) / 2,
            (Canvas.GetAllocatedHeight() - _viewSize.Height) / 2);

        /// <summary>
        /// Scale factor for the zoomed image.
        /// </summary>
        public double Scale
        {
            get { return _scale; }
            set
            {
                if (document.ImageSize.Width == 0)
                {
                    document.ImageSize = new ImageSize(1, document.ImageSize.Height);
                }

                if (document.ImageSize.Height == 0)
                {
                    document.ImageSize = new ImageSize(document.ImageSize.Width, 1);
                }

                var changed = value != _scale;
                _scale = value;
                // Zooming changes only the view, not the pixels: ViewSizeChanged, never CanvasInvalidated
                // (which re-flattens every layer). Also raised when rounding keeps the same view size, for the zoom text.
                if (!SetViewSize(ScaledSize(value)) && changed)
                    OnViewSizeChanged();

                //if (PintaCore.Tools.CurrentTool?.CursorChangesOnZoom == true)
                //{
                //    //The current tool's cursor changes when the zoom changes.
                //    PintaCore.Tools.CurrentTool.SetCursor(PintaCore.Tools.CurrentTool.DefaultCursor);
                //}
            }
        }

        #endregion

        #region Public Methods
        public void Invalidate()
        {
            OnCanvasInvalidated(new CanvasEventItem(document, DocumentEventEnum.CanvasInvalidated, 
                this, RectangleI.Zero));
        }

        /// <summary>
        /// Repaints only <paramref name="imageRect"/> (image pixel coordinates); the event's Rect carries it.
        /// A zero rectangle (from <see cref="Invalidate()"/>) means the whole image.
        /// </summary>
        public void Invalidate(RectangleI imageRect)
        {
            OnCanvasInvalidated(new CanvasEventItem(document, DocumentEventEnum.CanvasInvalidated, this, imageRect));
        }

        /// <summary>
        /// Repaints a rectangle region in the window.
        /// Note that this overload uses window coordinates, whereas Invalidate() uses canvas coordinates.
        /// </summary>
        public void InvalidateWindowRect(RectangleI windowRect)
        {
            OnCanvasInvalidated(new CanvasEventItem(document, DocumentEventEnum.CanvasInvalidated, this, windowRect));
        }

        /// <summary>
        /// Determines whether the rectangle lies (at least partially) outside the canvas area.
        /// </summary>
        public bool IsPartiallyOffscreen(RectangleI rect)
        {
            return (rect.IsEmpty || rect.Left < 0 || rect.Top < 0);
        }

        public bool PointInCanvas(PointD point)
        {
            if (point.X < 0 || point.Y < 0)
                return false;

            if (point.X >= document.ImageSize.Width || point.Y >= document.ImageSize.Height)
                return false;

            return true;
        }

        public void RecenterView(double x, double y)
        {
            var view = Canvas.Viewport!;

            var hAdjust = view.GetHadjustment()!;
            hAdjust.Value = Utility.Clamp(x * Scale - hAdjust.PageSize / 2, hAdjust.Lower, hAdjust.Upper);
            var vAdjust = view.GetVadjustment()!;
            vAdjust.Value = Utility.Clamp(y * Scale - vAdjust.PageSize / 2, vAdjust.Lower, vAdjust.Upper);
        }

        public void ScrollCanvas(int dx, int dy)
        {
           var view = Canvas.Viewport!;

            var hAdjust = view.GetHadjustment()!;
            hAdjust.Value = Utility.Clamp(dx + hAdjust.Value, hAdjust.Lower, hAdjust.Upper - hAdjust.PageSize);
            var vAdjust = view.GetVadjustment()!;
            vAdjust.Value = Utility.Clamp(dy + vAdjust.Value, vAdjust.Lower, vAdjust.Upper - vAdjust.PageSize);
        }

        /// <summary>
        /// Converts a point from image view coordinates to canvas coordinates
        /// </summary>
        /// <param name='x'>
        /// The X coordinate of the view point
        /// </param>
        /// <param name='y'>
        /// The Y coordinate of the view point
        /// </param>
        public PointD ViewPointToCanvas(double x, double y)
        {
            var sf = new ScaleFactor(document.ImageSize.Width, ViewSize.Width);
            var pt = sf.ScalePoint(new PointD(x - Offset.X, y - Offset.Y));
            return new PointD(pt.X, pt.Y);
        }

        /// <summary>
        /// Converts a point from image view coordinates to canvas coordinates
        /// </summary>
        public PointD ViewPointToCanvas(in PointD point) => ViewPointToCanvas(point.X, point.Y);

        /// <summary>
        /// Converts a point from canvas coordinates to view coordinates
        /// </summary>
        /// <param name='x'>
        /// The X coordinate of the canvas point
        /// </param>
        /// <param name='y'>
        /// The Y coordinate of the canvas point
        /// </param>
        public PointD CanvasPointToView(double x, double y)
        {
            var sf = new ScaleFactor(document.ImageSize.Width, ViewSize.Width);
            var pt = sf.UnscalePoint(new PointD(x, y));
            return new PointD(pt.X + Offset.X, pt.Y + Offset.Y);
        }

        /// <summary>
        /// Converts a point from canvas coordinates to view coordinates
        /// </summary>
        public PointD CanvasPointToView(in PointD point) => CanvasPointToView(point.X, point.Y);

        public void ZoomIn()
        {
            ZoomAndRecenterView(ZoomType.ZoomIn, center_point: null); // Zoom in relative to the center of the viewport.
        }

        public void ZoomOut()
        {
            ZoomAndRecenterView(ZoomType.ZoomOut, center_point: null); // Zoom out relative to the center of the viewport.
        }

        public void ZoomInAroundViewPoint(in PointD viewPoint)
        {
            ZoomAndRecenterView(ZoomType.ZoomIn, viewPoint); // Zoom in relative to mouse position.
        }

        public void ZoomInAroundCanvasPoint(in PointD canvasPoint)
        {
            ZoomInAroundViewPoint(CanvasPointToView(canvasPoint));
        }

        public void ZoomOutAroundViewPoint(in PointD viewPoint)
        {
            ZoomAndRecenterView(ZoomType.ZoomOut, viewPoint); // Zoom out relative to mouse position.
        }

        public void ZoomOutAroundCanvasPoint(in PointD canvasPoint)
        {
            ZoomOutAroundViewPoint(CanvasPointToView(canvasPoint));
        }

        public void ZoomManually()
        {
            ZoomAndRecenterView(ZoomType.ZoomManually, center_point: null);
        }

        public void ZoomToCanvasRectangle(RectangleD rect)
        {
            double ratio;

            if (document.ImageSize.Width / rect.Width <= document.ImageSize.Height / rect.Height)
                ratio = document.ImageSize.Width / rect.Width;
            else
                ratio = document.ImageSize.Height / rect.Height;

            _logger.LogWarning("ZoomToCanvasRectangle");
            //PintaCore.Actions.View.ZoomComboBox.ComboBox.GetEntry().SetText(ViewActions.ToPercent(ratio));
            //GLib.MainContext.Default().Iteration(false); //Force update of scrollbar upper before recenter
            RecenterView(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        }
        #endregion

        #region Private Methods
        private void OnCanvasInvalidated(CanvasEventItem e)
        {
            _documentEventsService.PushEvent(e);
        }

        private void OnViewSizeChanged()
        {
            _documentEventsService.PushEvent(new CanvasEventItem(document, DocumentEventEnum.ViewSizeChanged, 
                this, _viewSize.ToInt()));
        }

        /// <summary>
        /// Zoom in/out around a specific point.
        /// </summary>
        /// <param name="center_point">Center point to zoom around, in view coordinates</param>
        private void ZoomAndRecenterView(ZoomType zoomType, PointD? center_point)
        {
            throw new NotImplementedException("ZoomAndRecenterView");
/*
            if (zoomType == ZoomType.ZoomOut && (ViewSize.Width == 1 || ViewSize.Height == 1))
                return; //Can't zoom in past a 1x1 px canvas

            double zoom;

            if (!ViewActions.TryParsePercent(PintaCore.Actions.View.ZoomComboBox.ComboBox.GetActiveText()!, out zoom))
                zoom = Scale * 100;

            zoom = Math.Min(zoom, 3600);

            PintaCore.Actions.View.SuspendZoomUpdate();

            Gtk.Viewport view = (Gtk.Viewport)Canvas.Parent!;

            // If no point was specified, zoom relative to the center of the screen.
            if (!center_point.HasValue)
            {
                center_point = new PointD(
                    view.Hadjustment!.Value + (view.Hadjustment.PageSize / 2.0),
                    view.Vadjustment!.Value + (view.Vadjustment.PageSize / 2.0));
            }

            var scroll_offset_x = center_point.Value.X - view.Hadjustment!.Value - Offset.X;
            var scroll_offset_y = center_point.Value.Y - view.Vadjustment!.Value - Offset.Y;

            var canvas_point = ViewPointToCanvas(center_point.Value);

            if (zoomType == ZoomType.ZoomIn || zoomType == ZoomType.ZoomOut)
            {
                int i = 0;

                Predicate<string> UpdateZoomLevel = zoomInList => {
                    double zoom_level;
                    if (!ViewActions.TryParsePercent(zoomInList, out zoom_level))
                        return false;

                    switch (zoomType)
                    {
                        case ZoomType.ZoomIn:
                            if (zoomInList == Translations.GetString("Window") || zoom_level <= zoom)
                            {
                                PintaCore.Actions.View.ZoomComboBox.ComboBox.Active = i - 1;
                                return true;
                            }

                            break;

                        case ZoomType.ZoomOut:
                            if (zoomInList == Translations.GetString("Window"))
                                return true;

                            if (zoom_level < zoom)
                            {
                                PintaCore.Actions.View.ZoomComboBox.ComboBox.Active = i;
                                return true;
                            }

                            break;
                    }

                    return false;
                };

                foreach (string item in PintaCore.Actions.View.ZoomCollection)
                {
                    if (UpdateZoomLevel(item))
                        break;

                    i++;
                }
            }

            PintaCore.Actions.View.UpdateCanvasScale();

            // Quick fix : need to manually update Upper limit because the value is not changing after updating the canvas scale.
            // TODO : I think there is an event need to be fired so that those values updated automatically.
            view.Hadjustment!.Upper = ViewSize.Width < view.Hadjustment.PageSize ? view.Hadjustment.PageSize : ViewSize.Width;
            view.Vadjustment!.Upper = ViewSize.Height < view.Vadjustment.PageSize ? view.Vadjustment.PageSize : ViewSize.Height;

            // Scroll so that the canvas position under 'center_point' is still the same after zooming.
            // Note that the canvas widget might not have resized yet, so using Offset is important for taking
            // the size difference into account.
            var new_center_point = CanvasPointToView(canvas_point);
            view.Hadjustment.Value = new_center_point.X - scroll_offset_x - Offset.X;
            view.Vadjustment.Value = new_center_point.Y - scroll_offset_y - Offset.Y;

            PintaCore.Actions.View.ResumeZoomUpdate();
*/
        }
        #endregion
    }

}

