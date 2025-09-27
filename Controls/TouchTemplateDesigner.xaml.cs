using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DesignerCanvas;
using DesignerCanvas.Controls;
using System.IO.Compression;
using Newtonsoft.Json;
using Microsoft.Win32;
using Photobooth.Services;
using Photobooth.MVVM.ViewModels.Designer;
using Photobooth.Database;
using CameraControl.Devices;
using Photobooth.Models;

namespace Photobooth.Controls
{
    /// <summary>
    /// Touch-optimized template designer control integrated with DesignerVM
    /// </summary>
    public partial class TouchTemplateDesigner : UserControl
    {
        private DesignerVM _designerVM;
        private double _currentZoom = 1.0;
        private int _currentTemplateId = -1;
        private bool _isInitialized = false;
        private Storyboard _currentPanelAnimation;
        private EventService _eventService;
        private EventData _selectedEvent;
        // Properties panel binding
        private SimpleCanvasItem _propertiesBoundItem;
        // Properties: coordinate display mode
        private bool _useCenterCoordinates = false;

        // Store desired canvas size to prevent template loading from overriding it
        private double? _desiredCanvasWidth = null;
        private double? _desiredCanvasHeight = null;

        // Color picker state
        private Color _currentSelectedColor = Colors.Black;

        // Auto-save functionality
        private System.Windows.Threading.DispatcherTimer _autoSaveTimer;
        private bool _hasUnsavedChanges = false;
        private DateTime _lastSaveTime = DateTime.Now;
        private int _autoSaveCount = 0;
        private int _lastItemCount = 0;
        private const int AUTO_SAVE_INTERVAL_SECONDS = 30; // Auto-save every 30 seconds

        // Flag to prevent checkbox event during programmatic updates
        private bool _suppressCheckboxEvents = false;

        public TouchTemplateDesigner()
        {
            InitializeComponent();
            InitializeDesigner();

            // Initialize event service
            _eventService = new EventService();
            LoadEvents();

            // Handle responsive layout
            SizeChanged += OnSizeChanged;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            PreviewKeyDown += OnRootPreviewKeyDown;
            // Close typography (font) panel when clicking outside
            PreviewMouseDown += OnRootPreviewMouseDown;
        }

        // Position the appearance popup just below the top toolbar
        private CustomPopupPlacement[] AppearancePopup_PlacementCallback(Size popupSize, Size targetSize, Point offset)
        {
            try
            {
                var target = AppearanceButton;
                var window = Window.GetWindow(this);
                var topToolbar = TopToolbar as FrameworkElement;
                if (target == null || window == null || topToolbar == null)
                {
                    // Fallback: to the right of the target
                    return new[] { new CustomPopupPlacement(new Point(targetSize.Width, 0), PopupPrimaryAxis.Vertical) };
                }

                // Convert screen pixels to device-independent units
                var source = PresentationSource.FromVisual(window);
                var fromDevice = source?.CompositionTarget?.TransformFromDevice;

                Point targetScreen = target.PointToScreen(new Point(0, 0));
                Point toolbarBottomScreen = topToolbar.PointToScreen(new Point(0, topToolbar.ActualHeight));

                if (fromDevice.HasValue)
                {
                    targetScreen = fromDevice.Value.Transform(targetScreen);
                    toolbarBottomScreen = fromDevice.Value.Transform(toolbarBottomScreen);
                }

                // Align popup top to sit just below the top toolbar
                const double marginTop = 8.0;
                double desiredTopRelativeToTarget = (toolbarBottomScreen.Y + marginTop) - targetScreen.Y;

                // Place to the right of the tool button
                double x = targetSize.Width + 8.0;
                double y = desiredTopRelativeToTarget;

                return new[] { new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Vertical) };
            }
            catch
            {
                // Safe fallback
                return new[] { new CustomPopupPlacement(new Point(targetSize.Width, 0), PopupPrimaryAxis.Vertical) };
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Save any pending changes before unloading
            SaveOnExit();

            // Clean up auto-save when control is unloaded
            StopAutoSave();
        }

        private void InitializeDesigner()
        {
            try
            {
                // Create and configure DesignerVM
                _designerVM = new DesignerVM();
                DataContext = _designerVM;

                // Set the designer canvas for DesignerVM
                // Commented out for SimpleDesignerCanvas compatibility
                // _designerVM.CustomDesignerCanvas = DesignerCanvas;

                InitializeCanvas();
                _isInitialized = true;

                // Initialize color preview
                UpdateColorPreview();

                // Initialize auto-save timer
                InitializeAutoSave();

                // Set up token resolvers for dynamic fields
                SimpleTextItem.GlobalTokenResolver = ResolveTokens;
                SimpleQRCodeItem.GlobalTokenResolver = ResolveTokens;
                try { SimpleBarcodeItem.GlobalTokenResolver = ResolveTokens; } catch { }

                Log.Debug("TouchTemplateDesigner: Initialized with DesignerVM");
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to initialize: {ex.Message}");
                MessageBox.Show($"Failed to initialize designer: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string ResolveTokens(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            try
            {
                string s = input;
                // Date/Time tokens
                s = System.Text.RegularExpressions.Regex.Replace(s, @"\{DATE(?::([^\}]+))?\}", m =>
                {
                    var fmt = m.Groups[1].Success ? m.Groups[1].Value : "yyyy-MM-dd";
                    return DateTime.Now.ToString(fmt);
                });
                s = System.Text.RegularExpressions.Regex.Replace(s, @"\{TIME(?::([^\}]+))?\}", m =>
                {
                    var fmt = m.Groups[1].Success ? m.Groups[1].Value : "HH:mm";
                    return DateTime.Now.ToString(fmt);
                });

                // Event tokens
                var ev = _selectedEvent;
                if (ev != null)
                {
                    s = s.Replace("{EVENT.NAME}", ev.Name ?? string.Empty);
                    s = System.Text.RegularExpressions.Regex.Replace(s, @"\{EVENT.DATE(?::([^\}]+))?\}", m =>
                    {
                        var fmt = m.Groups[1].Success ? m.Groups[1].Value : "yyyy-MM-dd";
                        if (ev.EventDate.HasValue) return ev.EventDate.Value.ToString(fmt);
                        return string.Empty;
                    });
                    // Use provided gallery URL if available; else fallback pattern
                    string galleryUrl = !string.IsNullOrWhiteSpace(ev.GalleryUrl)
                        ? ev.GalleryUrl
                        : $"https://gallery.local/{(ev.Name ?? "event").Replace(' ', '-')}";
                    s = s.Replace("{EVENT.URL}", galleryUrl);
                }
                else
                {
                    s = s.Replace("{EVENT.NAME}", string.Empty).Replace("{EVENT.URL}", string.Empty);
                    s = System.Text.RegularExpressions.Regex.Replace(s, @"\{EVENT.DATE(?::([^\}]+))?\}", m => string.Empty);
                }
                return s;
            }
            catch { return input; }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private void AddBarcode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var canvas = DesignerCanvas as SimpleDesignerCanvas;
                if (canvas == null) return;
                var item = new SimpleBarcodeItem
                {
                    Left = canvas.ActualWidth / 2 - 120,
                    Top = canvas.ActualHeight / 2 - 50,
                    Width = 240,
                    Height = 100,
                    Value = "12345",
                    ModuleWidth = 2.0,
                    IncludeLabel = true
                };
                canvas.AddItem(item);
                canvas.SelectItem(item);
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to add barcode: {ex.Message}");
            }
        }

        private void InitializeCanvas()
        {
            // Set up the designer canvas with touch support
            DesignerCanvas.AllowDrop = true;
            // Canvas background is set in XAML for proper visual hierarchy

            // Subscribe to the placeholder number edit request event
            DesignerCanvas.PlaceholderNumberEditRequested += OnPlaceholderNumberEditRequested;

            // Set initial canvas size to 4x6 (1200x1800 at 300 DPI) - most common print size
            // Will be overridden by paper size selection or template loading
            DesignerCanvas.Width = 1200;
            DesignerCanvas.Height = 1800;

            Log.Debug($"TouchTemplateDesigner: InitializeCanvas set size to {DesignerCanvas.Width}x{DesignerCanvas.Height}");
            // ShowGrid and SnapToGrid are not available on TouchEnabledCanvas
            // These would need to be implemented if grid functionality is required

            // Enable touch manipulation
            DesignerCanvas.IsManipulationEnabled = true;
            DesignerCanvas.ManipulationDelta += Canvas_ManipulationDelta;
            DesignerCanvas.ManipulationStarting += Canvas_ManipulationStarting;

            // Listen for canvas size changes to trigger auto-fit
            DesignerCanvas.SizeChanged += DesignerCanvas_SizeChanged;

            // Initialize panels
            // Set canvas constraint for FontControlsPanel eyedropper
            if (FontControlsPanel != null)
            {
                FontControlsPanel.ConstrainToElement = DesignerCanvas;
            }
            // Set constrain element for toolbar color pickers
                try
                {
                    var ic1 = this.FindName("ToolbarStrokeColorPicker") as InlineColorPicker;
                    if (ic1 != null)
                    {
                        ic1.ConstrainToElement = DesignerCanvas;
                        ic1.AnchorTopElement = TopToolbar;
                    }
                    var ic2 = this.FindName("ToolbarShadowColorPicker") as InlineColorPicker;
                    if (ic2 != null)
                    {
                        ic2.ConstrainToElement = DesignerCanvas;
                        ic2.AnchorTopElement = TopToolbar;
                    }
                    var ic3 = this.FindName("ToolbarPlaceholderFillColorPicker") as InlineColorPicker;
                    if (ic3 != null)
                    {
                        ic3.ConstrainToElement = DesignerCanvas;
                        ic3.AnchorTopElement = TopToolbar;
                    }
                    var ic4 = this.FindName("ToolbarCanvasBgColorPicker") as InlineColorPicker;
                    if (ic4 != null)
                    {
                        ic4.ConstrainToElement = DesignerCanvas;
                        ic4.AnchorTopElement = TopToolbar;
                    }
                    var ic5 = this.FindName("ToolbarShapeFillColorPicker") as InlineColorPicker;
                    if (ic5 != null)
                    {
                        ic5.ConstrainToElement = DesignerCanvas;
                        ic5.AnchorTopElement = TopToolbar;
                    }
                }
                catch { }

            // Set up layers panel with SimpleDesignerCanvas
            var simpleCanvas = DesignerCanvas as SimpleDesignerCanvas;
            if (simpleCanvas != null)
            {
                LayersPanel.SetSimpleDesignerCanvas(simpleCanvas);
                simpleCanvas.SelectionChanged += (s, e) =>
                {
                    LayersPanel.RefreshLayers();
                    CheckTextSelectionAndActivateFontControls();

                    // Update aspect ratio lock toggle button state
                    if (AspectRatioLockToggle != null)
                    {
                        AspectRatioLockToggle.IsChecked = simpleCanvas.SelectedItem?.IsAspectRatioLocked ?? false;
                    }

                    // Update Properties panel to reflect current selection
                    BindPropertiesPanelToSelectedItem();
                };
            }
            FontControlsPanel.FontChanged += FontControlsPanel_FontChanged;

            // Add canvas click event for text insertion
            // Use PreviewMouseLeftButtonDown to capture events before child controls
            DesignerCanvas.PreviewMouseLeftButtonDown += DesignerCanvas_MouseLeftButtonDown;
        }

        private void DesignerCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isInitialized && CanvasScrollViewer != null)
            {
                // Auto-fit when canvas size changes to maintain fit
                Dispatcher.BeginInvoke(new Action(() => AutoFitCanvasToViewport()),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        #region Text Insertion Methods

        private void DesignerCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Note: Eyedropper mode has been moved to the color picker dialog

            if (e.ClickCount == 2)
            {
                try
                {
                    var canvas = DesignerCanvas as SimpleDesignerCanvas;
                    if (canvas != null)
                    {
                        // Determine which item was double-clicked
                        var pt = e.GetPosition(canvas);
                        var clicked = canvas.GetItemAt(pt);
                        if (clicked != null)
                        {
                            canvas.SelectItem(clicked);
                        }

                        // For text items: future inline edit hook (kept), but also open Properties for now
                        var selectedItem = canvas.SelectedItem;
                        if (selectedItem is SimpleTextItem)
                        {
                            Debug.WriteLine("Double-click text: opening Properties (inline edit TBD)");
                        }

                        // Open Properties panel and focus first field
                        PropertiesToggle.IsChecked = true;
                        ShowPropertiesPanel();
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                // Focus first editable field if available
                                var firstBox = FindVisualChild<TextBox>(PropertiesContent);
                                firstBox?.Focus();
                                firstBox?.SelectAll();
                            }
                            catch { }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                        e.Handled = true;
                    }
                }
                catch (Exception doubleClickEx)
                {
                    Log.Debug($"TouchTemplateDesigner: Error in double-click handling: {doubleClickEx.Message}");
                }
            }
        }

        /* Eyedropper methods - no longer used, moved to color picker dialog
        // Keeping for reference in case we want to add canvas-specific color picking later
        private void HandleEyedropperClick(MouseButtonEventArgs e)
        {
            try
            {
                var canvas = DesignerCanvas as SimpleDesignerCanvas;
                if (canvas == null) return;

                var clickPoint = e.GetPosition(DesignerCanvas);
                Log.Debug($"TouchTemplateDesigner: Eyedropper clicked at {clickPoint.X}, {clickPoint.Y}");

                // Find the item at the click point
                SimpleCanvasItem clickedItem = null;
                foreach (var item in canvas.Items)
                {
                    var itemBounds = new Rect(item.Left, item.Top, item.Width, item.Height);
                    Log.Debug($"TouchTemplateDesigner: Checking item at {item.Left}, {item.Top}, {item.Width}x{item.Height}");
                    if (itemBounds.Contains(clickPoint))
                    {
                        clickedItem = item;
                        Log.Debug($"TouchTemplateDesigner: Found clicked item: {item.GetType().Name}");
                        break;
                    }
                }

                if (clickedItem != null)
                {
                    // Extract color from the clicked element
                    var extractedColor = ExtractColorFromElement(clickedItem, clickPoint);
                    _currentSelectedColor = extractedColor;
                    UpdateColorPreview();

                    // Apply the extracted color to the currently selected item (if any)
                    ApplySelectedColor();

                    Log.Debug($"TouchTemplateDesigner: Eyedropper extracted color {extractedColor} from {clickedItem.GetType().Name}");

                    // Exit eyedropper mode after capturing color
                    _isEyedropperMode = false;
                    DesignerCanvas.Cursor = Cursors.Arrow;
                }
                else
                {
                    // Clicked on empty canvas - use canvas background color (white)
                    _currentSelectedColor = Colors.White;
                    UpdateColorPreview();
                    ApplySelectedColor();

                    Log.Debug("TouchTemplateDesigner: Eyedropper clicked on empty canvas, extracted white color");

                    // Exit eyedropper mode
                    _isEyedropperMode = false;
                    DesignerCanvas.Cursor = Cursors.Arrow;
                }

                e.Handled = true;
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to handle eyedropper click: {ex.Message}");

                // Exit eyedropper mode on error
                _isEyedropperMode = false;
                DesignerCanvas.Cursor = Cursors.Arrow;
            }
        }
        */

        private void CheckTextSelectionAndActivateFontControls()
        {
            try
            {
                var simpleCanvas = DesignerCanvas as SimpleDesignerCanvas;
                if (simpleCanvas != null && simpleCanvas.SelectedItem != null)
                {
                    var hasTextItem = simpleCanvas.SelectedItem is SimpleTextItem;

                    if (hasTextItem)
                    {
                        // Use dispatcher to safely activate font controls after selection is stable
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                ActivateFontControls();
                            }
                            catch (Exception ex)
                            {
                                Log.Debug($"TouchTemplateDesigner: Could not activate font controls: {ex.Message}");
                            }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"TouchTemplateDesigner: Error in text selection check: {ex.Message}");
            }
        }

        private void ActivateFontControls()
        {
            try
            {
                // Auto-open font controls panel when text is selected
                ShowFontPanel();

                // Update font controls to match selected text
                var simpleCanvas = DesignerCanvas as SimpleDesignerCanvas;
                if (simpleCanvas != null && simpleCanvas.SelectedItem is SimpleTextItem textItem)
                {
                    // Update FontControlsPanel with the selected text item's properties
                    FontControlsPanel.SetSelectedTextItem(textItem);
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"TouchTemplateDesigner: Error activating font controls: {ex.Message}");
            }
        }

        #endregion

        #region Appearance Tools (Stroke & Shadow)

        private void BackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Toggle dedicated background popup and ensure appearance popup is closed
                if (BackgroundPopup != null)
                {
                    BackgroundPopup.IsOpen = !BackgroundPopup.IsOpen;
                }
                if (AppearancePopup != null && BackgroundPopup != null && BackgroundPopup.IsOpen)
                {
                    AppearancePopup.IsOpen = false;
                }
            }
            catch { }
        }

        private void AppearanceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppearancePopup.IsOpen = !AppearancePopup.IsOpen;
                if (BackgroundPopup != null && AppearancePopup.IsOpen)
                {
                    BackgroundPopup.IsOpen = false;
                }
                if (AppearancePopup.IsOpen)
                {
                    UpdatePreviewRectangle();
                }
            }
            catch { }
        }

        private IEnumerable<SimpleCanvasItem> GetSelectedCanvasItems()
        {
            var canvas = DesignerCanvas as SimpleDesignerCanvas;
            if (canvas == null) yield break;
            foreach (var item in canvas.SelectedItems) yield return item;
        }

        private void ToolbarStrokeColorPicker_ColorChanged(object sender, RoutedEventArgs e)
        {
            if (sender is InlineColorPicker picker)
            {
                var brush = new SolidColorBrush(picker.SelectedColor);
                foreach (var item in GetSelectedCanvasItems())
                {
                    if (item is SimpleTextItem text) text.StrokeBrush = brush;
                    else if (item is SimpleImageItem img) img.StrokeBrush = brush;
                    else if (item is SimpleShapeItem shape) shape.Stroke = brush;
                }
                UpdatePreviewRectangle();
            }
        }

        private void ToolbarStrokeWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ToolbarStrokeWidthText != null)
                ToolbarStrokeWidthText.Text = $"{(int)e.NewValue} px";
            foreach (var item in GetSelectedCanvasItems())
            {
                if (item is SimpleTextItem text) text.StrokeThickness = e.NewValue;
                else if (item is SimpleImageItem img) img.StrokeThickness = e.NewValue;
                else if (item is SimpleShapeItem shape) shape.StrokeThickness = e.NewValue;
            }
            UpdatePreviewRectangle();
        }

        private void ToolbarShadowEnable_Checked(object sender, RoutedEventArgs e)
        {
            ApplyShadowToSelection();
            UpdatePreviewRectangle();
        }

        private void ToolbarShadowEnable_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var item in GetSelectedCanvasItems())
            {
                item.Effect = null;
            }
            UpdatePreviewRectangle();
        }

        private void ToolbarShadowColorPicker_ColorChanged(object sender, RoutedEventArgs e)
        {
            ApplyShadowToSelection();
            UpdatePreviewRectangle();
        }

        private void ToolbarShadowBlurSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ToolbarShadowBlurText != null)
                ToolbarShadowBlurText.Text = $"{(int)e.NewValue} px";
            ApplyShadowToSelection();
            UpdatePreviewRectangle();
        }

        private void ToolbarShadowOffsetXSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ToolbarShadowOffsetXText != null)
                ToolbarShadowOffsetXText.Text = $"{(int)e.NewValue} px";
            ApplyShadowToSelection();
            UpdatePreviewRectangle();
        }

        private void ToolbarShadowOffsetYSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ToolbarShadowOffsetYText != null)
                ToolbarShadowOffsetYText.Text = $"{(int)e.NewValue} px";
            ApplyShadowToSelection();
            UpdatePreviewRectangle();
        }

        private void ApplyShadowToSelection()
        {
            if (ToolbarShadowEnable == null || ToolbarShadowEnable.IsChecked != true)
                return;

            var color = (ToolbarShadowColorPicker != null) ? ToolbarShadowColorPicker.SelectedColor : Colors.Black;
            var blur = ToolbarShadowBlurSlider?.Value ?? 5;
            var offX = ToolbarShadowOffsetXSlider?.Value ?? 2;
            var offY = ToolbarShadowOffsetYSlider?.Value ?? 2;

            // Convert X,Y offsets to Direction and ShadowDepth
            var direction = Math.Atan2(offY, offX) * (180 / Math.PI);
            var depth = Math.Sqrt(offX * offX + offY * offY);

            var effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = color,
                BlurRadius = blur,
                Direction = direction,
                ShadowDepth = depth,
                Opacity = 0.8
            };

            foreach (var item in GetSelectedCanvasItems())
            {
                item.Effect = effect.Clone();
            }
        }

        private void UpdatePreviewRectangle()
        {
            try
            {
                var previewRect = this.FindName("PreviewRectangle") as Rectangle;
                if (previewRect == null) return;

                // Update stroke
                if (ToolbarStrokeColorPicker != null && ToolbarStrokeWidthSlider != null)
                {
                    previewRect.Stroke = new SolidColorBrush(ToolbarStrokeColorPicker.SelectedColor);
                    previewRect.StrokeThickness = ToolbarStrokeWidthSlider.Value;
                }

                // Update shadow
                if (ToolbarShadowEnable != null && ToolbarShadowEnable.IsChecked == true)
                {
                    var color = (ToolbarShadowColorPicker != null) ? ToolbarShadowColorPicker.SelectedColor : Colors.Black;
                    var blur = ToolbarShadowBlurSlider?.Value ?? 10;
                    var offX = ToolbarShadowOffsetXSlider?.Value ?? 5;
                    var offY = ToolbarShadowOffsetYSlider?.Value ?? 5;

                    var direction = Math.Atan2(offY, offX) * (180 / Math.PI);
                    var depth = Math.Sqrt(offX * offX + offY * offY);

                    previewRect.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = color,
                        BlurRadius = blur,
                        Direction = direction,
                        ShadowDepth = depth,
                        Opacity = 0.6
                    };
                }
                else
                {
                    previewRect.Effect = null;
                }
            }
            catch { }
        }

        private void ToolbarPlaceholderNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                var name = ToolbarPlaceholderNameBox?.Text ?? string.Empty;
                foreach (var item in GetSelectedCanvasItems())
                {
                    if (item is SimpleImageItem img && img.IsPlaceholder)
                    {
                        img.PlaceholderName = name;
                    }
                }
            }
            catch { }
        }

        private void ToolbarPlaceholderFillColorPicker_ColorChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is InlineColorPicker picker)
                {
                    var brush = new SolidColorBrush(picker.SelectedColor);
                    foreach (var item in GetSelectedCanvasItems())
                    {
                        if (item is SimpleImageItem img && img.IsPlaceholder)
                        {
                            img.PlaceholderBackground = brush;
                        }
                    }
                }
            }
            catch { }
        }

        private void ToolbarCanvasBgColorPicker_ColorChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is InlineColorPicker picker)
                {
                    var brush = new SolidColorBrush(picker.SelectedColor);
                    var canvas = DesignerCanvas as SimpleDesignerCanvas;
                    if (canvas != null)
                    {
                        canvas.CanvasBackground = brush;
                    }
                    try
                    {
                        // Keep DesignerVM in sync so background color is saved into templates
                        if (_designerVM != null)
                        {
                            _designerVM.CanvasBackgroundColor = brush;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void ToolbarShapeFillColorPicker_ColorChanged(object sender, RoutedEventArgs e)
        {
            if (sender is InlineColorPicker picker)
            {
                var brush = new SolidColorBrush(picker.SelectedColor);
                foreach (var item in GetSelectedCanvasItems())
                {
                    if (item is SimpleShapeItem shape)
                    {
                        shape.Fill = brush;
                    }
                }
            }
        }

        #endregion

        private void Canvas_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.ManipulationContainer = CanvasScrollViewer;
            e.Mode = ManipulationModes.All;
        }

        private void Canvas_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            // Handle pinch to zoom
            if (e.DeltaManipulation.Scale.X != 1.0 || e.DeltaManipulation.Scale.Y != 1.0)
            {
                double zoomDelta = (e.DeltaManipulation.Scale.X + e.DeltaManipulation.Scale.Y) / 2;
                _currentZoom *= zoomDelta;
                _currentZoom = Math.Max(0.1, Math.Min(5.0, _currentZoom));
                UpdateZoom();
            }
        }

        #region File Operations

        private void NewTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_designerVM.HasUnsavedChanges)
                {
                    var result = MessageBox.Show("Create a new template? Any unsaved changes will be lost.",
                        "New Template", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                        return;
                }

                // Clear our SimpleDesignerCanvas directly (DesignerVM's CustomDesignerCanvas is not connected to our canvas)
                var canvas = DesignerCanvas as SimpleDesignerCanvas;
                if (canvas != null)
                {
                    canvas.Items.Clear();
                }
                SimpleImageItem.ResetPlaceholderCounter(); // Reset placeholder counter for new template

                // Auto-name template after selected event (use current event if none selected in designer)
                string templateName = "Untitled";
                var eventToUse = _selectedEvent ?? EventSelectionService.Instance.SelectedEvent;
                if (eventToUse != null && eventToUse.Id > 0)
                {
                    // Generate template name based on event name and current date/time
                    var timestamp = DateTime.Now.ToString("MMdd_HHmm");
                    templateName = $"{eventToUse.Name}_{timestamp}";
                    _selectedEvent = eventToUse; // Update the selected event
                }
                TemplateNameText.Text = templateName;

                // Auto-save the new template immediately
                AutoSaveNewTemplate(templateName);

                // Reset auto-save state for new template
                _hasUnsavedChanges = false;
                _lastSaveTime = DateTime.Now;
                UpdateAutoSaveIndicator();

                Log.Debug("TouchTemplateDesigner: Created new template");
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to create new template: {ex.Message}");
                MessageBox.Show($"Failed to create new template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region Auto-Save Functionality

        private void InitializeAutoSave()
        {
            _autoSaveTimer = new System.Windows.Threading.DispatcherTimer();
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(AUTO_SAVE_INTERVAL_SECONDS);
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            _autoSaveTimer.Start();

            // Subscribe to canvas change events
            var canvas = DesignerCanvas as SimpleDesignerCanvas;
            if (canvas != null)
            {
                canvas.Items.CollectionChanged += Canvas_Items_CollectionChanged;
            }
        }

        private void Canvas_Items_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            MarkAsChanged();

            // Subscribe to property changes on new items
            if (e.NewItems != null)
            {
                foreach (SimpleCanvasItem item in e.NewItems)
                {
                    item.PropertyChanged += CanvasItem_PropertyChanged;
                }
            }

            // Unsubscribe from removed items
            if (e.OldItems != null)
            {
                foreach (SimpleCanvasItem item in e.OldItems)
                {
                    item.PropertyChanged -= CanvasItem_PropertyChanged;
                }
            }
        }

        private void CanvasItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Mark as changed for any property change except selection
            if (e.PropertyName != "IsSelected")
            {
                MarkAsChanged();
            }
        }

        private void MarkAsChanged()
        {
            _hasUnsavedChanges = true;
            UpdateAutoSaveIndicator();
        }

        private void AutoSaveTimer_Tick(object sender, EventArgs e)
        {
            if (_hasUnsavedChanges && _currentTemplateId > 0)
            {
                // Only auto-save if we have an existing template
                PerformAutoSave();
            }
        }

        private void AutoSaveNewTemplate(string templateName)
        {
            try
            {
                var canvas = DesignerCanvas as SimpleDesignerCanvas;
                if (canvas == null) return;

                Log.Debug($"★★★ AutoSaveNewTemplate: Canvas dimensions at save time: {canvas.Width}x{canvas.Height} ★★★");
                Log.Debug($"★★★ AutoSaveNewTemplate: _desiredCanvasWidth={_desiredCanvasWidth}, _desiredCanvasHeight={_desiredCanvasHeight} ★★★");

                // Generate both thumbnail and full preview
                string thumbnailPath = GenerateTemplateThumbnail(canvas, templateName);
                string previewPath = GenerateTemplatePreview(canvas, templateName);

                // Create template data
                var database = new TemplateDatabase();
                var template = new TemplateData
                {
                    Name = templateName,
                    CanvasWidth = canvas.Width,
                    CanvasHeight = canvas.Height,
                    CanvasItems = new List<CanvasItemData>(),
                    BackgroundImagePath = GetCanvasBackgroundPath(),
                    ThumbnailImagePath = thumbnailPath ?? previewPath,  // Use preview as fallback
                    CreatedDate = DateTime.Now,
                    ModifiedDate = DateTime.Now,
                    IsActive = true
                };

                // Save to database
                _currentTemplateId = database.SaveTemplate(template);

                // Auto-assign to event if one is selected
                if (_selectedEvent != null && _currentTemplateId > 0)
                {
                    _eventService.AssignTemplateToEvent(_selectedEvent.Id, _currentTemplateId, false);
                    ShowAutoSaveNotification($"Template '{templateName}' created for event '{_selectedEvent.Name}'");
                }
                else
                {
                    ShowAutoSaveNotification($"Template '{templateName}' created");
                }

                Log.Debug($"TouchTemplateDesigner: Auto-saved new template '{templateName}' with ID {_currentTemplateId}");
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to auto-save new template: {ex.Message}");
            }
        }

        private string GetCanvasBackgroundPath()
        {
            // Try to get the background image path from the canvas
            var canvas = DesignerCanvas as SimpleDesignerCanvas;
            if (canvas?.CanvasBackground is ImageBrush imageBrush)
            {
                if (imageBrush.ImageSource is BitmapImage bitmapImage)
                {
                    return bitmapImage.UriSource?.LocalPath ?? string.Empty;
                }
            }
            return string.Empty;
        }

        private async void PerformAutoSave()
        {
            try
            {
                var canvas = DesignerCanvas as SimpleDesignerCanvas;
                if (canvas == null) return;

                // Auto-create template if it hasn't been saved yet
                if (_currentTemplateId <= 0)
                {
                    // Only auto-create if user has made changes:
                    // - Added items to canvas, OR
                    // - Explicitly set a paper size (indicated by _desiredCanvasWidth being set)
                    bool hasItems = canvas.Items.Count > 0;
                    bool hasPaperSizeSet = _desiredCanvasWidth.HasValue && _desiredCanvasHeight.HasValue;

                    if (!hasItems && !hasPaperSizeSet)
                    {
                        Log.Debug("TouchTemplateDesigner: Skipping auto-save - no items and no paper size set");
                        return;
                    }

                    string templateName = TemplateNameText.Text;
                    if (string.IsNullOrWhiteSpace(templateName) || templateName == "Untitled")
                    {
                        if (_selectedEvent != null)
                        {
                            var timestamp = DateTime.Now.ToString("MMdd_HHmm");
                            templateName = $"{_selectedEvent.Name}_{timestamp}";
                            TemplateNameText.Text = templateName;
                        }
                        else
                        {
                            templateName = $"Template_{DateTime.Now:MMdd_HHmm}";
                            TemplateNameText.Text = templateName;
                        }
                    }
                    AutoSaveNewTemplate(templateName);
                    return;
                }

                if (canvas.Items.Count == 0) return;

                var database = new TemplateDatabase();
                var template = database.GetTemplate(_currentTemplateId);
                if (template == null) return;

                // Clear existing canvas items in database
                database.DeleteCanvasItems(_currentTemplateId);

                // Save all current canvas items
                foreach (var item in canvas.Items.OrderBy(i => i.ZIndex))
                {
                    var canvasItem = ConvertToCanvasItemData(item, _currentTemplateId);
                    database.SaveCanvasItem(canvasItem);
                }

                // Regenerate thumbnail periodically (every 5 saves) or if significant changes
                if (_autoSaveCount % 5 == 0 || canvas.Items.Count != _lastItemCount)
                {
                    string newThumbnailPath = GenerateTemplateThumbnail(canvas, template.Name);
                    if (!string.IsNullOrEmpty(newThumbnailPath))
                    {
                        template.ThumbnailImagePath = newThumbnailPath;
                        database.UpdateTemplate(_currentTemplateId, template);
                    }
                    _lastItemCount = canvas.Items.Count;
                }
                _autoSaveCount++;

                _hasUnsavedChanges = false;
                _lastSaveTime = DateTime.Now;
                UpdateAutoSaveIndicator();

                // Show subtle auto-save notification
                ShowAutoSaveNotification("Auto-saved");

                Log.Debug($"TouchTemplateDesigner: Auto-saved template ID {_currentTemplateId}");
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Auto-save failed: {ex.Message}");
                // Don't show error messages for auto-save failures to avoid disrupting the user
            }
        }

        private CanvasItemData ConvertToCanvasItemData(SimpleCanvasItem item, int templateId)
        {
            var canvasItem = new CanvasItemData
            {
                TemplateId = templateId,
                X = item.Left,
                Y = item.Top,
                Width = item.Width,
                Height = item.Height,
                ZIndex = item.ZIndex,
                Rotation = item.RotationAngle,
                Opacity = item.Opacity,
                IsLocked = false,
                IsVisible = true
            };

            if (item is SimpleImageItem imageItem)
            {
                canvasItem.ItemType = imageItem.IsPlaceholder ? "Placeholder" : "Image";
                canvasItem.Name = imageItem.PlaceholderName ?? "";
                canvasItem.PlaceholderNumber = imageItem.PlaceholderNumber;
                canvasItem.ImagePath = imageItem.ImagePath;

                if (imageItem.IsPlaceholder && imageItem.PlaceholderBackground is SolidColorBrush brush)
                {
                    canvasItem.PlaceholderColor = brush.Color.ToString();
                }

                // Save stroke properties for images
                if (imageItem.StrokeBrush is SolidColorBrush strokeBrush)
                {
                    canvasItem.StrokeColor = strokeBrush.Color.ToString();
                }
                canvasItem.StrokeThickness = imageItem.StrokeThickness;
            }
            else if (item is SimpleTextItem textItem)
            {
                canvasItem.ItemType = "Text";
                canvasItem.Name = "Text";  // Set a default name for text items
                canvasItem.Text = textItem.Text;
                canvasItem.FontFamily = textItem.FontFamily?.Source ?? textItem.FontFamily?.ToString() ?? "Arial";
                canvasItem.FontSize = textItem.FontSize;
                canvasItem.FontWeight = textItem.FontWeight.ToString();
                canvasItem.FontStyle = textItem.FontStyle.ToString();
                canvasItem.TextAlignment = textItem.TextAlignment.ToString();
                canvasItem.IsBold = textItem.FontWeight == FontWeights.Bold;
                canvasItem.IsItalic = textItem.FontStyle == FontStyles.Italic;

                // Get text color from either TextColor property or Foreground
                if (textItem.TextColor is SolidColorBrush textColorBrush)
                {
                    canvasItem.TextColor = textColorBrush.Color.ToString();
                }
                else if (textItem.Foreground is SolidColorBrush textBrush)
                {
                    canvasItem.TextColor = textBrush.Color.ToString();
                }

                // Persist outline from stroke settings - save as both Outline and Stroke for compatibility
                if (textItem.StrokeThickness > 0 && textItem.StrokeBrush is SolidColorBrush outlineBrush)
                {
                    canvasItem.HasOutline = true;
                    canvasItem.OutlineThickness = textItem.StrokeThickness;
                    canvasItem.OutlineColor = outlineBrush.Color.ToString();
                    // Also save as stroke for consistency
                    canvasItem.StrokeColor = outlineBrush.Color.ToString();
                    canvasItem.StrokeThickness = textItem.StrokeThickness;
                }
                else
                {
                    canvasItem.HasOutline = false;
                    canvasItem.OutlineThickness = 0;
                    canvasItem.OutlineColor = null;
                    canvasItem.StrokeColor = null;
                    canvasItem.StrokeThickness = 0;
                }

                // Note: TextDecorations/Underline not currently supported in SimpleTextItem
                // This would need to be added to SimpleTextItem if underline support is needed
                canvasItem.IsUnderlined = false;
            }
            else if (item is SimpleShapeItem shapeItem)
            {
                canvasItem.ItemType = "Shape";
                canvasItem.Name = shapeItem.ShapeType.ToString();  // Use shape type as name
                canvasItem.ShapeType = shapeItem.ShapeType.ToString();

                if (shapeItem.Fill is SolidColorBrush fillBrush)
                {
                    canvasItem.FillColor = fillBrush.Color.ToString();
                }
                if (shapeItem.Stroke is SolidColorBrush strokeBrush)
                {
                    canvasItem.StrokeColor = strokeBrush.Color.ToString();
                }

                canvasItem.StrokeThickness = shapeItem.StrokeThickness;
            }
            else if (item is SimpleQRCodeItem qrItem)
            {
                canvasItem.ItemType = "QRCode";
                canvasItem.Name = "QR Code";
                var val = qrItem.Value ?? string.Empty;
                var ecc = qrItem.EccLevel.ToString();
                var ppm = qrItem.PixelsPerModule;
                canvasItem.CustomProperties = $"{{\"Value\":\"{EscapeJson(val)}\",\"ECC\":\"{EscapeJson(ecc)}\",\"PixelsPerModule\":{ppm}}}";
            }
            else if (item is SimpleBarcodeItem bcItem)
            {
                canvasItem.ItemType = "Barcode";
                canvasItem.Name = "Barcode";
                var val = bcItem.Value ?? string.Empty;
                var sym = bcItem.Symbology.ToString();
                var mw = bcItem.ModuleWidth;
                var lbl = bcItem.IncludeLabel ? "true" : "false";
                canvasItem.CustomProperties = $"{{\"Value\":\"{EscapeJson(val)}\",\"Symbology\":\"{EscapeJson(sym)}\",\"ModuleWidth\":{mw},\"IncludeLabel\":{lbl}}}";
            }

            // Check for shadow effects on any item type
            if (item.Effect is System.Windows.Media.Effects.DropShadowEffect shadow)
            {
                canvasItem.HasShadow = true;
                canvasItem.ShadowColor = shadow.Color.ToString();
                canvasItem.ShadowBlurRadius = shadow.BlurRadius;
                canvasItem.ShadowOpacity = shadow.Opacity;

                // Calculate X,Y offsets from Direction and ShadowDepth
                var radians = shadow.Direction * (Math.PI / 180);
                canvasItem.ShadowOffsetX = Math.Cos(radians) * shadow.ShadowDepth;
                canvasItem.ShadowOffsetY = Math.Sin(radians) * shadow.ShadowDepth;
            }
            else
            {
                canvasItem.HasShadow = false;
                canvasItem.ShadowOffsetX = 0;
                canvasItem.ShadowOffsetY = 0;
                canvasItem.ShadowBlurRadius = 0;
                canvasItem.ShadowOpacity = 1.0;
                canvasItem.ShadowColor = null;
            }

            return canvasItem;
        }

        private void UpdateAutoSaveIndicator()
        {
            // Update UI to show auto-save status
            // This could be a status text or icon in the UI
            Dispatcher.Invoke(() =>
            {
                if (_hasUnsavedChanges)
                {
                    TemplateNameText.Foreground = new SolidColorBrush(Colors.Orange);
                }
                else
                {
                    TemplateNameText.Foreground = new SolidColorBrush(Colors.White);
                }
            });
        }

        public void SaveBeforeClose()
        {
            // Public method that can be called before closing
            SaveOnExit();
        }

        private void SaveOnExit()
        {
            try
            {
                // Only save if there are unsaved changes and we have a template ID
                if (_hasUnsavedChanges && _currentTemplateId > 0)
                {
                    Log.Debug("TouchTemplateDesigner: Saving changes on exit...");

                    // Perform a synchronous save before exit
                    var canvas = DesignerCanvas as SimpleDesignerCanvas;
                    if (canvas == null) return;

                    var database = new TemplateDatabase();

                    // Clear existing canvas items for this template
                    database.DeleteCanvasItems(_currentTemplateId);

                    // Save all canvas items
                    foreach (var item in canvas.Items.OrderBy(i => i.ZIndex))
                    {
                        var canvasItem = ConvertToCanvasItemData(item, _currentTemplateId);
                        database.SaveCanvasItem(canvasItem);
                    }

                    _hasUnsavedChanges = false;
                    Log.Debug($"TouchTemplateDesigner: Successfully saved template on exit (ID: {_currentTemplateId})");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to save on exit: {ex.Message}");
                // Don't show error message on exit to avoid blocking the close
            }
        }

        private void StopAutoSave()
        {
            if (_autoSaveTimer != null)
            {
                _autoSaveTimer.Stop();
                _autoSaveTimer.Tick -= AutoSaveTimer_Tick;
            }

            // Unsubscribe from canvas events
            var canvas = DesignerCanvas as SimpleDesignerCanvas;
            if (canvas != null)
            {
                canvas.Items.CollectionChanged -= Canvas_Items_CollectionChanged;

                foreach (var item in canvas.Items)
                {
                    item.PropertyChanged -= CanvasItem_PropertyChanged;
                }
            }
        }

        #endregion

        // Save button removed - using auto-save only
        // Keeping method for reference but it's no longer called from UI
        private async void SaveTemplate_Click(object sender, RoutedEventArgs e)
        {
            // This method is deprecated - auto-save handles all saving
            return;

            /*
            try
            {
                // Pre-fill with event-based name if current name is Untitled
                string defaultName = TemplateNameText.Text;
                if ((string.IsNullOrWhiteSpace(defaultName) || defaultName == "Untitled") && _selectedEvent != null)
                {
                    var timestamp = DateTime.Now.ToString("MMdd_HHmm");
                    defaultName = $"{_selectedEvent.Name}_{timestamp}";
                }

                var templateName = ShowInputDialog("Save Template", "Enter template name:", defaultName);

                if (string.IsNullOrWhiteSpace(templateName))
                    return;

                var canvas = DesignerCanvas as SimpleDesignerCanvas;
                if (canvas == null)
                {
                    MessageBox.Show("Unable to access designer canvas", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Generate thumbnail before saving
                string thumbnailPath = GenerateTemplateThumbnail(canvas, templateName);

                // Create template data from SimpleDesignerCanvas
                var template = new TemplateData
                {
                    Name = templateName,
                    CanvasWidth = canvas.Width,
                    CanvasHeight = canvas.Height,
                    ThumbnailImagePath = thumbnailPath,
                    CanvasItems = new List<CanvasItemData>()
                };

                // Convert SimpleCanvasItems to CanvasItemData
                foreach (var item in canvas.Items.OrderBy(i => i.ZIndex))
                {
                    var canvasItem = new CanvasItemData
                    {
                        X = item.Left,
                        Y = item.Top,
                        Width = item.Width,
                        Height = item.Height,
                        ZIndex = item.ZIndex,
                        Rotation = item.RotationAngle,
                        Opacity = item.Opacity,
                        IsLocked = false,
                        IsVisible = true
                    };

                    if (item is SimpleImageItem imageItem)
                    {
                        canvasItem.ItemType = imageItem.IsPlaceholder ? "Placeholder" : "Image";
                        canvasItem.Name = imageItem.PlaceholderName ?? "";
                        canvasItem.PlaceholderNumber = imageItem.PlaceholderNumber;
                        canvasItem.ImagePath = imageItem.ImagePath;

                        // Get the placeholder background color if it's a placeholder
                        if (imageItem.IsPlaceholder && imageItem.PlaceholderBackground is SolidColorBrush brush)
                        {
                            canvasItem.PlaceholderColor = brush.Color.ToString();
                        }

                        // Save stroke properties for images
                        if (imageItem.StrokeBrush is SolidColorBrush strokeBrush)
                        {
                            canvasItem.StrokeColor = strokeBrush.Color.ToString();
                        }
                        canvasItem.StrokeThickness = imageItem.StrokeThickness;
                    }
                    else if (item is SimpleTextItem textItem)
                    {
                        canvasItem.ItemType = "Text";
                        canvasItem.Name = "Text";  // Set a default name for text items
                        canvasItem.Text = textItem.Text;
                        canvasItem.FontFamily = textItem.FontFamily?.Source ?? textItem.FontFamily?.ToString() ?? "Arial";
                        canvasItem.FontSize = textItem.FontSize;
                        canvasItem.FontWeight = textItem.FontWeight.ToString();
                        canvasItem.FontStyle = textItem.FontStyle.ToString();
                        canvasItem.TextAlignment = textItem.TextAlignment.ToString();
                        canvasItem.IsBold = textItem.FontWeight == FontWeights.Bold;
                        canvasItem.IsItalic = textItem.FontStyle == FontStyles.Italic;

                        // Get text color from either TextColor property or Foreground
                        if (textItem.TextColor is SolidColorBrush textColorBrush)
                        {
                            canvasItem.TextColor = textColorBrush.Color.ToString();
                        }
                        else if (textItem.Foreground is SolidColorBrush textBrush)
                        {
                            canvasItem.TextColor = textBrush.Color.ToString();
                        }

                        // Persist outline settings from stroke - save as both Outline and Stroke for compatibility
                        if (textItem.StrokeThickness > 0 && textItem.StrokeBrush is SolidColorBrush outlineBrush)
                        {
                            canvasItem.HasOutline = true;
                            canvasItem.OutlineThickness = textItem.StrokeThickness;
                            canvasItem.OutlineColor = outlineBrush.Color.ToString();
                            // Also save as stroke for consistency
                            canvasItem.StrokeColor = outlineBrush.Color.ToString();
                            canvasItem.StrokeThickness = textItem.StrokeThickness;
                        }
                        else
                        {
                            canvasItem.HasOutline = false;
                            canvasItem.OutlineThickness = 0;
                            canvasItem.OutlineColor = null;
                            canvasItem.StrokeColor = null;
                            canvasItem.StrokeThickness = 0;
                        }

                        // Note: TextDecorations/Underline not currently supported in SimpleTextItem
                        // This would need to be added to SimpleTextItem if underline support is needed
                        canvasItem.IsUnderlined = false;
                    }
                    else if (item is SimpleShapeItem shapeItem)
                    {
                        canvasItem.ItemType = "Shape";
                        canvasItem.Name = shapeItem.ShapeType.ToString();  // Use shape type as name
                        canvasItem.ShapeType = shapeItem.ShapeType.ToString();

                        if (shapeItem.Fill is SolidColorBrush fillBrush)
                            canvasItem.FillColor = fillBrush.Color.ToString();
                        if (shapeItem.Stroke is SolidColorBrush strokeBrush)
                            canvasItem.StrokeColor = strokeBrush.Color.ToString();
                        canvasItem.StrokeThickness = shapeItem.StrokeThickness;
                    }
                    else if (item is SimpleQRCodeItem qrItem)
                    {
                        canvasItem.ItemType = "QRCode";
                        canvasItem.Name = "QR Code";
                        var val = qrItem.Value ?? string.Empty;
                        var ecc = qrItem.EccLevel.ToString();
                        var ppm = qrItem.PixelsPerModule;
                        canvasItem.CustomProperties = $"{{\"Value\":\"{EscapeJson(val)}\",\"ECC\":\"{EscapeJson(ecc)}\",\"PixelsPerModule\":{ppm}}}";
                    }
                    else if (item is SimpleBarcodeItem bcItem)
                    {
                        canvasItem.ItemType = "Barcode";
                        canvasItem.Name = "Barcode";
                        var val = bcItem.Value ?? string.Empty;
                        var sym = bcItem.Symbology.ToString();
                        var mw = bcItem.ModuleWidth;
                        var lbl = bcItem.IncludeLabel ? "true" : "false";
                        canvasItem.CustomProperties = $"{{\"Value\":\"{EscapeJson(val)}\",\"Symbology\":\"{EscapeJson(sym)}\",\"ModuleWidth\":{mw},\"IncludeLabel\":{lbl}}}";
                    }

                    // Check for shadow effects on any item type
                    if (item.Effect is System.Windows.Media.Effects.DropShadowEffect shadow)
                    {
                        canvasItem.HasShadow = true;
                        canvasItem.ShadowColor = shadow.Color.ToString();
                        canvasItem.ShadowBlurRadius = shadow.BlurRadius;
                        canvasItem.ShadowOpacity = shadow.Opacity;

                        // Calculate X,Y offsets from Direction and ShadowDepth
                        var radians = shadow.Direction * (Math.PI / 180);
                        canvasItem.ShadowOffsetX = Math.Cos(radians) * shadow.ShadowDepth;
                        canvasItem.ShadowOffsetY = Math.Sin(radians) * shadow.ShadowDepth;
                    }
                    else
                    {
                        canvasItem.HasShadow = false;
                        canvasItem.ShadowOffsetX = 0;
                        canvasItem.ShadowOffsetY = 0;
                        canvasItem.ShadowBlurRadius = 0;
                        canvasItem.ShadowOpacity = 1.0;
                        canvasItem.ShadowColor = null;
                    }

                    template.CanvasItems.Add(canvasItem);
                }

                // Save to database
                var database = new TemplateDatabase();
                int templateId;

                if (_currentTemplateId > 0)
                {
                    // Update existing template
                    template.Id = _currentTemplateId;
                    database.UpdateTemplate(_currentTemplateId, template);
                    templateId = _currentTemplateId;

                    // Clear existing canvas items for this template
                    database.DeleteCanvasItems(templateId);
                }
                else
                {
                    // Save new template
                    templateId = database.SaveTemplate(template);
                    _currentTemplateId = templateId;
                }

                // Save all canvas items
                foreach (var canvasItem in template.CanvasItems)
                {
                    canvasItem.TemplateId = templateId;
                    database.SaveCanvasItem(canvasItem);
                }

                // Assign to event if checkbox is checked and event is selected
                if ((AssignToEventCheckBox as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked == true && _selectedEvent != null)
                {
                    // Check if template is already assigned to this event
                    var eventTemplates = _eventService.GetEventTemplates(_selectedEvent.Id);
                    bool alreadyAssigned = eventTemplates.Any(t => t.Id == templateId);

                    if (!alreadyAssigned)
                    {
                        _eventService.AssignTemplateToEvent(_selectedEvent.Id, templateId, false);
                    }

                    MessageBox.Show($"Template '{templateName}' saved successfully.",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Template '{templateName}' saved successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }

                TemplateNameText.Text = templateName;

                // Reset auto-save flags after successful manual save
                _hasUnsavedChanges = false;
                _lastSaveTime = DateTime.Now;
                UpdateAutoSaveIndicator();

                Log.Debug($"TouchTemplateDesigner: Saved template '{templateName}' with ID {templateId}");
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to save template: {ex.Message}");
                MessageBox.Show($"Error saving template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            */
        }

        private void LoadTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log.Debug("TouchTemplateDesigner: Opening Template Browser Overlay");

                // Show the template browser overlay
                TemplateBrowserOverlay.Visibility = Visibility.Visible;
                TemplateBrowserOverlay.ShowOverlay(_currentTemplateId);

                // Handle template selection
                TemplateBrowserOverlay.TemplateSelected -= OnTemplateSelected;
                TemplateBrowserOverlay.TemplateSelected += OnTemplateSelected;

                // Handle cancellation
                TemplateBrowserOverlay.SelectionCancelled -= OnTemplateSelectionCancelled;
                TemplateBrowserOverlay.SelectionCancelled += OnTemplateSelectionCancelled;
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to open template browser: {ex.Message}");
                MessageBox.Show($"Failed to open template browser: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void LoadTemplate(int templateId)
        {
            try
            {
                Log.Debug($"TouchTemplateDesigner: Loading template ID {templateId}");

                var database = new TemplateDatabase();
                var template = database.GetTemplate(templateId);
                if (template != null)
                {
                    OnTemplateSelected(this, template);
                }
                else
                {
                    Log.Debug($"TouchTemplateDesigner: Template ID {templateId} not found");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to load template ID {templateId}: {ex.Message}");
                MessageBox.Show($"Failed to load template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnTemplateSelected(object sender, TemplateData template)
        {
            try
            {
                Log.Debug($"TouchTemplateDesigner: Loading template {template.Name}");

                // Update template name display
                TemplateNameText.Text = template.Name ?? "Untitled";

                // Store current template ID for future reference
                _currentTemplateId = template.Id;

                // Reset auto-save state when loading a template
                _hasUnsavedChanges = false;
                _lastSaveTime = DateTime.Now;
                UpdateAutoSaveIndicator();

                // Check if this template is assigned to the current event
                _suppressCheckboxEvents = true;
                if (_selectedEvent != null && _selectedEvent.Id > 0)
                {
                    var eventTemplates = _eventService.GetEventTemplates(_selectedEvent.Id);
                    (AssignToEventCheckBox as System.Windows.Controls.Primitives.ToggleButton).IsChecked = eventTemplates.Any(t => t.Id == template.Id);
                }
                else
                {
                    (AssignToEventCheckBox as System.Windows.Controls.Primitives.ToggleButton).IsChecked = false;
                }
                _suppressCheckboxEvents = false;

                // Don't call _designerVM.LoadFromDbCmd as it will cause NullReferenceException
                // Instead, load the template directly from database and apply to our canvas
                try
                {
                    // Get the template data from the database
                    var database = new TemplateDatabase();
                    var templateData = database.GetTemplate(template.Id);
                    if (templateData != null)
                    {
                        // Convert TemplateData to Models.Template format
                        var convertedTemplate = ConvertTemplateDataToTemplate(templateData);
                        if (convertedTemplate != null)
                        {
                            // Apply the template to our SimpleDesignerCanvas
                            ApplyTemplateFromDesignerVM(convertedTemplate);

                            // Don't set _designerVM.CurrentTemplate as it triggers ApplyTemplate which causes NullReferenceException
                            // We've already loaded and applied the template to our canvas directly

                            Log.Debug($"TouchTemplateDesigner: Successfully loaded template {template.Name}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"TouchTemplateDesigner: Failed to load template: {ex.Message}");
                    MessageBox.Show($"Failed to load template: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }

                // Update canvas size display
                UpdateCanvasSizeDisplay();

                // Update orientation from loaded canvas dimensions
                UpdateOrientationFromCanvas();

                Log.Debug($"TouchTemplateDesigner: Template {template.Name} loaded successfully");
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to load template: {ex.Message}");
                MessageBox.Show($"Failed to load template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnTemplateSelectionCancelled(object sender, EventArgs e)
        {
            Log.Debug("TouchTemplateDesigner: Template selection cancelled");
        }

        private void UpdateCanvasSizeDisplay()
        {
            if (CanvasSizeText != null && DesignerCanvas != null)
            {
                var width = DesignerCanvas.Width;
                var height = DesignerCanvas.Height;
                var widthInches = width / 300.0;
                var heightInches = height / 300.0;
                CanvasSizeText.Text = $"{width:0} x {height:0} px ({widthInches:0.#} x {heightInches:0.#} in)";
            }
        }

        private void ShowCustomSizeDialog()
        {
            try
            {
                var widthStr = ShowInputDialog("Custom Canvas Size", "Enter width in pixels (or inches with 'in' suffix):", "1200");
                if (string.IsNullOrWhiteSpace(widthStr)) return;

                var heightStr = ShowInputDialog("Custom Canvas Size", "Enter height in pixels (or inches with 'in' suffix):", "1800");
                if (string.IsNullOrWhiteSpace(heightStr)) return;

                double width, height;

                // Parse width (handle inches)
                if (widthStr.EndsWith("in", StringComparison.OrdinalIgnoreCase))
                {
                    var inchStr = widthStr.Substring(0, widthStr.Length - 2).Trim();
                    if (double.TryParse(inchStr, out double widthInches))
                        width = widthInches * 300; // Convert to pixels at 300 DPI
                    else
                        return;
                }
                else
                {
                    if (!double.TryParse(widthStr, out width))
                        return;
                }

                // Parse height (handle inches)
                if (heightStr.EndsWith("in", StringComparison.OrdinalIgnoreCase))
                {
                    var inchStr = heightStr.Substring(0, heightStr.Length - 2).Trim();
                    if (double.TryParse(inchStr, out double heightInches))
                        height = heightInches * 300; // Convert to pixels at 300 DPI
                    else
                        return;
                }
                else
                {
                    if (!double.TryParse(heightStr, out height))
                        return;
                }

                if (width > 0 && height > 0 && width <= 10000 && height <= 10000)
                {
                    SetCanvasSize(width, height);
                    UpdateTemplateDimensions();
                }
                else
                {
                    MessageBox.Show("Please enter valid dimensions between 1 and 10000 pixels.",
                        "Invalid Size", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to set custom canvas size: {ex.Message}");
            }
        }

        private async void ImportTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "Template Package (*.zip)|*.zip|Template files (*.template, *.xml)|*.template;*.xml|All files (*.*)|*.*",
                    Title = "Import Template"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var extension = System.IO.Path.GetExtension(openFileDialog.FileName).ToLower();

                    if (extension == ".zip")
                    {
                        // Import ZIP package with assets
                        await ImportTemplatePackage(openFileDialog.FileName);
                    }
                    else
                    {
                        // Legacy import (just the template file)
                        _designerVM.LoadTemplateCmd.Execute(openFileDialog.FileName);

                        // Update display
                        var templateName = System.IO.Path.GetFileNameWithoutExtension(openFileDialog.FileName);
                        TemplateNameText.Text = templateName;
                        UpdateCanvasSizeDisplay();

                        Log.Debug($"TouchTemplateDesigner: Imported template from {openFileDialog.FileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to import template: {ex.Message}");
                MessageBox.Show($"Failed to import template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ImportTemplatePackage(string zipFilePath)
        {
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"template_import_{Guid.NewGuid()}");

            try
            {
                // Extract ZIP to temporary directory
                ZipFile.ExtractToDirectory(zipFilePath, tempDir);

                // Read template JSON
                var templateJsonPath = System.IO.Path.Combine(tempDir, "template.json");
                if (!System.IO.File.Exists(templateJsonPath))
                {
                    throw new FileNotFoundException("template.json not found in package");
                }

                var json = System.IO.File.ReadAllText(templateJsonPath);
                var templateData = JsonConvert.DeserializeObject<TemplateData>(json);

                // Copy assets to application's assets folder
                var assetsSourceDir = System.IO.Path.Combine(tempDir, "assets");
                if (System.IO.Directory.Exists(assetsSourceDir))
                {
                    await ImportAssets(assetsSourceDir, templateData);
                }

                // Clear canvas and apply template
                var canvas = this.DesignerCanvas as SimpleDesignerCanvas;
                if (canvas == null) return;
                canvas.Items.Clear();
                TemplateNameText.Text = templateData.Name;

                // Set canvas size
                canvas.Width = templateData.CanvasWidth;
                canvas.Height = templateData.CanvasHeight;
                UpdateCanvasSizeDisplay();

                // Set background color
                if (!string.IsNullOrEmpty(templateData.BackgroundColor))
                {
                    try
                    {
                        var color = (Color)ColorConverter.ConvertFromString(templateData.BackgroundColor);
                        // Apply to SimpleDesignerCanvas background
                        canvas.CanvasBackground = new SolidColorBrush(color);
                    }
                    catch { /* Ignore color conversion errors */ }
                }

                // Recreate canvas items - sort by Z-index to maintain proper layer order
                if (templateData.CanvasItems != null)
                {
                    // Sort items by Z-index to ensure proper layering
                    var sortedItems = templateData.CanvasItems.OrderBy(item => item.ZIndex).ToList();

                    foreach (var itemData in sortedItems)
                    {
                        await CreateCanvasItemFromData(itemData, templateData.AssetMappings);
                    }
                }

                ShowAutoSaveNotification($"Template '{templateData.Name}' imported successfully!");
                Log.Debug($"TouchTemplateDesigner: Imported template package from {zipFilePath}");
            }
            finally
            {
                // Clean up temporary directory
                if (Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch { /* Ignore cleanup errors */ }
                }
            }
        }

        private async Task ImportAssets(string assetsSourceDir, TemplateData templateData)
        {
            // Create app assets directory if it doesn't exist
            var appAssetsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TemplateAssets");
            if (!System.IO.Directory.Exists(appAssetsDir))
            {
                System.IO.Directory.CreateDirectory(appAssetsDir);
            }

            // Copy all assets and update mappings
            if (templateData.AssetMappings != null)
            {
                var newMappings = new Dictionary<string, string>();

                foreach (var mapping in templateData.AssetMappings)
                {
                    var sourceFile = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(assetsSourceDir), mapping.Value.Replace('/', '\\'));
                    if (System.IO.File.Exists(sourceFile))
                    {
                        var fileName = System.IO.Path.GetFileName(sourceFile);
                        var destFile = System.IO.Path.Combine(appAssetsDir, fileName);

                        // Copy with unique name if file exists
                        if (System.IO.File.Exists(destFile))
                        {
                            var uniqueName = $"{System.IO.Path.GetFileNameWithoutExtension(fileName)}_{Guid.NewGuid():N}{System.IO.Path.GetExtension(fileName)}";
                            destFile = System.IO.Path.Combine(appAssetsDir, uniqueName);
                        }

                        System.IO.File.Copy(sourceFile, destFile, true);
                        newMappings[mapping.Value] = destFile;
                    }
                }

                // Update template data with new local paths
                templateData.AssetMappings = newMappings;
            }
        }

        private async Task CreateCanvasItemFromData(CanvasItemData itemData, Dictionary<string, string> assetMappings)
        {
            SimpleCanvasItem canvasItem = null;

            // Support both old and new type names
            var type = (itemData.ItemType ?? string.Empty).Trim();
            switch (type)
            {
                case "ImageItem":
                case "Image":
                    var imageItem = new SimpleImageItem();

                    // Load image if path is available
                    var imgPath = itemData.ImagePath;
                    // Check mapping for packaged assets
                    if (!string.IsNullOrEmpty(imgPath) && assetMappings != null && assetMappings.TryGetValue(imgPath, out var mapped))
                    {
                        imgPath = mapped;
                    }
                    if (!string.IsNullOrEmpty(imgPath) && System.IO.File.Exists(imgPath))
                    {
                        imageItem.ImageSource = new BitmapImage(new Uri(imgPath, UriKind.Absolute));
                        imageItem.ImagePath = imgPath;
                    }

                    canvasItem = imageItem;
                    break;

                case "TextItem":
                case "Text":
                    var textItem = new SimpleTextItem();

                    textItem.Text = itemData.Text;
                    if (!string.IsNullOrEmpty(itemData.FontFamily))
                        textItem.FontFamily = new FontFamily(itemData.FontFamily);
                    if (itemData.FontSize.HasValue)
                        textItem.FontSize = itemData.FontSize.Value;
                    if (!string.IsNullOrEmpty(itemData.TextColor))
                    {
                        try
                        {
                            var color = (Color)ColorConverter.ConvertFromString(itemData.TextColor);
                            textItem.Foreground = new SolidColorBrush(color);
                        }
                        catch { /* Ignore */ }
                    }

                    // Apply text outline (stroke) if it has outline properties
                    if (itemData.HasOutline && !string.IsNullOrEmpty(itemData.OutlineColor))
                    {
                        try
                        {
                            var outlineColor = (Color)ColorConverter.ConvertFromString(itemData.OutlineColor);
                            textItem.StrokeBrush = new SolidColorBrush(outlineColor);
                            textItem.StrokeThickness = itemData.OutlineThickness;
                        }
                        catch { /* Ignore */ }
                    }

                    canvasItem = textItem;
                    break;

                case "PlaceholderItem":
                case "Placeholder":
                    var placeholder = new SimpleImageItem
                    {
                        IsPlaceholder = true
                    };
                    if (itemData.PlaceholderNumber.HasValue)
                    {
                        placeholder.PlaceholderNumber = itemData.PlaceholderNumber.Value;
                    }
                    if (!string.IsNullOrEmpty(itemData.Name))
                    {
                        placeholder.PlaceholderName = itemData.Name;
                    }
                    if (!string.IsNullOrEmpty(itemData.PlaceholderColor))
                    {
                        try
                        {
                            var color = (Color)ColorConverter.ConvertFromString(itemData.PlaceholderColor);
                            placeholder.PlaceholderBackground = new SolidColorBrush(color);
                        }
                        catch { /* Ignore */ }
                    }

                    canvasItem = placeholder;
                    break;

                case "ShapeItem":
                case "Shape":
                    var shapeType = SimpleShapeType.Rectangle; // Default
                    if (!string.IsNullOrEmpty(itemData.ShapeType))
                    {
                        var st = itemData.ShapeType.Trim();
                        // Accept both Circle/Ellipse synonyms
                        if (string.Equals(st, "Circle", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(st, "Ellipse", StringComparison.OrdinalIgnoreCase))
                        {
                            shapeType = SimpleShapeType.Ellipse;
                        }
                        else if (!Enum.TryParse<SimpleShapeType>(st, true, out shapeType))
                        {
                            shapeType = SimpleShapeType.Rectangle;
                        }
                    }
                    var shape = new SimpleShapeItem
                    {
                        ShapeType = shapeType
                    };

                    canvasItem = shape;
                    break;
            }

            if (canvasItem != null)
            {
                // Set common properties
                canvasItem.Width = itemData.Width;
                canvasItem.Height = itemData.Height;

                // Set position using both Canvas attached properties and item properties
                Canvas.SetLeft(canvasItem, itemData.X);
                Canvas.SetTop(canvasItem, itemData.Y);
                canvasItem.Left = itemData.X;  // Also set the internal property
                canvasItem.Top = itemData.Y;    // Also set the internal property

                // Set Z-index (layer order)
                Canvas.SetZIndex(canvasItem, itemData.ZIndex);
                canvasItem.ZIndex = itemData.ZIndex;

                // Set rotation
                canvasItem.RotationAngle = itemData.Rotation;

                // Set aspect ratio lock
                canvasItem.IsAspectRatioLocked = itemData.LockedAspectRatio;

                // Set visibility
                canvasItem.Visibility = itemData.IsVisible ? Visibility.Visible : Visibility.Collapsed;

                // Apply fill color for shapes
                if (canvasItem is SimpleShapeItem shapeItem && !string.IsNullOrEmpty(itemData.FillColor))
                {
                    try
                    {
                        var fillColor = (Color)ColorConverter.ConvertFromString(itemData.FillColor);
                        shapeItem.Fill = new SolidColorBrush(fillColor);
                    }
                    catch
                    {
                        // Fallback handled below
                    }
                }
                // Explicitly apply transparent fill if requested or color was invalid/missing
                if (canvasItem is SimpleShapeItem shapeItemFill)
                {
                    if (itemData.HasNoFill || string.IsNullOrEmpty(itemData.FillColor))
                    {
                        shapeItemFill.Fill = Brushes.Transparent;
                    }
                }

                // Apply stroke properties - check both StrokeColor and OutlineColor for compatibility
                var hasStroke = (!string.IsNullOrEmpty(itemData.StrokeColor) ||
                                !string.IsNullOrEmpty(itemData.OutlineColor)) &&
                               (itemData.StrokeThickness > 0 || itemData.OutlineThickness > 0) &&
                               !itemData.HasNoStroke;

                if (hasStroke)
                {
                    try
                    {
                        // Use StrokeColor first, fall back to OutlineColor
                        var colorStr = !string.IsNullOrEmpty(itemData.StrokeColor) ? itemData.StrokeColor : itemData.OutlineColor;
                        var thickness = itemData.StrokeThickness > 0 ? itemData.StrokeThickness : itemData.OutlineThickness;

                        var strokeColor = (Color)ColorConverter.ConvertFromString(colorStr);
                        var strokeBrush = new SolidColorBrush(strokeColor);

                        if (canvasItem is SimpleTextItem textItemStroke)
                        {
                            textItemStroke.StrokeBrush = strokeBrush;
                            textItemStroke.StrokeThickness = thickness;
                        }
                        else if (canvasItem is SimpleImageItem imgItem)
                        {
                            imgItem.StrokeBrush = strokeBrush;
                            imgItem.StrokeThickness = thickness;
                        }
                        else if (canvasItem is SimpleShapeItem shapeItem2)
                        {
                            shapeItem2.Stroke = strokeBrush;
                            shapeItem2.StrokeThickness = thickness;
                        }
                    }
                    catch { /* Ignore invalid colors */ }
                }
                else if (itemData.HasNoStroke && canvasItem is SimpleShapeItem shapeItem3)
                {
                    shapeItem3.Stroke = null;
                }

                // Apply shadow properties
                if (itemData.HasShadow && !string.IsNullOrEmpty(itemData.ShadowColor))
                {
                    try
                    {
                        var shadowColor = (Color)ColorConverter.ConvertFromString(itemData.ShadowColor);

                        // Calculate Direction and ShadowDepth from X,Y offsets
                        var direction = Math.Atan2(itemData.ShadowOffsetY, itemData.ShadowOffsetX) * (180 / Math.PI);
                        var shadowDepth = Math.Sqrt(itemData.ShadowOffsetX * itemData.ShadowOffsetX +
                                                   itemData.ShadowOffsetY * itemData.ShadowOffsetY);

                        var shadow = new DropShadowEffect
                        {
                            Color = shadowColor,
                            BlurRadius = itemData.ShadowBlurRadius,
                            Direction = direction,
                            ShadowDepth = shadowDepth,
                            Opacity = itemData.ShadowOpacity > 0 ? itemData.ShadowOpacity : 1.0
                        };

                        canvasItem.Effect = shadow;
                    }
                    catch { /* Ignore invalid shadow settings */ }
                }

                // Apply opacity if specified
                if (itemData.Opacity > 0 && itemData.Opacity <= 1)
                {
                    canvasItem.Opacity = itemData.Opacity;
                }

                // SimpleCanvasItem does not support IsLocked; ignore this flag for now

                // Add to canvas - IMPORTANT: Add items in Z-index order
                var canvas = this.DesignerCanvas as SimpleDesignerCanvas;
                canvas?.Items.Add(canvasItem);
            }
        }

        private async void ExportTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "Template Package (*.zip)|*.zip|Template files (*.template)|*.template|All files (*.*)|*.*",
                    Title = "Export Template Package",
                    FileName = $"{TemplateNameText.Text}_package.zip"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var extension = System.IO.Path.GetExtension(saveFileDialog.FileName).ToLower();

                    if (extension == ".zip")
                    {
                        // Export as ZIP package with assets
                        await ExportTemplatePackage(saveFileDialog.FileName);
                    }
                    else
                    {
                        // Legacy export (just the template file)
                        _designerVM.SaveAsCmd.Execute(saveFileDialog.FileName);
                        Log.Debug($"TouchTemplateDesigner: Exported template to {saveFileDialog.FileName}");
                        MessageBox.Show($"Template exported successfully to {System.IO.Path.GetFileName(saveFileDialog.FileName)}",
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to export template: {ex.Message}");
                MessageBox.Show($"Failed to export template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ExportTemplatePackage(string zipFilePath)
        {
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"template_export_{Guid.NewGuid()}");

            try
            {
                // Create temporary directory
                Directory.CreateDirectory(tempDir);

                // Create assets directory
                var assetsDir = System.IO.Path.Combine(tempDir, "assets");
                System.IO.Directory.CreateDirectory(assetsDir);

                // Create template data with assets
                var templateData = await CreateTemplateDataWithAssets(assetsDir);

                // Save template JSON
                var templateJsonPath = System.IO.Path.Combine(tempDir, "template.json");
                var json = JsonConvert.SerializeObject(templateData, Formatting.Indented);
                System.IO.File.WriteAllText(templateJsonPath, json);

                // Create manifest file
                await CreateManifest(tempDir, templateData);

                // Create ZIP package
                if (System.IO.File.Exists(zipFilePath))
                    System.IO.File.Delete(zipFilePath);

                ZipFile.CreateFromDirectory(tempDir, zipFilePath);

                ShowAutoSaveNotification($"Template package exported successfully!");
                Log.Debug($"TouchTemplateDesigner: Exported template package to {zipFilePath}");
            }
            finally
            {
                // Clean up temporary directory
                if (System.IO.Directory.Exists(tempDir))
                {
                    try
                    {
                        System.IO.Directory.Delete(tempDir, true);
                    }
                    catch { /* Ignore cleanup errors */ }
                }
            }
        }

        private async Task<TemplateData> CreateTemplateDataWithAssets(string assetsDir)
        {
            var canvas = this.DesignerCanvas as SimpleDesignerCanvas;
            if (canvas == null) throw new InvalidOperationException("DesignerCanvas is not available");
            // Create template data from current canvas
            var templateData = new TemplateData
            {
                Name = TemplateNameText.Text,
                Description = "Template created in TouchTemplateDesigner",
                CanvasWidth = canvas.Width,
                CanvasHeight = canvas.Height,
                BackgroundColor = ((DesignerCanvas.CanvasBackground as SolidColorBrush)?.Color.ToString()) ?? "#FFFFFF",
                CreatedDate = DateTime.Now,
                ModifiedDate = DateTime.Now,
                IsActive = true,
                CanvasItems = new List<CanvasItemData>()
            };

            var assetCounter = 1;
            var assetMappings = new Dictionary<string, string>();

            // Process all canvas items to find and copy assets
            foreach (var item in canvas.Items)
            {
                var canvasItemData = await ConvertToCanvasItemData(item, assetsDir, assetCounter, assetMappings);
                if (canvasItemData != null)
                {
                    templateData.CanvasItems.Add(canvasItemData);
                    if (item is SimpleImageItem)
                        assetCounter++;
                }
            }

            // Store asset mappings in template data
            templateData.AssetMappings = assetMappings;

            return templateData;
        }

        private async Task<CanvasItemData> ConvertToCanvasItemData(SimpleCanvasItem item, string assetsDir, int assetId, Dictionary<string, string> assetMappings)
        {
            var itemData = new CanvasItemData
            {
                ItemType = item.GetType().Name,
                X = Canvas.GetLeft(item),
                Y = Canvas.GetTop(item),
                Width = item.Width,
                Height = item.Height,
                ZIndex = Canvas.GetZIndex(item),
                // using typed fields in CanvasItemData
            };

            // Handle different item types
            if (item is SimpleImageItem imageItem)
            {
                // Mark as placeholder if applicable, else image
                itemData.ItemType = imageItem.IsPlaceholder ? "Placeholder" : "Image";
                // Copy image asset if it exists
                if (imageItem.ImageSource != null && !imageItem.IsPlaceholder)
                {
                    var imagePath = await ProcessImageAsset(imageItem, assetsDir, assetId, assetMappings);
                    if (!string.IsNullOrEmpty(imagePath))
                    {
                        itemData.ImagePath = imagePath;
                    }
                }
                if (imageItem.IsPlaceholder)
                {
                    itemData.PlaceholderNumber = imageItem.PlaceholderNumber;
                }

                // Export optional stroke for images (border)
                if (imageItem.StrokeBrush is SolidColorBrush imgStroke)
                {
                    itemData.OutlineColor = imgStroke.Color.ToString();
                }
                itemData.OutlineThickness = imageItem.StrokeThickness;

                // Export drop shadow if present
                if (imageItem.Effect is System.Windows.Media.Effects.DropShadowEffect dsImg)
                {
                    itemData.HasShadow = true;
                    itemData.ShadowColor = dsImg.Color.ToString();
                    itemData.ShadowBlurRadius = dsImg.BlurRadius;
                    // Convert Direction/Depth to X/Y offsets
                    var radians = dsImg.Direction * (Math.PI / 180.0);
                    itemData.ShadowOffsetX = Math.Cos(radians) * dsImg.ShadowDepth;
                    itemData.ShadowOffsetY = Math.Sin(radians) * dsImg.ShadowDepth;
                    itemData.ShadowOpacity = dsImg.Opacity;
                }
            }
            else if (item is SimpleTextItem textItem)
            {
                itemData.ItemType = "Text";
                itemData.Text = textItem.Text;
                itemData.FontFamily = textItem.FontFamily?.ToString();
                itemData.FontSize = textItem.FontSize;
                itemData.FontWeight = textItem.FontWeight.ToString();
                itemData.TextColor = (textItem.Foreground as SolidColorBrush)?.Color.ToString();

                // Export text outline (stroke)
                if (textItem.StrokeBrush is SolidColorBrush txtStroke && textItem.StrokeThickness > 0)
                {
                    itemData.HasOutline = true;
                    itemData.OutlineColor = txtStroke.Color.ToString();
                    itemData.OutlineThickness = textItem.StrokeThickness;
                }

                // Export drop shadow if present
                if (textItem.Effect is System.Windows.Media.Effects.DropShadowEffect ds)
                {
                    itemData.HasShadow = true;
                    itemData.ShadowColor = ds.Color.ToString();
                    itemData.ShadowBlurRadius = ds.BlurRadius;
                    var radians = ds.Direction * (Math.PI / 180.0);
                    itemData.ShadowOffsetX = Math.Cos(radians) * ds.ShadowDepth;
                    itemData.ShadowOffsetY = Math.Sin(radians) * ds.ShadowDepth;
                    itemData.ShadowOpacity = ds.Opacity;
                }
            }
            else if (item is SimpleShapeItem shapeItem)
            {
                itemData.ItemType = "Shape";
                // Map SimpleShapeType to a portable string
                switch (shapeItem.ShapeType)
                {
                    case SimpleShapeType.Rectangle:
                        itemData.ShapeType = "Rectangle";
                        break;
                    case SimpleShapeType.Ellipse:
                        // Use "Circle" for compatibility with DesignerCanvas enum
                        itemData.ShapeType = "Circle";
                        break;
                    case SimpleShapeType.Line:
                        itemData.ShapeType = "Line";
                        break;
                }

                // Fill
                if (shapeItem.Fill is SolidColorBrush fillBrush)
                {
                    // Treat Transparent as no fill
                    if (fillBrush.Color.A == 0)
                    {
                        itemData.HasNoFill = true;
                    }
                    else
                    {
                        itemData.FillColor = fillBrush.Color.ToString();
                    }
                }
                else
                {
                    itemData.HasNoFill = true;
                }

                // Stroke
                if (shapeItem.Stroke is SolidColorBrush sBrush && shapeItem.StrokeThickness > 0)
                {
                    if (sBrush.Color.A == 0)
                    {
                        itemData.HasNoStroke = true;
                    }
                    else
                    {
                        itemData.StrokeColor = sBrush.Color.ToString();
                        itemData.StrokeThickness = shapeItem.StrokeThickness;
                    }
                }
                else
                {
                    itemData.HasNoStroke = true;
                }

                // Drop shadow
                if (shapeItem.Effect is System.Windows.Media.Effects.DropShadowEffect dsShape)
                {
                    itemData.HasShadow = true;
                    itemData.ShadowColor = dsShape.Color.ToString();
                    itemData.ShadowBlurRadius = dsShape.BlurRadius;
                    var radians = dsShape.Direction * (Math.PI / 180.0);
                    itemData.ShadowOffsetX = Math.Cos(radians) * dsShape.ShadowDepth;
                    itemData.ShadowOffsetY = Math.Sin(radians) * dsShape.ShadowDepth;
                    itemData.ShadowOpacity = dsShape.Opacity;
                }
            }
            // No SimplePlaceholderItem type in current code — placeholders are SimpleImageItem with IsPlaceholder=true

            return itemData;
        }

        private async Task<string> ProcessImageAsset(SimpleImageItem imageItem, string assetsDir, int assetId, Dictionary<string, string> assetMappings)
        {
            try
            {
                if (imageItem.ImageSource is BitmapImage bitmapImage && bitmapImage.UriSource != null)
                {
                    var originalPath = bitmapImage.UriSource.LocalPath;

                    if (System.IO.File.Exists(originalPath))
                    {
                        var extension = System.IO.Path.GetExtension(originalPath);
                        var newFileName = $"asset_{assetId:D3}{extension}";
                        var newFilePath = System.IO.Path.Combine(assetsDir, newFileName);

                        // Copy the asset file
                        System.IO.File.Copy(originalPath, newFilePath);

                        // Store the mapping and return relative path
                        var relativePath = $"assets/{newFileName}";
                        assetMappings[originalPath] = relativePath;

                        return relativePath;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to process image asset: {ex.Message}");
            }

            return null;
        }

        private async Task CreateManifest(string tempDir, TemplateData templateData)
        {
            var manifest = new
            {
                PackageVersion = "1.0",
                ExportDate = DateTime.Now,
                ApplicationName = "Photobooth TouchTemplateDesigner",
                Template = new
                {
                    templateData.Name,
                    templateData.Description,
                    templateData.CanvasWidth,
                    templateData.CanvasHeight,
                    ItemCount = templateData.CanvasItems?.Count ?? 0
                },
                Assets = templateData.AssetMappings?.Count ?? 0,
                EventAssociation = _selectedEvent?.Name,
                Instructions = new[]
                {
                    "To import this template package:",
                    "1. Use the Import function in TouchTemplateDesigner",
                    "2. Select this ZIP file",
                    "3. All assets will be automatically restored"
                }
            };

            var manifestPath = System.IO.Path.Combine(tempDir, "manifest.json");
            var manifestJson = JsonConvert.SerializeObject(manifest, Formatting.Indented);
            System.IO.File.WriteAllText(manifestPath, manifestJson);
        }

        #endregion

        #region Canvas Operations

        private void AddPlaceholder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var canvas = this.DesignerCanvas as SimpleDesignerCanvas;
                if (canvas != null)
                {
                    // Use the new SimpleDesignerCanvas to add placeholder
                    var placeholder = canvas.AddImage(null,
                        canvas.ActualWidth / 2 - 150,
                        canvas.ActualHeight / 2 - 100,
                        300, 200);
                    if (placeholder != null)
                    {
                        // IsPlaceholder is already set by AddImage when imagePath is null
                        canvas.SelectItem(placeholder);

                        // Refresh layers panel if visible
                        if (LayersPanelContainer.Visibility == Visibility.Visible)
                        {
                            LayersPanel.RefreshLayers();
                        }
                    }
                    Log.Debug("TouchTemplateDesigner: Added placeholder using SimpleDesignerCanvas");
                }
                else
                {
                    // Fallback to old method
                    _designerVM.AddPlaceholderCmd.Execute(null);
                }
                Log.Debug("TouchTemplateDesigner: Added placeholder to canvas");
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to add placeholder: {ex.Message}");
                MessageBox.Show($"Failed to add placeholder: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddText_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Use the new SimpleDesignerCanvas to add text directly
                var canvas = this.DesignerCanvas as SimpleDesignerCanvas;
                if (canvas != null)
                {
                    canvas.PushUndo();
                    var text = canvas.AddText("Sample Text",
                        canvas.ActualWidth / 2 - 100,
                        canvas.ActualHeight / 2 - 25);
                    if (text != null)
                    {
                        text.FontFamily = new System.Windows.Media.FontFamily("Arial");
                        text.FontSize = 72;
                        text.TextColor = new SolidColorBrush(_currentSelectedColor);
                        canvas.SelectItem(text);

                        // Refresh layers panel if visible
                        if (LayersPanelContainer.Visibility == Visibility.Visible)
                        {
                            LayersPanel.RefreshLayers();
                        }
                    }
                    Log.Debug("TouchTemplateDesigner: Added text using SimpleDesignerCanvas");
                }
                else
                {
                    // Fallback to old method if canvas is not SimpleDesignerCanvas
                    _designerVM.AddTextCmd.Execute(null);
                    Log.Debug("TouchTemplateDesigner: Added text via DesignerVM (fallback)");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to add text: {ex.Message}");
                MessageBox.Show($"Failed to add text: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddImage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var canvas = this.DesignerCanvas as SimpleDesignerCanvas;
                if (canvas != null)
                {
                    canvas.PushUndo();
                    // Open file dialog to select image
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                        Title = "Select an Image"
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        // Load the image to get its actual dimensions while preserving transparency
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(dialog.FileName);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        // Get actual image dimensions (convert to device-independent units)
                        double dpiX = bitmap.DpiX > 0 ? bitmap.DpiX : 96.0;
                        double dpiY = bitmap.DpiY > 0 ? bitmap.DpiY : 96.0;
                        double imageWidthDip = bitmap.PixelWidth * (96.0 / dpiX);
                        double imageHeightDip = bitmap.PixelHeight * (96.0 / dpiY);

                        double imageWidth = imageWidthDip;
                        double imageHeight = imageHeightDip;

                        // If the image matches the canvas size (within tolerance), fit edge-to-edge
                        double tolerance = 2.0; // DIPs tolerance for equality
                        bool sizeMatchesCanvas = Math.Abs(imageWidthDip - canvas.ActualWidth) <= tolerance &&
                                                  Math.Abs(imageHeightDip - canvas.ActualHeight) <= tolerance;
                        if (sizeMatchesCanvas)
                        {
                            imageWidth = canvas.ActualWidth;
                            imageHeight = canvas.ActualHeight;
                            double xFit = 0;
                            double yFit = 0;
                            var imageFit = canvas.AddImage(dialog.FileName, xFit, yFit, imageWidth, imageHeight);
                            if (imageFit != null)
                            {
                                canvas.SelectItem(imageFit);
                            }

                            // Refresh layers panel if visible
                            if (LayersPanelContainer.Visibility == Visibility.Visible)
                            {
                                LayersPanel.RefreshLayers();
                            }

                            Log.Debug("TouchTemplateDesigner: Added full-canvas image (auto-fit)");
                            return;
                        }

                        // Otherwise, if aspect ratios match, fill the canvas preserving aspect
                        double imgAspect = imageWidthDip / imageHeightDip;
                        double canvasAspect = canvas.ActualWidth / canvas.ActualHeight;
                        double ratioTolerance = 0.01; // ~1%
                        if (Math.Abs(imgAspect - canvasAspect) <= ratioTolerance)
                        {
                            imageWidth = canvas.ActualWidth;
                            imageHeight = canvas.ActualHeight;
                            double xFitAspect = 0;
                            double yFitAspect = 0;
                            var imageFill = canvas.AddImage(dialog.FileName, xFitAspect, yFitAspect, imageWidth, imageHeight);
                            if (imageFill != null)
                            {
                                canvas.SelectItem(imageFill);
                            }

                            // Refresh layers panel if visible
                            if (LayersPanelContainer.Visibility == Visibility.Visible)
                            {
                                LayersPanel.RefreshLayers();
                            }

                            Log.Debug("TouchTemplateDesigner: Added canvas-fill image (aspect match)");
                            return;
                        }

                        // Otherwise, scale down if image is too large for the canvas
                        double maxWidth = canvas.ActualWidth * 0.8;  // 80% of canvas width
                        double maxHeight = canvas.ActualHeight * 0.8; // 80% of canvas height

                        if (imageWidth > maxWidth || imageHeight > maxHeight)
                        {
                            // Calculate scale factor to fit within bounds while maintaining aspect ratio
                            double scaleX = maxWidth / imageWidth;
                            double scaleY = maxHeight / imageHeight;
                            double scale = Math.Min(scaleX, scaleY);

                            imageWidth *= scale;
                            imageHeight *= scale;
                        }

                        // Center the image on the canvas
                        double x = (canvas.ActualWidth - imageWidth) / 2;
                        double y = (canvas.ActualHeight - imageHeight) / 2;

                        // Add the image with its actual dimensions
                        var image = canvas.AddImage(dialog.FileName, x, y, imageWidth, imageHeight);
                        if (image != null)
                        {
                            canvas.SelectItem(image);

                            // Refresh layers panel if visible
                            if (LayersPanelContainer.Visibility == Visibility.Visible)
                            {
                                LayersPanel.RefreshLayers();
                            }
                        }
                    }
                    Log.Debug("TouchTemplateDesigner: Added image using SimpleDesignerCanvas");
                }
                else
                {
                    // Fallback to old method
                    _designerVM.ImportImageCmd.Execute(null);
                }
                Log.Debug("TouchTemplateDesigner: Added image to canvas");
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to add image: {ex.Message}");
                MessageBox.Show($"Failed to add image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddShape_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShapePopup.IsOpen = true;
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to show shape menu: {ex.Message}");
            }
        }

        private void AddQRCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var canvas = DesignerCanvas as SimpleDesignerCanvas;
                if (canvas == null) return;
                var item = new SimpleQRCodeItem
                {
                    Left = canvas.ActualWidth / 2 - 80,
                    Top = canvas.ActualHeight / 2 - 80,
                    Width = 160,
                    Height = 160,
                    Value = "https://example.com",
                    PixelsPerModule = 6
                };
                canvas.AddItem(item);
                canvas.SelectItem(item);
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to add QR code: {ex.Message}");
            }
        }

        private void ShapePicker_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShapePopup.IsOpen = false;
                if (sender is Button btn && btn.Tag is string tag)
                {
                    AddShapeToCanvas(tag);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: ShapePicker_Click error: {ex.Message}");
            }
        }

        private void AddShapeToCanvas(string shapeType)
        {
            try
            {
                var canvas = DesignerCanvas as SimpleDesignerCanvas;
                if (canvas == null) return;
                canvas.PushUndo();
                var x = canvas.ActualWidth / 2 - 50;
                var y = canvas.ActualHeight / 2 - 50;
                SimpleShapeType type = SimpleShapeType.Rectangle;
                if (shapeType == "Circle") type = SimpleShapeType.Ellipse;
                else if (shapeType == "Line") type = SimpleShapeType.Line;
                var shape = canvas.AddShape(type, x, y, 100, 100);
                if (shape != null) canvas.SelectItem(shape);
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to add shape: {ex.Message}");
                MessageBox.Show($"Failed to add shape: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Arrange Operations

        private void BringToFront_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var canvas = this.DesignerCanvas as SimpleDesignerCanvas;
                if (canvas != null && canvas.SelectedItem != null)
                {
                    canvas.PushUndo();
                    canvas.BringToFront();
                    Log.Debug("TouchTemplateDesigner: Brought item to front");

                    // Update layers panel if visible
                    if (LayersPanel.Visibility == Visibility.Visible)
                    {
                        LayersPanel.RefreshLayers();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to bring to front: {ex.Message}");
            }
        }

        private void SendToBack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var canvas = this.DesignerCanvas as SimpleDesignerCanvas;
                if (canvas != null && canvas.SelectedItem != null)
                {
                    canvas.PushUndo();
                    canvas.SendToBack();
                    Log.Debug("TouchTemplateDesigner: Sent item to back");

                    // Update layers panel if visible
                    if (LayersPanel.Visibility == Visibility.Visible)
                    {
                        LayersPanel.RefreshLayers();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to send to back: {ex.Message}");
            }
        }

        private void AlignItems_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Check if any items are selected
                var canvas = this.DesignerCanvas as SimpleDesignerCanvas;
                if (canvas == null || canvas.SelectedItems == null || canvas.SelectedItems.Count == 0)
                {
                    MessageBox.Show("Please select one or more items to align.", "No Selection",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Show the touch-friendly alignment popup
                AlignmentPopup.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to show alignment popup: {ex.Message}");
            }
        }

        private void AlignSelectedItem(string alignment)
        {
            try
            {
                var canvas = this.DesignerCanvas as SimpleDesignerCanvas;
                if (canvas != null && canvas.SelectedItems != null && canvas.SelectedItems.Count > 0)
                {
                    var canvasWidth = canvas.Width;
                    var canvasHeight = canvas.Height;

                    // For multiple items, we need to find the alignment reference point
                    if (canvas.SelectedItems.Count == 1)
                    {
                        // Single item - align to canvas
                        var item = canvas.SelectedItems.First();

                        switch (alignment)
                        {
                            case "Left":
                                item.Left = 10;
                                break;
                            case "CenterH":
                                item.Left = (canvasWidth - item.Width) / 2;
                                break;
                            case "Right":
                                item.Left = canvasWidth - item.Width - 10;
                                break;
                            case "Top":
                                item.Top = 10;
                                break;
                            case "CenterV":
                                item.Top = (canvasHeight - item.Height) / 2;
                                break;
                            case "Bottom":
                                item.Top = canvasHeight - item.Height - 10;
                                break;
                            case "Center":
                                item.Left = (canvasWidth - item.Width) / 2;
                                item.Top = (canvasHeight - item.Height) / 2;
                                break;
                        }
                    }
                    else
                    {
                        // Multiple items - align to the first selected item or to each other
                        var referenceItem = canvas.SelectedItem ?? canvas.SelectedItems.First();

                        switch (alignment)
                        {
                            case "Left":
                                double leftMost = canvas.SelectedItems.Min(i => i.Left);
                                foreach (var item in canvas.SelectedItems)
                                {
                                    item.Left = leftMost;
                                }
                                break;

                            case "CenterH":
                                double avgCenterX = canvas.SelectedItems.Average(i => i.Left + i.Width / 2);
                                foreach (var item in canvas.SelectedItems)
                                {
                                    item.Left = avgCenterX - item.Width / 2;
                                }
                                break;

                            case "Right":
                                double rightMost = canvas.SelectedItems.Max(i => i.Left + i.Width);
                                foreach (var item in canvas.SelectedItems)
                                {
                                    item.Left = rightMost - item.Width;
                                }
                                break;

                            case "Top":
                                double topMost = canvas.SelectedItems.Min(i => i.Top);
                                foreach (var item in canvas.SelectedItems)
                                {
                                    item.Top = topMost;
                                }
                                break;

                            case "CenterV":
                                double avgCenterY = canvas.SelectedItems.Average(i => i.Top + i.Height / 2);
                                foreach (var item in canvas.SelectedItems)
                                {
                                    item.Top = avgCenterY - item.Height / 2;
                                }
                                break;

                            case "Bottom":
                                double bottomMost = canvas.SelectedItems.Max(i => i.Top + i.Height);
                                foreach (var item in canvas.SelectedItems)
                                {
                                    item.Top = bottomMost - item.Height;
                                }
                                break;

                            case "Center":
                                // Center all items as a group in the canvas
                                double minX = canvas.SelectedItems.Min(i => i.Left);
                                double maxX = canvas.SelectedItems.Max(i => i.Left + i.Width);
                                double minY = canvas.SelectedItems.Min(i => i.Top);
                                double maxY = canvas.SelectedItems.Max(i => i.Top + i.Height);

                                double groupWidth = maxX - minX;
                                double groupHeight = maxY - minY;
                                double offsetX = (canvasWidth - groupWidth) / 2 - minX;
                                double offsetY = (canvasHeight - groupHeight) / 2 - minY;

                                foreach (var item in canvas.SelectedItems)
                                {
                                    item.Left += offsetX;
                                    item.Top += offsetY;
                                }
                                break;
                        }
                    }

                    Log.Debug($"TouchTemplateDesigner: Aligned {canvas.SelectedItems.Count} items {alignment}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to align items: {ex.Message}");
            }
        }

        private void StackSelectedItems(bool vertical)
        {
            try
            {
                var canvas = this.DesignerCanvas as SimpleDesignerCanvas;
                if (canvas != null && canvas.SelectedItems != null && canvas.SelectedItems.Count > 1)
                {
                    // Sort items by current position
                    var sortedItems = vertical
                        ? canvas.SelectedItems.OrderBy(i => i.Top).ToList()
                        : canvas.SelectedItems.OrderBy(i => i.Left).ToList();

                    // Stack items with a small gap between them
                    double gap = 10; // Gap between items

                    if (vertical)
                    {
                        // Stack vertically - maintain left position, stack top to bottom
                        double currentTop = sortedItems.First().Top;
                        foreach (var item in sortedItems)
                        {
                            item.Top = currentTop;
                            currentTop += item.Height + gap;
                        }
                    }
                    else
                    {
                        // Stack horizontally - maintain top position, stack left to right
                        double currentLeft = sortedItems.First().Left;
                        foreach (var item in sortedItems)
                        {
                            item.Left = currentLeft;
                            currentLeft += item.Width + gap;
                        }
                    }

                    Log.Debug($"TouchTemplateDesigner: Stacked {canvas.SelectedItems.Count} items {(vertical ? "vertically" : "horizontally")}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to stack items: {ex.Message}");
            }
        }

        private void DistributeSelectedItems(bool vertical)
        {
            try
            {
                var canvas = this.DesignerCanvas as SimpleDesignerCanvas;
                if (canvas != null && canvas.SelectedItems != null && canvas.SelectedItems.Count > 2)
                {
                    if (vertical)
                    {
                        // Distribute vertically - equal spacing between items
                        var sortedItems = canvas.SelectedItems.OrderBy(i => i.Top).ToList();
                        double topMost = sortedItems.First().Top;
                        double bottomMost = sortedItems.Last().Top + sortedItems.Last().Height;
                        double totalHeight = sortedItems.Sum(i => i.Height);
                        double availableSpace = bottomMost - topMost - totalHeight;
                        double gap = availableSpace / (sortedItems.Count - 1);

                        double currentTop = topMost;
                        foreach (var item in sortedItems)
                        {
                            item.Top = currentTop;
                            currentTop += item.Height + gap;
                        }
                    }
                    else
                    {
                        // Distribute horizontally - equal spacing between items
                        var sortedItems = canvas.SelectedItems.OrderBy(i => i.Left).ToList();
                        double leftMost = sortedItems.First().Left;
                        double rightMost = sortedItems.Last().Left + sortedItems.Last().Width;
                        double totalWidth = sortedItems.Sum(i => i.Width);
                        double availableSpace = rightMost - leftMost - totalWidth;
                        double gap = availableSpace / (sortedItems.Count - 1);

                        double currentLeft = leftMost;
                        foreach (var item in sortedItems)
                        {
                            item.Left = currentLeft;
                            currentLeft += item.Width + gap;
                        }
                    }

                    Log.Debug($"TouchTemplateDesigner: Distributed {canvas.SelectedItems.Count} items {(vertical ? "vertically" : "horizontally")}");
                }
                else if (canvas != null && canvas.SelectedItems != null && canvas.SelectedItems.Count == 2)
                {
                    // For 2 items, just align them with equal spacing from edges
                    var items = canvas.SelectedItems.ToList();
                    if (vertical)
                    {
                        var sortedItems = items.OrderBy(i => i.Top).ToList();
                        // Keep first and last in place, which is already the case for 2 items
                    }
                    else
                    {
                        var sortedItems = items.OrderBy(i => i.Left).ToList();
                        // Keep first and last in place, which is already the case for 2 items
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to distribute items: {ex.Message}");
            }
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var canvas = this.DesignerCanvas as SimpleDesignerCanvas;
                if (canvas != null && canvas.SelectedItem != null)
                {
                    // Confirm deletion for safety
                    var result = MessageBox.Show("Are you sure you want to delete the selected item?",
                        "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        canvas.PushUndo();
                        canvas.RemoveSelectedItem();
                        Log.Debug("TouchTemplateDesigner: Deleted selected item");

                        // Update layers panel if visible
                        if (LayersPanel.Visibility == Visibility.Visible)
                        {
                            LayersPanel.RefreshLayers();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to delete item: {ex.Message}");
                MessageBox.Show($"Failed to delete item: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AspectRatioLock_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var canvas = this.DesignerCanvas as SimpleDesignerCanvas;
                if (canvas != null && canvas.SelectedItem != null)
                {
                    var toggleButton = sender as ToggleButton;
                    bool isLocked = toggleButton?.IsChecked ?? false;

                    canvas.SelectedItem.IsAspectRatioLocked = isLocked;

                    Log.Debug($"TouchTemplateDesigner: Aspect ratio {(isLocked ? "locked" : "unlocked")} for selected item");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to toggle aspect ratio lock: {ex.Message}");
            }
        }

        // Touch-friendly alignment button handlers
        private void AlignLeft_Click(object sender, RoutedEventArgs e)
        {
            AlignSelectedItem("Left");
            CloseAlignmentPopup();
        }

        private void AlignCenterH_Click(object sender, RoutedEventArgs e)
        {
            AlignSelectedItem("CenterH");
            CloseAlignmentPopup();
        }

        private void AlignRight_Click(object sender, RoutedEventArgs e)
        {
            AlignSelectedItem("Right");
            CloseAlignmentPopup();
        }

        private void AlignTop_Click(object sender, RoutedEventArgs e)
        {
            AlignSelectedItem("Top");
            CloseAlignmentPopup();
        }

        private void AlignCenterV_Click(object sender, RoutedEventArgs e)
        {
            AlignSelectedItem("CenterV");
            CloseAlignmentPopup();
        }

        private void AlignBottom_Click(object sender, RoutedEventArgs e)
        {
            AlignSelectedItem("Bottom");
            CloseAlignmentPopup();
        }

        private void AlignCenter_Click(object sender, RoutedEventArgs e)
        {
            AlignSelectedItem("Center");
            CloseAlignmentPopup();
        }

        private void CloseAlignmentPopup_Click(object sender, RoutedEventArgs e)
        {
            CloseAlignmentPopup();
        }

        private void StackVertical_Click(object sender, RoutedEventArgs e)
        {
            StackSelectedItems(true);
            CloseAlignmentPopup();
        }

        private void StackHorizontal_Click(object sender, RoutedEventArgs e)
        {
            StackSelectedItems(false);
            CloseAlignmentPopup();
        }

        private void DistributeVertical_Click(object sender, RoutedEventArgs e)
        {
            DistributeSelectedItems(true);
            CloseAlignmentPopup();
        }

        private void DistributeHorizontal_Click(object sender, RoutedEventArgs e)
        {
            DistributeSelectedItems(false);
            CloseAlignmentPopup();
        }

        private void AlignmentPopup_BackgroundClick(object sender, MouseButtonEventArgs e)
        {
            // Close popup if clicked on background
            if (e.Source == sender)
            {
                CloseAlignmentPopup();
            }
        }

        private void CloseAlignmentPopup()
        {
            AlignmentPopup.Visibility = Visibility.Collapsed;
        }

        // Paper Size popup handlers
        private bool _isLandscape = false;

        private void OrientationToggle_Click(object sender, RoutedEventArgs e)
        {
            _isLandscape = !_isLandscape;
            var toggle = sender as ToggleButton;
            if (toggle != null)
            {
                toggle.Content = _isLandscape ? "Landscape" : "Portrait";
                toggle.Background = _isLandscape ? new SolidColorBrush(Color.FromRgb(255, 152, 0)) : new SolidColorBrush(Color.FromRgb(76, 175, 80));
            }
        }

        private void UpdateOrientationFromCanvas()
        {
            try
            {
                var canvas = DesignerCanvas as SimpleDesignerCanvas;
                if (canvas == null) return;

                bool isLandscape = canvas.Width > canvas.Height;
                _isLandscape = isLandscape;

                var toggle = this.FindName("OrientationToggle") as ToggleButton;
                if (toggle != null)
                {
                    toggle.Content = _isLandscape ? "Landscape" : "Portrait";
                    toggle.Background = _isLandscape ? new SolidColorBrush(Color.FromRgb(255, 152, 0)) : new SolidColorBrush(Color.FromRgb(76, 175, 80));
                    toggle.IsChecked = _isLandscape;
                }

                Log.Debug($"TouchTemplateDesigner: Detected orientation from canvas {canvas.Width}x{canvas.Height}: {(_isLandscape ? "Landscape" : "Portrait")}");
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to update orientation from canvas: {ex.Message}");
            }
        }

        private void Size4x6_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log.Debug($"★★★ Size4x6_Click: Called, _isLandscape = {_isLandscape} ★★★");
                Log.Debug($"★★★ Size4x6_Click: Canvas BEFORE: {DesignerCanvas.Width}x{DesignerCanvas.Height} ★★★");

                if (_isLandscape)
                {
                    Log.Debug("★★★ Size4x6_Click: Setting canvas to 1800x1200 (6x4 landscape) ★★★");
                    SetCanvasSize(1800, 1200); // 6x4
                }
                else
                {
                    Log.Debug("★★★ Size4x6_Click: Setting canvas to 1200x1800 (4x6 portrait) ★★★");
                    SetCanvasSize(1200, 1800); // 4x6
                }

                Log.Debug($"★★★ Size4x6_Click: Canvas AFTER: {DesignerCanvas.Width}x{DesignerCanvas.Height} ★★★");

                // Update template dimensions (create if doesn't exist, update if it does)
                UpdateTemplateDimensions();

                ClosePaperSizePopup();
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Error in Size4x6_Click: {ex.Message}");
                MessageBox.Show($"Error setting 4x6 size: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Size5x7_Click(object sender, RoutedEventArgs e)
        {
            if (_isLandscape)
                SetCanvasSize(2100, 1500); // 7x5
            else
                SetCanvasSize(1500, 2100); // 5x7

            UpdateTemplateDimensions();
            ClosePaperSizePopup();
        }

        private void Size8x10_Click(object sender, RoutedEventArgs e)
        {
            if (_isLandscape)
                SetCanvasSize(3000, 2400); // 10x8
            else
                SetCanvasSize(2400, 3000); // 8x10

            UpdateTemplateDimensions();
            ClosePaperSizePopup();
        }

        private void Size2x6_Click(object sender, RoutedEventArgs e)
        {
            if (_isLandscape)
                SetCanvasSize(1800, 600); // 6x2
            else
                SetCanvasSize(600, 1800); // 2x6

            UpdateTemplateDimensions();
            ClosePaperSizePopup();
        }

        private void SizeInstagram_Click(object sender, RoutedEventArgs e)
        {
            SetCanvasSize(1080, 1080); // Square format
            UpdateTemplateDimensions();
            ClosePaperSizePopup();
        }

        private void UpdateTemplateDimensions()
        {
            var canvas = DesignerCanvas as SimpleDesignerCanvas;
            if (canvas == null) return;

            if (_currentTemplateId <= 0)
            {
                // Template doesn't exist yet - create it
                Log.Debug("TouchTemplateDesigner: Paper size selected, creating new template");
                PerformAutoSave();
            }
            else
            {
                // Template exists - update its dimensions
                try
                {
                    var database = new TemplateDatabase();
                    var template = database.GetTemplate(_currentTemplateId);
                    if (template != null)
                    {
                        Log.Debug($"TouchTemplateDesigner: Updating template {_currentTemplateId} dimensions from {template.CanvasWidth}x{template.CanvasHeight} to {canvas.Width}x{canvas.Height}");
                        template.CanvasWidth = canvas.Width;
                        template.CanvasHeight = canvas.Height;
                        template.ModifiedDate = DateTime.Now;
                        database.UpdateTemplate(_currentTemplateId, template);
                        Log.Debug($"TouchTemplateDesigner: Template dimensions updated successfully");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"TouchTemplateDesigner: Failed to update template dimensions: {ex.Message}");
                }
            }
        }

        private void SizeFacebook_Click(object sender, RoutedEventArgs e)
        {
            if (_isLandscape)
                SetCanvasSize(1200, 630); // Facebook landscape
            else
                SetCanvasSize(630, 1200); // Facebook portrait
            ClosePaperSizePopup();
        }

        private void CustomSize_Click(object sender, RoutedEventArgs e)
        {
            ClosePaperSizePopup();
            ShowCustomSizeDialog();
        }

        private void ChangeOrientation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Swap width and height to change orientation
                var currentWidth = DesignerCanvas.Width;
                var currentHeight = DesignerCanvas.Height;

                // Set the new size (swapped dimensions)
                SetCanvasSize(currentHeight, currentWidth);

                Log.Debug($"TouchTemplateDesigner: Changed orientation from {currentWidth}x{currentHeight} to {currentHeight}x{currentWidth}");
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to change orientation: {ex.Message}");
                MessageBox.Show($"Failed to change orientation: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClosePaperSizePopup_Click(object sender, RoutedEventArgs e)
        {
            ClosePaperSizePopup();
        }

        private void PaperSizePopup_BackgroundClick(object sender, MouseButtonEventArgs e)
        {
            if (e.Source == sender)
            {
                ClosePaperSizePopup();
            }
        }

        private void ClosePaperSizePopup()
        {
            PaperSizePopup.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Zoom Operations

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            _currentZoom = Math.Min(_currentZoom * 1.2, 5.0);
            UpdateZoom();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _currentZoom = Math.Max(_currentZoom / 1.2, 0.05);
            UpdateZoom();
        }

        private void ZoomReset_Click(object sender, RoutedEventArgs e)
        {
            _currentZoom = 1.0;
            UpdateZoom();
            CenterCanvas();
        }

        private void ZoomFit_Click(object sender, RoutedEventArgs e)
        {
            AutoFitCanvasToViewport();
        }

        private void FitCanvasToViewport()
        {
            // Legacy method - redirect to AutoFitCanvasToViewport for consistency
            AutoFitCanvasToViewport();
        }

        private void CenterCanvas()
        {
            // Center the canvas in the scroll viewer
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var horizontalOffset = (CanvasScrollViewer.ScrollableWidth) / 2;
                var verticalOffset = (CanvasScrollViewer.ScrollableHeight) / 2;
                CanvasScrollViewer.ScrollToHorizontalOffset(horizontalOffset);
                CanvasScrollViewer.ScrollToVerticalOffset(verticalOffset);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void UpdateZoom()
        {
            CanvasScaleTransform.ScaleX = _currentZoom;
            CanvasScaleTransform.ScaleY = _currentZoom;
            ZoomLevelText.Text = $"{(_currentZoom * 100):F0}%";
        }

        #endregion

        #region Edit Operations

        private void Undo_Click(object sender, RoutedEventArgs e)
        { (DesignerCanvas as SimpleDesignerCanvas)?.Undo(); }

        private void Redo_Click(object sender, RoutedEventArgs e)
        { (DesignerCanvas as SimpleDesignerCanvas)?.Redo(); }

        private void Copy_Click(object sender, RoutedEventArgs e)
        { (DesignerCanvas as SimpleDesignerCanvas)?.CopySelection(); }

        private void Paste_Click(object sender, RoutedEventArgs e)
        { (DesignerCanvas as SimpleDesignerCanvas)?.PasteClipboard(); }

        #endregion

        #region Properties Panel

        private void PropertiesToggle_Click(object sender, RoutedEventArgs e)
        {
            if (PropertiesToggle.IsChecked == true)
            {
                ShowPropertiesPanel();
            }
            else
            {
                HidePropertiesPanel();
            }
        }

        private void CloseProperties_Click(object sender, RoutedEventArgs e)
        {
            PropertiesToggle.IsChecked = false;
            HidePropertiesPanel();
        }

        private void ShowPropertiesPanel()
        {
            PropertiesPanel.Visibility = Visibility.Visible;
            PropertiesBackdrop.Visibility = Visibility.Visible;
            
            var animation = new DoubleAnimation
            {
                From = 400,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            PropertiesPanelTransform.BeginAnimation(TranslateTransform.XProperty, animation);
            UpdatePropertiesPanel();
        }

        private void HidePropertiesPanel()
        {
            var animation = new DoubleAnimation
            {
                From = 0,
                To = 400,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            animation.Completed += (s, e) =>
            {
                PropertiesPanel.Visibility = Visibility.Collapsed;
                PropertiesBackdrop.Visibility = Visibility.Collapsed;
            };
            PropertiesPanelTransform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        private void PropertiesBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Quick close when clicking outside the panel
            PropertiesToggle.IsChecked = false;
            HidePropertiesPanel();
        }

        private void OnRootPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                bool handled = false;

                if (PropertiesPanel.Visibility == Visibility.Visible)
                {
                    PropertiesToggle.IsChecked = false;
                    HidePropertiesPanel();
                    handled = true;
                }

                if (FontControlsPanelContainer.Visibility == Visibility.Visible)
                {
                    // Also close typography tool on ESC
                    HideFontPanel();
                    handled = true;
                }

                if (handled)
                {
                    e.Handled = true;
                }
            }
        }

        private void OnRootPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // If the typography (font) panel is open and the click is outside it, close it
                if (FontControlsPanelContainer != null &&
                    FontControlsPanelContainer.Visibility == Visibility.Visible)
                {
                    var source = e.OriginalSource as DependencyObject;
                    if (!IsDescendantOf(source, FontControlsPanelContainer))
                    {
                        HideFontPanel();
                    }
                }
            }
            catch { }
        }

        private static bool IsDescendantOf(DependencyObject child, DependencyObject parent)
        {
            while (child != null)
            {
                if (child == parent) return true;
                child = VisualTreeHelper.GetParent(child);
            }
            return false;
        }

        #endregion

        #region Template Import/Export from DesignerVM

        /// <summary>
        /// Converts TemplateData from database to Models.Template format
        /// </summary>
        private Photobooth.Models.Template ConvertTemplateDataToTemplate(TemplateData templateData)
        {
            if (templateData == null) return null;

            var template = new Photobooth.Models.Template
            {
                Name = templateData.Name,
                Dimensions = $"{(int)templateData.CanvasWidth}x{(int)templateData.CanvasHeight}",
                Elements = new List<ElementBase>(),
                LastSavedDate = templateData.ModifiedDate.ToString("yyyy-MM-dd HH:mm:ss")
            };

            // Get canvas items from database
            var database = new TemplateDatabase();
            var canvasItems = database.GetCanvasItems(templateData.Id);

            int photoNumber = 1;
            foreach (var item in canvasItems.OrderBy(i => i.ZIndex))
            {
                ElementBase element = null;

                switch (item.ItemType.ToLower())
                {
                    case "placeholder":
                    case "photo":
                        element = new PhotoElement
                        {
                            Left = (int)item.X,
                            Top = (int)item.Y,
                            Width = (int)item.Width,
                            Height = (int)item.Height,
                            ZIndex = item.ZIndex,
                            PhotoNumber = item.PlaceholderNumber ?? photoNumber++,
                            KeepAspect = item.LockedAspectRatio.ToString(),
                            // Outline (stroke) for placeholders/images
                            StrokeColor = string.IsNullOrEmpty(item.StrokeColor) ? null : item.StrokeColor,
                            Thickness = (int)Math.Round(item.StrokeThickness)
                        };
                        break;

                    case "image":
                        element = new ImageElement
                        {
                            Left = (int)item.X,
                            Top = (int)item.Y,
                            Width = (int)item.Width,
                            Height = (int)item.Height,
                            ZIndex = item.ZIndex,
                            ImagePath = item.ImagePath,
                            KeepAspect = item.LockedAspectRatio.ToString(),
                            ImageRotation = (int)item.Rotation,
                            // Outline (stroke)
                            StrokeColor = string.IsNullOrEmpty(item.StrokeColor) ? null : item.StrokeColor,
                            Thickness = (int)Math.Round(item.StrokeThickness)
                        };
                        break;

                    case "text":
                        element = new TextElement
                        {
                            Left = (int)item.X,
                            Top = (int)item.Y,
                            Width = (int)item.Width,
                            Height = (int)item.Height,
                            ZIndex = item.ZIndex,
                            Text = item.Text,
                            FontFamily = item.FontFamily,
                            FontSize = item.FontSize ?? 14,
                            FontWeight = item.FontWeight,
                            FontStyle = item.FontStyle,
                            TextColor = item.TextColor,
                            IsBold = item.IsBold,
                            IsItalic = item.IsItalic,
                            IsUnderlined = item.IsUnderlined,
                            // Persist outline from DB fields
                            StrokeColor = item.HasOutline ? item.OutlineColor : null,
                            Thickness = item.HasOutline ? (int)Math.Round(item.OutlineThickness) : 0
                        };
                        break;

                    case "shape":
                        element = new ShapeElement
                        {
                            Left = (int)item.X,
                            Top = (int)item.Y,
                            Width = (int)item.Width,
                            Height = (int)item.Height,
                            ZIndex = item.ZIndex,
                            ShapeType = item.ShapeType,
                            FillColor = item.FillColor,
                            StrokeColor = item.StrokeColor,
                            StrokeThickness = item.StrokeThickness,
                            HasNoFill = item.HasNoFill,
                            HasNoStroke = item.HasNoStroke
                        };
                        break;

                    case "qrcode":
                        // QR codes are stored in CustomProperties
                        if (!string.IsNullOrEmpty(item.CustomProperties))
                        {
                            element = new QrCodeElement
                            {
                                Left = (int)item.X,
                                Top = (int)item.Y,
                                Width = (int)item.Width,
                                Height = (int)item.Height,
                                ZIndex = item.ZIndex,
                                CustomProperties = item.CustomProperties
                            };
                        }
                        break;

                    case "barcode":
                        // Barcodes are stored in CustomProperties
                        if (!string.IsNullOrEmpty(item.CustomProperties))
                        {
                            element = new BarcodeElement
                            {
                                Left = (int)item.X,
                                Top = (int)item.Y,
                                Width = (int)item.Width,
                                Height = (int)item.Height,
                                ZIndex = item.ZIndex,
                                CustomProperties = item.CustomProperties
                            };
                        }
                        break;
                }

                if (element != null)
                {
                    // Map shadow from DB item to template element (ElementBase)
                    try
                    {
                        if (item.HasShadow)
                        {
                            element.ShadowEnabled = "true";
                            element.ShadowColor = item.ShadowColor;
                            element.ShadowRadius = (int)Math.Round(item.ShadowBlurRadius);
                            // Convert XY offsets to polar
                            var depth = Math.Sqrt(item.ShadowOffsetX * item.ShadowOffsetX + item.ShadowOffsetY * item.ShadowOffsetY);
                            var direction = Math.Atan2(item.ShadowOffsetY, item.ShadowOffsetX) * (180.0 / Math.PI);
                            element.ShadowDepth = (int)Math.Round(depth);
                            element.ShadowDirection = (int)Math.Round(direction);
                        }
                        else
                        {
                            element.ShadowEnabled = "false";
                        }
                    }
                    catch { }

                    template.Elements.Add(element);
                }
            }

            return template;
        }

        /// <summary>
        /// Applies a template from DesignerVM format to the SimpleDesignerCanvas
        /// </summary>
        public void ApplyTemplateFromDesignerVM(Photobooth.Models.Template template, List<string> placeholderImages = null)
        {
            try
            {
                var canvas = this.DesignerCanvas as SimpleDesignerCanvas;
                if (canvas == null)
                {
                    Log.Error("TouchTemplateDesigner: Canvas is not SimpleDesignerCanvas");
                    return;
                }

                // Clear existing items
                canvas.Items.Clear();

                // Reset placeholder counter when loading a template
                SimpleImageItem.ResetPlaceholderCounter();

                if (template == null) return;

                // Parse and set canvas dimensions - but check for desired size first
                if (_desiredCanvasWidth.HasValue && _desiredCanvasHeight.HasValue)
                {
                    // Use the desired canvas size if one has been set via paper size selection
                    canvas.Width = _desiredCanvasWidth.Value;
                    canvas.Height = _desiredCanvasHeight.Value;
                    Log.Debug($"TouchTemplateDesigner: Using desired canvas size {_desiredCanvasWidth.Value}x{_desiredCanvasHeight.Value}");
                }
                else if (!string.IsNullOrEmpty(template.Dimensions))
                {
                    // Fall back to template dimensions if no desired size is set
                    var dimensions = template.Dimensions.Split('x').Select(int.Parse).ToArray();
                    if (dimensions.Length == 2)
                    {
                        canvas.Width = dimensions[0];
                        canvas.Height = dimensions[1];
                        Log.Debug($"TouchTemplateDesigner: Using template dimensions {dimensions[0]}x{dimensions[1]}");
                    }
                }

                // Update the canvas size display regardless of which size was used
                UpdateCanvasSizeDisplay();

                // Auto-fit canvas after size changes
                Dispatcher.BeginInvoke(new Action(() => AutoFitCanvasToViewport()),
                    System.Windows.Threading.DispatcherPriority.Background);

                // Process template elements
                foreach (var element in template.Elements.OrderBy(e => e.ZIndex))
                {
                    SimpleCanvasItem newItem = null;

                    if (element is PhotoElement p)
                    {
                        // Create photo placeholder or image with the specific number from the template
                        var imageItem = new SimpleImageItem(true, p.PhotoNumber); // Pass the specific placeholder number
                        imageItem.Left = p.Left;
                        imageItem.Top = p.Top;
                        imageItem.Width = p.Width;
                        imageItem.Height = p.Height;
                        imageItem.ZIndex = p.ZIndex;

                        // Note: Rotation property is not available in the current PhotoElement model
                        // TODO: Add rotation support when model is updated

                        // If we have a placeholder image for this position, load it
                        if (placeholderImages != null && placeholderImages.Count > p.PhotoNumber - 1)
                        {
                            string photoPath = placeholderImages[p.PhotoNumber - 1];
                            try
                            {
                                var imageSource = new BitmapImage(new Uri(photoPath, UriKind.RelativeOrAbsolute));
                                imageItem.ImageSource = imageSource;
                                imageItem.IsPlaceholder = false;
                            }
                            catch (Exception imgEx)
                            {
                                Log.Error($"Failed to load placeholder image: {imgEx.Message}");
                            }
                        }

                        newItem = imageItem;
                    }
                    else if (element is ImageElement i)
                    {
                        // Create regular image
                        var imageItem = new SimpleImageItem(false);
                        imageItem.Left = i.Left;
                        imageItem.Top = i.Top;
                        imageItem.Width = i.Width;
                        imageItem.Height = i.Height;
                        imageItem.ZIndex = i.ZIndex;

                        // Note: Rotation property is not available in the current ImageElement model
                        // TODO: Add rotation support when model is updated

                        try
                        {
                            var imageSource = new BitmapImage(new Uri(i.ImagePath, UriKind.RelativeOrAbsolute));
                            imageItem.ImageSource = imageSource;
                            imageItem.ImagePath = i.ImagePath;
                        }
                        catch (Exception imgEx)
                        {
                            Log.Error($"Failed to load image: {imgEx.Message}");
                        }

                        newItem = imageItem;
                    }
                    else if (element is TextElement t)
                    {
                        // Create text item
                        var textItem = new SimpleTextItem();
                        textItem.Left = t.Left;
                        textItem.Top = t.Top;
                        textItem.Width = t.Width;
                        textItem.Height = t.Height;
                        textItem.ZIndex = t.ZIndex;
                        textItem.Text = t.Text ?? "";

                        // Set font properties
                        if (!string.IsNullOrEmpty(t.FontFamily))
                        {
                            textItem.FontFamily = new FontFamily(t.FontFamily);
                        }
                        textItem.FontSize = t.FontSize > 0 ? t.FontSize : 14;

                        // Parse font weight and style
                        if (!string.IsNullOrEmpty(t.FontWeight))
                        {
                            try
                            {
                                textItem.FontWeight = (FontWeight)new FontWeightConverter().ConvertFromString(t.FontWeight);
                            }
                            catch { textItem.FontWeight = FontWeights.Normal; }
                        }

                        if (!string.IsNullOrEmpty(t.FontStyle))
                        {
                            try
                            {
                                textItem.FontStyle = (FontStyle)new FontStyleConverter().ConvertFromString(t.FontStyle);
                            }
                            catch { textItem.FontStyle = FontStyles.Normal; }
                        }

                        // Set text color
                        if (!string.IsNullOrEmpty(t.TextColor))
                        {
                            try
                            {
                                var color = (Color)ColorConverter.ConvertFromString(t.TextColor);
                                textItem.Foreground = new SolidColorBrush(color);
                                textItem.TextColor = new SolidColorBrush(color);
                            }
                            catch
                            {
                                textItem.Foreground = Brushes.Black;
                                textItem.TextColor = Brushes.Black;
                            }
                        }

                        newItem = textItem;
                    }
                    else if (element is ShapeElement s)
                    {
                        // Create shape item
                        var shapeItem = new SimpleShapeItem();
                        shapeItem.Left = s.Left;
                        shapeItem.Top = s.Top;
                        shapeItem.Width = s.Width;
                        shapeItem.Height = s.Height;
                        shapeItem.ZIndex = s.ZIndex;

                        // Parse shape type
                        if (!string.IsNullOrEmpty(s.ShapeType))
                        {
                            if (Enum.TryParse<SimpleShapeType>(s.ShapeType, out var shapeType))
                            {
                                shapeItem.ShapeType = shapeType;
                            }
                        }

                        // Set fill color
                        if (!string.IsNullOrEmpty(s.FillColor) && !s.HasNoFill)
                        {
                            try
                            {
                                var color = (Color)ColorConverter.ConvertFromString(s.FillColor);
                                shapeItem.Fill = new SolidColorBrush(color);
                            }
                            catch { shapeItem.Fill = Brushes.Transparent; }
                        }
                        else if (s.HasNoFill)
                        {
                            shapeItem.Fill = Brushes.Transparent;
                        }

                        // Set stroke color
                        if (!string.IsNullOrEmpty(s.StrokeColor) && !s.HasNoStroke)
                        {
                            try
                            {
                                var color = (Color)ColorConverter.ConvertFromString(s.StrokeColor);
                                shapeItem.Stroke = new SolidColorBrush(color);
                            }
                            catch { shapeItem.Stroke = Brushes.Black; }
                        }
                        else if (s.HasNoStroke)
                        {
                            shapeItem.Stroke = null;
                        }

                        shapeItem.StrokeThickness = s.StrokeThickness > 0 ? s.StrokeThickness : 1;

                        newItem = shapeItem;
                    }
                    else if (element is QrCodeElement qr)
                    {
                        // Create QR code item
                        var qrItem = new SimpleQRCodeItem();
                        qrItem.Left = qr.Left;
                        qrItem.Top = qr.Top;
                        qrItem.Width = qr.Width;
                        qrItem.Height = qr.Height;
                        qrItem.ZIndex = qr.ZIndex;

                        // Parse CustomProperties JSON to set QR code specific properties
                        if (!string.IsNullOrEmpty(qr.CustomProperties))
                        {
                            try
                            {
                                dynamic props = Newtonsoft.Json.JsonConvert.DeserializeObject(qr.CustomProperties);
                                if (props != null)
                                {
                                    qrItem.Value = props.Value ?? "https://example.com";
                                    var eccLevelStr = props.ECC?.ToString() ?? "Q";
                                    QRCoder.QRCodeGenerator.ECCLevel eccLevel;
                                    if (Enum.TryParse<QRCoder.QRCodeGenerator.ECCLevel>(eccLevelStr, out eccLevel))
                                    {
                                        qrItem.EccLevel = eccLevel;
                                    }
                                    qrItem.PixelsPerModule = (int)(props.PixelsPerModule ?? 4);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"TouchTemplateDesigner: Failed to parse QR code properties: {ex.Message}");
                            }
                        }

                        newItem = qrItem;
                    }
                    else if (element is BarcodeElement bc)
                    {
                        // Create barcode item
                        var bcItem = new SimpleBarcodeItem();
                        bcItem.Left = bc.Left;
                        bcItem.Top = bc.Top;
                        bcItem.Width = bc.Width;
                        bcItem.Height = bc.Height;
                        bcItem.ZIndex = bc.ZIndex;

                        // Parse CustomProperties JSON to set barcode specific properties
                        if (!string.IsNullOrEmpty(bc.CustomProperties))
                        {
                            try
                            {
                                dynamic props = Newtonsoft.Json.JsonConvert.DeserializeObject(bc.CustomProperties);
                                if (props != null)
                                {
                                    bcItem.Value = props.Value ?? "123456789";
                                    var symbologyStr = props.Symbology?.ToString() ?? "Code39";
                                    BarcodeSymbology symbology;
                                    if (Enum.TryParse<BarcodeSymbology>(symbologyStr, out symbology))
                                    {
                                        bcItem.Symbology = symbology;
                                    }
                                    bcItem.ModuleWidth = (int)(props.ModuleWidth ?? 2);
                                    bcItem.IncludeLabel = props.IncludeLabel ?? true;
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"TouchTemplateDesigner: Failed to parse barcode properties: {ex.Message}");
                            }
                        }

                        newItem = bcItem;
                    }
                    else
                    {
                        // For any unknown element types, log and continue
                        Log.Debug($"TouchTemplateDesigner: Skipping unsupported element type: {element.GetType().Name}");
                    }

                    // Apply common appearance (stroke/shadow) from element base and add to canvas
                    if (newItem != null)
                    {
                        try
                        {
                            // Stroke/outline
                            if (!string.IsNullOrEmpty(element.StrokeColor) && element.Thickness > 0)
                            {
                                var sc = (Color)ColorConverter.ConvertFromString(element.StrokeColor);
                                var sb = new SolidColorBrush(sc);
                                if (newItem is SimpleTextItem ti)
                                {
                                    ti.StrokeBrush = sb;
                                    ti.StrokeThickness = element.Thickness;
                                }
                                else if (newItem is SimpleImageItem ii)
                                {
                                    ii.StrokeBrush = sb;
                                    ii.StrokeThickness = element.Thickness;
                                }
                                else if (newItem is SimpleShapeItem si)
                                {
                                    si.Stroke = sb;
                                    if (element.Thickness > 0) si.StrokeThickness = element.Thickness;
                                }
                            }

                            // Drop shadow
                            bool shadowEnabled = false;
                            bool.TryParse(element.ShadowEnabled, out shadowEnabled);
                            if (shadowEnabled)
                            {
                                var shadow = new System.Windows.Media.Effects.DropShadowEffect
                                {
                                    BlurRadius = Math.Max(0, element.ShadowRadius),
                                    ShadowDepth = Math.Max(0, element.ShadowDepth),
                                    Direction = element.ShadowDirection,
                                    Opacity = 0.8
                                };
                                if (!string.IsNullOrEmpty(element.ShadowColor))
                                {
                                    try { shadow.Color = (Color)ColorConverter.ConvertFromString(element.ShadowColor); } catch { }
                                }
                                newItem.Effect = shadow;
                            }
                        }
                        catch { }

                        canvas.Items.Add(newItem);
                    }
                }

                // Update template name
                TemplateNameText.Text = template.Name ?? "Imported Template";

                // Refresh layers panel
                if (LayersPanel.Visibility == Visibility.Visible)
                {
                    LayersPanel.RefreshLayers();
                }

                Log.Debug($"TouchTemplateDesigner: Applied template '{template.Name}' with {template.Elements.Count} elements");
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to apply template: {ex.Message}");
                MessageBox.Show($"Failed to apply template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Panel Toggle Methods

        private void LayersToggle_Click(object sender, RoutedEventArgs e)
        {
            if (LayersToggle.IsChecked == true)
            {
                // Close font panel if open
                if (FontToggle.IsChecked == true)
                {
                    FontToggle.IsChecked = false;
                    HideFontPanel();
                }

                ShowLayersPanel();
            }
            else
            {
                HideLayersPanel();
            }
        }

        private void FontToggle_Click(object sender, RoutedEventArgs e)
        {
            if (FontToggle.IsChecked == true)
            {
                // Close layers panel if open
                if (LayersToggle.IsChecked == true)
                {
                    LayersToggle.IsChecked = false;
                    HideLayersPanel();
                }

                ShowFontPanel();
            }
            else
            {
                HideFontPanel();
            }
        }

        private void ShowLayersPanel()
        {
            LayersPanelContainer.Visibility = Visibility.Visible;
            LayersPanel.RefreshLayers();

            var storyboard = FindResource("SlideInLayers") as Storyboard;
            storyboard?.Begin();
        }

        private void HideLayersPanel()
        {
            var storyboard = FindResource("SlideOutLayers") as Storyboard;
            if (storyboard != null)
            {
                storyboard.Completed += (s, e) => LayersPanelContainer.Visibility = Visibility.Collapsed;
                storyboard.Begin();
            }
        }

        private void ShowFontPanel()
        {
            FontControlsPanelContainer.Visibility = Visibility.Visible;

            // Set selected text item if any
            var selectedItem = DesignerCanvas.SelectedItems.FirstOrDefault();
            if (selectedItem != null)
            {
                FontControlsPanel.SetSelectedTextItem(selectedItem);
            }

            var storyboard = FindResource("SlideInFont") as Storyboard;
            storyboard?.Begin();
        }

        private void HideFontPanel()
        {
            var storyboard = FindResource("SlideOutFont") as Storyboard;
            if (storyboard != null)
            {
                storyboard.Completed += (s, e) => FontControlsPanelContainer.Visibility = Visibility.Collapsed;
                storyboard.Begin();
            }
        }

        private void FontControlsPanel_FontChanged(object sender, Controls.FontChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("TouchTemplateDesigner: FontControlsPanel_FontChanged event received");

            // Apply font changes to selected text item
            var selectedItem = DesignerCanvas.SelectedItems.FirstOrDefault();

            System.Diagnostics.Debug.WriteLine($"TouchTemplateDesigner: SelectedItem = {selectedItem?.GetType().Name ?? "null"}");
            if (selectedItem == null)
            {
                // No selection: still persist picked text color as the current default for new text
                if (e.TextColor is SolidColorBrush scb)
                {
                    _currentSelectedColor = scb.Color;
                    UpdateColorPreview();
                    System.Diagnostics.Debug.WriteLine($"TouchTemplateDesigner: No selection; stored picked color {_currentSelectedColor} for future text");
                }
                return;
            }

            // Check if it's a SimpleTextItem (the correct type for SimpleDesignerCanvas text items)
            if (selectedItem is SimpleTextItem textItem)
            {
                try
                {
                    // Apply font changes to the SimpleTextItem
                    if (e.FontFamily != null)
                        textItem.FontFamily = new System.Windows.Media.FontFamily(e.FontFamily.Source);

                    if (e.FontSize.HasValue)
                        textItem.FontSize = e.FontSize.Value;

                    if (e.FontWeight.HasValue)
                        textItem.FontWeight = e.FontWeight.Value;

                    if (e.FontStyle.HasValue)
                        textItem.FontStyle = e.FontStyle.Value;

                    if (e.TextAlignment.HasValue)
                        textItem.TextAlignment = e.TextAlignment.Value;

                    if (e.TextColor != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"TouchTemplateDesigner: Applying TextColor to textItem. Color type: {e.TextColor.GetType().Name}");

                        // Store old color for comparison
                        var oldColor = textItem.TextColor;

                        // Apply the new color
                        textItem.TextColor = e.TextColor;

                        // Force a visual refresh
                        textItem.InvalidateVisual();

                        // Keep the inline color preview/defaults in sync with picker/eyedropper
                        if (e.TextColor is SolidColorBrush scb)
                        {
                            _currentSelectedColor = scb.Color;
                            UpdateColorPreview();
                        }

                        System.Diagnostics.Debug.WriteLine($"TouchTemplateDesigner: TextColor changed from {oldColor} to {textItem.TextColor}");
                        Log.Debug($"TouchTemplateDesigner: Applied text color {e.TextColor} to SimpleTextItem");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("TouchTemplateDesigner: e.TextColor is null, skipping color update");
                    }

                    // Typography spacing
                    if (e.LineHeight.HasValue)
                        textItem.LineHeight = e.LineHeight.Value;
                    if (e.LetterSpacing.HasValue)
                        textItem.LetterSpacing = e.LetterSpacing.Value;

                    // Text transform (uppercase/lowercase/capitalize)
                    if (e.TextTransform.HasValue)
                    {
                        try
                        {
                            var mode = e.TextTransform.Value;
                            var current = textItem.Text ?? string.Empty;
                            string transformed = current;
                            switch (mode)
                            {
                                case Controls.TextTransform.Uppercase:
                                    transformed = current.ToUpper();
                                    break;
                                case Controls.TextTransform.Lowercase:
                                    transformed = current.ToLower();
                                    break;
                                case Controls.TextTransform.Capitalize:
                                    transformed = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(current.ToLower());
                                    break;
                                case Controls.TextTransform.None:
                                default:
                                    // leave as-is
                                    break;
                            }
                            if (transformed != current)
                            {
                                textItem.Text = transformed;
                            }
                        }
                        catch (System.Exception tex)
                        {
                            System.Diagnostics.Debug.WriteLine($"TouchTemplateDesigner: Error applying text transform: {tex.Message}");
                        }
                    }

                    // Note: TextDecorations (underline) not yet implemented in SimpleTextItem
                    if (e.TextDecorations != null)
                    {
                        Debug.WriteLine("TextDecorations not yet implemented for SimpleTextItem");
                    }

                    // Shadow effect
                    if (e.DropShadow is System.Windows.Media.Effects.DropShadowEffect shadow)
                    {
                        textItem.Effect = shadow;
                    }

                    // Stroke
                    if (e.StrokeBrush != null)
                        textItem.StrokeBrush = e.StrokeBrush;
                    if (e.StrokeThickness.HasValue)
                        textItem.StrokeThickness = e.StrokeThickness.Value;

                    if (!string.IsNullOrEmpty(e.InsertCharacter))
                    {
                        textItem.Text += e.InsertCharacter;
                    }

                    // Orientation
                    if (e.IsVerticalStack.HasValue)
                        textItem.IsVerticalStack = e.IsVerticalStack.Value;
                    if (e.IsVertical.HasValue)
                        textItem.IsVertical = e.IsVertical.Value;

                    Log.Debug($"TouchTemplateDesigner: Applied font changes to SimpleTextItem - Text: '{textItem.Text}', Color: {textItem.TextColor}");
                }
                catch (Exception ex)
                {
                    Log.Error($"TouchTemplateDesigner: Error applying font changes: {ex.Message}");
                }
            }
            else if (selectedItem is SimpleImageItem imageItem)
            {
                try
                {
                    if (e.StrokeBrush != null)
                        imageItem.StrokeBrush = e.StrokeBrush;
                    if (e.StrokeThickness.HasValue)
                        imageItem.StrokeThickness = e.StrokeThickness.Value;
                    if (e.DropShadow is System.Windows.Media.Effects.DropShadowEffect shadow)
                        imageItem.Effect = shadow;
                }
                catch (Exception ex)
                {
                    Log.Error($"TouchTemplateDesigner: Error applying appearance to SimpleImageItem: {ex.Message}");
                }
            }
            else
            {
                Log.Debug($"TouchTemplateDesigner: Selected item is not a SimpleTextItem, it's a {selectedItem?.GetType().Name}");
                if (e.TextColor is SolidColorBrush scb)
                {
                    _currentSelectedColor = scb.Color;
                    UpdateColorPreview();
                }
            }
        }

        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;

                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }

            return null;
        }

        private void UpdatePropertiesPanel()
        {
            PropertiesContent.Children.Clear();

            var canvas = DesignerCanvas as SimpleDesignerCanvas;
            var item = canvas?.SelectedItem;

            if (item == null)
            {
                PropertiesContent.Children.Add(new TextBlock
                {
                    Text = "Select an item to view properties",
                    Foreground = Brushes.Gray,
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                });
                return;
            }

            // Header
            PropertiesContent.Children.Add(new TextBlock
            {
                Text = item.GetDisplayName(),
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            // QR Code item properties
            if (item is SimpleQRCodeItem qr)
            {
                var qrGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
                qrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                qrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                void AddQrRow(string label, FrameworkElement input)
                {
                    int r = qrGrid.RowDefinitions.Count;
                    qrGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var lbl = new TextBlock { Text = label, Foreground = Brushes.LightGray, FontSize = 14, Margin = new Thickness(0, 6, 10, 6), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetRow(lbl, r); Grid.SetColumn(lbl, 0);
                    Grid.SetRow(input, r); Grid.SetColumn(input, 1);
                    qrGrid.Children.Add(lbl); qrGrid.Children.Add(input);
                }

                var valBox = new TextBox { Text = qr.Value, Background = new SolidColorBrush(Color.FromRgb(45,45,48)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(64,64,64)), Padding = new Thickness(8,5,8,5), FontSize = 14 };
                valBox.LostFocus += (s, e2) => { qr.Value = valBox.Text; };
                valBox.KeyDown += (s, e2) => { if (e2.Key == Key.Enter) { qr.Value = valBox.Text; e2.Handled = true; } };
                AddQrRow("Value", valBox);

                // Token helper row
                var tokenPanel = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                FrameworkElement MakeTokenButton(string label, string token)
                {
                    var btn = new Button
                    {
                        Content = label,
                        Margin = new Thickness(0, 0, 6, 6),
                        Padding = new Thickness(8, 4, 8, 4),
                        Background = new SolidColorBrush(Color.FromRgb(45,45,48)),
                        Foreground = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(64,64,64)),
                        BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand
                    };
                    btn.Click += (s, e2) =>
                    {
                        // Insert token (append if empty)
                        if (string.IsNullOrWhiteSpace(valBox.Text))
                            valBox.Text = token;
                        else if (!valBox.Text.Contains(token))
                            valBox.Text += (valBox.Text.EndsWith(" ") ? "" : " ") + token;
                        qr.Value = valBox.Text;
                    };
                    return btn;
                }
                tokenPanel.Children.Add(MakeTokenButton("Session URL", "{SESSION.URL}"));
                tokenPanel.Children.Add(MakeTokenButton("Event URL", "{EVENT.URL}"));
                tokenPanel.Children.Add(MakeTokenButton("Date", "{DATE:yyyy-MM-dd}"));
                tokenPanel.Children.Add(MakeTokenButton("Time", "{TIME:HH:mm}"));
                AddQrRow("Quick tokens", tokenPanel);

                var eccBox = new ComboBox { Background = new SolidColorBrush(Color.FromRgb(45,45,48)), Foreground = Brushes.White, Padding = new Thickness(8,5,8,5) };
                eccBox.Items.Add("L"); eccBox.Items.Add("M"); eccBox.Items.Add("Q"); eccBox.Items.Add("H");
                eccBox.SelectedItem = qr.EccLevel.ToString();
                eccBox.SelectionChanged += (s, e2) => {
                    switch (eccBox.SelectedItem as string)
                    {
                        case "L": qr.EccLevel = QRCoder.QRCodeGenerator.ECCLevel.L; break;
                        case "M": qr.EccLevel = QRCoder.QRCodeGenerator.ECCLevel.M; break;
                        case "Q": qr.EccLevel = QRCoder.QRCodeGenerator.ECCLevel.Q; break;
                        case "H": qr.EccLevel = QRCoder.QRCodeGenerator.ECCLevel.H; break;
                    }
                };
                AddQrRow("ECC", eccBox);

                var ppmSlider = new Slider { Minimum = 1, Maximum = 20, Value = qr.PixelsPerModule, Height = 24 };
                ppmSlider.ValueChanged += (s, e2) => { qr.PixelsPerModule = (int)e2.NewValue; };
                AddQrRow("Pixels/module", ppmSlider);

                PropertiesContent.Children.Add(qrGrid);
                return;
            }

            // Barcode item properties
            if (item is SimpleBarcodeItem bc)
            {
                var bcGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
                bcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                bcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                void AddBcRow(string label, FrameworkElement input)
                {
                    int r = bcGrid.RowDefinitions.Count;
                    bcGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var lbl = new TextBlock { Text = label, Foreground = Brushes.LightGray, FontSize = 14, Margin = new Thickness(0, 6, 10, 6), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetRow(lbl, r); Grid.SetColumn(lbl, 0);
                    Grid.SetRow(input, r); Grid.SetColumn(input, 1);
                    bcGrid.Children.Add(lbl); bcGrid.Children.Add(input);
                }

                // Value + tokens helper
                var valBox = new TextBox { Text = bc.Value, Background = new SolidColorBrush(Color.FromRgb(45,45,48)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(64,64,64)), Padding = new Thickness(8,5,8,5), FontSize = 14 };
                valBox.LostFocus += (s, e2) => { bc.Value = valBox.Text; };
                valBox.KeyDown += (s, e2) => { if (e2.Key == Key.Enter) { bc.Value = valBox.Text; e2.Handled = true; } };
                AddBcRow("Value", valBox);

                var tokenPanel = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                FrameworkElement MakeBcTokenButton(string label, string token)
                {
                    var btn = new Button
                    {
                        Content = label,
                        Margin = new Thickness(0, 0, 6, 6),
                        Padding = new Thickness(8, 4, 8, 4),
                        Background = new SolidColorBrush(Color.FromRgb(45,45,48)),
                        Foreground = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(64,64,64)),
                        BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand
                    };
                    btn.Click += (s, e2) =>
                    {
                        if (string.IsNullOrWhiteSpace(valBox.Text))
                            valBox.Text = token;
                        else if (!valBox.Text.Contains(token))
                            valBox.Text += (valBox.Text.EndsWith(" ") ? "" : " ") + token;
                        bc.Value = valBox.Text;
                    };
                    return btn;
                }
                tokenPanel.Children.Add(MakeBcTokenButton("Session URL", "{SESSION.URL}"));
                tokenPanel.Children.Add(MakeBcTokenButton("Event URL", "{EVENT.URL}"));
                tokenPanel.Children.Add(MakeBcTokenButton("Date", "{DATE:yyyy-MM-dd}"));
                tokenPanel.Children.Add(MakeBcTokenButton("Time", "{TIME:HH:mm}"));
                AddBcRow("Quick tokens", tokenPanel);

                // Symbology (currently Code39 only)
                var symBox = new ComboBox { Background = new SolidColorBrush(Color.FromRgb(45,45,48)), Foreground = Brushes.White, Padding = new Thickness(8,5,8,5) };
                symBox.Items.Add("Code39");
                symBox.SelectedItem = bc.Symbology.ToString();
                symBox.SelectionChanged += (s, e2) =>
                {
                    if ((symBox.SelectedItem as string) == "Code39")
                        bc.Symbology = BarcodeSymbology.Code39;
                };
                AddBcRow("Symbology", symBox);

                // Module width
                var mwSlider = new Slider { Minimum = 1, Maximum = 10, Value = bc.ModuleWidth, Height = 24 };
                mwSlider.ValueChanged += (s, e2) => { bc.ModuleWidth = (int)e2.NewValue; };
                AddBcRow("Module width", mwSlider);

                // Include label
                var labelCheck = new CheckBox { IsChecked = bc.IncludeLabel, Content = "Show human-readable text", Foreground = Brushes.White };
                labelCheck.Checked += (s, e2) => { bc.IncludeLabel = true; };
                labelCheck.Unchecked += (s, e2) => { bc.IncludeLabel = false; };
                AddBcRow("Label", labelCheck);

                PropertiesContent.Children.Add(bcGrid);
                return;
            }

            // Grid with labels and inputs
            var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            void AddRow(string label, FrameworkElement input)
            {
                int row = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var lbl = new TextBlock
                {
                    Text = label,
                    Foreground = Brushes.LightGray,
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 6, 10, 6)
                };
                Grid.SetRow(lbl, row);
                Grid.SetColumn(lbl, 0);
                Grid.SetRow(input, row);
                Grid.SetColumn(input, 1);
                grid.Children.Add(lbl);
                grid.Children.Add(input);
            }

            TextBox MakeNumberBox(double value)
            {
                return new TextBox
                {
                    Text = ((int)Math.Round(value)).ToString(),
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(64, 64, 64)),
                    Padding = new Thickness(8, 5, 8, 5),
                    FontSize = 14,
                    MinWidth = 80
                };
            }

            FrameworkElement BuildStepper(TextBox box, Action increment, Action decrement, string unit = "px")
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var minusBtn = new Button
                {
                    Content = "-",
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = new SolidColorBrush(Color.FromRgb(45,45,48)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(64,64,64)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(0)
                };
                minusBtn.Click += (s, e) => decrement();

                var plusBtn = new Button
                {
                    Content = "+",
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(6, 0, 0, 0),
                    Background = new SolidColorBrush(Color.FromRgb(45,45,48)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(64,64,64)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(0)
                };
                plusBtn.Click += (s, e) => increment();

                panel.Children.Add(minusBtn);
                panel.Children.Add(box);
                panel.Children.Add(new TextBlock
                {
                    Text = $" {unit}",
                    Foreground = Brushes.Gray,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0)
                });
                panel.Children.Add(plusBtn);
                return panel;
            }

            // Coordinate mode toggle (Left/Top vs Center)
            var centerCheck = new CheckBox
            {
                Content = "Use center origin",
                IsChecked = _useCenterCoordinates,
                Foreground = Brushes.White,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10)
            };
            centerCheck.Checked += (s, e) => { _useCenterCoordinates = true; UpdatePropertiesPanel(); };
            centerCheck.Unchecked += (s, e) => { _useCenterCoordinates = false; UpdatePropertiesPanel(); };

            var xBox = MakeNumberBox(_useCenterCoordinates ? item.Left + item.Width / 2 : item.Left);
            var yBox = MakeNumberBox(_useCenterCoordinates ? item.Top + item.Height / 2 : item.Top);
            var wBox = MakeNumberBox(item.Width);
            var hBox = MakeNumberBox(item.Height);
            var aspectCheck = new CheckBox
            {
                Content = "Lock aspect ratio",
                IsChecked = item.IsAspectRatioLocked,
                Foreground = Brushes.White,
                FontSize = 14,
                Margin = new Thickness(0, 6, 0, 6)
            };

            void ApplyIfValid(TextBox box, Action<double> setter)
            {
                if (double.TryParse(box.Text, out var v))
                {
                    setter(Math.Max(1, v));
                }
            }

            aspectCheck.Checked += (s, e) => { item.IsAspectRatioLocked = true; };
            aspectCheck.Unchecked += (s, e) => { item.IsAspectRatioLocked = false; };

            void SyncBoxesFromItem()
            {
                var dispX = _useCenterCoordinates ? item.Left + item.Width / 2 : item.Left;
                var dispY = _useCenterCoordinates ? item.Top + item.Height / 2 : item.Top;
                xBox.Text = ((int)Math.Round(dispX)).ToString();
                yBox.Text = ((int)Math.Round(dispY)).ToString();
                wBox.Text = ((int)Math.Round(item.Width)).ToString();
                hBox.Text = ((int)Math.Round(item.Height)).ToString();
                aspectCheck.IsChecked = item.IsAspectRatioLocked;
            }

            void WidthChanged()
            {
                double prevW = item.Width;
                double prevH = item.Height;
                if (double.TryParse(wBox.Text, out var newW))
                {
                    newW = Math.Max(1, newW);
                    if (item.IsAspectRatioLocked && prevH > 0)
                    {
                        var ratio = prevW / prevH;
                        item.Width = newW;
                        item.Height = Math.Max(1, newW / (ratio == 0 ? 1 : ratio));
                    }
                    else
                    {
                        item.Width = newW;
                    }
                    SyncBoxesFromItem();
                }
            }

            void HeightChanged()
            {
                double prevW = item.Width;
                double prevH = item.Height;
                if (double.TryParse(hBox.Text, out var newH))
                {
                    newH = Math.Max(1, newH);
                    if (item.IsAspectRatioLocked && prevH > 0)
                    {
                        var ratio = prevW / prevH;
                        item.Height = newH;
                        item.Width = Math.Max(1, newH * ratio);
                    }
                    else
                    {
                        item.Height = newH;
                    }
                    SyncBoxesFromItem();
                }
            }

            void LeftChanged()
            {
                ApplyIfValid(xBox, v =>
                {
                    if (_useCenterCoordinates)
                        item.Left = v - item.Width / 2;
                    else
                        item.Left = v;
                });
                SyncBoxesFromItem();
            }
            void TopChanged()
            {
                ApplyIfValid(yBox, v =>
                {
                    if (_useCenterCoordinates)
                        item.Top = v - item.Height / 2;
                    else
                        item.Top = v;
                });
                SyncBoxesFromItem();
            }

            xBox.LostFocus += (s, e) => LeftChanged();
            yBox.LostFocus += (s, e) => TopChanged();
            wBox.LostFocus += (s, e) => WidthChanged();
            hBox.LostFocus += (s, e) => HeightChanged();

            // Arrow-key nudging with Shift for x10
            void WithStep(KeyEventArgs e, Action<int> action)
            {
                int step = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? 10 : 1;
                if (e.Key == Key.Up)
                {
                    action(step);
                    e.Handled = true;
                }
                else if (e.Key == Key.Down)
                {
                    action(-step);
                    e.Handled = true;
                }
            }

            void NudgeX(int delta)
            {
                double val = 0;
                if (!double.TryParse(xBox.Text, out val))
                {
                    val = _useCenterCoordinates ? (item.Left + item.Width / 2) : item.Left;
                }
                val += delta;
                xBox.Text = ((int)Math.Round(val)).ToString();
                LeftChanged();
            }
            void NudgeY(int delta)
            {
                double val = 0;
                if (!double.TryParse(yBox.Text, out val))
                {
                    val = _useCenterCoordinates ? (item.Top + item.Height / 2) : item.Top;
                }
                val += delta;
                yBox.Text = ((int)Math.Round(val)).ToString();
                TopChanged();
            }
            void NudgeW(int delta)
            {
                double val = 0;
                if (!double.TryParse(wBox.Text, out val)) val = item.Width;
                val = Math.Max(1, val + delta);
                wBox.Text = ((int)Math.Round(val)).ToString();
                WidthChanged();
            }
            void NudgeH(int delta)
            {
                double val = 0;
                if (!double.TryParse(hBox.Text, out val)) val = item.Height;
                val = Math.Max(1, val + delta);
                hBox.Text = ((int)Math.Round(val)).ToString();
                HeightChanged();
            }

            xBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { LeftChanged(); e.Handled = true; return; }
                WithStep(e, NudgeX);
            };
            yBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { TopChanged(); e.Handled = true; return; }
                WithStep(e, NudgeY);
            };
            wBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { WidthChanged(); e.Handled = true; return; }
                WithStep(e, NudgeW);
            };
            hBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { HeightChanged(); e.Handled = true; return; }
                WithStep(e, NudgeH);
            };

            // Add controls
            PropertiesContent.Children.Add(centerCheck);
            AddRow(_useCenterCoordinates ? "X (Center)" : "X (Left)", BuildStepper(xBox, () => NudgeX(+1), () => NudgeX(-1)));
            AddRow(_useCenterCoordinates ? "Y (Center)" : "Y (Top)", BuildStepper(yBox, () => NudgeY(+1), () => NudgeY(-1)));
            AddRow("Width", BuildStepper(wBox, () => NudgeW(+1), () => NudgeW(-1)));
            AddRow("Height", BuildStepper(hBox, () => NudgeH(+1), () => NudgeH(-1)));
            AddRow("Aspect", aspectCheck);

            PropertiesContent.Children.Add(grid);
        }

        private void BindPropertiesPanelToSelectedItem()
        {
            var canvas = DesignerCanvas as SimpleDesignerCanvas;
            var item = canvas?.SelectedItem;

            if (_propertiesBoundItem == item)
            {
                // Still the same item; refresh fields
                if (PropertiesPanel.Visibility == Visibility.Visible)
                    UpdatePropertiesPanel();
                return;
            }

            // Unsubscribe previous (if needed)
            if (_propertiesBoundItem != null)
            {
                _propertiesBoundItem.PropertyChanged -= PropertiesBoundItem_PropertyChanged;
            }

            _propertiesBoundItem = item;
            if (_propertiesBoundItem != null)
            {
                _propertiesBoundItem.PropertyChanged += PropertiesBoundItem_PropertyChanged;
            }

            if (PropertiesPanel.Visibility == Visibility.Visible)
                UpdatePropertiesPanel();
        }

        private void PropertiesBoundItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Keep panel in sync while dragging/resizing
            if (PropertiesPanel.Visibility == Visibility.Visible)
            {
                Dispatcher.BeginInvoke(new Action(UpdatePropertiesPanel), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        #endregion

        #region Responsive Layout

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            AdaptToScreenSize();
            // Auto-fit canvas to viewport when first loaded to prevent scrolling
            Dispatcher.BeginInvoke(new Action(() => AutoFitCanvasToViewport()),
                System.Windows.Threading.DispatcherPriority.Loaded);

            // Try to auto-select the current event after UI is fully loaded
            // Use a slight delay to ensure EventSelectionService has fully initialized
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TryAutoSelectCurrentEvent();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void TryAutoSelectCurrentEvent()
        {
            try
            {
                var currentEvent = EventSelectionService.Instance.SelectedEvent;
                Log.Debug($"TouchTemplateDesigner.TryAutoSelectCurrentEvent: Current event is '{currentEvent?.Name ?? "None"}' (ID: {currentEvent?.Id ?? 0})");

                if (currentEvent != null && currentEvent.Id > 0 && _selectedEvent?.Id != currentEvent.Id)
                {
                    // Only update if not already selected
                    for (int i = 0; i < EventComboBox.Items.Count; i++)
                    {
                        if (EventComboBox.Items[i] is EventData evt && evt.Id == currentEvent.Id)
                        {
                            EventComboBox.SelectedIndex = i;
                            _selectedEvent = evt;
                            Log.Debug($"TouchTemplateDesigner: Successfully auto-selected event '{currentEvent.Name}' in OnLoaded");

                            // Try to load the default template for this event
                            LoadDefaultTemplateForEvent(evt);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner.TryAutoSelectCurrentEvent: Failed - {ex.Message}");
            }
        }

        private void LoadDefaultTemplateForEvent(EventData eventData)
        {
            try
            {
                if (eventData == null || eventData.Id <= 0)
                    return;

                // Get the default template for this event
                var defaultTemplate = _eventService.GetDefaultEventTemplate(eventData.Id);
                if (defaultTemplate != null)
                {
                    Log.Debug($"TouchTemplateDesigner: Loading default template '{defaultTemplate.Name}' for event '{eventData.Name}'");

                    // Check if we already have content in the designer
                    var canvas = DesignerCanvas as SimpleDesignerCanvas;
                    if (canvas != null && canvas.Items.Count == 0 && _currentTemplateId <= 0)
                    {
                        // Only load if canvas is empty and no template is currently loaded
                        LoadTemplate(defaultTemplate.Id);
                        ShowAutoSaveNotification($"Loaded default template '{defaultTemplate.Name}' for {eventData.Name}");
                    }
                    else
                    {
                        Log.Debug($"TouchTemplateDesigner: Canvas not empty or template already loaded, skipping default template load");
                    }
                }
                else
                {
                    Log.Debug($"TouchTemplateDesigner: No default template found for event '{eventData.Name}'");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner.LoadDefaultTemplateForEvent: Failed - {ex.Message}");
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            AdaptToScreenSize();

            // Auto-fit canvas when the container size changes
            if (_isInitialized && CanvasScrollViewer != null)
            {
                // Delay the auto-fit to allow layout to complete
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    AutoFitCanvasToViewport();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void AdaptToScreenSize()
        {
            double width = ActualWidth;
            double height = ActualHeight;
            
            // Determine orientation
            bool isPortrait = height > width;
            bool isSmallScreen = width < 1024 || height < 768;
            bool isMediumScreen = width < 1440 || height < 900;
            
            // Adjust button sizes based on screen size
            double buttonSize = isSmallScreen ? 44 : (isMediumScreen ? 50 : 60);
            UpdateButtonSizes(buttonSize);
            
            // Adjust layout for portrait mode
            if (isPortrait)
            {
                ConfigurePortraitLayout();
            }
            else
            {
                ConfigureLandscapeLayout();
            }
            
            // Keep canvas container margins minimal to prevent white borders
            CanvasContainerBorder.Margin = new Thickness(0);
            
            // Adjust properties panel width
            double propertiesWidth = Math.Min(width * 0.3, 500);
            propertiesWidth = Math.Max(propertiesWidth, 280);
            PropertiesPanel.Width = propertiesWidth;
            
            // Adjust toolbar heights
            TopToolbarRow.Height = new GridLength(isSmallScreen ? 60 : 80);
            BottomToolbarRow.Height = new GridLength(isSmallScreen ? 80 : 100);
            
            // Adjust side tools panel
            if (SideToolsContainer != null)
            {
                SideToolsContainer.Width = isSmallScreen ? 410 : (isMediumScreen ? 420 : 430);
            }
            
            // Update zoom controls visibility
            if (isSmallScreen && isPortrait)
            {
                ZoomControlsPanel.Margin = new Thickness(5);
            }
            else
            {
                ZoomControlsPanel.Margin = new Thickness(10);
            }
        }
        
        private void UpdateButtonSizes(double size)
        {
            var buttonStyle = Resources["TouchToolButton"] as Style;
            if (buttonStyle != null)
            {
                foreach (var button in FindVisualChildren<Button>(this))
                {
                    if (button.Style == buttonStyle)
                    {
                        button.MinWidth = size;
                        button.MinHeight = size;
                    }
                }
            }
        }
        
        private void ConfigurePortraitLayout()
        {
            if (SideToolsContainer != null)
            {
                Grid.SetRow(SideToolsContainer, 2);
                SideToolsContainer.HorizontalAlignment = HorizontalAlignment.Center;
                SideToolsContainer.VerticalAlignment = VerticalAlignment.Center;
                SideToolsContainer.Width = double.NaN;
                SideToolsContainer.MaxWidth = ActualWidth * 0.8;
            }
            
            CanvasScrollViewer.Margin = new Thickness(0);
        }
        
        private void ConfigureLandscapeLayout()
        {
            if (SideToolsContainer != null)
            {
                Grid.SetRow(SideToolsContainer, 1);
                SideToolsContainer.HorizontalAlignment = HorizontalAlignment.Left;
                SideToolsContainer.VerticalAlignment = VerticalAlignment.Center;
                SideToolsContainer.Width = 410;
                SideToolsContainer.MaxWidth = 450;
            }
            
            CanvasScrollViewer.Margin = new Thickness(90, 0, 0, 0);
        }
        
        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T)
                    {
                        yield return (T)child;
                    }

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }

        #endregion

        #region Other Operations

        private void RenameTemplate_Click(object sender, RoutedEventArgs e)
        {
            // Show the rename overlay
            RenameTemplateTextBox.Text = TemplateNameText.Text;
            RenameTemplateOverlay.Visibility = Visibility.Visible;

            // Focus the textbox and select all text for easy replacement
            RenameTemplateTextBox.Focus();
            RenameTemplateTextBox.SelectAll();
        }

        private void ApplyRenameTemplate_Click(object sender, RoutedEventArgs e)
        {
            var currentName = TemplateNameText.Text;
            var newName = RenameTemplateTextBox.Text?.Trim();

            if (!string.IsNullOrWhiteSpace(newName) && newName != currentName)
            {
                TemplateNameText.Text = newName;
                if (_designerVM != null)
                {
                    // Update template name
                    if (_designerVM.CurrentTemplate != null)
                    {
                        _designerVM.CurrentTemplate.Name = newName;
                    }

                    // If template is already saved, update it in database
                    if (_currentTemplateId > 0)
                    {
                        _designerVM.SaveToDbCmd.Execute(null);
                        ShowAutoSaveNotification($"Template renamed to '{newName}'");
                        Log.Debug($"TouchTemplateDesigner: Renamed template to '{newName}'");
                    }
                }
            }

            // Hide the overlay
            RenameTemplateOverlay.Visibility = Visibility.Collapsed;
        }

        private void CancelRenameTemplate_Click(object sender, RoutedEventArgs e)
        {
            // Hide the overlay without saving
            RenameTemplateOverlay.Visibility = Visibility.Collapsed;
        }

        private void RenameTemplateTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyRenameTemplate_Click(sender, e);
            }
            else if (e.Key == Key.Escape)
            {
                CancelRenameTemplate_Click(sender, e);
            }
        }

        private void RenameTemplateOverlay_BackgroundClick(object sender, MouseButtonEventArgs e)
        {
            // Only close if clicking on the background, not the content
            if (e.OriginalSource == sender)
            {
                RenameTemplateOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ChangeCanvasSize_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show the touch-friendly paper size popup
                PaperSizePopup.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to show paper size popup: {ex.Message}");
            }
        }

        private void SetCanvasSize(double width, double height)
        {
            try
            {
                Log.Debug($"TouchTemplateDesigner: SetCanvasSize called with {width}x{height}");
                Log.Debug($"TouchTemplateDesigner: DesignerCanvas type: {DesignerCanvas?.GetType().Name}");
                Log.Debug($"TouchTemplateDesigner: DesignerCanvas current size: {DesignerCanvas?.Width}x{DesignerCanvas?.Height}");

                // Store the desired canvas size to persist through template loads
                _desiredCanvasWidth = width;
                _desiredCanvasHeight = height;

                DesignerCanvas.Width = width;
                DesignerCanvas.Height = height;

                Log.Debug($"TouchTemplateDesigner: After setting, DesignerCanvas size: {DesignerCanvas.Width}x{DesignerCanvas.Height}");

                // Keep the dark gray background (#FF3A3A3C) as set in XAML - don't reset to white
                // The dark background provides better contrast and matches the overall design theme
                Log.Debug($"TouchTemplateDesigner: Canvas background maintained as dark gray (#FF3A3A3C)");

                // Auto-fit the canvas to eliminate scrolling
                if (CanvasScrollViewer != null)
                {
                    CanvasScrollViewer.UpdateLayout();
                    // Delay auto-fit to allow layout updates to complete
                    Dispatcher.BeginInvoke(new Action(() => AutoFitCanvasToViewport()),
                        System.Windows.Threading.DispatcherPriority.Background);
                }

                UpdateCanvasSizeDisplay();

                // Update orientation indicator after canvas size changes
                UpdateOrientationFromCanvas();

                Log.Debug($"TouchTemplateDesigner: Set canvas size to {width}x{height} (will persist through template loads)");
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to set canvas size: {ex.Message}");
                MessageBox.Show($"Failed to set canvas size: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Clear the desired canvas size so templates will use their original dimensions
        /// </summary>
        private void ClearDesiredCanvasSize()
        {
            _desiredCanvasWidth = null;
            _desiredCanvasHeight = null;
            Log.Debug("TouchTemplateDesigner: Cleared desired canvas size - templates will use original dimensions");
        }

        /// <summary>
        /// Auto-fit the canvas size to the available viewport to eliminate scrolling while preserving aspect ratio.
        /// This method is called automatically when:
        /// - The control is first loaded
        /// - The window is resized
        /// - The canvas size changes (e.g., when changing paper size or loading templates)
        /// - The "Fit to Window" zoom button is clicked
        /// </summary>
        private void AutoFitCanvasToViewport()
        {
            try
            {
                if (CanvasScrollViewer == null || !_isInitialized) return;

                // Wait for layout to complete if needed
                if (CanvasScrollViewer.ActualWidth == 0 || CanvasScrollViewer.ActualHeight == 0)
                {
                    Dispatcher.BeginInvoke(new Action(() => AutoFitCanvasToViewport()),
                        System.Windows.Threading.DispatcherPriority.Loaded);
                    return;
                }

                // Get the available viewport size with some padding for breathing room
                var availableWidth = CanvasScrollViewer.ActualWidth - 20; // 10px padding on each side
                var availableHeight = CanvasScrollViewer.ActualHeight - 20; // 10px padding on top/bottom

                if (availableWidth <= 0 || availableHeight <= 0) return;

                // Get current canvas dimensions
                var currentWidth = DesignerCanvas.Width;
                var currentHeight = DesignerCanvas.Height;

                if (currentWidth <= 0 || currentHeight <= 0) return;

                // Calculate scale to fit within viewport while maintaining aspect ratio
                var scaleByWidth = availableWidth / currentWidth;
                var scaleByHeight = availableHeight / currentHeight;

                // Use the smaller scale to ensure canvas fits entirely within viewport
                var scale = Math.Min(scaleByWidth, scaleByHeight);

                // Apply reasonable zoom limits (don't scale too small or too large)
                scale = Math.Max(0.1, Math.Min(2.0, scale));

                // Update the scale transform
                if (CanvasScaleTransform != null)
                {
                    CanvasScaleTransform.ScaleX = scale;
                    CanvasScaleTransform.ScaleY = scale;
                    _currentZoom = scale;

                    // Update zoom level display
                    if (ZoomLevelText != null)
                    {
                        ZoomLevelText.Text = $"{(_currentZoom * 100):F0}%";
                    }
                }

                // Center the canvas after scaling
                Dispatcher.BeginInvoke(new Action(() => CenterCanvas()),
                    System.Windows.Threading.DispatcherPriority.Background);

                Log.Debug($"TouchTemplateDesigner: Auto-fitted canvas with scale {scale:F2} (viewport: {availableWidth:F0}x{availableHeight:F0}, canvas: {currentWidth:F0}x{currentHeight:F0})");
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to auto-fit canvas: {ex.Message}");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // First check if we're inside a TouchTemplateDesignerOverlay
                var overlay = this.GetVisualParent<TouchTemplateDesignerOverlay>();
                if (overlay != null)
                {
                    Log.Debug("TouchTemplateDesigner: Closing via overlay");

                    // Call the overlay's Close method
                    overlay.Close();
                }
                else
                {
                    Log.Debug("TouchTemplateDesigner: Closing standalone control");

                    // If not in overlay, just remove this control
                    if (Parent is Panel parentPanel)
                    {
                        parentPanel.Children.Remove(this);
                    }
                    else if (Parent is ContentControl parentControl)
                    {
                        parentControl.Content = null;
                    }

                    // Also collapse visibility
                    this.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Error closing designer: {ex.Message}");
            }
        }

        private T GetVisualParent<T>() where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(this);
            while (parent != null)
            {
                if (parent is T typedParent)
                    return typedParent;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        #endregion

        #region Helper Methods

        private string ShowInputDialog(string title, string prompt, string defaultValue = "")
        {
            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                WindowStyle = WindowStyle.ToolWindow
            };

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = prompt,
                Foreground = Brushes.White,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            var textBox = new TextBox
            {
                Text = defaultValue,
                FontSize = 14,
                Padding = new Thickness(5),
                Margin = new Thickness(0, 0, 0, 15),
                Height = 35
            };
            Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Height = 35,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            okButton.Click += (s, e) => { dialog.DialogResult = true; };
            buttonPanel.Children.Add(okButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 35,
                IsCancel = true
            };
            cancelButton.Click += (s, e) => { dialog.DialogResult = false; };
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;
            textBox.Focus();
            textBox.SelectAll();

            if (dialog.ShowDialog() == true)
            {
                return textBox.Text;
            }

            return null;
        }

        #endregion

        #region Color Picker and Eyedropper

        private void ColorPicker_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show the color picker dialog with canvas constraint
                var parentWindow = Window.GetWindow(this);
                var selectedColor = SimpleColorPickerDialog.ShowDialog(
                    parentWindow,
                    "Select Color",
                    _currentSelectedColor,
                    DesignerCanvas);  // Pass the canvas element to constrain eyedropper

                if (selectedColor.HasValue)
                {
                    _currentSelectedColor = selectedColor.Value;
                    UpdateColorPreview();
                    ApplySelectedColor();
                    Log.Debug($"TouchTemplateDesigner: Color picker selected color: {_currentSelectedColor}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to show color picker: {ex.Message}");
                MessageBox.Show($"Failed to open color picker: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Note: Eyedropper functionality has been moved to PixiEditorColorPickerDialog
        // The eyedropper button is now integrated within the color picker dialog itself
        // This follows standard UI patterns where the eyedropper is part of the color selection tools

        private void ApplySelectedColor()
        {
            try
            {
                var canvas = DesignerCanvas as SimpleDesignerCanvas;
                if (canvas == null) return;

                var selectedItem = canvas.SelectedItem;
                if (selectedItem == null)
                {
                    // If no item is selected, apply color to new items
                    Log.Debug("TouchTemplateDesigner: No item selected, color will be applied to new items");
                    return;
                }

                // Apply color based on item type
                if (selectedItem is SimpleTextItem textItem)
                {
                    var colorBrush = new SolidColorBrush(_currentSelectedColor);
                    textItem.TextColor = colorBrush;
                    Log.Debug($"TouchTemplateDesigner: Applied color {_currentSelectedColor} to text item");
                }

                // TODO: Add shape color application when SimpleShapeItem is implemented
                // Example for future implementation:
                // if (selectedItem is SimpleShapeItem shapeItem)
                // {
                //     var colorBrush = new SolidColorBrush(_currentSelectedColor);
                //     shapeItem.Fill = colorBrush;
                //     Log.Debug($"TouchTemplateDesigner: Applied color {_currentSelectedColor} to shape item");
                // }

                // TODO: Add background color application for image items if supported
                // Example for future implementation:
                // if (selectedItem is SimpleImageItem imageItem && imageItem.SupportsBackgroundColor)
                // {
                //     var colorBrush = new SolidColorBrush(_currentSelectedColor);
                //     imageItem.BackgroundColor = colorBrush;
                //     Log.Debug($"TouchTemplateDesigner: Applied background color {_currentSelectedColor} to image item");
                // }

                Log.Debug($"TouchTemplateDesigner: Applied color {_currentSelectedColor} to selected item");
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to apply selected color: {ex.Message}");
            }
        }

        /* Commented out - ExtractColorFromElement was part of the old eyedropper implementation
        private Color ExtractColorFromElement(SimpleCanvasItem item, Point clickPoint)
        {
            try
            {
                // Extract from text items
                if (item is SimpleTextItem textItem)
                {
                    // First try the TextColor property
                    if (textItem.TextColor is SolidColorBrush textBrush)
                    {
                        return textBrush.Color;
                    }

                    // Fallback: Find the actual TextBlock control
                    var textBlock = FindVisualChild<TextBlock>(textItem);
                    if (textBlock != null && textBlock.Foreground is SolidColorBrush foregroundBrush)
                    {
                        return foregroundBrush.Color;
                    }
                }

                // Extract from image items (background color)
                if (item is SimpleImageItem imageItem)
                {
                    // Try to get the border background
                    var border = FindVisualChild<Border>(imageItem);
                    if (border != null && border.Background is SolidColorBrush borderBrush)
                    {
                        return borderBrush.Color;
                    }
                }

                // TODO: Add shape color extraction when SimpleShapeItem is implemented
                // Example for future implementation:
                // if (item is SimpleShapeItem shapeItem && shapeItem.Fill is SolidColorBrush shapeBrush)
                // {
                //     return shapeBrush.Color;
                // }

                // TODO: Add image color extraction (sample color from image at click point)
                // Example for future implementation:
                // if (item is SimpleImageItem imageItem && imageItem.ImageSource is BitmapSource bitmap)
                // {
                //     return SampleColorFromImage(bitmap, clickPoint, item);
                // }

                // Default to black if no color can be extracted
                return Colors.Black;
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to extract color from element: {ex.Message}");
                return Colors.Black;
            }
        }
        */

        private void UpdateColorPreview()
        {
            // Toolbar color preview was removed; keep method to avoid ref churn
            try
            {
                Log.Debug($"TouchTemplateDesigner: Current color set to {_currentSelectedColor}");
            }
            catch { }
        }

        #endregion

        #region Photo Number Edit Popup

        private SimpleImageItem _currentEditingPlaceholder;

        private void OnPlaceholderNumberEditRequested(object sender, SimpleImageItem placeholder)
        {
            _currentEditingPlaceholder = placeholder;
            CurrentPhotoNumberText.Text = placeholder.PlaceholderNumber.ToString();
            NewPhotoNumberText.Text = "";
            PhotoNumberEditPopup.Visibility = Visibility.Visible;
        }

        private void NumberPad_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string digit)
            {
                string currentText = NewPhotoNumberText.Text ?? "";
                if (currentText.Length < 3) // Limit to 3 digits (max 999)
                {
                    NewPhotoNumberText.Text = currentText + digit;
                }
            }
        }

        private void NumberPadClear_Click(object sender, RoutedEventArgs e)
        {
            NewPhotoNumberText.Text = "";
        }

        private void NumberPadBackspace_Click(object sender, RoutedEventArgs e)
        {
            string currentText = NewPhotoNumberText.Text ?? "";
            if (currentText.Length > 0)
            {
                NewPhotoNumberText.Text = currentText.Substring(0, currentText.Length - 1);
            }
        }

        private void ApplyPhotoNumber_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEditingPlaceholder != null && !string.IsNullOrWhiteSpace(NewPhotoNumberText.Text))
            {
                if (int.TryParse(NewPhotoNumberText.Text, out int newNumber) && newNumber > 0)
                {
                    _currentEditingPlaceholder.PlaceholderNumber = newNumber;

                    // Update the placeholder background color based on the new number
                    var colorIndex = (newNumber - 1) % 18; // We have 18 colors in the palette
                    var colors = new Color[]
                    {
                        Color.FromRgb(255, 182, 193), // Light Pink
                        Color.FromRgb(173, 216, 230), // Light Blue
                        Color.FromRgb(152, 251, 152), // Pale Green
                        Color.FromRgb(255, 218, 185), // Peach
                        Color.FromRgb(221, 160, 221), // Plum
                        Color.FromRgb(255, 255, 224), // Light Yellow
                        Color.FromRgb(176, 224, 230), // Powder Blue
                        Color.FromRgb(255, 228, 225), // Misty Rose
                        Color.FromRgb(240, 230, 140), // Khaki
                        Color.FromRgb(255, 192, 203), // Pink
                        Color.FromRgb(135, 206, 235), // Sky Blue
                        Color.FromRgb(144, 238, 144), // Light Green
                        Color.FromRgb(255, 160, 122), // Light Salmon
                        Color.FromRgb(216, 191, 216), // Thistle
                        Color.FromRgb(255, 239, 213), // Papaya Whip
                        Color.FromRgb(175, 238, 238), // Pale Turquoise
                        Color.FromRgb(255, 245, 238), // Seashell
                        Color.FromRgb(250, 250, 210), // Light Goldenrod Yellow
                    };

                    _currentEditingPlaceholder.PlaceholderBackground = new SolidColorBrush(colors[colorIndex]);
                }
            }
            ClosePhotoNumberEditPopup_Click(sender, e);
        }

        private void ClosePhotoNumberEditPopup_Click(object sender, RoutedEventArgs e)
        {
            PhotoNumberEditPopup.Visibility = Visibility.Collapsed;
            _currentEditingPlaceholder = null;
        }

        private void PhotoNumberEditPopup_BackgroundClick(object sender, MouseButtonEventArgs e)
        {
            // Check if click was on the background, not the popup content
            if (e.OriginalSource == sender)
            {
                ClosePhotoNumberEditPopup_Click(sender, e);
            }
        }

        private void PhotoNumberEditPopup_BackgroundTouch(object sender, TouchEventArgs e)
        {
            // Check if touch was on the background, not the popup content
            if (e.OriginalSource == sender)
            {
                ClosePhotoNumberEditPopup_Click(sender, e);
            }
        }

        #endregion

        #region Thumbnail Generation

        private string GenerateTemplateThumbnail(SimpleDesignerCanvas canvas, string templateName)
        {
            try
            {
                // Create thumbnails directory
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string thumbnailsPath = System.IO.Path.Combine(appDataPath, "Photobooth", "Thumbnails");
                if (!Directory.Exists(thumbnailsPath))
                {
                    Directory.CreateDirectory(thumbnailsPath);
                }

                // Generate unique filename
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safeName = string.Join("_", templateName.Split(System.IO.Path.GetInvalidFileNameChars()));
                string thumbnailFileName = $"thumb_{safeName}_{timestamp}.png";
                string thumbnailPath = System.IO.Path.Combine(thumbnailsPath, thumbnailFileName);

                // Calculate thumbnail size (maintain aspect ratio) - higher resolution for better quality
                int maxDimension = 600;  // Double the size for retina-quality thumbnails
                double aspectRatio = canvas.Width / canvas.Height;
                int thumbnailWidth, thumbnailHeight;

                if (aspectRatio > 1)
                {
                    // Landscape
                    thumbnailWidth = maxDimension;
                    thumbnailHeight = (int)(maxDimension / aspectRatio);
                }
                else
                {
                    // Portrait or square
                    thumbnailHeight = maxDimension;
                    thumbnailWidth = (int)(maxDimension * aspectRatio);
                }

                // Create a RenderTargetBitmap at higher DPI for better quality
                var renderTarget = new RenderTargetBitmap(thumbnailWidth, thumbnailHeight, 144, 144, PixelFormats.Pbgra32);

                // Create a visual to render
                var drawingVisual = new DrawingVisual();
                using (var context = drawingVisual.RenderOpen())
                {
                    // Draw white background
                    context.DrawRectangle(Brushes.White, null, new Rect(0, 0, thumbnailWidth, thumbnailHeight));

                    // Calculate scale
                    double scaleX = thumbnailWidth / canvas.Width;
                    double scaleY = thumbnailHeight / canvas.Height;
                    double scale = Math.Min(scaleX, scaleY);

                    // Apply transform for scaling
                    context.PushTransform(new ScaleTransform(scale, scale));

                    // Draw canvas background if it exists
                    if (canvas.Background != null)
                    {
                        context.DrawRectangle(canvas.Background, null, new Rect(0, 0, canvas.Width, canvas.Height));
                    }

                    // Draw each canvas item
                    foreach (var item in canvas.Items.OrderBy(i => i.ZIndex))
                    {
                        // Create a rect for the item
                        var itemRect = new Rect(item.Left, item.Top, item.Width, item.Height);

                        if (item is SimpleImageItem imageItem)
                        {
                            if (imageItem.IsPlaceholder)
                            {
                                // Draw placeholder with its background color
                                var placeholderBrush = imageItem.PlaceholderBackground ?? new SolidColorBrush(Color.FromRgb(255, 182, 193));
                                context.DrawRectangle(placeholderBrush, new Pen(Brushes.DarkGray, 1), itemRect);

                                // Draw placeholder text
                                var formattedText = new FormattedText(
                                    $"Photo {imageItem.PlaceholderNumber}",
                                    System.Globalization.CultureInfo.CurrentCulture,
                                    FlowDirection.LeftToRight,
                                    new Typeface("Arial"),
                                    14, // Use fixed font size, scaling is handled by transform
                                    Brushes.Black,
                                    96);

                                formattedText.TextAlignment = TextAlignment.Center;
                                formattedText.MaxTextWidth = item.Width;

                                // Center text in placeholder
                                var textX = item.Left + (item.Width - formattedText.Width) / 2;
                                var textY = item.Top + (item.Height - formattedText.Height) / 2;
                                context.DrawText(formattedText, new Point(textX, textY));
                            }
                            else if (!string.IsNullOrEmpty(imageItem.ImagePath))
                            {
                                // Draw actual image
                                try
                                {
                                    var bitmap = new BitmapImage(new Uri(imageItem.ImagePath));
                                    context.DrawImage(bitmap, itemRect);
                                }
                                catch { /* Skip if image can't be loaded */ }
                            }
                        }
                        else if (item is SimpleTextItem textItem)
                        {
                            // Draw text with proper scaling and positioning
                            var formattedText = new FormattedText(
                                textItem.Text ?? "Text",
                                System.Globalization.CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                new Typeface(textItem.FontFamily, textItem.FontStyle, textItem.FontWeight, FontStretches.Normal),
                                textItem.FontSize, // Use original font size, scaling is handled by transform
                                textItem.Foreground ?? Brushes.Black,
                                96);

                            // Set max width to prevent text overflow
                            formattedText.MaxTextWidth = item.Width;
                            formattedText.MaxTextHeight = item.Height;

                            // Center text within its bounds
                            var textX = item.Left;
                            var textY = item.Top;

                            // For vertical centering
                            if (formattedText.Height < item.Height)
                            {
                                textY = item.Top + (item.Height - formattedText.Height) / 2;
                            }

                            context.DrawText(formattedText, new Point(textX, textY));
                        }
                        else if (item is SimpleShapeItem shapeItem)
                        {
                            // Draw shape
                            var pen = new Pen(shapeItem.Stroke ?? Brushes.Black, shapeItem.StrokeThickness);

                            switch (shapeItem.ShapeType)
                            {
                                case SimpleShapeType.Rectangle:
                                    context.DrawRectangle(shapeItem.Fill, pen, itemRect);
                                    break;
                                case SimpleShapeType.Ellipse:
                                    context.DrawEllipse(shapeItem.Fill, pen,
                                        new Point(item.Left + item.Width / 2, item.Top + item.Height / 2),
                                        item.Width / 2, item.Height / 2);
                                    break;
                                case SimpleShapeType.Line:
                                    context.DrawLine(pen, new Point(item.Left, item.Top),
                                        new Point(item.Left + item.Width, item.Top + item.Height));
                                    break;
                            }
                        }
                    }

                    context.Pop(); // Remove transform
                }

                // Render the visual to the bitmap
                renderTarget.Render(drawingVisual);

                // Save to file
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderTarget));

                using (var fileStream = new FileStream(thumbnailPath, FileMode.Create))
                {
                    encoder.Save(fileStream);
                }

                return thumbnailPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to generate thumbnail: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Generates a full-resolution preview PNG of the template
        /// This is used for fast preview rendering without recreating the entire canvas
        /// </summary>
        private string GenerateTemplatePreview(SimpleDesignerCanvas canvas, string templateName)
        {
            try
            {
                // Create previews directory
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string previewsPath = System.IO.Path.Combine(appDataPath, "Photobooth", "TemplatePreviews");
                if (!Directory.Exists(previewsPath))
                {
                    Directory.CreateDirectory(previewsPath);
                }

                // Generate unique filename
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safeName = string.Join("_", templateName.Split(System.IO.Path.GetInvalidFileNameChars()));
                string previewFileName = $"preview_{safeName}_{timestamp}.png";
                string previewPath = System.IO.Path.Combine(previewsPath, previewFileName);

                // Create high-resolution render (up to 4K resolution)
                int maxWidth = 3840;  // 4K width
                int maxHeight = 2160; // 4K height
                double canvasWidth = canvas.Width;
                double canvasHeight = canvas.Height;

                // Calculate scale - we want to upscale small templates too for better quality
                double scale = 1.0;

                // If canvas is smaller than max, upscale it (up to 2x for quality)
                if (canvasWidth < maxWidth && canvasHeight < maxHeight)
                {
                    double scaleX = Math.Min(maxWidth / canvasWidth, 2.0);
                    double scaleY = Math.Min(maxHeight / canvasHeight, 2.0);
                    scale = Math.Min(scaleX, scaleY);
                }
                // If canvas is larger than max, downscale it
                else if (canvasWidth > maxWidth || canvasHeight > maxHeight)
                {
                    double scaleX = maxWidth / canvasWidth;
                    double scaleY = maxHeight / canvasHeight;
                    scale = Math.Min(scaleX, scaleY);
                }

                int renderWidth = (int)(canvasWidth * scale);
                int renderHeight = (int)(canvasHeight * scale);

                // Create a RenderTargetBitmap at higher DPI (150) for better quality
                var renderTarget = new RenderTargetBitmap(renderWidth, renderHeight, 150, 150, PixelFormats.Pbgra32);

                // Create a visual to render
                var drawingVisual = new DrawingVisual();
                using (var context = drawingVisual.RenderOpen())
                {
                    // Apply scale if needed
                    if (scale != 1.0)
                    {
                        context.PushTransform(new ScaleTransform(scale, scale));
                    }

                    // Draw canvas background
                    if (canvas.Background != null)
                    {
                        context.DrawRectangle(canvas.Background, null, new Rect(0, 0, canvasWidth, canvasHeight));
                    }
                    else
                    {
                        context.DrawRectangle(Brushes.White, null, new Rect(0, 0, canvasWidth, canvasHeight));
                    }

                    // Draw each canvas item
                    foreach (var item in canvas.Items.OrderBy(i => i.ZIndex))
                    {
                        DrawCanvasItemToContext(context, item);
                    }

                    if (scale != 1.0)
                    {
                        context.Pop();
                    }
                }

                // Render the visual
                renderTarget.Render(drawingVisual);

                // Save to PNG file with optimized settings
                var encoder = new PngBitmapEncoder();
                encoder.Interlace = PngInterlaceOption.Off; // Better quality, slightly larger file
                encoder.Frames.Add(BitmapFrame.Create(renderTarget));

                using (var fileStream = new FileStream(previewPath, FileMode.Create))
                {
                    encoder.Save(fileStream);
                }

                Log.Debug($"TouchTemplateDesigner: Generated preview image at {previewPath}");
                return previewPath;
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to generate preview: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Helper method to draw a canvas item to a drawing context
        /// </summary>
        private void DrawCanvasItemToContext(DrawingContext context, SimpleCanvasItem item)
        {
            // Save current transform
            context.PushTransform(new TranslateTransform(item.Left, item.Top));

            // Apply rotation if any
            if (item.RotationAngle != 0)
            {
                context.PushTransform(new RotateTransform(item.RotationAngle, item.Width / 2, item.Height / 2));
            }

            // Apply opacity
            if (item.Opacity < 1.0)
            {
                context.PushOpacity(item.Opacity);
            }

            var itemRect = new Rect(0, 0, item.Width, item.Height);

            if (item is SimpleImageItem imageItem)
            {
                if (imageItem.IsPlaceholder)
                {
                    // Draw placeholder
                    var placeholderBrush = imageItem.PlaceholderBackground ??
                        new SolidColorBrush(Color.FromRgb(40, 40, 40));
                    var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(76, 175, 80)), 2);
                    context.DrawRectangle(placeholderBrush, borderPen, itemRect);

                    // Draw placeholder text
                    var formattedText = new FormattedText(
                        $"PHOTO {imageItem.PlaceholderNumber}",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Arial"),
                        16,
                        new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                        96);

                    formattedText.TextAlignment = TextAlignment.Center;
                    formattedText.MaxTextWidth = item.Width;

                    var textY = (item.Height - formattedText.Height) / 2;
                    context.DrawText(formattedText, new Point(item.Width / 2, textY));
                }
                else if (!string.IsNullOrEmpty(imageItem.ImagePath) && File.Exists(imageItem.ImagePath))
                {
                    // Draw actual image
                    try
                    {
                        var bitmap = new BitmapImage(new Uri(imageItem.ImagePath, UriKind.Absolute));
                        context.DrawImage(bitmap, itemRect);
                    }
                    catch
                    {
                        // If image fails to load, draw placeholder
                        context.DrawRectangle(Brushes.Gray, null, itemRect);
                    }
                }

                // Draw stroke if present
                if (imageItem.StrokeBrush != null && imageItem.StrokeThickness > 0)
                {
                    var strokePen = new Pen(imageItem.StrokeBrush, imageItem.StrokeThickness);
                    context.DrawRectangle(null, strokePen, itemRect);
                }
            }
            else if (item is SimpleTextItem textItem && textItem.Text != null)
            {
                // Create formatted text from text item properties
                var formattedText = new FormattedText(
                    textItem.Text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(textItem.FontFamily ?? new FontFamily("Arial"),
                                textItem.FontStyle,
                                textItem.FontWeight,
                                textItem.FontStretch),
                    textItem.FontSize,
                    textItem.Foreground ?? Brushes.Black,
                    96);

                formattedText.TextAlignment = textItem.TextAlignment;
                formattedText.MaxTextWidth = item.Width;
                formattedText.MaxTextHeight = item.Height;

                context.DrawText(formattedText, new Point(0, 0));
            }
            else if (item is SimpleShapeItem shapeItem)
            {
                // Draw shape
                if (shapeItem.ShapeType == SimpleShapeType.Rectangle)
                {
                    context.DrawRectangle(shapeItem.Fill,
                        shapeItem.StrokeThickness > 0 ? new Pen(shapeItem.Stroke, shapeItem.StrokeThickness) : null,
                        itemRect);
                }
                else if (shapeItem.ShapeType == SimpleShapeType.Ellipse)
                {
                    var center = new Point(item.Width / 2, item.Height / 2);
                    context.DrawEllipse(shapeItem.Fill,
                        shapeItem.StrokeThickness > 0 ? new Pen(shapeItem.Stroke, shapeItem.StrokeThickness) : null,
                        center, item.Width / 2, item.Height / 2);
                }
            }

            // Pop transforms in reverse order
            if (item.Opacity < 1.0)
            {
                context.Pop();
            }
            if (item.RotationAngle != 0)
            {
                context.Pop();
            }
            context.Pop();
        }

        #endregion

        #region Event Management

        /// <summary>
        /// Public method to refresh the event selection with the current event from EventSelectionService
        /// </summary>
        public void RefreshEventSelection()
        {
            try
            {
                Log.Debug("TouchTemplateDesigner.RefreshEventSelection: Called");
                TryAutoSelectCurrentEvent();
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner.RefreshEventSelection: Failed - {ex.Message}");
            }
        }

        private void LoadEvents(bool tryAutoSelect = true)
        {
            try
            {
                var events = _eventService.GetAllEvents();
                EventComboBox.Items.Clear();

                // Add "No Event" option
                EventComboBox.Items.Add(new EventData { Id = -1, Name = "(No Event)" });

                foreach (var evt in events)
                {
                    EventComboBox.Items.Add(evt);
                }

                if (tryAutoSelect)
                {
                    // Try to auto-select the current event from EventSelectionService
                    var currentEvent = EventSelectionService.Instance.SelectedEvent;
                    Log.Debug($"TouchTemplateDesigner: Checking for current event - Found: {currentEvent?.Name ?? "None"} (ID: {currentEvent?.Id ?? 0})");

                    if (currentEvent != null && currentEvent.Id > 0)
                    {
                        // Find the matching event in the combo box
                        bool found = false;
                        for (int i = 0; i < EventComboBox.Items.Count; i++)
                        {
                            if (EventComboBox.Items[i] is EventData evt && evt.Id == currentEvent.Id)
                            {
                                EventComboBox.SelectedIndex = i;
                                _selectedEvent = evt;
                                Log.Debug($"TouchTemplateDesigner: Auto-selected current event '{currentEvent.Name}' at index {i}");
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            Log.Debug($"TouchTemplateDesigner: Current event '{currentEvent.Name}' not found in dropdown");
                            EventComboBox.SelectedIndex = 0;
                        }
                    }
                    else
                    {
                        // Select first item (No Event) if no current event
                        Log.Debug("TouchTemplateDesigner: No current event found, selecting 'No Event'");
                        EventComboBox.SelectedIndex = 0;
                    }
                }
                else
                {
                    // Don't auto-select, just default to first item
                    EventComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to load events: {ex.Message}");
                MessageBox.Show($"Failed to load events: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void EventComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var previousEvent = _selectedEvent;
                _selectedEvent = EventComboBox.SelectedItem as EventData;

                if (_selectedEvent != null && _selectedEvent.Id > 0)
                {
                    (AssignToEventCheckBox as System.Windows.Controls.Primitives.ToggleButton).IsEnabled = true;
                    (AssignToEventCheckBox as System.Windows.Controls.Primitives.ToggleButton).IsChecked = false;
                    LoadEventTemplates();

                    // If this is a different event and the canvas is empty, try to load the default template
                    if (previousEvent?.Id != _selectedEvent.Id)
                    {
                        var canvas = DesignerCanvas as SimpleDesignerCanvas;
                        if (canvas != null && canvas.Items.Count == 0 && _currentTemplateId <= 0)
                        {
                            LoadDefaultTemplateForEvent(_selectedEvent);
                        }
                    }
                }
                else
                {
                    (AssignToEventCheckBox as System.Windows.Controls.Primitives.ToggleButton).IsEnabled = false;
                    (AssignToEventCheckBox as System.Windows.Controls.Primitives.ToggleButton).IsChecked = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to handle event selection: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<TemplateData> _eventTemplates;

        private void LoadEventTemplates()
        {
            try
            {
                if (_selectedEvent != null && _selectedEvent.Id > 0)
                {
                    var templates = _eventService.GetEventTemplates(_selectedEvent.Id);

                    // Update the template count and visibility
                    UpdateEventTemplatesButton(templates);

                    // Store templates for later use
                    _eventTemplates = templates;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load event templates: {ex.Message}");
            }
        }

        private void UpdateEventTemplatesButton(List<TemplateData> templates)
        {
            if (templates != null && templates.Count > 0)
            {
                EventTemplatesButton.Visibility = Visibility.Visible;
                TemplateCountText.Text = templates.Count.ToString();

                // Update tooltip with template names
                var tooltipText = $"Event has {templates.Count} template(s):\n";
                foreach (var template in templates.Take(5))
                {
                    tooltipText += $"• {template.Name}";
                    if (template.IsDefault)
                        tooltipText += " (Default)";
                    tooltipText += "\n";
                }
                if (templates.Count > 5)
                    tooltipText += $"... and {templates.Count - 5} more";

                EventTemplatesButton.ToolTip = tooltipText;
            }
            else
            {
                EventTemplatesButton.Visibility = Visibility.Collapsed;
            }
        }

        private void AssignToEventCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // Skip if we're programmatically updating the checkbox
            if (_suppressCheckboxEvents)
                return;

            try
            {
                // Check if we have a saved template and selected event
                if (_currentTemplateId <= 0)
                {
                    MessageBox.Show("Please save the template first before assigning it to an event.",
                        "Template Not Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                    (AssignToEventCheckBox as System.Windows.Controls.Primitives.ToggleButton).IsChecked = false;
                    return;
                }

                if (_selectedEvent == null || _selectedEvent.Id <= 0)
                {
                    MessageBox.Show("Please select an event first.",
                        "No Event Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                    (AssignToEventCheckBox as System.Windows.Controls.Primitives.ToggleButton).IsChecked = false;
                    return;
                }

                // Auto-save the template association with the event
                if ((AssignToEventCheckBox as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked == true)
                {
                    // Assign template to event
                    _eventService.AssignTemplateToEvent(_selectedEvent.Id, _currentTemplateId, false);

                    // Show confirmation
                    var templateName = TemplateNameText.Text;
                    ShowAutoSaveNotification($"Template '{templateName}' assigned to event '{_selectedEvent.Name}'");
                }
                else
                {
                    // Unassign template from event
                    _eventService.RemoveTemplateFromEvent(_selectedEvent.Id, _currentTemplateId);

                    // Show confirmation
                    var templateName = TemplateNameText.Text;
                    ShowAutoSaveNotification($"Template '{templateName}' unassigned from event '{_selectedEvent.Name}'");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update template assignment: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                // Revert checkbox state on error
                var toggleBtn = AssignToEventCheckBox as System.Windows.Controls.Primitives.ToggleButton;
                toggleBtn.IsChecked = !toggleBtn.IsChecked;
            }
        }

        private void ShowAutoSaveNotification(string message)
        {
            // Create a temporary notification that disappears after 2 seconds
            var notification = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(230, 40, 167, 69)),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(15, 10, 15, 10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 50, 0, 0),
                Child = new TextBlock
                {
                    Text = message,
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.Medium
                }
            };

            // Add to the main grid
            if (MainGrid.Children.Count > 0)
            {
                Grid.SetRow(notification, 0);
                Grid.SetColumnSpan(notification, 3);
                Grid.SetZIndex(notification, 1000);
                MainGrid.Children.Add(notification);

                // Fade in animation
                notification.Opacity = 0;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                notification.BeginAnimation(OpacityProperty, fadeIn);

                // Remove after 2 seconds with fade out
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                    fadeOut.Completed += (s2, args2) => MainGrid.Children.Remove(notification);
                    notification.BeginAnimation(OpacityProperty, fadeOut);
                };
                timer.Start();
            }
        }

        private void CreateEvent_Click(object sender, RoutedEventArgs e)
        {
            // Show the create event overlay
            CreateEventNameTextBox.Text = "";
            CreateEventDescriptionTextBox.Text = "";
            CreateEventOverlay.Visibility = Visibility.Visible;

            // Focus the name textbox
            CreateEventNameTextBox.Focus();
        }

        private void ApplyCreateEvent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var eventName = CreateEventNameTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(eventName))
                {
                    ShowAutoSaveNotification("Please enter an event name");
                    return;
                }

                var description = CreateEventDescriptionTextBox.Text?.Trim();

                var eventId = _eventService.CreateEvent(eventName, description);
                if (eventId > 0)
                {
                    LoadEvents(false);  // Don't auto-select, we'll select the new event

                    // Select the newly created event
                    for (int i = 0; i < EventComboBox.Items.Count; i++)
                    {
                        if (EventComboBox.Items[i] is EventData evt && evt.Id == eventId)
                        {
                            EventComboBox.SelectedIndex = i;
                            break;
                        }
                    }

                    ShowAutoSaveNotification($"Event '{eventName}' created successfully!");
                }

                // Hide the overlay
                CreateEventOverlay.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                ShowAutoSaveNotification($"Failed to create event: {ex.Message}");
                Log.Error($"TouchTemplateDesigner: Failed to create event: {ex.Message}");
            }
        }

        private void CancelCreateEvent_Click(object sender, RoutedEventArgs e)
        {
            // Hide the overlay without creating
            CreateEventOverlay.Visibility = Visibility.Collapsed;
        }

        private void CreateEventTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                // Move to description field
                CreateEventDescriptionTextBox.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelCreateEvent_Click(sender, e);
            }
        }

        private void CreateEventDescriptionTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                // Shift+Enter adds a new line, Enter alone submits
                ApplyCreateEvent_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelCreateEvent_Click(sender, e);
            }
        }

        private void CreateEventOverlay_BackgroundClick(object sender, MouseButtonEventArgs e)
        {
            // Only close if clicking on the background, not the content
            if (e.OriginalSource == sender)
            {
                CreateEventOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void RefreshEvents_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadEvents(true);  // Try to auto-select current event on refresh
                ShowAutoSaveNotification("Events refreshed successfully");
            }
            catch (Exception ex)
            {
                ShowAutoSaveNotification($"Failed to refresh events: {ex.Message}");
                Log.Error($"TouchTemplateDesigner: Failed to refresh events: {ex.Message}");
            }
        }

        private void EventTemplatesButton_Click(object sender, RoutedEventArgs e)
        {
            ShowEventTemplatesDialog();
        }

        private void ShowEventTemplatesDialog()
        {
            try
            {
                // Use the new overlay instead of dialog
                if (_selectedEvent != null)
                {
                    EventTemplatesOverlay.ShowForEvent(_selectedEvent, (templateId) =>
                    {
                        // Load the selected template
                        LoadTemplate(templateId);
                    });
                }
                else
                {
                    ShowAutoSaveNotification("Please select an event first");
                }
                return;

                // Create a selection window
                var templateWindow = new Window
                {
                    Title = $"Event Templates - {_selectedEvent.Name}",
                    Width = 650,
                    Height = 500,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this),
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                    Foreground = Brushes.White
                };

                // Create main grid
                var mainGrid = new Grid();
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // Title
                var titlePanel = new StackPanel
                {
                    Margin = new Thickness(15, 10, 15, 5)
                };

                var titleText = new TextBlock
                {
                    Text = $"Select a template to load ({_eventTemplates.Count} available)",
                    FontSize = 16,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 5)
                };

                var instructionText = new TextBlock
                {
                    Text = "Click any template to load it immediately",
                    FontSize = 13,
                    FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150))
                };

                titlePanel.Children.Add(titleText);
                titlePanel.Children.Add(instructionText);
                Grid.SetRow(titlePanel, 0);
                mainGrid.Children.Add(titlePanel);

                // Create scrollable list of templates
                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(10)
                };

                var templatesPanel = new StackPanel();

                // Add each template as a clickable item
                foreach (var template in _eventTemplates)
                {
                    var border = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(50, 50, 52)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 85)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(5),
                        Margin = new Thickness(8),
                        Padding = new Thickness(20, 15, 20, 15),
                        MinHeight = 80  // Minimum height for touch targets
                    };

                    var contentGrid = new Grid();
                    // Single column layout since Load button is removed
                    contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Keep for layout compatibility

                    var infoPanel = new StackPanel();

                    // Template name
                    var nameText = new TextBlock
                    {
                        Text = template.Name,
                        FontSize = 14,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White
                    };
                    infoPanel.Children.Add(nameText);

                    // Template info
                    var infoText = new TextBlock
                    {
                        Text = $"Size: {template.CanvasWidth} x {template.CanvasHeight}",
                        FontSize = 12,
                        Foreground = Brushes.LightGray,
                        Margin = new Thickness(0, 2, 0, 0)
                    };
                    infoPanel.Children.Add(infoText);

                    // Show if it's the default or current template
                    if (template.IsDefault)
                    {
                        var defaultBadgePanel = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Margin = new Thickness(0, 2, 0, 0)
                        };
                        var defaultIcon = new TextBlock
                        {
                            Text = "\uE735", // Star icon
                            FontFamily = new FontFamily("Segoe MDL2 Assets"),
                            FontSize = 12,
                            Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                            Margin = new Thickness(0, 0, 3, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        var defaultText = new TextBlock
                        {
                            Text = "Default Template",
                            FontSize = 11,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        defaultBadgePanel.Children.Add(defaultIcon);
                        defaultBadgePanel.Children.Add(defaultText);
                        infoPanel.Children.Add(defaultBadgePanel);
                    }

                    if (template.Id == _currentTemplateId)
                    {
                        var currentBadgePanel = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Margin = new Thickness(0, 2, 0, 0)
                        };
                        var currentIcon = new TextBlock
                        {
                            Text = "\uE73E", // CheckMark icon
                            FontFamily = new FontFamily("Segoe MDL2 Assets"),
                            FontSize = 12,
                            Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                            Margin = new Thickness(0, 0, 3, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        var currentText = new TextBlock
                        {
                            Text = "Currently Loaded",
                            FontSize = 11,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        currentBadgePanel.Children.Add(currentIcon);
                        currentBadgePanel.Children.Add(currentText);
                        infoPanel.Children.Add(currentBadgePanel);
                    }

                    Grid.SetColumn(infoPanel, 0);
                    Grid.SetColumnSpan(infoPanel, 2); // Span both columns since no button
                    contentGrid.Children.Add(infoPanel);

                    border.Child = contentGrid;

                    // Make the entire template item clickable for one-touch loading
                    border.Cursor = Cursors.Hand;
                    border.Tag = template;

                    // Single tap to load template immediately without confirmation
                    border.MouseLeftButtonDown += (s, args) =>
                    {
                        var clickedBorder = (Border)s;
                        var selectedTemplate = (TemplateData)clickedBorder.Tag;

                        // Skip if already loaded
                        if (_currentTemplateId == selectedTemplate.Id)
                        {
                            ShowAutoSaveNotification("This template is already loaded");
                            return;
                        }

                        // Visual feedback - briefly highlight
                        clickedBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                        clickedBorder.BorderThickness = new Thickness(3);

                        // Load template immediately without any confirmation
                        LoadTemplate(selectedTemplate.Id);
                        ShowAutoSaveNotification($"Loaded: {selectedTemplate.Name}");

                        // Close dialog after successful load
                        templateWindow.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            templateWindow.Close();
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    };

                    // Add hover effect for better UX
                    border.MouseEnter += (s, args) =>
                    {
                        var hoveredBorder = (Border)s;
                        hoveredBorder.Background = new SolidColorBrush(Color.FromRgb(65, 65, 68));
                        if (hoveredBorder.Tag != null && ((TemplateData)hoveredBorder.Tag).Id != _currentTemplateId)
                        {
                            hoveredBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 105));
                            hoveredBorder.BorderThickness = new Thickness(2);
                        }
                    };

                    border.MouseLeave += (s, args) =>
                    {
                        var hoveredBorder = (Border)s;
                        hoveredBorder.Background = new SolidColorBrush(Color.FromRgb(50, 50, 52));
                        if (hoveredBorder.Tag != null && ((TemplateData)hoveredBorder.Tag).Id != _currentTemplateId)
                        {
                            hoveredBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 85));
                            hoveredBorder.BorderThickness = new Thickness(1);
                        }
                    };

                    templatesPanel.Children.Add(border);
                }

                scrollViewer.Content = templatesPanel;
                Grid.SetRow(scrollViewer, 1);
                mainGrid.Children.Add(scrollViewer);

                // Add close button
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(10)
                };

                var closeButton = new Button
                {
                    Content = "Close",
                    Width = 100,
                    Height = 40,
                    FontSize = 14,
                    Background = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                closeButton.Click += (s, args) => templateWindow.Close();
                buttonPanel.Children.Add(closeButton);

                Grid.SetRow(buttonPanel, 2);
                mainGrid.Children.Add(buttonPanel);

                templateWindow.Content = mainGrid;
                templateWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner.ShowEventTemplatesDialog: Failed - {ex.Message}");
                MessageBox.Show($"Failed to show event templates: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}
