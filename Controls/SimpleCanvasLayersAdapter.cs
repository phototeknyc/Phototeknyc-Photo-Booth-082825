using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photobooth.Controls
{
    /// <summary>
    /// Adapter to make SimpleDesignerCanvas work with the existing LayersPanel
    /// </summary>
    public class SimpleCanvasLayersAdapter
    {
        private SimpleDesignerCanvas _canvas;
        private LayersPanel _layersPanel;
        private ObservableCollection<LayerItem> _layerItems;

        public SimpleCanvasLayersAdapter(SimpleDesignerCanvas canvas, LayersPanel layersPanel)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _layersPanel = layersPanel ?? throw new ArgumentNullException(nameof(layersPanel));

            _layerItems = new ObservableCollection<LayerItem>();

            // Connect events
            _canvas.ItemAdded += Canvas_ItemAdded;
            _canvas.ItemRemoved += Canvas_ItemRemoved;
            _canvas.ItemSelected += Canvas_ItemSelected;
            _canvas.SelectionCleared += Canvas_SelectionCleared;

            // Initialize layers panel
            InitializeLayersPanel();
        }

        private void InitializeLayersPanel()
        {
            // Set the layer items source
            _layersPanel.Layers.Clear();
            foreach (var item in _layerItems)
            {
                _layersPanel.Layers.Add(item);
            }

            // Connect layer panel events
            _layersPanel.LayerSelectionChanged += LayersPanel_LayerSelectionChanged;
            _layersPanel.LayersReordered += LayersPanel_LayersReordered;
        }

        private void Canvas_ItemAdded(object sender, SimpleCanvasItem item)
        {
            var layerItem = CreateLayerItem(item);
            _layerItems.Add(layerItem);
            _layersPanel.Layers.Add(layerItem);

            // Subscribe to item property changes
            item.PropertyChanged += Item_PropertyChanged;
        }

        private void Canvas_ItemRemoved(object sender, SimpleCanvasItem item)
        {
            var layerItem = _layerItems.FirstOrDefault(l => l.CanvasItem == item);
            if (layerItem != null)
            {
                _layerItems.Remove(layerItem);
                _layersPanel.Layers.Remove(layerItem);

                // Unsubscribe from item property changes
                item.PropertyChanged -= Item_PropertyChanged;
            }
        }

        private void Canvas_ItemSelected(object sender, SimpleCanvasItem item)
        {
            var layerItem = _layerItems.FirstOrDefault(l => l.CanvasItem == item);
            if (layerItem != null)
            {
                // Update layer selection
                foreach (var layer in _layerItems)
                {
                    layer.IsSelected = (layer == layerItem);
                }
            }
        }

        private void Canvas_SelectionCleared(object sender, EventArgs e)
        {
            // Clear all layer selections
            foreach (var layer in _layerItems)
            {
                layer.IsSelected = false;
            }
        }

        private void LayersPanel_LayerSelectionChanged(object sender, LayerItem selectedLayer)
        {
            if (selectedLayer?.CanvasItem is SimpleCanvasItem canvasItem)
            {
                _canvas.SelectItem(canvasItem);
            }
            else
            {
                _canvas.ClearSelection();
            }
        }

        private void LayersPanel_LayersReordered(object sender, EventArgs e)
        {
            // Update Z-indices based on layer order
            UpdateZIndicesFromLayerOrder();
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is SimpleCanvasItem item)
            {
                var layerItem = _layerItems.FirstOrDefault(l => l.CanvasItem == item);
                if (layerItem != null)
                {
                    // Update layer item properties based on canvas item changes
                    switch (e.PropertyName)
                    {
                        case "ZIndex":
                            UpdateLayerOrderFromZIndex();
                            break;
                        case "Text":
                        case "ImageSource":
                            UpdateLayerDisplayName(layerItem);
                            break;
                    }
                }
            }
        }

        private LayerItem CreateLayerItem(SimpleCanvasItem canvasItem)
        {
            var layerItem = new LayerItem
            {
                CanvasItem = canvasItem,
                Name = canvasItem.GetDisplayName(),
                IsVisible = true,
                Opacity = 1.0,
                BlendMode = "Normal",
                ZIndex = canvasItem.ZIndex
            };

            // Set appropriate icon based on item type
            if (canvasItem is SimpleTextItem)
            {
                layerItem.Icon = CreateTextIcon();
                layerItem.ItemType = "Text";
            }
            else if (canvasItem is SimpleImageItem imageItem)
            {
                layerItem.Icon = CreateImageIcon(imageItem);
                layerItem.ItemType = imageItem.IsPlaceholder ? "Placeholder" : "Image";
            }
            else
            {
                layerItem.Icon = CreateGenericIcon();
                layerItem.ItemType = "Item";
            }

            return layerItem;
        }

        private ImageSource CreateTextIcon()
        {
            // Create a simple text icon (using a system icon or creating a simple one)
            // For now, return null and let the LayersPanel handle the default
            return null;
        }

        private ImageSource CreateImageIcon(SimpleImageItem imageItem)
        {
            if (imageItem.IsPlaceholder)
            {
                // Return placeholder icon
                return null;
            }
            else if (imageItem.ImageSource != null)
            {
                // Create thumbnail from the image
                try
                {
                    if (imageItem.ImageSource is BitmapSource bitmap)
                    {
                        // Create a small thumbnail
                        var thumbnail = new TransformedBitmap(bitmap,
                            new ScaleTransform(32.0 / bitmap.PixelWidth, 32.0 / bitmap.PixelHeight));
                        return thumbnail;
                    }
                }
                catch
                {
                    // Fall back to default if thumbnail creation fails
                }
            }
            return null;
        }

        private ImageSource CreateGenericIcon()
        {
            return null;
        }

        private void UpdateLayerDisplayName(LayerItem layerItem)
        {
            if (layerItem.CanvasItem != null)
            {
                layerItem.Name = layerItem.CanvasItem.GetDisplayName();
            }
        }

        private void UpdateZIndicesFromLayerOrder()
        {
            // Update canvas item Z-indices to match layer panel order
            for (int i = 0; i < _layersPanel.Layers.Count; i++)
            {
                var layer = _layersPanel.Layers[i];
                if (layer.CanvasItem is SimpleCanvasItem canvasItem)
                {
                    canvasItem.ZIndex = i;
                }
            }
        }

        private void UpdateLayerOrderFromZIndex()
        {
            // Sort layers based on Z-index
            var sortedLayers = _layerItems.OrderBy(l => l.CanvasItem?.ZIndex ?? 0).ToList();

            _layersPanel.Layers.Clear();
            foreach (var layer in sortedLayers)
            {
                _layersPanel.Layers.Add(layer);
            }
        }

        public void RefreshLayers()
        {
            // Clear and rebuild layer list
            _layerItems.Clear();
            _layersPanel.Layers.Clear();

            foreach (var item in _canvas.GetItemsInZOrder())
            {
                var layerItem = CreateLayerItem(item);
                _layerItems.Add(layerItem);
                _layersPanel.Layers.Add(layerItem);
            }
        }

        public void Dispose()
        {
            // Unsubscribe from events
            if (_canvas != null)
            {
                _canvas.ItemAdded -= Canvas_ItemAdded;
                _canvas.ItemRemoved -= Canvas_ItemRemoved;
                _canvas.ItemSelected -= Canvas_ItemSelected;
                _canvas.SelectionCleared -= Canvas_SelectionCleared;
            }

            if (_layersPanel != null)
            {
                _layersPanel.LayerSelectionChanged -= LayersPanel_LayerSelectionChanged;
                _layersPanel.LayersReordered -= LayersPanel_LayersReordered;
            }

            // Unsubscribe from all item property changes
            foreach (var item in _canvas.Items)
            {
                item.PropertyChanged -= Item_PropertyChanged;
            }
        }
    }

    /// <summary>
    /// Layer item class that the LayersPanel expects
    /// </summary>
    public class LayerItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        private bool _isVisible;
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                _isVisible = value;
                OnPropertyChanged(nameof(IsVisible));
            }
        }

        private double _opacity;
        public double Opacity
        {
            get => _opacity;
            set
            {
                _opacity = value;
                OnPropertyChanged(nameof(Opacity));
            }
        }

        private string _blendMode;
        public string BlendMode
        {
            get => _blendMode;
            set
            {
                _blendMode = value;
                OnPropertyChanged(nameof(BlendMode));
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        private int _zIndex;
        public int ZIndex
        {
            get => _zIndex;
            set
            {
                _zIndex = value;
                OnPropertyChanged(nameof(ZIndex));
            }
        }

        public SimpleCanvasItem CanvasItem { get; set; }
        public ImageSource Icon { get; set; }
        public string ItemType { get; set; }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}