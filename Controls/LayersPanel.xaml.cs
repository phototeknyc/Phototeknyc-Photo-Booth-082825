using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesignerCanvas;
using DesignerCanvas.Controls;

namespace Photobooth.Controls
{
    public partial class LayersPanel : UserControl
    {
        public ObservableCollection<LayerItem> Layers { get; set; }
        private TouchEnabledCanvas _designerCanvas;
        private SimpleDesignerCanvas _simpleCanvas;
        private Point _dragStartPoint;
        private bool _isDragging;
        private int _lastSelectedIndex = -1;

        public event EventHandler<LayerItem> LayerSelectionChanged;
        public event EventHandler LayersReordered;

        public LayersPanel()
        {
            InitializeComponent();
            Layers = new ObservableCollection<LayerItem>();
            LayersList.ItemsSource = Layers;

            // Opacity and Blend Mode controls removed from UI per request
            UpdateMergeDownEnabled();
        }

        public void SetDesignerCanvas(TouchEnabledCanvas canvas)
        {
            _designerCanvas = canvas;
            if (_designerCanvas != null)
            {
                _designerCanvas.SelectionChanged += Canvas_SelectionChanged;
                RefreshLayers();
            }
        }

        public void SetSimpleDesignerCanvas(SimpleDesignerCanvas canvas)
        {
            _simpleCanvas = canvas;
            if (_simpleCanvas != null)
            {
                _simpleCanvas.SelectionChanged += SimpleCanvas_SelectionChanged;
                _simpleCanvas.ItemAdded += (s, e) => RefreshLayers();
                _simpleCanvas.ItemRemoved += (s, e) => RefreshLayers();
                RefreshLayers();
            }
        }

        private void SimpleCanvas_SelectionChanged(object sender, EventArgs e)
        {
            if (_simpleCanvas == null) return;
            // Sync multi-selection from canvas to panel
            foreach (var l in Layers) l.IsSelected = false;
            foreach (var si in _simpleCanvas.SelectedItems)
            {
                var layer = Layers.FirstOrDefault(l => l.SimpleCanvasItem == si);
                if (layer != null) layer.IsSelected = true;
            }
            // Track last selected index for shift behavior
            var last = _simpleCanvas.SelectedItems.LastOrDefault();
            if (last != null)
            {
                _lastSelectedIndex = Layers.IndexOf(Layers.FirstOrDefault(l => l.SimpleCanvasItem == last));
            }
            LayerSelectionChanged?.Invoke(this, null);
            UpdateMergeDownEnabled();
        }

        private void Canvas_SelectionChanged(object sender, EventArgs e)
        {
            if (_designerCanvas == null) return;

            var selectedItem = _designerCanvas.SelectedItems.FirstOrDefault();
            if (selectedItem != null)
            {
                var layer = Layers.FirstOrDefault(l => l.CanvasItem == selectedItem);
                if (layer != null)
                {
                    SelectLayer(layer);
                }
            }
        }

        public void RefreshLayers()
        {
            Layers.Clear();

            // Handle SimpleDesignerCanvas
            if (_simpleCanvas != null)
            {
                // Show topmost (highest ZIndex) first in the panel
                var items = _simpleCanvas.Items.OrderByDescending(i => i.ZIndex).ToList();
                int index = 1;

                foreach (var item in items)
                {
                    var layer = new LayerItem
                    {
                        SimpleCanvasItem = item,
                        Name = GetSimpleItemName(item, index),
                        Type = GetSimpleItemType(item),
                        IsVisible = item.Visibility == Visibility.Visible,
                        IsLocked = false,
                        IsSelected = _simpleCanvas.SelectedItem == item,
                        Thumbnail = GenerateSimpleThumbnail(item) as BitmapSource
                    };

                    Layers.Add(layer);
                    index++;
                }
            }
            // Handle original TouchEnabledCanvas
            else if (_designerCanvas != null)
            {
                var items = _designerCanvas.Items.Cast<ICanvasItem>().Reverse();
                int index = 1;

                foreach (var item in items)
                {
                    var layer = new LayerItem
                    {
                        CanvasItem = item,
                        Name = GetItemName(item, index),
                        Type = GetItemType(item),
                        IsVisible = GetItemVisibility(item),
                        IsLocked = false,
                        IsSelected = _designerCanvas.SelectedItems.Contains(item),
                        Thumbnail = GenerateThumbnail(item)
                    };

                    Layers.Add(layer);
                    index++;
                }
            }

            // Update merge/controls state after rebuilding the list
            UpdateMergeDownEnabled();
        }

        private string GetSimpleItemName(SimpleCanvasItem item, int index)
        {
            if (item is SimpleImageItem imageItem)
            {
                if (imageItem.IsPlaceholder)
                    return $"Photo {imageItem.PlaceholderNumber}";
                return $"Image {index}";
            }
            if (item is SimpleTextItem)
                return $"Text {index}";

            if (item is SimpleQRCodeItem)
                return $"QR Code {index}";

            if (item is SimpleBarcodeItem)
                return $"Barcode {index}";

            return $"Item {index}";
        }

        private string GetSimpleItemType(SimpleCanvasItem item)
        {
            if (item is SimpleImageItem imageItem)
            {
                if (imageItem.IsPlaceholder)
                    return "Photo";
                return "Image";
            }
            if (item is SimpleTextItem)
                return "Text";

            if (item is SimpleQRCodeItem)
                return "QR Code";

            if (item is SimpleBarcodeItem)
                return "Barcode";

            return "Item";
        }

        private ImageSource GenerateSimpleThumbnail(SimpleCanvasItem item)
        {
            try
            {
                if (item == null)
                    return null;

                const int thumbW = 40;
                const int thumbH = 40;

                // Render a 40x40 thumbnail preserving aspect ratio (letterboxed)
                var renderBitmap = new RenderTargetBitmap(thumbW, thumbH, 96, 96, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen())
                {
                    // Draw background (letterbox bars) - matches panel tile background
                    context.DrawRectangle(Brushes.White, null, new Rect(0, 0, thumbW, thumbH));

                    var brush = new VisualBrush(item)
                    {
                        Stretch = Stretch.Uniform,
                        AlignmentX = AlignmentX.Center,
                        AlignmentY = AlignmentY.Center
                    };

                    // Draw into full thumbnail rect; Uniform keeps aspect ratio with letterboxing
                    context.DrawRectangle(brush, null, new Rect(0, 0, thumbW, thumbH));
                }
                renderBitmap.Render(visual);
                return renderBitmap;
            }
            catch
            {
                return null;
            }
        }

        private string GetItemName(ICanvasItem item, int index)
        {
            if (item is FrameworkElement fe && fe.Tag != null && fe.Tag is string name && !string.IsNullOrEmpty(name))
                return name;

            string type = GetItemType(item);
            return $"{type} {index}";
        }

        private string GetItemType(ICanvasItem item)
        {
            string typeName = item.GetType().Name;
            if (typeName.Contains("PlaceholderCanvasItem")) return "Photo";
            if (typeName.Contains("TextCanvasItem")) return "Text";
            if (typeName.Contains("ImageCanvasItem")) return "Image";
            if (typeName.Contains("ShapeCanvasItem")) return "Shape";
            return "Layer";
        }

        private bool GetItemVisibility(ICanvasItem item)
        {
            if (item is FrameworkElement fe)
                return fe.Visibility == Visibility.Visible;
            return true;
        }

        private BitmapSource GenerateThumbnail(ICanvasItem element)
        {
            try
            {
                if (element is Visual visualElement)
                {
                    var renderBitmap = new RenderTargetBitmap(40, 40, 96, 96, PixelFormats.Pbgra32);

                    var visual = new DrawingVisual();
                    using (var context = visual.RenderOpen())
                    {
                        var brush = new VisualBrush(visualElement);
                        context.DrawRectangle(brush, null, new Rect(0, 0, 40, 40));
                    }

                    renderBitmap.Render(visual);
                    return renderBitmap;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private void SelectLayer(LayerItem layer)
        {
            foreach (var l in Layers)
                l.IsSelected = false;

            layer.IsSelected = true;

            if (_designerCanvas != null && layer.CanvasItem != null)
            {
                _designerCanvas.SelectedItems.Clear();
                _designerCanvas.SelectedItems.Add(layer.CanvasItem);
            }

            UpdatePropertiesForLayer(layer);
            LayerSelectionChanged?.Invoke(this, layer);
            UpdateMergeDownEnabled();
        }

        private void UpdatePropertiesForLayer(LayerItem layer)
        {
            if (layer == null) return;

            // No per-layer opacity UI; nothing to sync here
            }

        private void ToggleVisibility_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as ToggleButton;
            var layer = (button?.DataContext as LayerItem);
            if (layer != null)
            {
                // Handle SimpleCanvasItem
                if (layer.SimpleCanvasItem != null)
                {
                    layer.SimpleCanvasItem.Visibility = layer.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                }
                // Handle CanvasItem
                else if (layer.CanvasItem != null && layer.CanvasItem is FrameworkElement fe)
                {
                    fe.Visibility = layer.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void ToggleLock_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as ToggleButton;
            var layer = (button?.DataContext as LayerItem);
            if (layer != null)
            {
                // Handle SimpleCanvasItem
                if (layer.SimpleCanvasItem != null)
                {
                    layer.SimpleCanvasItem.IsEnabled = !layer.IsLocked;
                }
                // Handle CanvasItem
                else if (layer.CanvasItem != null && layer.CanvasItem is FrameworkElement fe)
                {
                    fe.IsEnabled = !layer.IsLocked;
                }
            }
        }

        // Opacity and Blend Mode handlers removed with UI

        private void AddLayer_Click(object sender, RoutedEventArgs e)
        {
            // This would open a dialog to choose layer type
            // For now, we'll add a placeholder
            if (_designerCanvas != null)
            {
                var placeholder = new PlaceholderCanvasItem
                {
                    Width = 200,
                    Height = 200,
                    Left = 100,
                    Top = 100
                };
                _designerCanvas.Items.Add(placeholder);
                RefreshLayers();
            }
        }

        private void DuplicateLayer_Click(object sender, RoutedEventArgs e)
        {
            var selectedLayer = Layers.FirstOrDefault(l => l.IsSelected);
            if (selectedLayer != null)
            {
                // Handle SimpleDesignerCanvas
                if (_simpleCanvas != null && selectedLayer.SimpleCanvasItem != null)
                {
                    var original = selectedLayer.SimpleCanvasItem;
                    var duplicate = original.Clone();
                    if (duplicate != null)
                    {
                        // Offset the duplicate so it's visible
                        duplicate.Left = original.Left + 20;
                        duplicate.Top = original.Top + 20;

                        _simpleCanvas.Items.Add(duplicate);
                        RefreshLayers();

                        // Select the new duplicate
                        _simpleCanvas.SetSelectedItem(duplicate);
                    }
                }
                // Handle original TouchEnabledCanvas
                else if (_designerCanvas != null && selectedLayer.CanvasItem != null)
                {
                    var original = selectedLayer.CanvasItem;
                    if (original is ICanvasItem canvasItem && canvasItem.Clone() is ICanvasItem duplicate)
                    {
                        if (duplicate is IBoxCanvasItem boxItem && original is IBoxCanvasItem originalBox)
                        {
                            boxItem.Left = originalBox.Left + 20;
                            boxItem.Top = originalBox.Top + 20;
                        }

                        _designerCanvas.Items.Add(duplicate);
                        RefreshLayers();
                    }
                }
            }
        }

        private void MergeDown_Click(object sender, RoutedEventArgs e)
        {
            // Merge selected layer down (SimpleDesignerCanvas only)
            try
            {
                if (_simpleCanvas == null)
                {
                    // Not supported for legacy DesignerCanvas path
                    return;
                }

                var selectedLayer = Layers.FirstOrDefault(l => l.IsSelected);
                if (selectedLayer == null) return;

                int index = Layers.IndexOf(selectedLayer);
                if (index < 0 || index >= Layers.Count - 1) return; // nothing below to merge into

                var belowLayer = Layers[index + 1];
                var topItem = selectedLayer.SimpleCanvasItem;
                var bottomItem = belowLayer.SimpleCanvasItem;
                if (topItem == null || bottomItem == null) return;

                // Do not merge placeholders (photo slots)
                if ((topItem is SimpleImageItem timg && timg.IsPlaceholder) ||
                    (bottomItem is SimpleImageItem bimg && bimg.IsPlaceholder))
                {
                    return;
                }

                // Prepare region to render: union of both items (inflate to be safe)
                Rect r1 = new Rect(topItem.Left, topItem.Top, Math.Max(1, topItem.Width), Math.Max(1, topItem.Height));
                Rect r2 = new Rect(bottomItem.Left, bottomItem.Top, Math.Max(1, bottomItem.Width), Math.Max(1, bottomItem.Height));
                Rect union = Rect.Union(r1, r2);
                union.Inflate(2, 2);

                int pixelW = Math.Max(1, (int)Math.Ceiling(union.Width));
                int pixelH = Math.Max(1, (int)Math.Ceiling(union.Height));

                // Temporarily hide selection handles for clean render
                var previouslySelected = _simpleCanvas.SelectedItems.ToList();
                foreach (var it in previouslySelected)
                {
                    it.IsSelected = false;
                }

                // Render the canvas region to a bitmap (preserve transparency)
                var rtb = new RenderTargetBitmap(pixelW, pixelH, 96, 96, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    var vb = new VisualBrush(_simpleCanvas)
                    {
                        Viewbox = union,
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Stretch = Stretch.Fill,
                        AlignmentX = AlignmentX.Center,
                        AlignmentY = AlignmentY.Center
                    };
                    dc.DrawRectangle(vb, null, new Rect(0, 0, pixelW, pixelH));
                }
                rtb.Render(dv);

                // Create a new image layer with the merged content
                var merged = new SimpleImageItem
                {
                    Left = union.Left,
                    Top = union.Top,
                    Width = union.Width,
                    Height = union.Height,
                    Stretch = Stretch.Fill,
                    ImageSource = rtb,
                    ZIndex = bottomItem.ZIndex // keep position of the lower layer
                };

                // Apply changes with undo support
                _simpleCanvas.PushUndo();
                _simpleCanvas.Items.Remove(topItem);
                _simpleCanvas.Items.Remove(bottomItem);
                _simpleCanvas.Items.Add(merged);

                // Select new merged layer and refresh list
                _simpleCanvas.SetSelectedItem(merged);
                RefreshLayers();
            }
            catch
            {
                // Swallow errors for now; could log if logging available here
            }
        }

        private void DeleteLayer_Click(object sender, RoutedEventArgs e)
        {
            var selectedLayer = Layers.FirstOrDefault(l => l.IsSelected);
            if (selectedLayer != null)
            {
                // Handle SimpleDesignerCanvas
                if (_simpleCanvas != null && selectedLayer.SimpleCanvasItem != null)
                {
                    _simpleCanvas.Items.Remove(selectedLayer.SimpleCanvasItem);
                    RefreshLayers();
                }
                // Handle original TouchEnabledCanvas
                else if (_designerCanvas != null && selectedLayer.CanvasItem != null)
                {
                    _designerCanvas.Items.Remove(selectedLayer.CanvasItem);
                    RefreshLayers();
                }
            }
        }

        private void MoveLayerUp_Click(object sender, RoutedEventArgs e)
        {
            var selectedLayer = Layers.FirstOrDefault(l => l.IsSelected);
            if (selectedLayer != null)
            {
                int index = Layers.IndexOf(selectedLayer);
                if (index > 0)
                {
                    Layers.Move(index, index - 1);
                    ReorderCanvasItems();
                }
            }
        }

        private void MoveLayerDown_Click(object sender, RoutedEventArgs e)
        {
            var selectedLayer = Layers.FirstOrDefault(l => l.IsSelected);
            if (selectedLayer != null)
            {
                int index = Layers.IndexOf(selectedLayer);
                if (index < Layers.Count - 1)
                {
                    Layers.Move(index, index + 1);
                    ReorderCanvasItems();
                }
            }
        }

        private void ReorderCanvasItems()
        {
            // Handle SimpleDesignerCanvas
            if (_simpleCanvas != null)
            {
                // Layers list is ordered top-to-bottom; assign highest Z to first
                var orderedItems = Layers
                    .Select(l => l.SimpleCanvasItem)
                    .Where(i => i != null)
                    .ToList();

                int count = orderedItems.Count;
                for (int i = 0; i < count; i++)
                {
                    var item = orderedItems[i];
                    // Highest ZIndex for the first (topmost) entry
                    item.ZIndex = (count - 1) - i;
                }
            }
            // Handle original TouchEnabledCanvas
            else if (_designerCanvas != null)
            {
                var items = Layers.Select(l => l.CanvasItem).Where(i => i != null).Reverse().ToList();
                _designerCanvas.Items.Clear();

                foreach (var item in items)
                {
                    _designerCanvas.Items.Add(item);
                }
            }

            LayersReordered?.Invoke(this, EventArgs.Empty);
            UpdateMergeDownEnabled();
        }

        // Drag and drop for reordering
        private void Layer_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            var border = sender as Border;
            var layer = border?.Tag as LayerItem;
            if (layer == null) return;

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            int currentIndex = Layers.IndexOf(layer);

            if (shift && _lastSelectedIndex >= 0)
            {
                // Range select
                int start = Math.Min(_lastSelectedIndex, currentIndex);
                int end = Math.Max(_lastSelectedIndex, currentIndex);
                foreach (var l in Layers) l.IsSelected = false;
                for (int i = start; i <= end; i++) Layers[i].IsSelected = true;
            }
            else if (ctrl)
            {
                // Toggle current without clearing others
                layer.IsSelected = !layer.IsSelected;
                _lastSelectedIndex = currentIndex;
            }
            else
            {
                // Single select
                foreach (var l in Layers) l.IsSelected = false;
                layer.IsSelected = true;
                _lastSelectedIndex = currentIndex;
            }

            SyncSelectionToCanvas();
            UpdateMergeDownEnabled();
        }

        private void Layer_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point position = e.GetPosition(null);
                if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    var border = sender as Border;
                    var layer = border?.Tag as LayerItem;

                    if (layer != null)
                    {
                        var dragData = new DataObject("LayerItem", layer);
                        DragDrop.DoDragDrop(border, dragData, DragDropEffects.Move);
                    }
                }
            }
        }

        private void Layer_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
        }

        private void Layer_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("LayerItem"))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Layer_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("LayerItem"))
            {
                var droppedLayer = e.Data.GetData("LayerItem") as LayerItem;
                var targetBorder = sender as Border;
                var targetLayer = targetBorder?.Tag as LayerItem;

                if (droppedLayer != null && targetLayer != null && droppedLayer != targetLayer)
                {
                    int oldIndex = Layers.IndexOf(droppedLayer);
                    int newIndex = Layers.IndexOf(targetLayer);

                    if (oldIndex != -1 && newIndex != -1)
                    {
                        Layers.Move(oldIndex, newIndex);
                        ReorderCanvasItems();
                    }
                }
            }
            _isDragging = false;
            UpdateMergeDownEnabled();
        }

        private void SyncSelectionToCanvas()
        {
            try
            {
                if (_simpleCanvas == null) return;

                var selectedItems = Layers
                    .Where(l => l.IsSelected && l.SimpleCanvasItem != null)
                    .Select(l => l.SimpleCanvasItem)
                    .ToList();

                // Clear existing selection
                _simpleCanvas.ClearSelection();

                // Apply selection flags and update SelectedItems collection
                foreach (var item in _simpleCanvas.Items)
                {
                    item.IsSelected = selectedItems.Contains(item);
                }
                _simpleCanvas.SelectedItems.Clear();
                foreach (var item in selectedItems)
                {
                    _simpleCanvas.SelectedItems.Add(item);
                }
            }
            catch { }
        }

        private static bool IsPlaceholderLayer(LayerItem layer)
        {
            if (layer?.SimpleCanvasItem is SimpleImageItem img)
            {
                return img.IsPlaceholder;
            }
            return false;
        }

        private void UpdateMergeDownEnabled()
        {
            try
            {
                if (MergeDownButton == null)
                    return;

                // Only supported in SimpleDesignerCanvas mode
                if (_simpleCanvas == null)
                {
                    MergeDownButton.IsEnabled = false;
                    return;
                }

                var selectedLayer = Layers.FirstOrDefault(l => l.IsSelected);
                if (selectedLayer == null)
                {
                    MergeDownButton.IsEnabled = false;
                    return;
                }

                int index = Layers.IndexOf(selectedLayer);
                if (index < 0 || index >= Layers.Count - 1)
                {
                    MergeDownButton.IsEnabled = false;
                    return;
                }

                var belowLayer = Layers[index + 1];
                // Disallow merge if either layer is a photo placeholder
                if (IsPlaceholderLayer(selectedLayer) || IsPlaceholderLayer(belowLayer))
                {
                    MergeDownButton.IsEnabled = false;
                    return;
                }

                // Both layers exist and are not placeholders
                MergeDownButton.IsEnabled = true;
            }
            catch
            {
                // Be safe in UI logic
                if (MergeDownButton != null) MergeDownButton.IsEnabled = false;
            }
        }
    }

    public class LayerItem : INotifyPropertyChanged
    {
        private bool _isVisible = true;
        private bool _isLocked = false;
        private bool _isSelected = false;

        public ICanvasItem CanvasItem { get; set; }
        public SimpleCanvasItem SimpleCanvasItem { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public BitmapSource Thumbnail { get; set; }

        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                _isVisible = value;
                OnPropertyChanged(nameof(IsVisible));
            }
        }

        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                _isLocked = value;
                OnPropertyChanged(nameof(IsLocked));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
