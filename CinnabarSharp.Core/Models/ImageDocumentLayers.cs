using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using CinnabarSharp.Core.Services;
using Microsoft.Extensions.Logging;
using System.Reflection;
using ImageMagick;
using CinnabarSharp.Core.Extensions;

namespace CinnabarSharp.Core.Models
{
    public class ImageDocumentLayers
    {
        private readonly ImageDocument document;
        private readonly IDocumentEventsService _documentEventsService;
        private readonly ILogger<ImageDocument> logger;
        private readonly List<UserLayer> user_layers = new();

        private int layer_name_int = 2;

        // The layer for tools to use until their output is committed
        private Layer? tool_layer;

        // The layer used for selections
        private Layer? selection_layer;

        public ImageDocumentLayers(ImageDocument document,
            IDocumentEventsService documentEventsService,
            ILogger<ImageDocument> logger)
        {
            this.document = document;
            this._documentEventsService = documentEventsService;
            this.logger = logger;
        }

        //public event EventHandler<IndexEventArgs>? LayerAdded;
        //public event EventHandler<IndexEventArgs>? LayerRemoved;
        //public event EventHandler? SelectedLayerChanged;
        //public event PropertyChangedEventHandler? LayerPropertyChanged;

        /// <summary>
        /// Gets the currently selected user created layer.
        /// </summary>
        public UserLayer CurrentUserLayer => user_layers[CurrentUserLayerIndex];

        /// <summary>
        /// Gets the index of the currently selected user created layer.
        /// </summary>
        public int CurrentUserLayerIndex { get; private set; } = -1;

        /// <summary>
        /// Gets the layer used for drawing and managing selections.
        /// </summary>
        public Layer SelectionLayer
        {
            get
            {
                if (selection_layer is null)
                    CreateSelectionLayer();

                return selection_layer;
            }
        }

        /// <summary>
        /// Gets or sets whether the Selection layer should be shown.
        /// </summary>
        public bool ShowSelectionLayer { get; set; }

        /// <summary>
        /// Gets a scratch layer for tools to temporarily use until their content
        /// is committed to the actual layer.
        /// </summary>
        public Layer ToolLayer
        {
            get
            {
                if (tool_layer is null || tool_layer.Surface.Width != document.ImageSize.Width ||
                    tool_layer.Surface.Height != document.ImageSize.Height)
                {
                    tool_layer = CreateLayer("Tool Layer");
                    tool_layer.Hidden = true;
                }

                return tool_layer;
            }
        }

        /// <summary>
        /// Collection of user layers.
        /// </summary>
        public IReadOnlyList<UserLayer> UserLayers => user_layers;

        /// <summary>
        /// Creates a new layer and adds it to the Layer collection after the
        /// currently selected layer.
        /// </summary>
        public UserLayer AddNewLayer(string name)
        {
            UserLayer layer;

            if (string.IsNullOrEmpty(name))
                layer = CreateLayer();
            else
                layer = CreateLayer(name);

            user_layers.Insert(CurrentUserLayerIndex + 1, layer);

            if (user_layers.Count == 1)
                CurrentUserLayerIndex = 0;

            layer.PropertyChanged += RaiseLayerPropertyChangedEvent;

            _documentEventsService.PushEvent(new LayerEventItem(document, DocumentEventEnum.LayerAdded, layer,
                user_layers.Count - 1));

            //LayerAdded?.Invoke(this, new IndexEventArgs(user_layers.Count - 1));
            //PintaCore.Layers.OnLayerAdded();
            return layer;
        }


        /// <summary>
        /// Disposes all user created and internal layers.
        /// </summary>
        internal void Close()
        {
            foreach (var layer in user_layers)
                layer.Surface.Dispose();
            tool_layer?.Surface.Dispose();
            selection_layer?.Surface.Dispose();
            user_layers.Clear();
            CurrentUserLayerIndex = -1;

            tool_layer = null;
            selection_layer = null;
        }

        /// <summary>
        /// Returns the number of user layers.
        /// </summary>
        public int Count() => user_layers.Count;

        /// <summary>
        /// Creates a new layer, but does not add it to the layer collection.
        /// </summary>
        public UserLayer CreateLayer(string? name = null, int? width = null, int? height = null)
        {
            // Translators: {0} is a unique id for new layers, e.g. "Layer 2".
            name ??= Translations.GetString("Layer {0}", layer_name_int++);
            width ??= document.ImageSize.Width;
            height ??= document.ImageSize.Height;

            IImageBuf surface = Utility.CreateImage(width.Value, height.Value);
            var layer = new UserLayer(surface) { Name = name };

            return layer;
        }

        /// <summary>
        /// Creates a new SelectionLayer.
        /// </summary>
        [MemberNotNull(nameof(selection_layer))]
        public void CreateSelectionLayer()
        {
            selection_layer = CreateLayer();
        }

        /// <summary>
        /// Creates a new SelectionLayer with the specified dimensions.
        /// </summary>
        [MemberNotNull(nameof(selection_layer))]
        public void CreateSelectionLayer(int width, int height)
        {
            selection_layer = CreateLayer(null, width, height);
        }

        /// <summary>
        /// Deletes the current layer. The last remaining layer can't be deleted.
        /// </summary>
        public void DeleteCurrentLayer() => DeleteLayer(CurrentUserLayerIndex);

        /// <summary>
        /// Removes the user layer at the specified index; the layer below (or the new bottom layer) becomes current.
        /// </summary>
        public void DeleteLayer(int index)
        {
            if (user_layers.Count <= 1)
                throw new InvalidOperationException("Cannot delete the only layer.");

            var layer = user_layers[index];
            user_layers.RemoveAt(index);
            if (CurrentUserLayerIndex > index || CurrentUserLayerIndex >= user_layers.Count)
                CurrentUserLayerIndex--;
            else if (CurrentUserLayerIndex == index && index > 0)
                CurrentUserLayerIndex = index - 1;

            layer.PropertyChanged -= RaiseLayerPropertyChangedEvent;

            _documentEventsService.PushEvent(new LayerEventItem(document, DocumentEventEnum.LayerRemoved, layer, index));
            document.Workspace.Invalidate();
        }

        /// <summary>
        /// Hide and reset the SelectionLayer.
        /// </summary>
        public void DestroySelectionLayer()
        {
            ShowSelectionLayer = false;
            SelectionLayer.Clear();
            //SelectionLayer.Transform.InitIdentity();
        }

        /// <summary>
        /// Duplicates the current layer above it and makes the copy current.
        /// </summary>
        public UserLayer DuplicateCurrentLayer()
        {
            var source = CurrentUserLayer;
            // Translators: {0} is the name of the source layer. Example: "Layer 3 copy".
            var layer = new UserLayer(source.Surface.Clone())
            {
                Name = Translations.GetString("{0} copy", source.Name),
                Hidden = source.Hidden,
                Opacity = source.Opacity,
                BlendMode = source.BlendMode,
                Tiled = source.Tiled,
            };

            Insert(layer, CurrentUserLayerIndex + 1);
            SetCurrentUserLayer(layer);
            document.Workspace.Invalidate();
            return layer;
        }

        /// <summary>
        /// Adds the image file as a new layer above the current one, placed at the top-left and cropped to the canvas.
        /// </summary>
        public UserLayer ImportFromFile(ImageFile file)
        {
            using var image = Utility.OpenImage(file);
            image.AutoOrient();
            var canvas = Utility.CreateImage(document.ImageSize.Width, document.ImageSize.Height);
            canvas.Composite(image, 0, 0, CompositeOperator.Copy);

            var layer = CreateLayer(file.GetDisplayName());
            layer.Surface.Dispose();
            layer.Surface = canvas;
            Insert(layer, CurrentUserLayerIndex + 1);
            SetCurrentUserLayer(layer);
            document.Workspace.Invalidate();
            return layer;
        }

        public void FlipCurrentLayerHorizontal()
        {
            CurrentUserLayer.FlipHorizontal();
            document.Workspace.Invalidate();
        }

        public void FlipCurrentLayerVertical()
        {
            CurrentUserLayer.FlipVertical();
            document.Workspace.Invalidate();
        }

        /// <summary>
        /// Flattens all user layers into the bottom layer, applying opacity and blend modes.
        /// </summary>
        public void FlattenLayers()
        {
            if (user_layers.Count < 2)
                throw new InvalidOperationException("Cannot flatten image because there is only one layer.");

            var bottom_layer = user_layers[0];
            var flattened = GetFlattenedImage();
            bottom_layer.Surface.Dispose();
            bottom_layer.Surface = flattened;
            bottom_layer.Hidden = false;
            bottom_layer.Opacity = 1;
            bottom_layer.BlendMode = BlendMode.Normal;

            CurrentUserLayerIndex = 0;
            while (user_layers.Count > 1)
                DeleteLayer(user_layers.Count - 1);

            document.Workspace.Invalidate();
        }

        /// <summary>
        /// Gets a copy of the specified layer, clipped to the current selection.
        /// </summary>
        public IMagickImage<byte> GetClippedLayer(int index)
        {
            throw new NotImplementedException("GetClippedLayer");
            /*
            var surf = CairoExtensions.CreateImageSurface(Format.Argb32, document.ImageSize.Width, document.ImageSize.Height);

            var g = new Context(surf);
            g.AppendPath(document.Selection.SelectionPath);
            g.Clip();

            g.SetSourceSurface(user_layers[index].Surface, 0, 0);
            g.Paint();

            return surf;
            */
        }

        /// <summary>
        /// Returns all visible layers composited (opacity + blend mode) onto a transparent image.
        /// </summary>
        internal IImageBuf GetFlattenedImage(bool clip_to_selection = false)
        {
            if (clip_to_selection)
                throw new NotImplementedException("GetClippedLayer");

            return Utility.FromBgra(GetFlattenedBgra(includeToolLayer: false),
                document.ImageSize.Width, document.ImageSize.Height);
        }

        /// <summary>
        /// Straight-alpha BGRA pixels of all visible layers composited, image-sized.
        /// </summary>
        public byte[] GetFlattenedBgra(bool includeToolLayer = true)
        {
            var result = new byte[document.ImageSize.Width * document.ImageSize.Height * 4];
            foreach (var layer in GetLayersToPaint(includeToolLayer))
            {
                if (layer.Opacity <= 0)
                    continue;
                var pixels = layer.Surface.ToBgra();
                if (pixels.Length != result.Length)
                    throw new InvalidOperationException($"Layer '{layer.Name}' is not the size of the image.");
                BlendOps.Composite(result, pixels, layer.BlendMode, layer.Opacity);
            }
            return result;
        }

        /// <summary>
        /// Returns all layers that are visible and need to be painted, optionally
        /// including tool and selection layers.
        /// </summary>
        public List<Layer> GetLayersToPaint(bool includeToolLayer = true)
        {
            var paint_layers = new List<Layer>();

            foreach (var layer in user_layers)
            {
                if (!layer.Hidden)
                    paint_layers.Add(layer);

                if (layer == CurrentUserLayer)
                {
                    if (includeToolLayer && tool_layer is not null && !ToolLayer.Hidden)
                        paint_layers.Add(ToolLayer);

                    if (ShowSelectionLayer && (!SelectionLayer.Hidden))
                        paint_layers.Add(SelectionLayer);
                }

                if (!layer.Hidden)
                {
                    foreach (var rel in layer.ReEditableLayers)
                    {
                        //Make sure that each UserLayer's ReEditableLayer is in use before adding it to the List of Layers to Paint.
                        if (rel.IsLayerSetup)
                            paint_layers.Add(rel.Layer);
                    }
                }
            }

            return paint_layers;
        }

        /// <summary>
        /// Returns the index of the specified user layer.
        /// </summary>
        public int IndexOf(UserLayer layer)
        {
            return user_layers.IndexOf(layer);
        }

        /// <summary>
        /// Adds the provided layer at the requested index of the layer collection.
        /// </summary>
        public void Insert(UserLayer layer, int index)
        {
            user_layers.Insert(index, layer);

            if (user_layers.Count == 1)
                CurrentUserLayerIndex = 0;

            layer.PropertyChanged += RaiseLayerPropertyChangedEvent;

            _documentEventsService.PushEvent(new LayerEventItem(document, DocumentEventEnum.LayerAdded, layer, index));
            //LayerAdded?.Invoke(this, new IndexEventArgs(index));
            //PintaCore.Layers.OnLayerAdded();
        }

        /// <summary>
        /// Merges the current layer into the one below it, using its opacity and blend mode.
        /// </summary>
        public void MergeCurrentLayerDown()
        {
            if (CurrentUserLayerIndex <= 0)
                throw new InvalidOperationException("Cannot merge down because current layer is the bottom layer.");

            var source = CurrentUserLayer;
            var dest = user_layers[CurrentUserLayerIndex - 1];

            if (!source.Hidden)
            {
                var pixels = dest.Surface.ToBgra();
                BlendOps.Composite(pixels, source.Surface.ToBgra(), source.BlendMode, source.Opacity);
                var merged = Utility.FromBgra(pixels, (int)dest.Surface.Width, (int)dest.Surface.Height);
                dest.Surface.Dispose();
                dest.Surface = merged;
            }

            DeleteCurrentLayer();
            SetCurrentUserLayer(dest);
        }

        /// <summary>
        /// Moves the current layer down 1 position in the layer collection.
        /// </summary>
        public void MoveCurrentLayerDown()
        {
            if (CurrentUserLayerIndex <= 0)
                throw new InvalidOperationException("Cannot move layer down because current layer is the bottom layer.");
            SwapCurrentWith(CurrentUserLayerIndex - 1);
        }

        /// <summary>
        /// Moves the current layer up 1 position in the layer collection.
        /// </summary>
        public void MoveCurrentLayerUp()
        {
            if (CurrentUserLayerIndex >= user_layers.Count - 1)
                throw new InvalidOperationException("Cannot move layer up because current layer is the top layer.");
            SwapCurrentWith(CurrentUserLayerIndex + 1);
        }

        private void SwapCurrentWith(int index)
        {
            var layer = CurrentUserLayer;
            (user_layers[CurrentUserLayerIndex], user_layers[index]) = (user_layers[index], layer);
            CurrentUserLayerIndex = index;

            _documentEventsService.PushEvent(new LayerEventItem(document, DocumentEventEnum.SelectedLayerChanged, layer, index));
            document.Workspace.Invalidate();
        }

        /// <summary>
        /// Set the current user layer to the index specified.
        /// </summary>
        public void SetCurrentUserLayer(int i)
        {
            // Ensure that the current tool's modifications are finalized before
            // switching layers.
            //PintaCore.Tools.CurrentTool?.DoCommit(document);

            CurrentUserLayerIndex = i;

            _documentEventsService.PushEvent(new LayerEventItem(document, DocumentEventEnum.SelectedLayerChanged,
                CurrentUserLayer, i));
            //SelectedLayerChanged?.Invoke(this, EventArgs.Empty);
            //PintaCore.Layers.OnSelectedLayerChanged();
        }

        /// <summary>
        /// Set the current user layer to the layer specified.
        /// </summary>
        public void SetCurrentUserLayer(UserLayer layer)
        {
            SetCurrentUserLayer(user_layers.IndexOf(layer));
        }

        /// <summary>
        /// Gets the user layer at the specified index.
        /// </summary>
        public UserLayer this[int index] => user_layers[index];

        private void RaiseLayerPropertyChangedEvent(object? sender, PropertyChangedEventArgs e)
        {
            var layer = (sender as UserLayer)!;
            var index = user_layers.IndexOf(layer);
            _documentEventsService.PushEvent(new LayerEventItem(document, DocumentEventEnum.LayerPropertyChanged, layer,
                index));
            //LayerPropertyChanged?.Invoke(sender, e);
            //PintaCore.Layers.RaiseLayerPropertyChangedEvent(sender, e);
        }
    }
}