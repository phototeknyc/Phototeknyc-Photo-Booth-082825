using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Controls.Primitives;

namespace Photobooth.Controls
{
    /// <summary>
    /// Inline color picker control for toolbar integration
    /// </summary>
    public partial class InlineColorPicker : UserControl
    {
        private bool _isUpdating = false;
        private double _currentHue = 0;
        private double _currentSaturation = 1;
        private double _currentBrightness = 1;
        private Color _selectedColor = Colors.Black;
        private FrameworkElement _constrainToElement;
        private FrameworkElement _anchorTopElement;
        private bool _isDraggingInColorCanvas = false;
        private bool _isDraggingInHueCanvas = false;
        // Single-click eyedropper state
        private bool _isEyedropperActive = false;
        private UIElement _eyedropperTarget;
        private Cursor _previousCursor;

        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register("SelectedColor", typeof(Color), typeof(InlineColorPicker),
                new PropertyMetadata(Colors.Black, OnSelectedColorChanged));

        public Color SelectedColor
        {
            get { return (Color)GetValue(SelectedColorProperty); }
            set { SetValue(SelectedColorProperty, value); }
        }

        public static readonly RoutedEvent ColorChangedEvent =
            EventManager.RegisterRoutedEvent("ColorChanged", RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(InlineColorPicker));

        public event RoutedEventHandler ColorChanged
        {
            add { AddHandler(ColorChangedEvent, value); }
            remove { RemoveHandler(ColorChangedEvent, value); }
        }

        public FrameworkElement ConstrainToElement
        {
            get => _constrainToElement;
            set => _constrainToElement = value;
        }

        public FrameworkElement AnchorTopElement
        {
            get => _anchorTopElement;
            set => _anchorTopElement = value;
        }

        public InlineColorPicker()
        {
            InitializeComponent();
            InitializeColorPicker();
            SetSelectedColorInternal(Colors.Black);
        }

        // Ensure the color picker popup aligns its top edge to the bottom of an anchor element (e.g., top toolbar)
        private CustomPopupPlacement[] ColorPickerPopup_PlacementCallback(Size popupSize, Size targetSize, Point offset)
        {
            try
            {
                var target = ColorPreviewButton as FrameworkElement;
                var anchor = _anchorTopElement;
                var window = Window.GetWindow(this);
                if (target == null || window == null || anchor == null)
                {
                    // Default: open below the target button
                    return new[] { new CustomPopupPlacement(new Point(0, targetSize.Height), PopupPrimaryAxis.Horizontal) };
                }

                var source = PresentationSource.FromVisual(window);
                var fromDevice = source?.CompositionTarget?.TransformFromDevice;

                Point targetScreen = target.PointToScreen(new Point(0, 0));
                Point anchorBottomScreen = anchor.PointToScreen(new Point(0, anchor.ActualHeight));

                if (fromDevice.HasValue)
                {
                    targetScreen = fromDevice.Value.Transform(targetScreen);
                    anchorBottomScreen = fromDevice.Value.Transform(anchorBottomScreen);
                }

                const double marginTop = 8.0;
                double y = (anchorBottomScreen.Y + marginTop) - targetScreen.Y;
                double x = 0; // align left edges; caller can place container as needed

                return new[] { new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Vertical) };
            }
            catch
            {
                return new[] { new CustomPopupPlacement(new Point(0, 0), PopupPrimaryAxis.Horizontal) };
            }
        }

        private void InitializeColorPicker()
        {
            // Initialize preset colors
            var presetColors = new List<SolidColorBrush>
            {
                new SolidColorBrush(Colors.Black),
                new SolidColorBrush(Colors.White),
                new SolidColorBrush(Colors.Red),
                new SolidColorBrush(Colors.Green),
                new SolidColorBrush(Colors.Blue),
                new SolidColorBrush(Colors.Yellow),
                new SolidColorBrush(Colors.Cyan),
                new SolidColorBrush(Colors.Magenta),
                new SolidColorBrush(Colors.Orange),
                new SolidColorBrush(Colors.Purple),
                new SolidColorBrush(Colors.Gray),
                new SolidColorBrush(Colors.Brown)
            };

            PresetColorsControl.ItemsSource = presetColors;

            // Draw initial gradients
            Loaded += (s, e) => {
                DrawHueBar();
                DrawColorCanvas();
            };
        }

        private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var picker = (InlineColorPicker)d;
            if (e.NewValue is Color newColor)
            {
                picker.SetSelectedColorInternal(newColor);
            }
        }

        private void SetSelectedColorInternal(Color color)
        {
            _selectedColor = color;
            RGBToHSV(color, out _currentHue, out _currentSaturation, out _currentBrightness);
            UpdateUI();
            DrawColorCanvas();
            UpdateColorSelectorPosition();
        }

        private void DrawHueBar()
        {
            if (HueCanvas == null) return;
            HueCanvas.Children.Clear();

            var hueGradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };

            // Add color stops for the full hue spectrum
            for (int i = 0; i <= 6; i++)
            {
                Color color = HSVToRGB(i * 60, 1, 1);
                hueGradient.GradientStops.Add(new GradientStop(color, i / 6.0));
            }

            var rect = new Rectangle
            {
                Width = HueCanvas.ActualWidth > 0 ? HueCanvas.ActualWidth : 240,
                Height = 20,
                Fill = hueGradient
            };

            HueCanvas.Children.Add(rect);
        }

        private void DrawColorCanvas()
        {
            if (ColorCanvas == null) return;
            ColorCanvas.Children.Clear();

            // Create horizontal gradient (white to current hue)
            var horizontalGradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            horizontalGradient.GradientStops.Add(new GradientStop(Colors.White, 0));
            horizontalGradient.GradientStops.Add(new GradientStop(HSVToRGB(_currentHue, 1, 1), 1));

            var horizontalRect = new Rectangle
            {
                Width = ColorCanvas.ActualWidth > 0 ? ColorCanvas.ActualWidth : 240,
                Height = ColorCanvas.ActualHeight > 0 ? ColorCanvas.ActualHeight : 200,
                Fill = horizontalGradient
            };

            // Create vertical gradient (transparent to black)
            var verticalGradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            verticalGradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
            verticalGradient.GradientStops.Add(new GradientStop(Colors.Black, 1));

            var verticalRect = new Rectangle
            {
                Width = ColorCanvas.ActualWidth > 0 ? ColorCanvas.ActualWidth : 240,
                Height = ColorCanvas.ActualHeight > 0 ? ColorCanvas.ActualHeight : 200,
                Fill = verticalGradient
            };

            ColorCanvas.Children.Add(horizontalRect);
            ColorCanvas.Children.Add(verticalRect);
        }

        private void UpdateColorSelectorPosition()
        {
            if (ColorCanvas == null || ColorSelectorTransform == null) return;

            double canvasWidth = ColorCanvas.ActualWidth > 0 ? ColorCanvas.ActualWidth : 240;
            double canvasHeight = ColorCanvas.ActualHeight > 0 ? ColorCanvas.ActualHeight : 200;

            ColorSelectorTransform.X = _currentSaturation * canvasWidth - 6;
            ColorSelectorTransform.Y = (1 - _currentBrightness) * canvasHeight - 6;
        }

        private void UpdateUI()
        {
            if (_isUpdating) return;
            _isUpdating = true;

            try
            {
                // Update preview button
                if (ColorPreviewButton != null)
                    ColorPreviewButton.Background = new SolidColorBrush(_selectedColor);

                // Update current color display
                if (CurrentColorDisplay != null)
                    CurrentColorDisplay.Fill = new SolidColorBrush(_selectedColor);

                // Update RGB text boxes
                if (RedTextBox != null)
                    RedTextBox.Text = _selectedColor.R.ToString();
                if (GreenTextBox != null)
                    GreenTextBox.Text = _selectedColor.G.ToString();
                if (BlueTextBox != null)
                    BlueTextBox.Text = _selectedColor.B.ToString();

                // Update hex text box
                if (HexTextBox != null)
                    HexTextBox.Text = $"#{_selectedColor.R:X2}{_selectedColor.G:X2}{_selectedColor.B:X2}";
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void ColorCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingInColorCanvas = true;
            ColorCanvas.CaptureMouse();
            UpdateColorFromCanvasPosition(e.GetPosition(ColorCanvas));
            System.Diagnostics.Debug.WriteLine($"InlineColorPicker: Color canvas mouse down at {e.GetPosition(ColorCanvas)}");
        }

        private void ColorCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingInColorCanvas && e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateColorFromCanvasPosition(e.GetPosition(ColorCanvas));
            }
        }

        // Touch support for color canvas
        private void ColorCanvas_TouchDown(object sender, TouchEventArgs e)
        {
            _isDraggingInColorCanvas = true;
            if (sender is UIElement element)
                element.CaptureTouch(e.TouchDevice);
            var position = e.GetTouchPoint(ColorCanvas).Position;
            UpdateColorFromCanvasPosition(position);
            System.Diagnostics.Debug.WriteLine($"InlineColorPicker: Color canvas touch down at {position}");
            e.Handled = true;
        }

        private void ColorCanvas_TouchMove(object sender, TouchEventArgs e)
        {
            if (_isDraggingInColorCanvas)
            {
                var position = e.GetTouchPoint(ColorCanvas).Position;
                UpdateColorFromCanvasPosition(position);
                e.Handled = true;
            }
        }

        private void ColorCanvas_TouchUp(object sender, TouchEventArgs e)
        {
            if (_isDraggingInColorCanvas)
            {
                _isDraggingInColorCanvas = false;
                if (sender is UIElement element)
                    element.ReleaseTouchCapture(e.TouchDevice);
                System.Diagnostics.Debug.WriteLine("InlineColorPicker: Color canvas touch up");
                e.Handled = true;
            }
        }

        private void UpdateColorFromCanvasPosition(Point position)
        {
            double width = ColorCanvas.ActualWidth;
            double height = ColorCanvas.ActualHeight;

            _currentSaturation = Math.Max(0, Math.Min(1, position.X / width));
            _currentBrightness = Math.Max(0, Math.Min(1, 1 - (position.Y / height)));

            _selectedColor = HSVToRGB(_currentHue, _currentSaturation, _currentBrightness);
            SelectedColor = _selectedColor;
            UpdateUI();
            UpdateColorSelectorPosition();
            RaiseColorChanged();
        }

        private void HueCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingInHueCanvas = true;
            HueCanvas.CaptureMouse();
            UpdateHueFromCanvasPosition(e.GetPosition(HueCanvas));
            System.Diagnostics.Debug.WriteLine($"InlineColorPicker: Hue canvas mouse down at {e.GetPosition(HueCanvas)}");
        }

        private void HueCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingInHueCanvas && e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateHueFromCanvasPosition(e.GetPosition(HueCanvas));
            }
        }

        // Touch support for hue canvas
        private void HueCanvas_TouchDown(object sender, TouchEventArgs e)
        {
            _isDraggingInHueCanvas = true;
            if (sender is UIElement element)
                element.CaptureTouch(e.TouchDevice);
            var position = e.GetTouchPoint(HueCanvas).Position;
            UpdateHueFromCanvasPosition(position);
            System.Diagnostics.Debug.WriteLine($"InlineColorPicker: Hue canvas touch down at {position}");
            e.Handled = true;
        }

        private void HueCanvas_TouchMove(object sender, TouchEventArgs e)
        {
            if (_isDraggingInHueCanvas)
            {
                var position = e.GetTouchPoint(HueCanvas).Position;
                UpdateHueFromCanvasPosition(position);
                e.Handled = true;
            }
        }

        private void HueCanvas_TouchUp(object sender, TouchEventArgs e)
        {
            if (_isDraggingInHueCanvas)
            {
                _isDraggingInHueCanvas = false;
                if (sender is UIElement element)
                    element.ReleaseTouchCapture(e.TouchDevice);
                System.Diagnostics.Debug.WriteLine("InlineColorPicker: Hue canvas touch up");
                e.Handled = true;
            }
        }

        private void UpdateHueFromCanvasPosition(Point position)
        {
            double width = HueCanvas.ActualWidth;
            _currentHue = Math.Max(0, Math.Min(360, (position.X / width) * 360));

            _selectedColor = HSVToRGB(_currentHue, _currentSaturation, _currentBrightness);
            SelectedColor = _selectedColor;
            UpdateUI();
            DrawColorCanvas();
            UpdateColorSelectorPosition();
            RaiseColorChanged();
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonUp(e);

            if (_isDraggingInColorCanvas)
            {
                _isDraggingInColorCanvas = false;
                ColorCanvas.ReleaseMouseCapture();
                System.Diagnostics.Debug.WriteLine("InlineColorPicker: Released color canvas mouse capture");
            }

            if (_isDraggingInHueCanvas)
            {
                _isDraggingInHueCanvas = false;
                HueCanvas.ReleaseMouseCapture();
                System.Diagnostics.Debug.WriteLine("InlineColorPicker: Released hue canvas mouse capture");
            }
        }

        private void RGBTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;
            if (RedTextBox == null || GreenTextBox == null || BlueTextBox == null) return;

            if (int.TryParse(RedTextBox.Text, out int r) &&
                int.TryParse(GreenTextBox.Text, out int g) &&
                int.TryParse(BlueTextBox.Text, out int b))
            {
                r = Math.Max(0, Math.Min(255, r));
                g = Math.Max(0, Math.Min(255, g));
                b = Math.Max(0, Math.Min(255, b));

                var newColor = Color.FromRgb((byte)r, (byte)g, (byte)b);
                SetSelectedColorInternal(newColor);
                SelectedColor = newColor;
                RaiseColorChanged();
            }
        }

        private void HexTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;
            if (HexTextBox == null) return;

            try
            {
                string hex = HexTextBox.Text.Replace("#", "");
                if (hex.Length == 6)
                {
                    int r = Convert.ToInt32(hex.Substring(0, 2), 16);
                    int g = Convert.ToInt32(hex.Substring(2, 2), 16);
                    int b = Convert.ToInt32(hex.Substring(4, 2), 16);

                    var newColor = Color.FromRgb((byte)r, (byte)g, (byte)b);
                    SetSelectedColorInternal(newColor);
                    SelectedColor = newColor;
                    RaiseColorChanged();
                }
            }
            catch
            {
                // Invalid hex format, ignore
            }
        }

        private void PresetColor_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is SolidColorBrush brush)
            {
                SetSelectedColorInternal(brush.Color);
                SelectedColor = brush.Color;
                RaiseColorChanged();
                System.Diagnostics.Debug.WriteLine($"InlineColorPicker: Preset color selected: {brush.Color}");
            }
        }

        // Touch support for preset colors
        private void PresetColor_TouchDown(object sender, TouchEventArgs e)
        {
            if (sender is Border border && border.DataContext is SolidColorBrush brush)
            {
                SetSelectedColorInternal(brush.Color);
                SelectedColor = brush.Color;
                RaiseColorChanged();
                System.Diagnostics.Debug.WriteLine($"InlineColorPicker: Preset color selected via touch: {brush.Color}");
                e.Handled = true;
            }
        }

        private void EyedropperButton_Click(object sender, RoutedEventArgs e)
        {
            StartInlineEyedropper();
        }

        private void StartInlineEyedropper()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("InlineColorPicker: Starting single-click eyedropper");
                try { if (ColorPickerPopup != null) ColorPickerPopup.IsOpen = false; } catch { }

                var target = _constrainToElement as UIElement ?? this as UIElement;
                if (target == null)
                {
                    MessageBox.Show("Eyedropper requires a target element.", "Eyedropper", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _eyedropperTarget = target;
                _isEyedropperActive = true;
                _previousCursor = Mouse.OverrideCursor;
                Mouse.OverrideCursor = Cursors.Cross;

                // Listen at window level on MouseLeftButtonUp to avoid interfering with canvas selection
                var window = Window.GetWindow(target);
                if (window != null)
                {
                    window.PreviewMouseLeftButtonDown += Eyedropper_WindowPreviewMouseLeftButtonDown;
                    window.PreviewMouseLeftButtonUp += Eyedropper_WindowPreviewMouseLeftButtonUp;
                    window.PreviewKeyDown += Eyedropper_WindowPreviewKeyDown;
                }
            }
            catch (Exception ex)
            {
                try { ColorPickerPopup.IsOpen = true; } catch { }
                MessageBox.Show($"Error starting eyedropper: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EndInlineEyedropper()
        {
            if (!_isEyedropperActive) return;
            _isEyedropperActive = false;
            Mouse.OverrideCursor = _previousCursor;
            if (_eyedropperTarget != null)
            {
                var window = Window.GetWindow(_eyedropperTarget);
                if (window != null)
                {
                    window.PreviewMouseLeftButtonDown -= Eyedropper_WindowPreviewMouseLeftButtonDown;
                    window.PreviewMouseLeftButtonUp -= Eyedropper_WindowPreviewMouseLeftButtonUp;
                    window.PreviewKeyDown -= Eyedropper_WindowPreviewKeyDown;
                }
                _eyedropperTarget = null;
            }
            try { ColorPickerPopup.IsOpen = true; } catch { }
        }

        private void Eyedropper_WindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _isEyedropperActive)
            {
                EndInlineEyedropper();
                e.Handled = true;
            }
        }

        private void Eyedropper_WindowPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Color picked;
                if (_constrainToElement is SimpleDesignerCanvas canvas)
                {
                    // Get position relative to canvas
                    var local = e.GetPosition(canvas);
                    var canvasColor = GetDesignerCanvasPixelColor(canvas, local);
                    picked = canvasColor ?? Colors.Transparent;
                    if (picked == Colors.Transparent)
                    {
                        // Fallback to screen sample
                        var window = sender as Window;
                        var ptWin = e.GetPosition(window);
                        var screen = window.PointToScreen(ptWin);
                        picked = GetPixelColor((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
                    }
                }
                else
                {
                    var window = sender as Window;
                    var ptWin = e.GetPosition(window);
                    var screen = window.PointToScreen(ptWin);
                    picked = GetPixelColor((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
                }

                SetSelectedColorInternal(picked);
                SelectedColor = picked;
                RaiseColorChanged();

                try
                {
                    if (_constrainToElement is SimpleDesignerCanvas sdc && sdc.SelectedItem is SimpleTextItem st)
                    {
                        st.TextColor = new SolidColorBrush(picked);
                    }
                }
                catch { }
            }
            finally
            {
                EndInlineEyedropper();
            }
        }

        private void Eyedropper_WindowPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isEyedropperActive) return;
            try
            {
                // Sample on mouse down and consume the event to avoid canvas selection changes
                Color picked;
                if (_constrainToElement is SimpleDesignerCanvas canvas)
                {
                    var local = e.GetPosition(canvas);
                    var canvasColor = GetDesignerCanvasPixelColor(canvas, local);
                    picked = canvasColor ?? Colors.Transparent;
                    if (picked == Colors.Transparent)
                    {
                        var window = sender as Window;
                        var ptWin = e.GetPosition(window);
                        var screen = window.PointToScreen(ptWin);
                        picked = GetPixelColor((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
                    }
                }
                else
                {
                    var window = sender as Window;
                    var ptWin = e.GetPosition(window);
                    var screen = window.PointToScreen(ptWin);
                    picked = GetPixelColor((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
                }

                SetSelectedColorInternal(picked);
                SelectedColor = picked;
                RaiseColorChanged();

                try
                {
                    if (_constrainToElement is SimpleDesignerCanvas sdc && sdc.SelectedItem is SimpleTextItem st)
                    {
                        st.TextColor = new SolidColorBrush(picked);
                    }
                }
                catch { }
            }
            finally
            {
                EndInlineEyedropper();
                e.Handled = true; // prevent selection changes
            }
        }

        private void Eyedropper_TargetTouchDown(object sender, TouchEventArgs e)
        {
            try
            {
                Color picked;
                if (_constrainToElement is SimpleDesignerCanvas canvas)
                {
                    var local = e.GetTouchPoint(canvas).Position;
                    var canvasColor = GetDesignerCanvasPixelColor(canvas, local);
                    picked = canvasColor ?? Colors.Transparent;
                    if (picked == Colors.Transparent)
                    {
                        var screen = ((UIElement)sender).PointToScreen(local);
                        picked = GetPixelColor((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
                    }
                }
                else
                {
                    var local = e.GetTouchPoint((IInputElement)sender).Position;
                    var screen = ((UIElement)sender).PointToScreen(local);
                    picked = GetPixelColor((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
                }

                SetSelectedColorInternal(picked);
                SelectedColor = picked;
                RaiseColorChanged();
            }
            finally
            {
                EndInlineEyedropper();
                e.Handled = true;
            }
        }

        // Win32 API imports for pixel color capture
        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        private Color GetPixelColor(int x, int y)
        {
            // Try canvas-aware sampling first if we have a constraint element
            if (_constrainToElement != null)
            {
                var canvasColor = GetCanvasPixelColor(x, y);
                if (canvasColor.HasValue)
                {
                    System.Diagnostics.Debug.WriteLine($"InlineColorPicker: Canvas color sampled: {canvasColor.Value}");
                    return canvasColor.Value;
                }
            }

            // Fall back to screen sampling
            IntPtr hdc = GetDC(IntPtr.Zero);
            uint pixel = GetPixel(hdc, x, y);
            ReleaseDC(IntPtr.Zero, hdc);

            byte r = (byte)(pixel & 0xFF);
            byte g = (byte)((pixel >> 8) & 0xFF);
            byte b = (byte)((pixel >> 16) & 0xFF);

            var screenColor = Color.FromRgb(r, g, b);
            System.Diagnostics.Debug.WriteLine($"InlineColorPicker: Screen color sampled: {screenColor}");
            return screenColor;
        }

        private Color? GetCanvasPixelColor(int screenX, int screenY)
        {
            try
            {
                if (_constrainToElement == null || !_constrainToElement.IsLoaded)
                    return null;

                var elementWindow = Window.GetWindow(_constrainToElement);
                if (elementWindow == null)
                    return null;

                // Convert screen coordinates to element coordinates
                var screenPoint = new Point(screenX, screenY);
                var elementPoint = elementWindow.PointFromScreen(screenPoint);

                // Transform to the constraint element's coordinate system
                var transform = elementWindow.TransformToDescendant(_constrainToElement);
                if (transform == null)
                    return null;

                var localPoint = transform.Transform(elementPoint);

                // Check if the point is within the element bounds
                if (localPoint.X < 0 || localPoint.Y < 0 ||
                    localPoint.X >= _constrainToElement.ActualWidth ||
                    localPoint.Y >= _constrainToElement.ActualHeight)
                {
                    return null;
                }

                // For SimpleDesignerCanvas, look for specific canvas items
                if (_constrainToElement is SimpleDesignerCanvas designerCanvas)
                {
                    return GetDesignerCanvasPixelColor(designerCanvas, localPoint);
                }

                // For other elements, render to bitmap and sample
                return RenderElementAndSample(_constrainToElement, localPoint);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InlineColorPicker: Error in canvas sampling: {ex.Message}");
                return null;
            }
        }

        private Color? GetDesignerCanvasPixelColor(SimpleDesignerCanvas canvas, Point localPoint)
        {
            try
            {
                // First, try to find the topmost item at this point
                var item = GetTopItemAtPoint(canvas, localPoint);
                if (item != null)
                {
                    // Convert to item-local coordinates
                    var itemLocalX = localPoint.X - item.Left;
                    var itemLocalY = localPoint.Y - item.Top;

                    // Check if point is within item bounds
                    if (itemLocalX >= 0 && itemLocalY >= 0 &&
                        itemLocalX < item.Width && itemLocalY < item.Height)
                    {
                        // Sample color from the specific item
                        var itemColor = SampleColorFromItem(item, itemLocalX, itemLocalY);
                        if (itemColor.HasValue)
                        {
                            System.Diagnostics.Debug.WriteLine($"InlineColorPicker: Item color sampled: {itemColor.Value} from {item.GetType().Name}");
                            return itemColor.Value;
                        }
                    }
                }

                // If no item found or item sampling failed, render the entire canvas
                return RenderElementAndSample(canvas, localPoint);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InlineColorPicker: Error in designer canvas sampling: {ex.Message}");
                return null;
            }
        }

        private SimpleCanvasItem GetTopItemAtPoint(SimpleDesignerCanvas canvas, Point point)
        {
            try
            {
                // Get items in Z-order (topmost first)
                return canvas.Items
                    .Where(item => point.X >= item.Left && point.X <= item.Left + item.Width &&
                                  point.Y >= item.Top && point.Y <= item.Top + item.Height)
                    .OrderByDescending(item => item.ZIndex)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private Color? SampleColorFromItem(SimpleCanvasItem item, double localX, double localY)
        {
            try
            {
                // Handle different item types
                if (item is SimpleTextItem textItem)
                {
                    return SampleColorFromTextItem(textItem, localX, localY);
                }
                else if (item is SimpleImageItem imageItem)
                {
                    return SampleColorFromImageItem(imageItem, localX, localY);
                }
                else
                {
                    // For other item types, render the item to a bitmap
                    return RenderElementAndSample(item, new Point(localX, localY));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InlineColorPicker: Error sampling from item: {ex.Message}");
                return null;
            }
        }

        private Color? SampleColorFromTextItem(SimpleTextItem textItem, double localX, double localY)
        {
            try
            {
                // For text items, the color is usually uniform based on the TextColor property
                if (textItem.TextColor is SolidColorBrush solidBrush)
                {
                    return solidBrush.Color;
                }

                // If it's a complex brush, render the item to sample the exact pixel
                return RenderElementAndSample(textItem, new Point(localX, localY));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InlineColorPicker: Error sampling text item: {ex.Message}");
                return null;
            }
        }

        private Color? SampleColorFromImageItem(SimpleImageItem imageItem, double localX, double localY)
        {
            try
            {
                // If it's a placeholder, sample the background color
                if (imageItem.IsPlaceholder)
                {
                    // Render the placeholder to get the exact color
                    return RenderElementAndSample(imageItem, new Point(localX, localY));
                }

                // For actual images, we need to sample the image pixels
                var imageSource = imageItem.ImageSource;
                if (imageSource is BitmapSource bitmap)
                {
                    return SampleColorFromBitmap(bitmap, localX, localY, imageItem.Width, imageItem.Height, imageItem.Stretch);
                }

                // Fall back to rendering
                return RenderElementAndSample(imageItem, new Point(localX, localY));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InlineColorPicker: Error sampling image item: {ex.Message}");
                return null;
            }
        }

        private Color? SampleColorFromBitmap(BitmapSource bitmap, double localX, double localY,
            double itemWidth, double itemHeight, Stretch stretch)
        {
            try
            {
                // Calculate the actual image coordinates based on stretch mode
                var imageX = localX;
                var imageY = localY;

                switch (stretch)
                {
                    case Stretch.Fill:
                        // Direct mapping
                        imageX = (localX / itemWidth) * bitmap.PixelWidth;
                        imageY = (localY / itemHeight) * bitmap.PixelHeight;
                        break;

                    case Stretch.Uniform:
                        // Maintain aspect ratio, center image
                        var scaleX = itemWidth / bitmap.Width;
                        var scaleY = itemHeight / bitmap.Height;
                        var scale = Math.Min(scaleX, scaleY);

                        var scaledWidth = bitmap.Width * scale;
                        var scaledHeight = bitmap.Height * scale;
                        var offsetX = (itemWidth - scaledWidth) / 2;
                        var offsetY = (itemHeight - scaledHeight) / 2;

                        if (localX < offsetX || localY < offsetY ||
                            localX >= offsetX + scaledWidth || localY >= offsetY + scaledHeight)
                        {
                            return Colors.Transparent; // Outside image bounds
                        }

                        imageX = ((localX - offsetX) / scaledWidth) * bitmap.PixelWidth;
                        imageY = ((localY - offsetY) / scaledHeight) * bitmap.PixelHeight;
                        break;

                    case Stretch.UniformToFill:
                        // Fill entire area, may crop
                        var scaleXFill = itemWidth / bitmap.Width;
                        var scaleYFill = itemHeight / bitmap.Height;
                        var scaleFill = Math.Max(scaleXFill, scaleYFill);

                        var scaledWidthFill = bitmap.Width * scaleFill;
                        var scaledHeightFill = bitmap.Height * scaleFill;
                        var offsetXFill = (itemWidth - scaledWidthFill) / 2;
                        var offsetYFill = (itemHeight - scaledHeightFill) / 2;

                        imageX = ((localX - offsetXFill) / scaledWidthFill) * bitmap.PixelWidth;
                        imageY = ((localY - offsetYFill) / scaledHeightFill) * bitmap.PixelHeight;
                        break;

                    default: // Stretch.None
                        // No scaling, image at natural size
                        imageX = localX;
                        imageY = localY;
                        break;
                }

                // Clamp to bitmap bounds
                var pixelX = Math.Max(0, Math.Min(bitmap.PixelWidth - 1, (int)Math.Round(imageX)));
                var pixelY = Math.Max(0, Math.Min(bitmap.PixelHeight - 1, (int)Math.Round(imageY)));

                // Sample the pixel
                var croppedBitmap = new CroppedBitmap(bitmap, new Int32Rect(pixelX, pixelY, 1, 1));
                var pixels = new byte[4];
                croppedBitmap.CopyPixels(pixels, 4, 0);

                // Convert to Color (assuming BGRA format)
                return Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InlineColorPicker: Error sampling bitmap: {ex.Message}");
                return null;
            }
        }

        private Color? RenderElementAndSample(UIElement element, Point localPoint)
        {
            try
            {
                // Cast to FrameworkElement to access ActualWidth/ActualHeight
                if (!(element is FrameworkElement frameworkElement))
                    return null;

                if (frameworkElement.ActualWidth <= 0 || frameworkElement.ActualHeight <= 0)
                    return null;

                // Create a render target bitmap
                var renderBitmap = new RenderTargetBitmap(
                    (int)Math.Ceiling(frameworkElement.ActualWidth),
                    (int)Math.Ceiling(frameworkElement.ActualHeight),
                    96, 96, PixelFormats.Pbgra32);

                // Render the element
                renderBitmap.Render(element);

                // Sample the pixel at the local point
                var pixelX = Math.Max(0, Math.Min(renderBitmap.PixelWidth - 1, (int)Math.Round(localPoint.X)));
                var pixelY = Math.Max(0, Math.Min(renderBitmap.PixelHeight - 1, (int)Math.Round(localPoint.Y)));

                var croppedBitmap = new CroppedBitmap(renderBitmap, new Int32Rect(pixelX, pixelY, 1, 1));
                var pixels = new byte[4];
                croppedBitmap.CopyPixels(pixels, 4, 0);

                // Convert to Color (BGRA format)
                var color = Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);

                System.Diagnostics.Debug.WriteLine($"InlineColorPicker: Rendered element color: {color} at ({pixelX}, {pixelY})");
                return color;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InlineColorPicker: Error rendering element: {ex.Message}");
                return null;
            }
        }

        private void RaiseColorChanged()
        {
            RaiseEvent(new RoutedEventArgs(ColorChangedEvent));
        }

        // HSV/RGB conversion utilities
        private static Color HSVToRGB(double hue, double saturation, double brightness)
        {
            double r, g, b;

            if (saturation == 0)
            {
                r = g = b = brightness;
            }
            else
            {
                double h = hue / 60;
                int i = (int)Math.Floor(h);
                double f = h - i;
                double p = brightness * (1 - saturation);
                double q = brightness * (1 - saturation * f);
                double t = brightness * (1 - saturation * (1 - f));

                switch (i % 6)
                {
                    case 0: r = brightness; g = t; b = p; break;
                    case 1: r = q; g = brightness; b = p; break;
                    case 2: r = p; g = brightness; b = t; break;
                    case 3: r = p; g = q; b = brightness; break;
                    case 4: r = t; g = p; b = brightness; break;
                    default: r = brightness; g = p; b = q; break;
                }
            }

            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private static void RGBToHSV(Color color, out double hue, out double saturation, out double brightness)
        {
            double rd = color.R / 255.0;
            double gd = color.G / 255.0;
            double bd = color.B / 255.0;

            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            double delta = max - min;

            brightness = max;
            saturation = max == 0 ? 0 : delta / max;

            if (delta == 0)
            {
                hue = 0;
            }
            else if (max == rd)
            {
                hue = 60 * (((gd - bd) / delta) % 6);
            }
            else if (max == gd)
            {
                hue = 60 * ((bd - rd) / delta + 2);
            }
            else
            {
                hue = 60 * ((rd - gd) / delta + 4);
            }

            if (hue < 0) hue += 360;
        }
    }
}
