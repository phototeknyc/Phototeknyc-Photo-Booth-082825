using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Photobooth.Controls
{
    public partial class HSVColorPicker : UserControl
    {
        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register("SelectedColor", typeof(Color), typeof(HSVColorPicker),
                new PropertyMetadata(Colors.Red, OnSelectedColorChanged));

        public Color SelectedColor
        {
            get { return (Color)GetValue(SelectedColorProperty); }
            set { SetValue(SelectedColorProperty, value); }
        }

        public event EventHandler<Color> ColorChanged;

        private bool _isUpdating = false;
        private double _hue = 0;
        private double _saturation = 1;
        private double _value = 1;

        public HSVColorPicker()
        {
            InitializeComponent();
            InitializeColorWheel();
            InitializeNamedColors();
            InitializeColorPalette();
            UpdateColorWheel();
            
            // Mouse events for color wheel interaction
            ColorWheelCanvas.MouseLeftButtonDown += ColorWheelCanvas_MouseLeftButtonDown;
            ColorWheelCanvas.MouseMove += ColorWheelCanvas_MouseMove;
        }

        private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HSVColorPicker picker && !picker._isUpdating)
            {
                picker.UpdateFromSelectedColor((Color)e.NewValue);
            }
        }

        private void InitializeColorWheel()
        {
            // Create the HSV color wheel using a custom drawing
            DrawColorWheel();
        }

        private void DrawColorWheel()
        {
            // Create radial gradient for hue wheel
            var gradientStops = new GradientStopCollection();
            for (int i = 0; i <= 360; i += 30)
            {
                var color = HSVToColor(i, 1, 1);
                gradientStops.Add(new GradientStop(color, i / 360.0));
            }

            var radialBrush = new RadialGradientBrush(gradientStops)
            {
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };

            ColorWheel.Fill = radialBrush;
        }

        private void InitializeNamedColors()
        {
            var namedColors = new Dictionary<string, Color>
            {
                {"White", Colors.White},
                {"Light Gray", Colors.LightGray},
                {"Gray", Colors.Gray},
                {"Dark Gray", Colors.DarkGray},
                {"Black", Colors.Black},
                {"Red", Colors.Red},
                {"Brown", Colors.Brown},
                {"Orange", Colors.Orange},
                {"Gold", Colors.Gold},
                {"Yellow", Colors.Yellow},
                {"Chartreuse", Colors.Chartreuse},
                {"Green", Colors.Green},
                {"DarkGreen", Colors.DarkGreen},
                {"SeaGreen", Colors.SeaGreen},
                {"Aquamarine", Colors.Aquamarine},
                {"Cyan", Colors.Cyan},
                {"Turquoise", Colors.Turquoise},
                {"Lavender", Colors.Lavender},
                {"Blue", Colors.Blue},
                {"Dark Blue", Colors.DarkBlue},
                {"Purple", Colors.Purple},
                {"Magenta", Colors.Magenta},
                {"Violet", Colors.Violet},
                {"Pink", Colors.Pink}
            };

            foreach (var namedColor in namedColors)
            {
                var button = new Button
                {
                    Height = 20,
                    Margin = new Thickness(2, 1, 2, 1),
                    Background = new SolidColorBrush(namedColor.Value),
                    BorderThickness = new Thickness(1, 1, 1, 1),
                    BorderBrush = Brushes.Gray,
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new Rectangle
                            {
                                Width = 16,
                                Height = 16,
                                Fill = new SolidColorBrush(namedColor.Value),
                                Stroke = Brushes.Black,
                                StrokeThickness = 1,
                                Margin = new Thickness(2, 0, 5, 0)
                            },
                            new TextBlock
                            {
                                Text = namedColor.Key,
                                FontSize = 10,
                                VerticalAlignment = VerticalAlignment.Center,
                                Foreground = GetContrastColor(namedColor.Value)
                            }
                        }
                    },
                    Tag = namedColor.Value
                };

                button.Click += (s, e) =>
                {
                    var btn = s as Button;
                    if (btn?.Tag is Color color)
                    {
                        SelectedColor = color;
                    }
                };

                NamedColorsList.Children.Add(button);
            }
        }

        private void InitializeColorPalette()
        {
            // Common color palette
            var paletteColors = new[]
            {
                Colors.Black, Colors.White, Colors.Red, Colors.Lime, Colors.Blue,
                Colors.Yellow, Colors.Cyan, Colors.Magenta, Colors.Silver, Colors.Gray,
                Colors.Maroon, Colors.Olive, Colors.Green, Colors.Purple, Colors.Teal,
                Colors.Navy, Colors.Orange, Colors.Pink, Colors.Gold, Colors.Brown
            };

            foreach (var color in paletteColors)
            {
                var button = new Button
                {
                    Width = 16,
                    Height = 16,
                    Background = new SolidColorBrush(color),
                    BorderThickness = new Thickness(1, 1, 1, 1),
                    BorderBrush = Brushes.Gray,
                    Margin = new Thickness(1, 1, 1, 1),
                    Tag = color,
                    ToolTip = color.ToString()
                };

                button.Click += (s, e) =>
                {
                    var btn = s as Button;
                    if (btn?.Tag is Color paletteColor)
                    {
                        SelectedColor = paletteColor;
                    }
                };

                ColorPaletteStrip.Children.Add(button);
            }
        }

        private Brush GetContrastColor(Color backgroundColor)
        {
            // Calculate luminance to determine if text should be black or white
            double luminance = 0.299 * backgroundColor.R + 0.587 * backgroundColor.G + 0.114 * backgroundColor.B;
            return luminance > 128 ? Brushes.Black : Brushes.White;
        }

        private void ColorWheelCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var position = e.GetPosition(ColorWheelCanvas);
            UpdateColorFromPosition(position);
            ColorWheelCanvas.CaptureMouse();
        }

        private void ColorWheelCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && ColorWheelCanvas.IsMouseCaptured)
            {
                var position = e.GetPosition(ColorWheelCanvas);
                UpdateColorFromPosition(position);
            }
        }

        private void UpdateColorFromPosition(Point position)
        {
            double centerX = 150;
            double centerY = 150;
            double dx = position.X - centerX;
            double dy = position.Y - centerY;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            // Check if click is in the outer ring (hue selection)
            if (distance >= 75 && distance <= 150)
            {
                _hue = (Math.Atan2(dy, dx) * 180.0 / Math.PI + 360) % 360;
                UpdateColorWheel();
                UpdateSelectedColor();
            }
            // Check if click is in the inner square (saturation/brightness)
            else if (position.X >= 75 && position.X <= 225 && position.Y >= 75 && position.Y <= 225)
            {
                _saturation = (position.X - 75) / 150.0;
                _value = 1.0 - (position.Y - 75) / 150.0;
                _saturation = Math.Max(0, Math.Min(1, _saturation));
                _value = Math.Max(0, Math.Min(1, _value));
                UpdateColorWheel();
                UpdateSelectedColor();
            }
        }

        private void UpdateColorWheel()
        {
            // Update saturation/brightness square with current hue
            var hueColor = HSVToColor(_hue, 1, 1);
            var gradient = new LinearGradientBrush();
            gradient.StartPoint = new Point(0, 0);
            gradient.EndPoint = new Point(1, 0);
            gradient.GradientStops.Add(new GradientStop(Colors.White, 0));
            gradient.GradientStops.Add(new GradientStop(hueColor, 1));

            var overlay = new LinearGradientBrush();
            overlay.StartPoint = new Point(0, 0);
            overlay.EndPoint = new Point(0, 1);
            overlay.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            overlay.GradientStops.Add(new GradientStop(Colors.Black, 1));

            // Create combined gradient
            SaturationBrightnessSquare.Fill = gradient;

            // Update selector positions
            double angle = _hue * Math.PI / 180.0;
            double radius = 112.5; // Middle of the hue ring
            Canvas.SetLeft(HueSelector, 150 + radius * Math.Cos(angle) - 4);
            Canvas.SetTop(HueSelector, 150 + radius * Math.Sin(angle) - 4);

            Canvas.SetLeft(SatBrightSelector, 75 + _saturation * 150 - 4);
            Canvas.SetTop(SatBrightSelector, 75 + (1 - _value) * 150 - 4);
        }

        private void UpdateSelectedColor()
        {
            _isUpdating = true;
            var newColor = HSVToColor(_hue, _saturation, _value);
            SelectedColor = newColor;
            CurrentColorPreview.Fill = new SolidColorBrush(newColor);

            // Update text boxes
            HueTextBox.Text = ((int)_hue).ToString();
            SaturationTextBox.Text = ((int)(_saturation * 100)).ToString();
            ValueTextBox.Text = ((int)(_value * 100)).ToString();
            RedTextBox.Text = newColor.R.ToString();
            GreenTextBox.Text = newColor.G.ToString();
            BlueTextBox.Text = newColor.B.ToString();

            ColorChanged?.Invoke(this, newColor);
            _isUpdating = false;
        }

        private void UpdateFromSelectedColor(Color color)
        {
            _isUpdating = true;
            var hsv = ColorToHSV(color);
            _hue = hsv.H;
            _saturation = hsv.S;
            _value = hsv.V;

            UpdateColorWheel();
            CurrentColorPreview.Fill = new SolidColorBrush(color);

            // Update text boxes
            HueTextBox.Text = ((int)_hue).ToString();
            SaturationTextBox.Text = ((int)(_saturation * 100)).ToString();
            ValueTextBox.Text = ((int)(_value * 100)).ToString();
            RedTextBox.Text = color.R.ToString();
            GreenTextBox.Text = color.G.ToString();
            BlueTextBox.Text = color.B.ToString();
            _isUpdating = false;
        }

        private Color HSVToColor(double hue, double saturation, double value)
        {
            int hi = (int)(hue / 60) % 6;
            double f = hue / 60 - hi;
            double p = value * (1 - saturation);
            double q = value * (1 - f * saturation);
            double t = value * (1 - (1 - f) * saturation);

            double r, g, b;
            switch (hi)
            {
                case 0: r = value; g = t; b = p; break;
                case 1: r = q; g = value; b = p; break;
                case 2: r = p; g = value; b = t; break;
                case 3: r = p; g = q; b = value; break;
                case 4: r = t; g = p; b = value; break;
                case 5: r = value; g = p; b = q; break;
                default: r = g = b = 0; break;
            }

            return Color.FromRgb(
                (byte)(r * 255),
                (byte)(g * 255),
                (byte)(b * 255));
        }

        private (double H, double S, double V) ColorToHSV(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            double h = 0;
            if (delta != 0)
            {
                if (max == r) h = ((g - b) / delta) % 6;
                else if (max == g) h = (b - r) / delta + 2;
                else h = (r - g) / delta + 4;
                h *= 60;
                if (h < 0) h += 360;
            }

            double s = max == 0 ? 0 : delta / max;
            double v = max;

            return (h, s, v);
        }

        // Text box event handlers
        private void HueTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isUpdating && double.TryParse(HueTextBox.Text, out double hue))
            {
                _hue = Math.Max(0, Math.Min(360, hue));
                UpdateColorWheel();
                UpdateSelectedColor();
            }
        }

        private void SaturationTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isUpdating && double.TryParse(SaturationTextBox.Text, out double saturation))
            {
                _saturation = Math.Max(0, Math.Min(100, saturation)) / 100.0;
                UpdateColorWheel();
                UpdateSelectedColor();
            }
        }

        private void ValueTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isUpdating && double.TryParse(ValueTextBox.Text, out double value))
            {
                _value = Math.Max(0, Math.Min(100, value)) / 100.0;
                UpdateColorWheel();
                UpdateSelectedColor();
            }
        }

        private void RedTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isUpdating && byte.TryParse(RedTextBox.Text, out byte red))
            {
                var color = Color.FromRgb(red, SelectedColor.G, SelectedColor.B);
                SelectedColor = color;
            }
        }

        private void GreenTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isUpdating && byte.TryParse(GreenTextBox.Text, out byte green))
            {
                var color = Color.FromRgb(SelectedColor.R, green, SelectedColor.B);
                SelectedColor = color;
            }
        }

        private void BlueTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isUpdating && byte.TryParse(BlueTextBox.Text, out byte blue))
            {
                var color = Color.FromRgb(SelectedColor.R, SelectedColor.G, blue);
                SelectedColor = color;
            }
        }
    }
}