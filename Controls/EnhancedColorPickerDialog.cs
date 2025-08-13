using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Photobooth.Controls
{
    public class EnhancedColorPickerDialog : Window
    {
        public Color SelectedColor { get; private set; } = Colors.Black;
        
        private Rectangle _currentColorPreview;
        private Slider _hueSlider;
        private Slider _saturationSlider;
        private Slider _brightnessSlider;
        private Slider _redSlider;
        private Slider _greenSlider;
        private Slider _blueSlider;
        private TextBlock _hueValue;
        private TextBlock _saturationValue;
        private TextBlock _brightnessValue;
        private TextBlock _redValue;
        private TextBlock _greenValue;
        private TextBlock _blueValue;
        
        private bool _isUpdating = false;

        public EnhancedColorPickerDialog(string title, Color initialColor)
        {
            Title = title;
            Width = 700;
            Height = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(128, 128, 128));
            ResizeMode = ResizeMode.NoResize;
            
            SelectedColor = initialColor;
            CreateContent();
            UpdateFromColor(initialColor);
        }

        private void CreateContent()
        {
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Named colors panel
            CreateNamedColorsPanel(mainGrid);
            
            // Color adjustment panel
            CreateColorAdjustmentPanel(mainGrid);
            
            // Values panel
            CreateValuesPanel(mainGrid);
            
            // Buttons
            CreateButtonPanel(mainGrid);
            
            Content = mainGrid;
        }

        private void CreateNamedColorsPanel(Grid parent)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(184, 184, 184)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(153, 153, 153)),
                BorderThickness = new Thickness(1, 1, 1, 1),
                Margin = new Thickness(5, 5, 5, 5)
            };
            Grid.SetColumn(border, 0);
            Grid.SetRow(border, 0);

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var stackPanel = new StackPanel { Margin = new Thickness(5, 5, 5, 5) };

            var namedColors = new Dictionary<string, Color>
            {
                {"White", Colors.White}, {"Light Gray", Colors.LightGray}, {"Gray", Colors.Gray},
                {"Dark Gray", Colors.DarkGray}, {"Black", Colors.Black}, {"Red", Colors.Red},
                {"Brown", Colors.Brown}, {"Orange", Colors.Orange}, {"Gold", Colors.Gold},
                {"Yellow", Colors.Yellow}, {"Chartreuse", Colors.Chartreuse}, {"Green", Colors.Green},
                {"DarkGreen", Colors.DarkGreen}, {"SeaGreen", Colors.SeaGreen}, {"Aquamarine", Colors.Aquamarine},
                {"Cyan", Colors.Cyan}, {"Turquoise", Colors.Turquoise}, {"Lavender", Colors.Lavender},
                {"Blue", Colors.Blue}, {"Dark Blue", Colors.DarkBlue}, {"Purple", Colors.Purple},
                {"Magenta", Colors.Magenta}, {"Violet", Colors.Violet}, {"Pink", Colors.Pink}
            };

            foreach (var namedColor in namedColors)
            {
                var button = new Button
                {
                    Height = 20,
                    Margin = new Thickness(2, 1, 2, 1),
                    Background = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                    BorderThickness = new Thickness(1, 1, 1, 1),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                    Tag = namedColor.Value,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };

                var contentPanel = new StackPanel { Orientation = Orientation.Horizontal };
                
                var colorRect = new Rectangle
                {
                    Width = 16,
                    Height = 16,
                    Fill = new SolidColorBrush(namedColor.Value),
                    Stroke = new SolidColorBrush(Colors.Black),
                    StrokeThickness = 1,
                    Margin = new Thickness(2, 0, 5, 0)
                };
                
                var textBlock = new TextBlock
                {
                    Text = namedColor.Key,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Colors.Black)
                };

                contentPanel.Children.Add(colorRect);
                contentPanel.Children.Add(textBlock);
                button.Content = contentPanel;

                button.Click += (s, e) =>
                {
                    if ((s as Button)?.Tag is Color color)
                    {
                        UpdateFromColor(color);
                    }
                };

                stackPanel.Children.Add(button);
            }

            scrollViewer.Content = stackPanel;
            border.Child = scrollViewer;
            parent.Children.Add(border);
        }

        private void CreateColorAdjustmentPanel(Grid parent)
        {
            var mainPanel = new StackPanel { Margin = new Thickness(10, 10, 10, 10) };
            Grid.SetColumn(mainPanel, 1);
            Grid.SetRow(mainPanel, 0);

            // Current color preview
            var previewLabel = new TextBlock
            {
                Text = "Selected Color:",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _currentColorPreview = new Rectangle
            {
                Width = 100,
                Height = 60,
                Stroke = new SolidColorBrush(Colors.Black),
                StrokeThickness = 2,
                Margin = new Thickness(0, 0, 0, 20),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            mainPanel.Children.Add(previewLabel);
            mainPanel.Children.Add(_currentColorPreview);

            // HSB Sliders
            var hsbLabel = new TextBlock
            {
                Text = "HSB Adjustments:",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            mainPanel.Children.Add(hsbLabel);

            _hueSlider = CreateSlider("Hue:", 0, 360, Colors.Red, out _hueValue);
            _saturationSlider = CreateSlider("Saturation:", 0, 100, Colors.Gray, out _saturationValue);
            _brightnessSlider = CreateSlider("Brightness:", 0, 100, Colors.White, out _brightnessValue);

            mainPanel.Children.Add(_hueSlider.Parent as FrameworkElement);
            mainPanel.Children.Add(_saturationSlider.Parent as FrameworkElement);
            mainPanel.Children.Add(_brightnessSlider.Parent as FrameworkElement);

            _hueSlider.ValueChanged += HSBSlider_ValueChanged;
            _saturationSlider.ValueChanged += HSBSlider_ValueChanged;
            _brightnessSlider.ValueChanged += HSBSlider_ValueChanged;

            // Color palette
            var paletteLabel = new TextBlock
            {
                Text = "Quick Colors:",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 15, 0, 5)
            };
            mainPanel.Children.Add(paletteLabel);

            var palettePanel = new WrapPanel();
            var quickColors = new[]
            {
                Colors.Black, Colors.White, Colors.Red, Colors.Lime, Colors.Blue,
                Colors.Yellow, Colors.Cyan, Colors.Magenta, Colors.Silver, Colors.Gray,
                Colors.Maroon, Colors.Olive, Colors.Green, Colors.Purple, Colors.Teal,
                Colors.Navy, Colors.Orange, Colors.Pink, Colors.Gold, Colors.Brown
            };

            foreach (var color in quickColors)
            {
                var colorButton = new Button
                {
                    Width = 20,
                    Height = 20,
                    Background = new SolidColorBrush(color),
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    BorderThickness = new Thickness(1, 1, 1, 1),
                    Margin = new Thickness(2, 2, 2, 2),
                    Tag = color
                };

                colorButton.Click += (s, e) =>
                {
                    if ((s as Button)?.Tag is Color paletteColor)
                    {
                        UpdateFromColor(paletteColor);
                    }
                };

                palettePanel.Children.Add(colorButton);
            }

            mainPanel.Children.Add(palettePanel);
            parent.Children.Add(mainPanel);
        }

        private void CreateValuesPanel(Grid parent)
        {
            var mainPanel = new StackPanel
            {
                Margin = new Thickness(5, 5, 5, 5),
                Background = new SolidColorBrush(Color.FromRgb(184, 184, 184))
            };
            Grid.SetColumn(mainPanel, 2);
            Grid.SetRow(mainPanel, 0);

            // RGB Sliders
            var rgbBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(153, 153, 153)),
                BorderThickness = new Thickness(1, 1, 1, 1),
                Background = new SolidColorBrush(Colors.White),
                Margin = new Thickness(5, 5, 5, 5)
            };

            var rgbPanel = new StackPanel { Margin = new Thickness(10, 10, 10, 10) };

            var rgbLabel = new TextBlock
            {
                Text = "RGB Values",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            rgbPanel.Children.Add(rgbLabel);

            _redSlider = CreateSlider("R:", 0, 255, Colors.Red, out _redValue);
            _greenSlider = CreateSlider("G:", 0, 255, Colors.Green, out _greenValue);
            _blueSlider = CreateSlider("B:", 0, 255, Colors.Blue, out _blueValue);

            rgbPanel.Children.Add(_redSlider.Parent as FrameworkElement);
            rgbPanel.Children.Add(_greenSlider.Parent as FrameworkElement);
            rgbPanel.Children.Add(_blueSlider.Parent as FrameworkElement);

            _redSlider.ValueChanged += RGBSlider_ValueChanged;
            _greenSlider.ValueChanged += RGBSlider_ValueChanged;
            _blueSlider.ValueChanged += RGBSlider_ValueChanged;

            rgbBorder.Child = rgbPanel;
            mainPanel.Children.Add(rgbBorder);
            parent.Children.Add(mainPanel);
        }

        private Slider CreateSlider(string label, double min, double max, Color color, out TextBlock valueLabel)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

            var labelBlock = new TextBlock
            {
                Text = label,
                Width = 25,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 10
            };

            var slider = new Slider
            {
                Width = 60,
                Height = 20,
                Minimum = min,
                Maximum = max,
                Background = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                Foreground = new SolidColorBrush(color)
            };

            valueLabel = new TextBlock
            {
                Text = "0",
                Width = 25,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 10,
                Margin = new Thickness(5, 0, 0, 0)
            };

            panel.Children.Add(labelBlock);
            panel.Children.Add(slider);
            panel.Children.Add(valueLabel);

            // Store the panel as the slider's parent for retrieval
            slider.Tag = panel;

            return slider;
        }

        private void CreateButtonPanel(Grid parent)
        {
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10, 10, 10, 10)
            };
            Grid.SetColumn(buttonPanel, 0);
            Grid.SetColumnSpan(buttonPanel, 3);
            Grid.SetRow(buttonPanel, 1);

            var okButton = new Button
            {
                Content = "OK",
                Width = 75,
                Height = 25,
                Margin = new Thickness(5, 5, 5, 5),
                Background = new SolidColorBrush(Color.FromRgb(225, 225, 225)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(171, 173, 179))
            };
            okButton.Click += (s, e) => { DialogResult = true; Close(); };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 75,
                Height = 25,
                Margin = new Thickness(5, 5, 5, 5),
                Background = new SolidColorBrush(Color.FromRgb(225, 225, 225)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(171, 173, 179))
            };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            parent.Children.Add(buttonPanel);
        }

        private void HSBSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdating) return;

            var hue = _hueSlider.Value;
            var saturation = _saturationSlider.Value / 100.0;
            var brightness = _brightnessSlider.Value / 100.0;

            var color = HSVToColor(hue, saturation, brightness);
            UpdateFromColor(color, false);
        }

        private void RGBSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdating) return;

            var red = (byte)_redSlider.Value;
            var green = (byte)_greenSlider.Value;
            var blue = (byte)_blueSlider.Value;

            var color = Color.FromRgb(red, green, blue);
            UpdateFromColor(color, false);
        }

        private void UpdateFromColor(Color color, bool updateSliders = true)
        {
            _isUpdating = true;
            SelectedColor = color;
            _currentColorPreview.Fill = new SolidColorBrush(color);

            if (updateSliders)
            {
                var hsv = ColorToHSV(color);
                _hueSlider.Value = hsv.H;
                _saturationSlider.Value = hsv.S * 100;
                _brightnessSlider.Value = hsv.V * 100;

                _redSlider.Value = color.R;
                _greenSlider.Value = color.G;
                _blueSlider.Value = color.B;
            }

            // Update value labels
            _hueValue.Text = ((int)_hueSlider.Value).ToString();
            _saturationValue.Text = ((int)_saturationSlider.Value).ToString();
            _brightnessValue.Text = ((int)_brightnessSlider.Value).ToString();
            _redValue.Text = color.R.ToString();
            _greenValue.Text = color.G.ToString();
            _blueValue.Text = color.B.ToString();

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

            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
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
    }
}