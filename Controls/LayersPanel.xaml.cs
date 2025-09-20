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

        public event EventHandler<LayerItem> LayerSelectionChanged;
        public event EventHandler LayersReordered;

        public LayersPanel()
        {
            InitializeComponent();
            Layers = new ObservableCollection<LayerItem>();
            LayersList.ItemsSource = Layers;

            OpacitySlider.ValueChanged += OpacitySlider_ValueChanged;
            BlendModeCombo.SelectionChanged += BlendModeCombo_SelectionChanged;
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

            var selectedItem = _simpleCanvas.SelectedItem;
            if (selectedItem != null)
            {
                var layer = Layers.FirstOrDefault(l => l.SimpleCanvasItem == selectedItem);
                if (layer != null)
                {
                    SelectLayer(layer);
                }
            }
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
                var items = _simpleCanvas.Items.Reverse();
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
            // TODO: Generate actual thumbnail
            return null;
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
        }

        private void UpdatePropertiesForLayer(LayerItem layer)
        {
            if (layer == null || layer.CanvasItem == null) return;

            if (layer.CanvasItem is FrameworkElement fe)
            {
                if (OpacitySlider != null)
                {
                    OpacitySlider.Value = fe.Opacity * 100;
                }
                if (OpacityText != null)
                {
                    OpacityText.Text = $"{(int)(fe.Opacity * 100)}%";
                }
            }
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

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var selectedLayer = Layers.FirstOrDefault(l => l.IsSelected);
            if (selectedLayer != null && selectedLayer.CanvasItem != null)
            {
                if (selectedLayer.CanvasItem is FrameworkElement fe)
                {
                    fe.Opacity = e.NewValue / 100.0;
                    if (OpacityText != null)
                    {
                        OpacityText.Text = $"{(int)e.NewValue}%";
                    }
                }
            }
        }

        private void BlendModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Blend modes would require custom rendering implementation
            // This is a placeholder for future enhancement
        }

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
            // This would merge the selected layer with the one below
            // Requires complex image processing
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
                var items = Layers.Select(l => l.SimpleCanvasItem).Where(i => i != null).Reverse().ToList();
                _simpleCanvas.Items.Clear();

                foreach (var item in items)
                {
                    _simpleCanvas.Items.Add(item);
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
        }

        // Drag and drop for reordering
        private void Layer_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            var border = sender as Border;
            var layer = border?.Tag as LayerItem;
            if (layer != null)
            {
                SelectLayer(layer);
            }
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
