using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Input;

namespace Photobooth.Controls
{
    public partial class FontControlsPanel : UserControl
    {
        private object _selectedTextItem;
        private bool _isUpdating = false;
        private bool _isLoaded = false;
        private FrameworkElement _constrainToElement;

        public event EventHandler<FontChangedEventArgs> FontChanged;

        // Property to set the canvas for eyedropper constraint
        public FrameworkElement ConstrainToElement
        {
            get => _constrainToElement;
            set
            {
                _constrainToElement = value;
                // Update inline color pickers
                if (TextColorPicker != null)
                    TextColorPicker.ConstrainToElement = value;
                if (ShadowColorPicker != null)
                    ShadowColorPicker.ConstrainToElement = value;
            }
        }

        public FontControlsPanel()
        {
            InitializeComponent();
            LoadSystemFonts();
            Loaded += (s, e) => _isLoaded = true;
        }

        private void LoadSystemFonts()
        {
            FontFamilyCombo.Items.Clear();

            var fonts = Fonts.SystemFontFamilies.OrderBy(f => f.Source);
            foreach (var font in fonts)
            {
                var item = new ComboBoxItem
                {
                    Content = font.Source,
                    FontFamily = font,
                    Height = 30
                };
                FontFamilyCombo.Items.Add(item);
            }

            if (FontFamilyCombo.Items.Count > 0)
                FontFamilyCombo.SelectedIndex = 0;
        }

        public void SetSelectedTextItem(object textItem)
        {
            _selectedTextItem = textItem;
            UpdateControlsFromSelection();
        }

        private void UpdateControlsFromSelection()
        {
            if (_selectedTextItem == null) return;

            try
            {
                _isUpdating = true;

                // Check if the selected item is a TextCanvasItem
                if (_selectedTextItem is DesignerCanvas.TextCanvasItem textCanvasItem)
                {
                    // Update font family
                    if (!string.IsNullOrEmpty(textCanvasItem.FontFamily))
                    {
                        foreach (ComboBoxItem item in FontFamilyCombo.Items)
                        {
                            if (item.Content.ToString() == textCanvasItem.FontFamily)
                            {
                                FontFamilyCombo.SelectedItem = item;
                                break;
                            }
                        }
                    }

                    // Update font size
                    if (FontSizeSlider != null)
                        FontSizeSlider.Value = textCanvasItem.FontSize;
                    if (FontSizeTextBox != null)
                        FontSizeTextBox.Text = textCanvasItem.FontSize.ToString() + " pt";

                    // Update font style
                    if (FontStyleCombo != null)
                    {
                        var styleString = textCanvasItem.FontStyle == FontStyles.Italic ? "Italic" :
                                        textCanvasItem.FontStyle == FontStyles.Oblique ? "Oblique" : "Normal";
                        foreach (ComboBoxItem item in FontStyleCombo.Items)
                        {
                            if (item.Content.ToString() == styleString)
                            {
                                FontStyleCombo.SelectedItem = item;
                                break;
                            }
                        }
                    }

                    // Update font weight
                    if (FontWeightCombo != null)
                    {
                        var weightString = textCanvasItem.FontWeight.ToString();
                        foreach (ComboBoxItem item in FontWeightCombo.Items)
                        {
                            if (item.Content.ToString() == weightString)
                            {
                                FontWeightCombo.SelectedItem = item;
                                break;
                            }
                        }
                    }

                    // Update text alignment
                    if (AlignLeftBtn != null && AlignCenterBtn != null && AlignRightBtn != null && AlignJustifyBtn != null)
                    {
                        AlignLeftBtn.IsChecked = textCanvasItem.TextAlignment == TextAlignment.Left;
                        AlignCenterBtn.IsChecked = textCanvasItem.TextAlignment == TextAlignment.Center;
                        AlignRightBtn.IsChecked = textCanvasItem.TextAlignment == TextAlignment.Right;
                        AlignJustifyBtn.IsChecked = textCanvasItem.TextAlignment == TextAlignment.Justify;
                    }

                    // Update text decorations
                    if (UnderlineBtn != null && UnderlineBtn is System.Windows.Controls.Primitives.ToggleButton underlineToggle)
                        underlineToggle.IsChecked = textCanvasItem.TextDecorations?.Contains(TextDecorations.Underline[0]) ?? false;

                    // Update shadow settings
                    if (TextShadowCheckBox != null)
                        TextShadowCheckBox.IsChecked = textCanvasItem.HasShadow;

                    System.Diagnostics.Debug.WriteLine($"FontControlsPanel: Updated controls from TextCanvasItem");
                }
                // Handle SimpleTextItem objects (used by SimpleDesignerCanvas)
                else if (_selectedTextItem is SimpleTextItem simpleTextItem)
                {
                    // Update font family
                    if (simpleTextItem.FontFamily != null)
                    {
                        foreach (ComboBoxItem item in FontFamilyCombo.Items)
                        {
                            if (item.Content.ToString() == simpleTextItem.FontFamily.Source)
                            {
                                FontFamilyCombo.SelectedItem = item;
                                break;
                            }
                        }
                    }

                    // Update font size
                    if (FontSizeSlider != null)
                        FontSizeSlider.Value = simpleTextItem.FontSize;
                    if (FontSizeTextBox != null)
                        FontSizeTextBox.Text = simpleTextItem.FontSize.ToString() + " pt";

                    // Update font style
                    if (FontStyleCombo != null)
                    {
                        var styleString = simpleTextItem.FontStyle == FontStyles.Italic ? "Italic" :
                                        simpleTextItem.FontStyle == FontStyles.Oblique ? "Oblique" : "Normal";
                        foreach (ComboBoxItem item in FontStyleCombo.Items)
                        {
                            if (item.Content.ToString() == styleString)
                            {
                                FontStyleCombo.SelectedItem = item;
                                break;
                            }
                        }
                    }

                    // Update font weight
                    if (FontWeightCombo != null)
                    {
                        var weightString = simpleTextItem.FontWeight.ToString();
                        foreach (ComboBoxItem item in FontWeightCombo.Items)
                        {
                            if (item.Content.ToString() == weightString)
                            {
                                FontWeightCombo.SelectedItem = item;
                                break;
                            }
                        }
                    }

                    // Update text alignment
                    if (AlignLeftBtn != null && AlignCenterBtn != null && AlignRightBtn != null && AlignJustifyBtn != null)
                    {
                        AlignLeftBtn.IsChecked = simpleTextItem.TextAlignment == TextAlignment.Left;
                        AlignCenterBtn.IsChecked = simpleTextItem.TextAlignment == TextAlignment.Center;
                        AlignRightBtn.IsChecked = simpleTextItem.TextAlignment == TextAlignment.Right;
                        AlignJustifyBtn.IsChecked = simpleTextItem.TextAlignment == TextAlignment.Justify;
                    }

                    // Update text color
                    if (TextColorPicker != null && simpleTextItem.TextColor is SolidColorBrush colorBrush)
                    {
                        TextColorPicker.SelectedColor = colorBrush.Color;
                    }

                    // Note: SimpleTextItem doesn't support text decorations or shadows yet
                    if (UnderlineBtn != null && UnderlineBtn is System.Windows.Controls.Primitives.ToggleButton underlineToggle)
                        underlineToggle.IsChecked = false; // Not supported

                    if (TextShadowCheckBox != null)
                        TextShadowCheckBox.IsChecked = false; // Not supported

                    // Orientation
                    if (VerticalTextCheckBox != null)
                        VerticalTextCheckBox.IsChecked = simpleTextItem.IsVertical;
                    if (VerticalStackCheckBox != null)
                        VerticalStackCheckBox.IsChecked = simpleTextItem.IsVerticalStack;

                    System.Diagnostics.Debug.WriteLine($"FontControlsPanel: Updated controls from SimpleTextItem");
                }

                _isUpdating = false;
            }
            catch (Exception ex)
            {
                _isUpdating = false;
                System.Diagnostics.Debug.WriteLine($"FontControlsPanel: Error updating controls from selection: {ex.Message}");
            }
        }

        private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdating) return;
            if (FontFamilyCombo.SelectedItem is ComboBoxItem item)
            {
                var fontFamily = item.FontFamily ?? new FontFamily(item.Content.ToString());
                RaiseFontChanged(new FontChangedEventArgs { FontFamily = fontFamily });
            }
        }

        private void FontStyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontStyleCombo.SelectedItem is ComboBoxItem item)
            {
                FontStyle style = FontStyles.Normal;
                switch (item.Content.ToString())
                {
                    case "Italic":
                        style = FontStyles.Italic;
                        break;
                    case "Oblique":
                        style = FontStyles.Oblique;
                        break;
                }
                RaiseFontChanged(new FontChangedEventArgs { FontStyle = style });
            }
        }

        private void FontWeightCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontWeightCombo.SelectedItem is ComboBoxItem item)
            {
                FontWeight weight = FontWeights.Normal;
                switch (item.Content.ToString())
                {
                    case "Light":
                        weight = FontWeights.Light;
                        break;
                    case "Regular":
                        weight = FontWeights.Regular;
                        break;
                    case "Medium":
                        weight = FontWeights.Medium;
                        break;
                    case "SemiBold":
                        weight = FontWeights.SemiBold;
                        break;
                    case "Bold":
                        weight = FontWeights.Bold;
                        break;
                    case "Black":
                        weight = FontWeights.Black;
                        break;
                }
                RaiseFontChanged(new FontChangedEventArgs { FontWeight = weight });
            }
        }

        private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdating) return;
            if (FontSizeTextBox != null)
            {
                FontSizeTextBox.Text = $"{(int)e.NewValue} pt";
            }
            RaiseFontChanged(new FontChangedEventArgs { FontSize = e.NewValue });
        }

        private void FontSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (double.TryParse(FontSizeTextBox.Text.Replace(" pt", ""), out double size))
            {
                if (size >= 8 && size <= 144)
                {
                    FontSizeSlider.Value = size;
                }
            }
        }

        private void Alignment_Click(object sender, RoutedEventArgs e)
        {
            TextAlignment alignment = TextAlignment.Left;

            if (AlignLeftBtn.IsChecked == true)
                alignment = TextAlignment.Left;
            else if (AlignCenterBtn.IsChecked == true)
                alignment = TextAlignment.Center;
            else if (AlignRightBtn.IsChecked == true)
                alignment = TextAlignment.Right;
            else if (AlignJustifyBtn.IsChecked == true)
                alignment = TextAlignment.Justify;

            RaiseFontChanged(new FontChangedEventArgs { TextAlignment = alignment });
        }

        private void Bold_Click(object sender, RoutedEventArgs e)
        {
            var weight = BoldBtn.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            RaiseFontChanged(new FontChangedEventArgs { FontWeight = weight });
        }

        private void Italic_Click(object sender, RoutedEventArgs e)
        {
            var style = ItalicBtn.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
            RaiseFontChanged(new FontChangedEventArgs { FontStyle = style });
        }

        private void Underline_Click(object sender, RoutedEventArgs e)
        {
            var decorations = UnderlineBtn.IsChecked == true ? TextDecorations.Underline : null;
            RaiseFontChanged(new FontChangedEventArgs { TextDecorations = decorations });
        }

        private void LineHeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LineHeightText != null)
            {
                LineHeightText.Text = e.NewValue.ToString("F1");
            }
            RaiseFontChanged(new FontChangedEventArgs { LineHeight = e.NewValue });
        }

        private void LetterSpacingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LetterSpacingText != null)
            {
                LetterSpacingText.Text = $"{(int)e.NewValue} px";
            }
            RaiseFontChanged(new FontChangedEventArgs { LetterSpacing = e.NewValue });
        }

        private void TextColorPicker_ColorChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdating) return;

            var picker = sender as InlineColorPicker;
            if (picker != null)
            {
                System.Diagnostics.Debug.WriteLine($"FontControlsPanel: Text color changed to {picker.SelectedColor}");
                var brush = new SolidColorBrush(picker.SelectedColor);
                RaiseFontChanged(new FontChangedEventArgs { TextColor = brush });

                // Fallback: if a SimpleDesignerCanvas is available via ConstrainToElement,
                // apply the color directly to the currently selected SimpleTextItem.
                try
                {
                    if (_constrainToElement is Photobooth.Controls.SimpleDesignerCanvas sdc && sdc.SelectedItem is Photobooth.Controls.SimpleTextItem st)
                    {
                        st.TextColor = brush;
                        System.Diagnostics.Debug.WriteLine("FontControlsPanel: Applied text color directly to SimpleTextItem via fallback.");
                    }
                }
                catch { }
            }
        }

        // Stroke moved to toolbar. Handlers retained if referenced elsewhere.

        private void TextShadow_Changed(object sender, RoutedEventArgs e)
        {
            if (ShadowOptionsPanel != null && TextShadowCheckBox != null)
            {
                ShadowOptionsPanel.Visibility = TextShadowCheckBox.IsChecked == true
                    ? Visibility.Visible : Visibility.Collapsed;

                if (TextShadowCheckBox.IsChecked == true)
                {
                    ApplyShadow();
                }
                else
                {
                    RaiseFontChanged(new FontChangedEventArgs { DropShadow = null });
                }
            }
        }

        private void Shadow_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TextShadowCheckBox?.IsChecked == true)
            {
                ApplyShadow();
            }
        }

        private void ApplyShadow()
        {
            // Check if all required shadow elements are loaded
            if (ShadowXSlider == null || ShadowYSlider == null || ShadowBlurSlider == null || ShadowColorPicker == null)
                return;

            var shadow = new DropShadowEffect
            {
                Direction = Math.Atan2(ShadowYSlider.Value, ShadowXSlider.Value) * (180 / Math.PI),
                ShadowDepth = Math.Sqrt(Math.Pow(ShadowXSlider.Value, 2) + Math.Pow(ShadowYSlider.Value, 2)),
                BlurRadius = ShadowBlurSlider.Value,
                Color = ShadowColorPicker.SelectedColor,
                Opacity = 0.8
            };

            RaiseFontChanged(new FontChangedEventArgs { DropShadow = shadow });
        }

        private void ShadowColorPicker_ColorChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdating) return;

            var picker = sender as InlineColorPicker;
            if (picker != null)
            {
                System.Diagnostics.Debug.WriteLine($"FontControlsPanel: Shadow color changed to {picker.SelectedColor}");

                if (TextShadowCheckBox?.IsChecked == true)
                {
                    ApplyShadow();
                }
            }
        }

        private void TextTransform_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (TextTransformCombo?.SelectedItem is ComboBoxItem item)
            {
                TextTransform transform = TextTransform.None;
                switch (item.Content.ToString())
                {
                    case "UPPERCASE":
                        transform = TextTransform.Uppercase;
                        break;
                    case "lowercase":
                        transform = TextTransform.Lowercase;
                        break;
                    case "Capitalize":
                        transform = TextTransform.Capitalize;
                        break;
                }

                RaiseFontChanged(new FontChangedEventArgs { TextTransform = transform });
            }
        }

        private void VerticalText_Changed(object sender, RoutedEventArgs e)
        {
            bool isVertical = VerticalTextCheckBox?.IsChecked == true;
            // If rotate is enabled, disable stacked
            if (isVertical && VerticalStackCheckBox != null)
                VerticalStackCheckBox.IsChecked = false;
            RaiseFontChanged(new FontChangedEventArgs { IsVertical = isVertical });
        }

        private void VerticalStack_Changed(object sender, RoutedEventArgs e)
        {
            bool isStack = VerticalStackCheckBox?.IsChecked == true;
            // If stacked is enabled, disable rotate
            if (isStack && VerticalTextCheckBox != null)
                VerticalTextCheckBox.IsChecked = false;
            RaiseFontChanged(new FontChangedEventArgs { IsVerticalStack = isStack });
        }

        private void OpenTypeFeature_Changed(object sender, RoutedEventArgs e)
        {
            // Check if all checkboxes are loaded to avoid null reference exceptions during initialization
            if (LigaturesCheckBox == null || KerningCheckBox == null || SmallCapsCheckBox == null)
                return;

            var features = new OpenTypeFeatures
            {
                Ligatures = LigaturesCheckBox.IsChecked == true,
                Kerning = KerningCheckBox.IsChecked == true,
                SmallCaps = SmallCapsCheckBox.IsChecked == true
            };

            RaiseFontChanged(new FontChangedEventArgs { OpenTypeFeatures = features });
        }

        private void CharacterPanel_Click(object sender, RoutedEventArgs e)
        {
            // Open character map or special characters panel
            var characterDialog = new Window
            {
                Title = "Special Characters",
                Width = 600,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow
            };

            var grid = new Grid();
            var wrapPanel = new WrapPanel { Margin = new Thickness(10) };

            // Add common special characters
            string[] specialChars = { "©", "®", "™", "€", "£", "¥", "¢", "§", "¶",
                                     "†", "‡", "•", "…", "°", "±", "×", "÷", "≠",
                                     "≤", "≥", "←", "→", "↑", "↓", "♠", "♣", "♥", "♦" };

            foreach (var ch in specialChars)
            {
                var btn = new Button
                {
                    Content = ch,
                    Width = 50,
                    Height = 50,
                    Margin = new Thickness(5),
                    FontSize = 20
                };
                btn.Click += (s, args) =>
                {
                    RaiseFontChanged(new FontChangedEventArgs { InsertCharacter = ch });
                    characterDialog.Close();
                };
                wrapPanel.Children.Add(btn);
            }

            var scrollViewer = new ScrollViewer { Content = wrapPanel };
            grid.Children.Add(scrollViewer);
            characterDialog.Content = grid;
            characterDialog.ShowDialog();
        }

        private void RaiseFontChanged(FontChangedEventArgs args)
        {
            // Only raise events after the control is fully loaded to avoid initialization issues
            if (_isLoaded)
            {
                FontChanged?.Invoke(this, args);
            }
        }
    }

    public class FontChangedEventArgs : EventArgs
    {
        public FontFamily FontFamily { get; set; }
        public FontStyle? FontStyle { get; set; }
        public FontWeight? FontWeight { get; set; }
        public double? FontSize { get; set; }
        public TextAlignment? TextAlignment { get; set; }
        public TextDecorationCollection TextDecorations { get; set; }
        public double? LineHeight { get; set; }
        public double? LetterSpacing { get; set; }
        public Brush TextColor { get; set; }
        public Effect DropShadow { get; set; }
        public TextTransform? TextTransform { get; set; }
        public OpenTypeFeatures OpenTypeFeatures { get; set; }
        public string InsertCharacter { get; set; }
        public Brush StrokeBrush { get; set; }
        public double? StrokeThickness { get; set; }
        public bool? IsVertical { get; set; }
        public bool? IsVerticalStack { get; set; }
    }

    public enum TextTransform
    {
        None,
        Uppercase,
        Lowercase,
        Capitalize
    }

    public class OpenTypeFeatures
    {
        public bool Ligatures { get; set; }
        public bool Kerning { get; set; }
        public bool SmallCaps { get; set; }
    }
}
