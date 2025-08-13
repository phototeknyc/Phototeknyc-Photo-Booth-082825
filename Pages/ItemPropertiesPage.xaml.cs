using DesignerCanvas;
using Photobooth.MVVM.ViewModels.Designer;
using Photobooth.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Photobooth.Pages
{
	/// <summary>
	/// Interaction logic for Properties.xaml
	/// </summary>
	public partial class ItemPropertiesPage : Page
	{
		private DesignerVM ViewModel => MainPage.Instance?.ViewModel;
		private IBoxCanvasItem canvasItem;

		public int ItemSizeX
		{
			get { return int.Parse(tbSizeX.Text); }
			set { tbSizeX.Text = value.ToString(); }
		}

		public int ItemSizeY
		{
			get { return int.Parse(tbSizeY.Text); }
			set { tbSizeY.Text = value.ToString(); }
		}

		public int ItemRatioX
		{
			get { return int.Parse(tbRatioX.Text); }
			set { tbRatioX.Text = value.ToString(); }
		}

		public int ItemRatioY
		{
			get { return int.Parse(tbRatioY.Text); }
			set { tbRatioY.Text = value.ToString(); }
		}

		private static ItemPropertiesPage _instance;
		public static ItemPropertiesPage Instance
		{
			get
			{
				if (_instance == null)
				{
					_instance = new ItemPropertiesPage();
				}
				return _instance;
			}
		}

		public IBoxCanvasItem CanvasItem
		{
			get { return canvasItem; }
			set
			{
				canvasItem = value;
				if (canvasItem != null)
				{
					// show location and size of new item
					tbSizeX.Text = canvasItem.Width.ToString();
					tbSizeY.Text = canvasItem.Height.ToString();

					tbRatioX.Text = canvasItem.Top.ToString();
					tbRatioY.Text = canvasItem.Left.ToString();
				}
			}
		}

		public ItemPropertiesPage()
		{
			InitializeComponent();
			Loaded += (s, e) => 
			{
				DataContext = ViewModel;
				if (ViewModel != null)
				{
					ViewModel.PropertyChanged += ViewModel_PropertyChanged;
				}
			};
			_instance = this;
		}

		private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(DesignerVM.SelectedTextItem))
			{
				// Update text formatting panel visibility
				if (TextFormattingPanel != null)
				{
					TextFormattingPanel.Visibility = ViewModel.SelectedTextItem != null ? Visibility.Visible : Visibility.Collapsed;
				}
			}
			else if (e.PropertyName == nameof(DesignerVM.SelectedPlaceholderItem))
			{
				// Update placeholder panel visibility
				if (PlaceholderPanel != null)
				{
					PlaceholderPanel.Visibility = ViewModel.SelectedPlaceholderItem != null ? Visibility.Visible : Visibility.Collapsed;
				}
			}
			else if (e.PropertyName == nameof(DesignerVM.SelectedShapeItem))
			{
				// Update shape properties panel visibility
				if (ShapePropertiesPanel != null)
				{
					ShapePropertiesPanel.Visibility = ViewModel.SelectedShapeItem != null ? Visibility.Visible : Visibility.Collapsed;
				}
			}
			
			// Show aspect ratio panel only for images and placeholders
			if (e.PropertyName == "SelectedItems" || e.PropertyName == "IsRightSidebarVisible")
			{
				UpdateAspectRatioPanelVisibility();
			}
		}

		private void UpdateAspectRatioPanelVisibility()
		{
			if (AspectRatioPanel != null && ViewModel?.CustomDesignerCanvas?.SelectedItems != null)
			{
				// Check if any selected item is an image or placeholder
				var hasResizableItem = ViewModel.CustomDesignerCanvas.SelectedItems
					.Any(item => item is DesignerCanvas.ImageCanvasItem || item is DesignerCanvas.PlaceholderCanvasItem);
				
				AspectRatioPanel.Visibility = hasResizableItem ? Visibility.Visible : Visibility.Collapsed;
			}
		}

		private void ToggleLockPosition_Click(object sender, MouseButtonEventArgs e)
		{
			ViewModel?.ToggleLockPositionCmd.Execute(null);
		}

		private void ToggleLockSize_Click(object sender, MouseButtonEventArgs e)
		{
			ViewModel?.ToggleLockSizeCmd.Execute(null);
		}

		private void ToggleLockAspectRatio_Click(object sender, MouseButtonEventArgs e)
		{
			ViewModel?.ToggleLockAspectRatioCmd.Execute(null);
		}

		private void StackPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			ToggleLock(image, tbRatioX, tbRatioY);
			// check if lock is closed then stop then selected item from moving
			if (image.Source.ToString().Contains("/images/lock.png"))
			{
				MainPage.Instance.dcvs.ChangeAspectRatio(true);
			}
			else
			{
				MainPage.Instance.dcvs.ChangeAspectRatio(false);
			}

		}

		private void ToggleLock(Image img, TextBox txtBox1, TextBox txtBox2)
		{
			string image1 = "/images/Padlock.png";
			string image2 = "/images/lock.png";

			if (img.Source.ToString().Contains(image1))
			{
				img.Source = new BitmapImage(new Uri(image2, UriKind.RelativeOrAbsolute));
				txtBox1.IsEnabled = false;
				txtBox2.IsEnabled = false;
			}
			else
			{
				img.Source = new BitmapImage(new Uri(image1, UriKind.RelativeOrAbsolute));
				txtBox1.IsEnabled = true;
				txtBox2.IsEnabled = true;
			}
		}

		private void StackPanel_MouseLeftButtonDown_1(object sender, MouseButtonEventArgs e)
		{
			ToggleLock(image8, tbSizeX, tbSizeY);
			// check if lock is closed then stop then selected item from moving
			if (image8.Source.ToString().Contains("/images/lock.png"))
			{
				MainPage.Instance.dcvs.LockSize(true);
			}
			else
			{
				MainPage.Instance.dcvs.LockSize(false);
			}
		}

		private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			MainPage.Instance.dcvs.BringToFront();
		}

		private void Image_MouseLeftButtonDown_1(object sender, MouseButtonEventArgs e)
		{
			MainPage.Instance.dcvs.SendToBack();
		}

		private void Image_MouseLeftButtonDown_2(object sender, MouseButtonEventArgs e)
		{
			MainPage.Instance.dcvs.AlignLeft();
		}

		private void Image_MouseLeftButtonDown_3(object sender, MouseButtonEventArgs e)
		{
			MainPage.Instance.dcvs.AlignRight();
		}

		private void Image_MouseLeftButtonDown_4(object sender, MouseButtonEventArgs e)
		{
			MainPage.Instance.dcvs.AlignTop();
		}

		private void Image_MouseLeftButtonDown_5(object sender, MouseButtonEventArgs e)
		{
			MainPage.Instance.dcvs.AlignCenter();
		}


        private void Image_MouseLeftButtonDown_8(object sender, MouseButtonEventArgs e)
        {
			MainPage.Instance.dcvs.AlignMiddle();
        }

        private void Image_MouseLeftButtonDown_6(object sender, MouseButtonEventArgs e)
		{
			MainPage.Instance.dcvs.DuplicateSelected();
		}

		private void Image_MouseLeftButtonDown_7(object sender, MouseButtonEventArgs e)
		{
			MainPage.Instance.dcvs.AlignBottom();
		}

		// Removed tbSizeW_TextChanged and tbSizeH_TextChanged since size fields are now read-only
		// The actual pixel dimensions are automatically calculated and displayed

		private void tbLocationX_TextChanged(object sender, TextChangedEventArgs e)
		{
			UpdateAspectRatio();
		}

		private void tbLocationY_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateAspectRatio();
        }

        private void UpdateAspectRatio()
        {
            try
            {
                double top = 0;
                double left = 0;
                if (tbRatioY != null && tbRatioY != null && tbRatioX.Text != "" && tbRatioY.Text != "")
                {
                    top = double.Parse(tbRatioX.Text);
                    left = double.Parse(tbRatioY.Text);
                    double[] aspectRatio = MainPage.Instance.dcvs.SetAspectRatioOfSelectedItems(top / left);
                    if (aspectRatio[0] != -1) tbSizeX.Text = aspectRatio[0].ToString();
                    if (aspectRatio[1] != -1) tbSizeY.Text = aspectRatio[1].ToString();
                }
            }
            catch (Exception)
            { }
        }

        private void ChooseTextColor_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedTextItem == null) return;

            // Get current color
            var currentColor = Colors.Black;
            if (ViewModel.SelectedTextItem.Foreground is SolidColorBrush brush)
            {
                currentColor = brush.Color;
            }

            // Create PixiEditor color dialog
            var parentWindow = Window.GetWindow(this);
            var selectedColor = PixiEditorColorPickerDialog.ShowDialog(parentWindow, "Choose Text Color", currentColor);
            if (selectedColor.HasValue)
            {
                ViewModel.SelectedTextItem.Foreground = new SolidColorBrush(selectedColor.Value);
            }
        }

        private void QuickColor_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedTextItem == null) return;

            var button = sender as Button;
            var colorName = button?.Tag?.ToString();
            if (!string.IsNullOrEmpty(colorName))
            {
                // Convert color name to brush
                var color = (Color)ColorConverter.ConvertFromString(colorName);
                ViewModel.SelectedTextItem.Foreground = new SolidColorBrush(color);
            }
        }

        private void QuickCanvasColor_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            var button = sender as Button;
            var colorName = button?.Tag?.ToString();
            if (!string.IsNullOrEmpty(colorName))
            {
                // Convert color name to brush
                var color = (Color)ColorConverter.ConvertFromString(colorName);
                ViewModel.CanvasBackgroundColor = new SolidColorBrush(color);
            }
        }

        private void ChooseOutlineColor_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedTextItem == null) return;

            // Get current outline color
            var currentColor = Colors.Black;
            if (ViewModel.SelectedTextItem.OutlineColor is SolidColorBrush brush)
            {
                currentColor = brush.Color;
            }

            // Create PixiEditor color dialog for outline color
            var parentWindow = Window.GetWindow(this);
            var selectedColor = PixiEditorColorPickerDialog.ShowDialog(parentWindow, "Choose Outline Color", currentColor);
            if (selectedColor.HasValue)
            {
                ViewModel.SelectedTextItem.OutlineColor = new SolidColorBrush(selectedColor.Value);
            }
        }

        private void QuickOutlineColor_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedTextItem == null) return;

            var button = sender as Button;
            var colorName = button?.Tag?.ToString();
            if (!string.IsNullOrEmpty(colorName))
            {
                // Convert color name to brush
                var color = (Color)ColorConverter.ConvertFromString(colorName);
                ViewModel.SelectedTextItem.OutlineColor = new SolidColorBrush(color);
            }
        }

        private void DecreasePlaceholderNumber_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedPlaceholderItem != null)
            {
                if (ViewModel.SelectedPlaceholderItem.PlaceholderNo > 1)
                {
                    ViewModel.SelectedPlaceholderItem.PlaceholderNo--;
                    // Update the color to match the new number
                    ViewModel.SelectedPlaceholderItem.Background = new SolidColorBrush(
                        GetColorForPlaceholder(ViewModel.SelectedPlaceholderItem.PlaceholderNo));
                }
            }
        }

        private void IncreasePlaceholderNumber_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedPlaceholderItem != null)
            {
                ViewModel.SelectedPlaceholderItem.PlaceholderNo++;
                // Update the color to match the new number
                ViewModel.SelectedPlaceholderItem.Background = new SolidColorBrush(
                    GetColorForPlaceholder(ViewModel.SelectedPlaceholderItem.PlaceholderNo));
            }
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            // Allow only numbers
            e.Handled = !int.TryParse(e.Text, out _);
        }

        // Helper method to get color for placeholder number (matching the palette in PlaceholderCanvasItem)
        private Color GetColorForPlaceholder(int placeholderNumber)
        {
            Color[] colorPalette = new Color[]
            {
                Color.FromRgb(255, 182, 193), // Light Pink
                Color.FromRgb(173, 216, 230), // Light Blue
                Color.FromRgb(144, 238, 144), // Light Green
                Color.FromRgb(255, 218, 185), // Peach
                Color.FromRgb(221, 160, 221), // Plum
                Color.FromRgb(255, 255, 224), // Light Yellow
                Color.FromRgb(176, 224, 230), // Powder Blue
                Color.FromRgb(255, 228, 196), // Bisque
                Color.FromRgb(216, 191, 216), // Thistle
                Color.FromRgb(240, 230, 140), // Khaki
                Color.FromRgb(255, 192, 203), // Pink
                Color.FromRgb(230, 230, 250), // Lavender
                Color.FromRgb(250, 240, 230), // Linen
                Color.FromRgb(255, 228, 225), // Misty Rose
                Color.FromRgb(224, 255, 255), // Light Cyan
                Color.FromRgb(240, 255, 240), // Honeydew
            };
            
            int colorIndex = (placeholderNumber - 1) % colorPalette.Length;
            return colorPalette[colorIndex];
        }

        // Shape color picker event handlers
        private void ChooseShapeFillColor_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedShapeItem == null) return;

            // Get current fill color
            var currentColor = Colors.LightBlue;
            if (ViewModel.SelectedShapeItem.Fill is SolidColorBrush brush)
            {
                currentColor = brush.Color;
            }

            // Create PixiEditor color dialog for fill color
            var parentWindow = Window.GetWindow(this);
            var selectedColor = PixiEditorColorPickerDialog.ShowDialog(parentWindow, "Choose Fill Color", currentColor);
            if (selectedColor.HasValue)
            {
                ViewModel.SelectedShapeItem.Fill = new SolidColorBrush(selectedColor.Value);
                ViewModel.SelectedShapeItem.HasNoFill = false; // Enable fill when color is chosen
            }
        }

        private void QuickShapeFillColor_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedShapeItem == null) return;

            var button = sender as Button;
            var colorName = button?.Tag?.ToString();
            if (!string.IsNullOrEmpty(colorName))
            {
                if (colorName == "Transparent")
                {
                    ViewModel.SelectedShapeItem.HasNoFill = true;
                }
                else
                {
                    // Convert color name to brush
                    var color = (Color)ColorConverter.ConvertFromString(colorName);
                    ViewModel.SelectedShapeItem.Fill = new SolidColorBrush(color);
                    ViewModel.SelectedShapeItem.HasNoFill = false;
                }
            }
        }

        private void ChooseShapeStrokeColor_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedShapeItem == null) return;

            // Get current stroke color
            var currentColor = Colors.DarkBlue;
            if (ViewModel.SelectedShapeItem.Stroke is SolidColorBrush brush)
            {
                currentColor = brush.Color;
            }

            // Create PixiEditor color dialog for stroke color
            var parentWindow = Window.GetWindow(this);
            var selectedColor = PixiEditorColorPickerDialog.ShowDialog(parentWindow, "Choose Stroke Color", currentColor);
            if (selectedColor.HasValue)
            {
                ViewModel.SelectedShapeItem.Stroke = new SolidColorBrush(selectedColor.Value);
                ViewModel.SelectedShapeItem.HasNoStroke = false; // Enable stroke when color is chosen
            }
        }

        private void QuickShapeStrokeColor_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedShapeItem == null) return;

            var button = sender as Button;
            var colorName = button?.Tag?.ToString();
            if (!string.IsNullOrEmpty(colorName))
            {
                if (colorName == "Transparent")
                {
                    ViewModel.SelectedShapeItem.HasNoStroke = true;
                }
                else
                {
                    // Convert color name to brush
                    var color = (Color)ColorConverter.ConvertFromString(colorName);
                    ViewModel.SelectedShapeItem.Stroke = new SolidColorBrush(color);
                    ViewModel.SelectedShapeItem.HasNoStroke = false;
                }
            }
        }

        private void ChooseShapeShadowColor_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedShapeItem == null) return;

            // Get current shadow color
            var currentColor = ViewModel.SelectedShapeItem.ShadowColor;

            // Create PixiEditor color dialog for shadow color
            var parentWindow = Window.GetWindow(this);
            var selectedColor = PixiEditorColorPickerDialog.ShowDialog(parentWindow, "Choose Shadow Color", currentColor);
            if (selectedColor.HasValue)
            {
                ViewModel.SelectedShapeItem.ShadowColor = selectedColor.Value;
            }
        }

        private void QuickShapeShadowColor_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedShapeItem == null) return;

            var button = sender as Button;
            var colorName = button?.Tag?.ToString();
            if (!string.IsNullOrEmpty(colorName))
            {
                // Convert color name to Color (not Brush for shadow)
                var color = (Color)ColorConverter.ConvertFromString(colorName);
                ViewModel.SelectedShapeItem.ShadowColor = color;
            }
        }
    }

    // Simple color picker dialog
    public class ColorPickerDialog : Window
    {
        public Color SelectedColor { get; private set; } = Colors.Black;

        public ColorPickerDialog(string title = "Choose Color")
        {
            Title = title;
            Width = 400;
            Height = 350;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(51, 51, 51));

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Current color preview
            var previewPanel = new StackPanel { Margin = new Thickness(10, 10, 10, 10) };
            var previewLabel = new TextBlock 
            { 
                Text = "Selected Color:", 
                Foreground = Brushes.White, 
                FontSize = 12, 
                Margin = new Thickness(0, 0, 0, 5) 
            };
            var previewBorder = new Border
            {
                Width = 60,
                Height = 30,
                BorderBrush = new SolidColorBrush(Color.FromRgb(204, 204, 204)),
                BorderThickness = new Thickness(1, 1, 1, 1),
                Background = new SolidColorBrush(SelectedColor),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            previewPanel.Children.Add(previewLabel);
            previewPanel.Children.Add(previewBorder);
            Grid.SetRow(previewPanel, 0);
            grid.Children.Add(previewPanel);

            // Enhanced color palette with more colors
            var colorPanel = new WrapPanel { Margin = new Thickness(10, 10, 10, 10) };
            var enhancedColors = new[]
            {
                // Basic colors
                Colors.Black, Colors.White, Colors.Red, Colors.Green, Colors.Blue,
                Colors.Yellow, Colors.Orange, Colors.Purple, Colors.Pink, Colors.Brown,
                Colors.Gray, Colors.LightGray, Colors.DarkRed, Colors.DarkGreen, Colors.DarkBlue,
                Colors.Gold, Colors.Silver, Colors.Cyan, Colors.Magenta, Colors.Lime,
                // Additional vibrant colors
                Colors.Crimson, Colors.DeepSkyBlue, Colors.ForestGreen, Colors.HotPink, Colors.Indigo,
                Colors.LawnGreen, Colors.MidnightBlue, Colors.OrangeRed, Colors.RoyalBlue, Colors.Tomato,
                Colors.Violet, Colors.YellowGreen, Colors.Coral, Colors.DarkOrange, Colors.DarkSlateBlue,
                Colors.Firebrick, Colors.Goldenrod, Colors.SeaGreen, Colors.SlateBlue, Colors.SteelBlue,
                // Light variants
                Colors.LightBlue, Colors.LightCoral, Colors.LightGreen, Colors.LightPink, Colors.LightSalmon,
                Colors.LightSeaGreen, Colors.LightSkyBlue, Colors.LightSlateGray, Colors.LightSteelBlue, Colors.LightYellow
            };

            foreach (var color in enhancedColors)
            {
                var colorButton = new Button
                {
                    Width = 24,
                    Height = 24,
                    Background = new SolidColorBrush(color),
                    Margin = new Thickness(1, 1, 1, 1),
                    BorderThickness = new Thickness(1, 1, 1, 1),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(102, 102, 102)),
                    Tag = color,
                    ToolTip = color.ToString()
                };
                colorButton.Click += (s, e) => 
                {
                    var btn = s as Button;
                    if (btn?.Tag is Color selectedColor)
                    {
                        SelectedColor = selectedColor;
                        previewBorder.Background = new SolidColorBrush(selectedColor);
                    }
                };
                colorPanel.Children.Add(colorButton);
            }

            Grid.SetRow(colorPanel, 1);
            grid.Children.Add(colorPanel);

            // RGB sliders
            var rgbPanel = new StackPanel { Margin = new Thickness(10, 10, 10, 10) };
            var rgbLabel = new TextBlock 
            { 
                Text = "Fine Tune (RGB):", 
                Foreground = Brushes.White, 
                FontSize = 12, 
                Margin = new Thickness(0, 0, 0, 5) 
            };
            rgbPanel.Children.Add(rgbLabel);

            var redSlider = CreateRGBSlider("Red", Colors.Red, 0, (value) => 
            {
                var color = SelectedColor;
                SelectedColor = Color.FromRgb((byte)value, color.G, color.B);
                previewBorder.Background = new SolidColorBrush(SelectedColor);
            });
            var greenSlider = CreateRGBSlider("Green", Colors.Green, 0, (value) => 
            {
                var color = SelectedColor;
                SelectedColor = Color.FromRgb(color.R, (byte)value, color.B);
                previewBorder.Background = new SolidColorBrush(SelectedColor);
            });
            var blueSlider = CreateRGBSlider("Blue", Colors.Blue, 0, (value) => 
            {
                var color = SelectedColor;
                SelectedColor = Color.FromRgb(color.R, color.G, (byte)value);
                previewBorder.Background = new SolidColorBrush(SelectedColor);
            });

            rgbPanel.Children.Add(redSlider);
            rgbPanel.Children.Add(greenSlider);
            rgbPanel.Children.Add(blueSlider);

            Grid.SetRow(rgbPanel, 2);
            grid.Children.Add(rgbPanel);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10, 10, 10, 10)
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 60,
                Height = 25,
                Margin = new Thickness(5, 0, 5, 0),
                Background = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                Foreground = Brushes.White
            };
            okButton.Click += (s, e) => { DialogResult = true; Close(); };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 60,
                Height = 25,
                Background = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                Foreground = Brushes.White
            };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, 3);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }

        private StackPanel CreateRGBSlider(string label, Color color, double value, Action<double> onValueChanged)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var labelBlock = new TextBlock 
            { 
                Text = label + ":", 
                Foreground = Brushes.White, 
                FontSize = 10, 
                Width = 35,
                VerticalAlignment = VerticalAlignment.Center
            };
            var slider = new Slider
            {
                Width = 200,
                Height = 20,
                Minimum = 0,
                Maximum = 255,
                Value = value,
                Background = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                Foreground = new SolidColorBrush(color)
            };
            var valueLabel = new TextBlock 
            { 
                Text = "0", 
                Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)), 
                FontSize = 10, 
                Width = 30,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0)
            };

            slider.ValueChanged += (s, e) => 
            {
                valueLabel.Text = ((int)e.NewValue).ToString();
                onValueChanged(e.NewValue);
            };

            panel.Children.Add(labelBlock);
            panel.Children.Add(slider);
            panel.Children.Add(valueLabel);
            
            return panel;
        }
    }
}
