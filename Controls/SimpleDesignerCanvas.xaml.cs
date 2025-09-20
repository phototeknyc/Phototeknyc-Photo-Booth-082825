using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Photobooth.Controls
{
    /// <summary>
    /// Simple designer canvas that provides direct manipulation without complex adorners
    /// </summary>
    public partial class SimpleDesignerCanvas : UserControl, INotifyPropertyChanged
    {
        public static readonly DependencyProperty CanvasBackgroundProperty =
            DependencyProperty.Register(
                nameof(CanvasBackground), typeof(Brush), typeof(SimpleDesignerCanvas),
                new PropertyMetadata(Brushes.Transparent));

        public Brush CanvasBackground
        {
            get => (Brush)GetValue(CanvasBackgroundProperty);
            set => SetValue(CanvasBackgroundProperty, value);
        }
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<SimpleCanvasItem> ItemSelected;
        public event EventHandler<SimpleCanvasItem> ItemDeselected;
        public event EventHandler<SimpleCanvasItem> ItemAdded;
        public event EventHandler<SimpleCanvasItem> ItemRemoved;
        public event EventHandler SelectionCleared;
        public event EventHandler SelectionChanged;
        public event EventHandler<SimpleImageItem> PlaceholderNumberEditRequested;

        // Collection of canvas items
        private ObservableCollection<SimpleCanvasItem> _items;
        public ObservableCollection<SimpleCanvasItem> Items
        {
            get => _items;
            private set
            {
                _items = value;
                OnPropertyChanged(nameof(Items));
            }
        }

        // Selected item tracking - single selection for backwards compatibility
        private SimpleCanvasItem _selectedItem;
        public SimpleCanvasItem SelectedItem
        {
            get => _selectedItem;
            private set
            {
                if (_selectedItem != value)
                {
                    var previousItem = _selectedItem;
                    _selectedItem = value;

                    // Clear multi-selection when setting single selection
                    if (!_isMultiSelecting)
                    {
                        foreach (var item in _selectedItems.ToList())
                        {
                            if (item != value)
                            {
                                item.IsSelected = false;
                                _selectedItems.Remove(item);
                            }
                        }
                    }

                    // Update selection state
                    if (previousItem != null && !_selectedItems.Contains(previousItem))
                    {
                        previousItem.IsSelected = false;
                        ItemDeselected?.Invoke(this, previousItem);
                    }

                    if (_selectedItem != null)
                    {
                        _selectedItem.IsSelected = true;
                        if (!_selectedItems.Contains(_selectedItem))
                        {
                            _selectedItems.Add(_selectedItem);
                        }
                        ItemSelected?.Invoke(this, _selectedItem);
                    }
                    else if (_selectedItems.Count == 0)
                    {
                        SelectionCleared?.Invoke(this, EventArgs.Empty);
                    }

                    OnPropertyChanged(nameof(SelectedItem));
                    UpdateHandles();
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        // Multi-selection support
        private ObservableCollection<SimpleCanvasItem> _selectedItems = new ObservableCollection<SimpleCanvasItem>();
        private bool _isMultiSelecting = false;

        public ObservableCollection<SimpleCanvasItem> SelectedItems
        {
            get => _selectedItems;
        }

        // Canvas properties
        private bool _showGrid = false;
        public bool ShowGrid
        {
            get => _showGrid;
            set
            {
                _showGrid = value;
                UpdateGrid();
                OnPropertyChanged(nameof(ShowGrid));
            }
        }

        private double _gridSize = 20;
        public double GridSize
        {
            get => _gridSize;
            set
            {
                _gridSize = value;
                if (ShowGrid) UpdateGrid();
                OnPropertyChanged(nameof(GridSize));
            }
        }

        // Canvas size properties
        public new double Width
        {
            get => MainCanvas.Width;
            set
            {
                MainCanvas.Width = value;
                GridCanvas.Width = value;
                HandleCanvas.Width = value;
                UpdateGrid();
                OnPropertyChanged(nameof(Width));
            }
        }

        public new double Height
        {
            get => MainCanvas.Height;
            set
            {
                MainCanvas.Height = value;
                GridCanvas.Height = value;
                HandleCanvas.Height = value;
                UpdateGrid();
                OnPropertyChanged(nameof(Height));
            }
        }

        // Manipulation state
        private bool _isManipulating = false;
        private Point _lastMousePosition;

        public SimpleDesignerCanvas()
        {
            InitializeComponent();
            InitializeCanvas();
            PreviewKeyDown += SimpleDesignerCanvas_PreviewKeyDown;
        }

        public void RaisePlaceholderNumberEditRequest(SimpleImageItem placeholder)
        {
            PlaceholderNumberEditRequested?.Invoke(this, placeholder);
        }

        private void InitializeCanvas()
        {
            Items = new ObservableCollection<SimpleCanvasItem>();
            Items.CollectionChanged += Items_CollectionChanged;

            // Set default size
            Width = 600;
            Height = 1800;

            // Enable multi-touch if available
            IsManipulationEnabled = true;
        }

        private void Items_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                    foreach (SimpleCanvasItem item in e.NewItems)
                    {
                        AddItemToCanvas(item);
                    }
                    break;

                case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                    foreach (SimpleCanvasItem item in e.OldItems)
                    {
                        RemoveItemFromCanvas(item);
                    }
                    break;

                case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                    ClearCanvas();
                    break;
            }
        }

        private void AddItemToCanvas(SimpleCanvasItem item)
        {
            if (item == null) return;

            // Add to main canvas
            MainCanvas.Children.Add(item);

            // Set initial position
            Canvas.SetLeft(item, item.Left);
            Canvas.SetTop(item, item.Top);
            Panel.SetZIndex(item, item.ZIndex);

            // Add all handles (selection and rotation) to handle canvas
            var handles = item.GetAllHandles();
            if (handles != null)
            {
                foreach (var handle in handles)
                {
                    HandleCanvas.Children.Add(handle);
                }
            }

            // Wire up events
            item.MouseLeftButtonDown += Item_MouseLeftButtonDown;
            item.TouchDown += Item_TouchDown;
            item.SelectionChanged += Item_SelectionChanged;
            item.PropertyChanged += Item_PropertyChanged;

            ItemAdded?.Invoke(this, item);
        }

        private void RemoveItemFromCanvas(SimpleCanvasItem item)
        {
            if (item == null) return;

            // Remove from main canvas
            MainCanvas.Children.Remove(item);

            // Remove all handles
            var handles = item.GetAllHandles();
            if (handles != null)
            {
                foreach (var handle in handles)
                {
                    HandleCanvas.Children.Remove(handle);
                }
            }

            // Unwire events
            item.MouseLeftButtonDown -= Item_MouseLeftButtonDown;
            item.TouchDown -= Item_TouchDown;
            item.SelectionChanged -= Item_SelectionChanged;
            item.PropertyChanged -= Item_PropertyChanged;

            // Clear selection if this item was selected
            if (SelectedItem == item)
            {
                SelectedItem = null;
            }

            ItemRemoved?.Invoke(this, item);
        }

        private void ClearCanvas()
        {
            MainCanvas.Children.Clear();
            HandleCanvas.Children.Clear();
            SelectedItem = null;
        }

        // Item event handlers
        private void Item_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is SimpleCanvasItem item)
            {
                bool ctrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                bool shiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

                if (ctrlPressed)
                {
                    // Toggle selection for this item
                    _isMultiSelecting = true;

                    if (_selectedItems.Contains(item))
                    {
                        // Deselect item
                        item.IsSelected = false;
                        _selectedItems.Remove(item);

                        // Update SelectedItem if this was the selected one
                        if (SelectedItem == item)
                        {
                            SelectedItem = _selectedItems.FirstOrDefault();
                        }
                    }
                    else
                    {
                        // Add to selection
                        item.IsSelected = true;
                        _selectedItems.Add(item);
                        SelectedItem = item; // Make this the primary selection
                    }

                    _isMultiSelecting = false;
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    // Single selection - clear others
                    SelectedItem = item;
                }

                // Ensure canvas receives keyboard shortcuts
                Focus();
                e.Handled = true;
            }
        }

        private void SimpleDesignerCanvas_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

                if (ctrl && e.Key == Key.Z)
                {
                    // Ctrl+Z or Ctrl+Shift+Z for undo
                    Undo();
                    e.Handled = true;
                    return;
                }
                if (ctrl && (e.Key == Key.Y || (shift && e.Key == Key.Z)))
                {
                    // Ctrl+Y or Ctrl+Shift+Z redo
                    Redo();
                    e.Handled = true;
                    return;
                }
                if (ctrl && e.Key == Key.C)
                {
                    CopySelection();
                    e.Handled = true;
                    return;
                }
                if (ctrl && e.Key == Key.V)
                {
                    PushUndo();
                    PasteClipboard();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Delete)
                {
                    if (SelectedItem != null)
                    {
                        PushUndo();
                        RemoveSelectedItem();
                        e.Handled = true;
                        return;
                    }
                }
                if (ctrl && e.Key == Key.Up)
                {
                    // Ctrl+Up = bring to front
                    PushUndo();
                    BringToFront();
                    e.Handled = true;
                    return;
                }
                if (ctrl && e.Key == Key.Down)
                {
                    // Ctrl+Down = send to back
                    PushUndo();
                    SendToBack();
                    e.Handled = true;
                    return;
                }
            }
            catch { }
        }

        private void Item_TouchDown(object sender, TouchEventArgs e)
        {
            if (sender is SimpleCanvasItem item)
            {
                // Single touch - select the item (clear other selections)
                _isMultiSelecting = false;
                SelectedItem = item;

                // Update last mouse position for manipulation
                _lastMousePosition = e.GetTouchPoint(MainCanvas).Position;
                _isManipulating = true;
            }
        }

        private void Item_SelectionChanged(object sender, EventArgs e)
        {
            UpdateHandles();
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is SimpleCanvasItem item)
            {
                // Update canvas position when item position changes
                if (e.PropertyName == "Left" || e.PropertyName == "Top")
                {
                    Canvas.SetLeft(item, item.Left);
                    Canvas.SetTop(item, item.Top);
                }
                else if (e.PropertyName == "ZIndex")
                {
                    Panel.SetZIndex(item, item.ZIndex);
                }

                UpdateHandles();
            }
        }

        // Canvas mouse events
        private void MainCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Clicked on empty canvas - clear selection
            SelectedItem = null;
            Focus(); // Take focus for keyboard events
        }

        private void MainCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isManipulating = false;
        }

        private void MainCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            _lastMousePosition = e.GetPosition(MainCanvas);
        }

        // Drag and drop support
        private void MainCanvas_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void MainCanvas_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files?.Length > 0)
                {
                    var dropPosition = e.GetPosition(MainCanvas);
                    HandleImageDrop(files[0], dropPosition);
                }
            }
        }

        private void HandleImageDrop(string filePath, Point position)
        {
            var extension = System.IO.Path.GetExtension(filePath)?.ToLower();
            if (extension == ".jpg" || extension == ".jpeg" || extension == ".png" ||
                extension == ".bmp" || extension == ".gif" || extension == ".tiff")
            {
                var imageItem = new SimpleImageItem();
                if (imageItem.LoadImage(filePath))
                {
                    try
                    {
                        // Auto-fit if the dropped image matches the canvas size (within tolerance)
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage(new Uri(filePath));
                        double dpiX = bitmap.DpiX > 0 ? bitmap.DpiX : 96.0;
                        double dpiY = bitmap.DpiY > 0 ? bitmap.DpiY : 96.0;
                        double imgW = bitmap.PixelWidth * (96.0 / dpiX);
                        double imgH = bitmap.PixelHeight * (96.0 / dpiY);
                        double tolerance = 2.0;
                        if (Math.Abs(imgW - this.Width) <= tolerance && Math.Abs(imgH - this.Height) <= tolerance)
                        {
                            imageItem.Left = 0;
                            imageItem.Top = 0;
                            imageItem.Width = this.Width;
                            imageItem.Height = this.Height;
                            AddItem(imageItem);
                            return;
                        }

                        // If aspect ratio matches, fill the canvas while preserving aspect
                        double imgAspect = imgW / imgH;
                        double canvasAspect = this.Width / this.Height;
                        double ratioTolerance = 0.01; // ~1%
                        if (Math.Abs(imgAspect - canvasAspect) <= ratioTolerance)
                        {
                            imageItem.Left = 0;
                            imageItem.Top = 0;
                            imageItem.Width = this.Width;
                            imageItem.Height = this.Height;
                            AddItem(imageItem);
                            return;
                        }
                    }
                    catch { }

                    // Default behavior: place at drop position with default size
                    imageItem.Left = position.X;
                    imageItem.Top = position.Y;
                    AddItem(imageItem);
                }
            }
        }

        // Public methods for managing items
        public void AddItem(SimpleCanvasItem item)
        {
            if (item != null)
            {
                Items.Add(item);
            }
        }

        public void RemoveItem(SimpleCanvasItem item)
        {
            if (item != null)
            {
                Items.Remove(item);
            }
        }

        public void RemoveSelectedItem()
        {
            if (SelectedItem != null)
            {
                RemoveItem(SelectedItem);
            }
        }

        public void ClearAllItems()
        {
            Items.Clear();
        }

        // Add shape helper
        public SimpleShapeItem AddShape(SimpleShapeType type, double x, double y, double width = 100, double height = 100)
        {
            var shape = new SimpleShapeItem
            {
                ShapeType = type,
                Left = x,
                Top = y,
                Width = width,
                Height = height
            };
            AddItem(shape);
            return shape;
        }

        // Clipboard support (internal static clipboard for simplicity)
        private static List<SimpleCanvasItem> _clipboard;

        public void CopySelection()
        {
            if (SelectedItems == null || SelectedItems.Count == 0) return;
            _clipboard = SelectedItems.Select(i => i.Clone()).ToList();
        }

        public void PasteClipboard()
        {
            if (_clipboard == null || _clipboard.Count == 0) return;
            // Deselect current
            ClearSelection();
            foreach (var item in _clipboard)
            {
                var clone = item.Clone();
                clone.Left += 20; clone.Top += 20; // offset for visibility
                AddItem(clone);
                clone.IsSelected = true;
                _selectedItems.Add(clone);
                SelectedItem = clone;
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        // Simple undo/redo of item collections
        private readonly Stack<List<SimpleCanvasItem>> _undo = new Stack<List<SimpleCanvasItem>>();
        private readonly Stack<List<SimpleCanvasItem>> _redo = new Stack<List<SimpleCanvasItem>>();

        private List<SimpleCanvasItem> Snapshot()
        {
            return Items.Select(i => i.Clone()).ToList();
        }

        private void Restore(List<SimpleCanvasItem> snapshot)
        {
            Items.CollectionChanged -= Items_CollectionChanged;
            try
            {
                MainCanvas.Children.Clear();
                HandleCanvas.Children.Clear();
                _selectedItems.Clear();
                _items = new ObservableCollection<SimpleCanvasItem>();
                foreach (var item in snapshot)
                {
                    AddItem(item); // AddItem wires visuals
                }
                OnPropertyChanged(nameof(Items));
            }
            finally
            {
                Items.CollectionChanged += Items_CollectionChanged;
            }
        }

        public void PushUndo()
        {
            _undo.Push(Snapshot());
            _redo.Clear();
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            var current = Snapshot();
            var prev = _undo.Pop();
            _redo.Push(current);
            Restore(prev);
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            var current = Snapshot();
            var next = _redo.Pop();
            _undo.Push(current);
            Restore(next);
        }

        // Method alias for compatibility
        public SimpleTextItem AddText(string text, double x, double y)
        {
            return AddTextItem(text, new Point(x, y));
        }

        public SimpleTextItem AddTextItem(string text = "New Text", Point? position = null)
        {
            var textItem = new SimpleTextItem(text);

            if (position.HasValue)
            {
                textItem.Left = position.Value.X;
                textItem.Top = position.Value.Y;
            }
            else
            {
                // Default position
                textItem.Left = 50;
                textItem.Top = 50;
            }

            AddItem(textItem);
            return textItem;
        }

        // Method alias for compatibility
        public SimpleImageItem AddImage(string imagePath, double x, double y, double width, double height)
        {
            SimpleImageItem imageItem;
            if (!string.IsNullOrEmpty(imagePath))
            {
                imageItem = new SimpleImageItem();
                imageItem.LoadImage(imagePath);
            }
            else
            {
                // Create as placeholder from the start to ensure proper numbering
                imageItem = new SimpleImageItem(true);
            }
            imageItem.Left = x;
            imageItem.Top = y;
            imageItem.Width = width;
            imageItem.Height = height;
            AddItem(imageItem);
            return imageItem;
        }

        public SimpleImageItem AddImageItem(Point? position = null, bool isPlaceholder = false)
        {
            var imageItem = new SimpleImageItem(isPlaceholder);

            if (position.HasValue)
            {
                imageItem.Left = position.Value.X;
                imageItem.Top = position.Value.Y;
            }
            else
            {
                // Default position
                imageItem.Left = 50;
                imageItem.Top = 50;
            }

            AddItem(imageItem);
            return imageItem;
        }

        // Selection methods
        public void SelectItem(SimpleCanvasItem item)
        {
            if (Items.Contains(item))
            {
                SelectedItem = item;
            }
        }

        public void SetSelectedItem(SimpleCanvasItem item)
        {
            SelectedItem = item;
        }

        public void ClearSelection()
        {
            foreach (var item in _selectedItems.ToList())
            {
                item.IsSelected = false;
            }
            _selectedItems.Clear();
            SelectedItem = null;
        }

        public void SelectAll()
        {
            _isMultiSelecting = true;
            foreach (var item in Items)
            {
                item.IsSelected = true;
                if (!_selectedItems.Contains(item))
                {
                    _selectedItems.Add(item);
                }
            }
            SelectedItem = Items.FirstOrDefault();
            _isMultiSelecting = false;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        // Z-order methods
        public void BringToFront(SimpleCanvasItem item = null)
        {
            if (item == null) item = SelectedItem;
            if (item == null) return;

            var maxZ = Items.Count > 0 ? Items.Max(i => i.ZIndex) : 0;
            item.ZIndex = maxZ + 1;
        }

        public void SendToBack(SimpleCanvasItem item = null)
        {
            if (item == null) item = SelectedItem;
            if (item == null) return;

            var minZ = Items.Count > 0 ? Items.Min(i => i.ZIndex) : 0;
            item.ZIndex = minZ - 1;
        }

        public void BringForward(SimpleCanvasItem item = null)
        {
            if (item == null) item = SelectedItem;
            if (item == null) return;

            item.ZIndex += 1;
        }

        public void SendBackward(SimpleCanvasItem item = null)
        {
            if (item == null) item = SelectedItem;
            if (item == null) return;

            item.ZIndex -= 1;
        }

        // Grid management
        private void UpdateGrid()
        {
            // Grid lines removed per requirement: editor shows no grid
            if (GridCanvas != null)
            {
                GridCanvas.Children.Clear();
                GridCanvas.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateHandles()
        {
            // Update all item handles
            foreach (var item in Items)
            {
                item.UpdateSelectionHandles();
            }
        }

        // Utility methods
        public List<SimpleCanvasItem> GetItemsInZOrder()
        {
            return Items.OrderBy(item => item.ZIndex).ToList();
        }

        public SimpleCanvasItem GetItemAt(Point position)
        {
            return Items
                .Where(item => position.X >= item.Left && position.X <= item.Left + item.Width &&
                              position.Y >= item.Top && position.Y <= item.Top + item.Height)
                .OrderByDescending(item => item.ZIndex)
                .FirstOrDefault();
        }

        // Layering operations
        public void BringToFront()
        {
            if (SelectedItem != null)
            {
                var maxZIndex = Items.Any() ? Items.Max(i => i.ZIndex) : 0;
                SelectedItem.ZIndex = maxZIndex + 1;
                UpdateZIndices();
            }
        }

        public void SendToBack()
        {
            if (SelectedItem != null)
            {
                var minZIndex = Items.Any() ? Items.Min(i => i.ZIndex) : 0;
                SelectedItem.ZIndex = minZIndex - 1;
                UpdateZIndices();
            }
        }

        private void UpdateZIndices()
        {
            // Normalize z-indices to prevent overflow
            var sortedItems = Items.OrderBy(i => i.ZIndex).ToList();
            for (int i = 0; i < sortedItems.Count; i++)
            {
                sortedItems[i].ZIndex = i;
            }
        }

        public Rect GetItemsBounds()
        {
            if (!Items.Any()) return new Rect();

            var minX = Items.Min(item => item.Left);
            var minY = Items.Min(item => item.Top);
            var maxX = Items.Max(item => item.Left + item.Width);
            var maxY = Items.Max(item => item.Top + item.Height);

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
