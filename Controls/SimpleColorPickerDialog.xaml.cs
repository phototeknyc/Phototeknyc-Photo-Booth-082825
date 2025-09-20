using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Photobooth.Controls
{
    /// <summary>
    /// Simple, reliable WPF color picker dialog with eyedropper functionality
    /// </summary>
    public partial class SimpleColorPickerDialog : Window
    {
        private bool _isUpdatingUI = false;
        private double _currentHue = 0;
        private double _currentSaturation = 1;
        private double _currentBrightness = 1;
        private Color _selectedColor = Colors.Red;
        private FrameworkElement _constrainToElement;

        public Color SelectedColor
        {
            get { return _selectedColor; }
            set { SetSelectedColor(value); }
        }

        public SimpleColorPickerDialog()
        {
            InitializeComponent();
            InitializeColorCanvas();
            SetSelectedColor(Colors.Red);
        }

        public SimpleColorPickerDialog(string title, Color initialColor) : this()
        {
            Title = title;
            SetSelectedColor(initialColor);
        }

        private void InitializeColorCanvas()
        {
            // Draw the hue bar
            DrawHueBar();
            // Draw the main color canvas
            DrawColorCanvas();
        }

        private void DrawHueBar()
        {
            if (HueCanvas == null) return;
            HueCanvas.Children.Clear();

            var hueRect = new Rectangle
            {
                Width = 30,
                Height = 300
            };

            var hueBrush = new LinearGradientBrush();
            hueBrush.StartPoint = new Point(0, 0);
            hueBrush.EndPoint = new Point(0, 1);

            // Add hue gradient stops
            for (int i = 0; i <= 6; i++)
            {
                double position = i / 6.0;
                Color color = HSVToRGB(i * 60, 1, 1);
                hueBrush.GradientStops.Add(new GradientStop(color, position));
            }

            hueRect.Fill = hueBrush;
            HueCanvas.Children.Add(hueRect);
        }

        private void DrawColorCanvas()
        {
            if (ColorCanvas == null) return;
            ColorCanvas.Children.Clear();

            var colorRect = new Rectangle
            {
                Width = 400,
                Height = 300
            };

            // Create a gradient from white to the current hue, then overlay black gradient
            var mesh = new DrawingBrush();
            var drawingGroup = new DrawingGroup();

            // Base color (current hue at full saturation and brightness)
            var baseColor = HSVToRGB(_currentHue, 1, 1);
            var baseRect = new GeometryDrawing(new SolidColorBrush(baseColor), null, new RectangleGeometry(new Rect(0, 0, 1, 1)));
            drawingGroup.Children.Add(baseRect);

            // White to transparent gradient (horizontal - controls saturation)
            var saturationBrush = new LinearGradientBrush();
            saturationBrush.StartPoint = new Point(0, 0);
            saturationBrush.EndPoint = new Point(1, 0);
            saturationBrush.GradientStops.Add(new GradientStop(Colors.White, 0));
            saturationBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));

            var saturationRect = new GeometryDrawing(saturationBrush, null, new RectangleGeometry(new Rect(0, 0, 1, 1)));
            drawingGroup.Children.Add(saturationRect);

            // Transparent to black gradient (vertical - controls brightness)
            var brightnessBrush = new LinearGradientBrush();
            brightnessBrush.StartPoint = new Point(0, 0);
            brightnessBrush.EndPoint = new Point(0, 1);
            brightnessBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            brightnessBrush.GradientStops.Add(new GradientStop(Colors.Black, 1));

            var brightnessRect = new GeometryDrawing(brightnessBrush, null, new RectangleGeometry(new Rect(0, 0, 1, 1)));
            drawingGroup.Children.Add(brightnessRect);

            mesh.Drawing = drawingGroup;
            colorRect.Fill = mesh;

            ColorCanvas.Children.Add(colorRect);
        }

        private void SetSelectedColor(Color color)
        {
            _selectedColor = color;

            // Convert to HSV
            RGBToHSV(color.R, color.G, color.B, out _currentHue, out _currentSaturation, out _currentBrightness);

            // Update UI
            UpdateUI();

            // Redraw color canvas with new hue
            DrawColorCanvas();
        }

        private void UpdateUI()
        {
            if (_isUpdatingUI) return;
            _isUpdatingUI = true;

            try
            {
                // Update color preview
                if (ColorPreview != null)
                    ColorPreview.Fill = new SolidColorBrush(_selectedColor);

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
                _isUpdatingUI = false;
            }
        }

        private void ColorCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ColorCanvas.CaptureMouse();
            UpdateColorFromCanvas(e.GetPosition(ColorCanvas));
        }

        private void ColorCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && ColorCanvas.IsMouseCaptured)
            {
                UpdateColorFromCanvas(e.GetPosition(ColorCanvas));
            }
        }

        private void UpdateColorFromCanvas(Point position)
        {
            double saturation = Math.Max(0, Math.Min(1, position.X / ColorCanvas.Width));
            double brightness = Math.Max(0, Math.Min(1, 1 - (position.Y / ColorCanvas.Height)));

            _currentSaturation = saturation;
            _currentBrightness = brightness;

            _selectedColor = HSVToRGB(_currentHue, _currentSaturation, _currentBrightness);
            UpdateUI();
        }

        private void HueCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            HueCanvas.CaptureMouse();
            UpdateHueFromCanvas(e.GetPosition(HueCanvas));
        }

        private void HueCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && HueCanvas.IsMouseCaptured)
            {
                UpdateHueFromCanvas(e.GetPosition(HueCanvas));
            }
        }

        private void UpdateHueFromCanvas(Point position)
        {
            double hue = Math.Max(0, Math.Min(360, (position.Y / HueCanvas.Height) * 360));
            _currentHue = hue;

            _selectedColor = HSVToRGB(_currentHue, _currentSaturation, _currentBrightness);
            UpdateUI();
            DrawColorCanvas(); // Redraw with new hue
        }

        private void RGBTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUI) return;

            // Check if textboxes are initialized
            if (RedTextBox == null || GreenTextBox == null || BlueTextBox == null) return;

            if (int.TryParse(RedTextBox.Text, out int r) &&
                int.TryParse(GreenTextBox.Text, out int g) &&
                int.TryParse(BlueTextBox.Text, out int b))
            {
                r = Math.Max(0, Math.Min(255, r));
                g = Math.Max(0, Math.Min(255, g));
                b = Math.Max(0, Math.Min(255, b));

                SetSelectedColor(Color.FromRgb((byte)r, (byte)g, (byte)b));
            }
        }

        private void HexTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUI) return;

            // Check if textbox is initialized
            if (HexTextBox == null) return;

            try
            {
                string hex = HexTextBox.Text.Replace("#", "");
                if (hex.Length == 6)
                {
                    int r = Convert.ToInt32(hex.Substring(0, 2), 16);
                    int g = Convert.ToInt32(hex.Substring(2, 2), 16);
                    int b = Convert.ToInt32(hex.Substring(4, 2), 16);

                    SetSelectedColor(Color.FromRgb((byte)r, (byte)g, (byte)b));
                }
            }
            catch
            {
                // Invalid hex format, ignore
            }
        }

        private void PresetColor_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Rectangle rect && rect.Tag is string hexColor)
            {
                try
                {
                    string hex = hexColor.Replace("#", "");
                    int r = Convert.ToInt32(hex.Substring(0, 2), 16);
                    int g = Convert.ToInt32(hex.Substring(2, 2), 16);
                    int b = Convert.ToInt32(hex.Substring(4, 2), 16);

                    SetSelectedColor(Color.FromRgb((byte)r, (byte)g, (byte)b));
                }
                catch
                {
                    // Invalid preset color, ignore
                }
            }
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void EyedropperButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Store dialog position
                var dialogLeft = this.Left;
                var dialogTop = this.Top;

                // Hide the dialog
                this.Hide();

                // Get canvas bounds if constrained
                Rect? constraintBounds = null;
                if (_constrainToElement != null)
                {
                    try
                    {
                        // Try to get the element's screen position
                        if (_constrainToElement.IsLoaded && PresentationSource.FromVisual(_constrainToElement) != null)
                        {
                            var elementWindow = Window.GetWindow(_constrainToElement);
                            if (elementWindow != null)
                            {
                                var transform = _constrainToElement.TransformToVisual(elementWindow);
                                var topLeft = transform.Transform(new Point(0, 0));
                                var screenTopLeft = elementWindow.PointToScreen(topLeft);
                                constraintBounds = new Rect(screenTopLeft.X, screenTopLeft.Y,
                                    _constrainToElement.ActualWidth, _constrainToElement.ActualHeight);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // If we can't get bounds, eyedropper will work on full screen
                        System.Diagnostics.Debug.WriteLine($"SimpleColorPickerDialog: Could not get constraint bounds: {ex.Message}");
                        constraintBounds = null;
                    }
                }

                // Create a full-screen capture window
                var captureWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    Left = 0,
                    Top = 0,
                    Width = SystemParameters.VirtualScreenWidth,
                    Height = SystemParameters.VirtualScreenHeight,
                    Cursor = Cursors.Cross,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual
                };

                bool colorCaptured = false;
                Color currentColor = Colors.Black;

                // Create canvas for overlay
                var canvas = new Canvas();

                // Photoshop-style instruction bar
                var instructionBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(240, 45, 45, 48)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(20, 12, 20, 12)
                };

                var instructionText = new TextBlock
                {
                    Text = "Click to sample color • Move for preview • ESC to cancel",
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.Medium
                };

                instructionBorder.Child = instructionText;
                Canvas.SetLeft(instructionBorder, SystemParameters.VirtualScreenWidth / 2 - 200);
                Canvas.SetTop(instructionBorder, 40);
                canvas.Children.Add(instructionBorder);

                // Photoshop-style magnifier loupe
                var loupeSize = 140.0;
                var loupeBorder = new Border
                {
                    Width = loupeSize,
                    Height = loupeSize,
                    CornerRadius = new CornerRadius(loupeSize / 2),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                    BorderThickness = new Thickness(3),
                    Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                    Visibility = Visibility.Collapsed,
                    IsHitTestVisible = false
                };

                var loupeContent = new Grid();

                // Add magnified color display (simplified)
                var magnifiedColor = new Rectangle
                {
                    Width = loupeSize - 6,
                    Height = loupeSize - 6,
                    Fill = Brushes.Black
                };

                // Center crosshair
                var crosshairH = new Rectangle
                {
                    Height = 2,
                    Width = loupeSize,
                    Fill = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var crosshairV = new Rectangle
                {
                    Width = 2,
                    Height = loupeSize,
                    Fill = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                // Grid pattern for pixel precision
                var gridCanvas = new Canvas();
                var gridBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
                for (int i = 1; i < 7; i++)
                {
                    var lineH = new Line
                    {
                        X1 = 0, Y1 = i * loupeSize / 7,
                        X2 = loupeSize, Y2 = i * loupeSize / 7,
                        Stroke = gridBrush,
                        StrokeThickness = 0.5
                    };
                    var lineV = new Line
                    {
                        X1 = i * loupeSize / 7, Y1 = 0,
                        X2 = i * loupeSize / 7, Y2 = loupeSize,
                        Stroke = gridBrush,
                        StrokeThickness = 0.5
                    };
                    gridCanvas.Children.Add(lineH);
                    gridCanvas.Children.Add(lineV);
                }

                loupeContent.Children.Add(magnifiedColor);
                loupeContent.Children.Add(gridCanvas);
                loupeContent.Children.Add(crosshairH);
                loupeContent.Children.Add(crosshairV);
                loupeBorder.Child = loupeContent;

                // Color info panel (Photoshop-style)
                var colorInfoBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(240, 45, 45, 48)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(15, 12, 15, 12),
                    Visibility = Visibility.Collapsed,
                    IsHitTestVisible = false
                };

                var colorInfoStack = new StackPanel();

                // Large color swatch
                var swatchBorder = new Border
                {
                    Width = 200,
                    Height = 50,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 0, 10)
                };
                var colorSwatch = new Rectangle
                {
                    Width = 200,
                    Height = 50,
                    Fill = Brushes.Black
                };
                swatchBorder.Child = colorSwatch;

                // RGB values
                var rgbText = new TextBlock
                {
                    Text = "RGB: 0, 0, 0",
                    Foreground = Brushes.White,
                    FontSize = 13,
                    FontFamily = new FontFamily("Consolas")
                };

                // Hex value
                var hexText = new TextBlock
                {
                    Text = "HEX: #000000",
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                    FontSize = 13,
                    FontFamily = new FontFamily("Consolas"),
                    Margin = new Thickness(0, 4, 0, 0)
                };

                colorInfoStack.Children.Add(swatchBorder);
                colorInfoStack.Children.Add(rgbText);
                colorInfoStack.Children.Add(hexText);
                colorInfoBorder.Child = colorInfoStack;

                canvas.Children.Add(loupeBorder);
                canvas.Children.Add(colorInfoBorder);
                captureWindow.Content = canvas;

                // Mouse move for real-time preview
                captureWindow.PreviewMouseMove += (s, args) =>
                {
                    var pos = args.GetPosition(captureWindow);
                    var screenPoint = captureWindow.PointToScreen(pos);

                    // Check if within constraint bounds
                    if (constraintBounds.HasValue)
                    {
                        if (!constraintBounds.Value.Contains(screenPoint))
                        {
                            // Hide preview if outside bounds
                            loupeBorder.Visibility = Visibility.Collapsed;
                            colorInfoBorder.Visibility = Visibility.Collapsed;
                            captureWindow.Cursor = Cursors.No;
                            return;
                        }
                        else
                        {
                            // Show preview if inside bounds
                            loupeBorder.Visibility = Visibility.Visible;
                            colorInfoBorder.Visibility = Visibility.Visible;
                            captureWindow.Cursor = Cursors.Cross;
                        }
                    }

                    // Get current pixel color
                    currentColor = GetPixelColor((int)screenPoint.X, (int)screenPoint.Y);

                    // Update displays
                    magnifiedColor.Fill = new SolidColorBrush(currentColor);
                    colorSwatch.Fill = new SolidColorBrush(currentColor);
                    rgbText.Text = $"RGB: {currentColor.R}, {currentColor.G}, {currentColor.B}";
                    hexText.Text = $"HEX: #{currentColor.R:X2}{currentColor.G:X2}{currentColor.B:X2}";

                    // Position loupe (offset from cursor)
                    var loupeX = pos.X + 30;
                    var loupeY = pos.Y - loupeSize - 30;

                    // Keep on screen
                    if (loupeX + loupeSize > SystemParameters.VirtualScreenWidth)
                        loupeX = pos.X - loupeSize - 30;
                    if (loupeY < 0)
                        loupeY = pos.Y + 30;

                    Canvas.SetLeft(loupeBorder, loupeX);
                    Canvas.SetTop(loupeBorder, loupeY);
                    loupeBorder.Visibility = Visibility.Visible;

                    // Position color info
                    var infoX = pos.X + 30;
                    var infoY = pos.Y + 30;

                    if (infoX + 240 > SystemParameters.VirtualScreenWidth)
                        infoX = pos.X - 240;
                    if (infoY + 140 > SystemParameters.VirtualScreenHeight)
                        infoY = pos.Y - 140;

                    Canvas.SetLeft(colorInfoBorder, infoX);
                    Canvas.SetTop(colorInfoBorder, infoY);
                    colorInfoBorder.Visibility = Visibility.Visible;
                };

                // Click to capture
                captureWindow.PreviewMouseLeftButtonDown += (s, args) =>
                {
                    var pos = args.GetPosition(captureWindow);
                    var screenPoint = captureWindow.PointToScreen(pos);

                    // Check if within constraint bounds
                    if (constraintBounds.HasValue)
                    {
                        if (!constraintBounds.Value.Contains(screenPoint))
                        {
                            // Don't capture if outside bounds
                            return;
                        }
                    }

                    colorCaptured = true;
                    captureWindow.Close();
                };

                // ESC to cancel
                captureWindow.PreviewKeyDown += (s, args) =>
                {
                    if (args.Key == Key.Escape)
                    {
                        captureWindow.Close();
                    }
                };

                // Handle close
                captureWindow.Closed += (s, args) =>
                {
                    // Use BeginInvoke to ensure we're not in the middle of window closing
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (colorCaptured)
                        {
                            // Apply color to dialog
                            SetSelectedColor(currentColor);
                            // Show dialog briefly to set DialogResult
                            this.Visibility = Visibility.Hidden;
                            this.Show();
                            DialogResult = true;
                            this.Close();
                        }
                        else
                        {
                            // Restore dialog position and show
                            this.Left = dialogLeft;
                            this.Top = dialogTop;
                            this.Show();
                            this.Activate();
                        }
                    }));
                };

                captureWindow.Show();
                captureWindow.Activate();
            }
            catch (Exception ex)
            {
                this.Show();
                MessageBox.Show($"Error using eyedropper: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            IntPtr hdc = GetDC(IntPtr.Zero);
            uint pixel = GetPixel(hdc, x, y);
            ReleaseDC(IntPtr.Zero, hdc);

            byte r = (byte)(pixel & 0xFF);
            byte g = (byte)((pixel >> 8) & 0xFF);
            byte b = (byte)((pixel >> 16) & 0xFF);

            return Color.FromRgb(r, g, b);
        }

        // HSV/RGB conversion utilities
        private Color HSVToRGB(double hue, double saturation, double brightness)
        {
            double r, g, b;

            if (saturation == 0)
            {
                r = g = b = brightness;
            }
            else
            {
                hue = hue / 60.0;
                int i = (int)Math.Floor(hue);
                double f = hue - i;
                double p = brightness * (1 - saturation);
                double q = brightness * (1 - saturation * f);
                double t = brightness * (1 - saturation * (1 - f));

                switch (i)
                {
                    case 0: r = brightness; g = t; b = p; break;
                    case 1: r = q; g = brightness; b = p; break;
                    case 2: r = p; g = brightness; b = t; break;
                    case 3: r = p; g = q; b = brightness; break;
                    case 4: r = t; g = p; b = brightness; break;
                    default: r = brightness; g = p; b = q; break;
                }
            }

            return Color.FromRgb(
                (byte)Math.Round(r * 255),
                (byte)Math.Round(g * 255),
                (byte)Math.Round(b * 255)
            );
        }

        private void RGBToHSV(byte r, byte g, byte b, out double hue, out double saturation, out double brightness)
        {
            double rd = r / 255.0;
            double gd = g / 255.0;
            double bd = b / 255.0;

            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            double delta = max - min;

            brightness = max;

            if (max == 0)
            {
                saturation = 0;
            }
            else
            {
                saturation = delta / max;
            }

            if (delta == 0)
            {
                hue = 0;
            }
            else
            {
                if (max == rd)
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

        /// <summary>
        /// Show the color picker dialog and return the selected color
        /// </summary>
        /// <param name="owner">Parent window</param>
        /// <param name="title">Dialog title</param>
        /// <param name="initialColor">Initial color to display</param>
        /// <returns>Selected color, or null if cancelled</returns>
        public static Color? ShowDialog(Window owner, string title, Color initialColor, FrameworkElement constrainToElement = null)
        {
            var dialog = new SimpleColorPickerDialog(title, initialColor)
            {
                Owner = owner
            };
            dialog._constrainToElement = constrainToElement;

            bool? result = dialog.ShowDialog();

            if (result == true)
            {
                return dialog.SelectedColor;
            }

            return null;
        }
    }
}