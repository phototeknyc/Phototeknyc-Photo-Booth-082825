using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;

namespace Photobooth.Controls
{
    /// <summary>
    /// Simple text item that can be manipulated on the canvas
    /// </summary>
    public class SimpleTextItem : SimpleCanvasItem
    {
        public static Func<string, string> GlobalTokenResolver;
        public static readonly DependencyProperty IsVerticalProperty =
            DependencyProperty.Register("IsVertical", typeof(bool), typeof(SimpleTextItem),
                new PropertyMetadata(false, OnIsVerticalChanged));
        // Stroke properties
        public static readonly DependencyProperty StrokeBrushProperty =
            DependencyProperty.Register("StrokeBrush", typeof(Brush), typeof(SimpleTextItem),
                new PropertyMetadata(Brushes.Transparent, OnStrokeChanged));

        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register("StrokeThickness", typeof(double), typeof(SimpleTextItem),
                new PropertyMetadata(0.0, OnStrokeChanged));
        // Dependency properties for text styling
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(SimpleTextItem),
                new PropertyMetadata("Sample Text", OnTextChanged));

        public static new readonly DependencyProperty FontFamilyProperty =
            DependencyProperty.Register("FontFamily", typeof(FontFamily), typeof(SimpleTextItem),
                new PropertyMetadata(new FontFamily("Arial"), OnFontChanged));

        public static new readonly DependencyProperty FontSizeProperty =
            DependencyProperty.Register("FontSize", typeof(double), typeof(SimpleTextItem),
                new PropertyMetadata(16.0, OnFontChanged));

        public static new readonly DependencyProperty FontWeightProperty =
            DependencyProperty.Register("FontWeight", typeof(FontWeight), typeof(SimpleTextItem),
                new PropertyMetadata(FontWeights.Normal, OnFontChanged));

        public static new readonly DependencyProperty FontStyleProperty =
            DependencyProperty.Register("FontStyle", typeof(FontStyle), typeof(SimpleTextItem),
                new PropertyMetadata(FontStyles.Normal, OnFontChanged));

        public static readonly DependencyProperty TextColorProperty =
            DependencyProperty.Register("TextColor", typeof(Brush), typeof(SimpleTextItem),
                new PropertyMetadata(Brushes.Black, OnTextColorChanged));

        public static readonly DependencyProperty TextAlignmentProperty =
            DependencyProperty.Register("TextAlignment", typeof(TextAlignment), typeof(SimpleTextItem),
                new PropertyMetadata(TextAlignment.Left, OnTextAlignmentChanged));

        public static readonly DependencyProperty LineHeightProperty =
            DependencyProperty.Register("LineHeight", typeof(double), typeof(SimpleTextItem),
                new PropertyMetadata(double.NaN, OnTypographyChanged));

        public static readonly DependencyProperty LetterSpacingProperty =
            DependencyProperty.Register("LetterSpacing", typeof(double), typeof(SimpleTextItem),
                new PropertyMetadata(0.0, OnTypographyChanged));

        // Properties
        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public new FontFamily FontFamily
        {
            get => (FontFamily)GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        public new double FontSize
        {
            get => (double)GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public new FontWeight FontWeight
        {
            get => (FontWeight)GetValue(FontWeightProperty);
            set => SetValue(FontWeightProperty, value);
        }

        public new FontStyle FontStyle
        {
            get => (FontStyle)GetValue(FontStyleProperty);
            set => SetValue(FontStyleProperty, value);
        }

        public Brush TextColor
        {
            get => (Brush)GetValue(TextColorProperty);
            set => SetValue(TextColorProperty, value);
        }

        public Brush StrokeBrush
        {
            get => (Brush)GetValue(StrokeBrushProperty);
            set => SetValue(StrokeBrushProperty, value);
        }

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        public TextAlignment TextAlignment
        {
            get => (TextAlignment)GetValue(TextAlignmentProperty);
            set => SetValue(TextAlignmentProperty, value);
        }

        public double LineHeight
        {
            get => (double)GetValue(LineHeightProperty);
            set => SetValue(LineHeightProperty, value);
        }

        public double LetterSpacing
        {
            get => (double)GetValue(LetterSpacingProperty);
            set => SetValue(LetterSpacingProperty, value);
        }

        public bool IsVertical
        {
            get => (bool)GetValue(IsVerticalProperty);
            set => SetValue(IsVerticalProperty, value);
        }

        public static readonly DependencyProperty IsVerticalStackProperty =
            DependencyProperty.Register("IsVerticalStack", typeof(bool), typeof(SimpleTextItem),
                new PropertyMetadata(false, OnIsVerticalStackChanged));

        public bool IsVerticalStack
        {
            get => (bool)GetValue(IsVerticalStackProperty);
            set => SetValue(IsVerticalStackProperty, value);
        }

        // UI elements
        private TextBlock _textBlock;
        private Border _border;

        public SimpleTextItem() : base()
        {
            InitializeTextItem();
        }

        public SimpleTextItem(string text) : this()
        {
            Text = text;
        }

        protected override void InitializeItem()
        {
            base.InitializeItem();

            // Set default size for text items
            Width = 150;
            Height = 30;
        }

        private void InitializeTextItem()
        {
            // Create the visual structure
            _border = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1)
            };

            _textBlock = new TextBlock
            {
                Text = Resolve(Text),
                FontFamily = FontFamily,
                FontSize = FontSize,
                FontWeight = FontWeight,
                FontStyle = FontStyle,
                Foreground = TextColor,
                TextAlignment = TextAlignment,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(5)
            };

            _border.Child = _textBlock;
            Content = _border;

            // Update visual when selection changes
            SelectionChanged += OnSelectionChanged;

            // Auto-size to text
            UpdateSizeToFitText();
        }

        private static void OnIsVerticalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleTextItem item)
            {
                item.ApplyOrientation();
                item.UpdateSizeToFitText();
                item.OnPropertyChanged("IsVertical");
            }
        }

        private void ApplyOrientation()
        {
            if (_textBlock == null) return;
            // Clear any custom inline composition when switching modes
            _textBlock.Inlines.Clear();

            if (IsVerticalStack)
            {
                _textBlock.LayoutTransform = Transform.Identity;
                ApplyTypography();
                ComposeVerticalStack();
            }
            else if (IsVertical)
            {
                _textBlock.LayoutTransform = new RotateTransform(-90);
                ApplyTypography();
                _textBlock.Text = ComposeDisplayText(Resolve(Text));
            }
            else
            {
                _textBlock.LayoutTransform = Transform.Identity;
                ApplyTypography();
                _textBlock.Text = ComposeDisplayText(Resolve(Text));
            }
        }

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            UpdateSelectionVisual();
        }

        private void UpdateSelectionVisual()
        {
            if (_border != null)
            {
                // Keep stroke visible; selection is indicated via handles
                _border.BorderBrush = StrokeBrush ?? Brushes.Transparent;
                _border.BorderThickness = new Thickness(Math.Max(0, StrokeThickness));
            }
        }

        private void UpdateSizeToFitText()
        {
            if (_textBlock != null)
            {
                _textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var desiredSize = _textBlock.DesiredSize;

                // Account for rotation when vertical
                if (IsVertical)
                {
                    desiredSize = new Size(desiredSize.Height, desiredSize.Width);
                }

                // Add padding for border and margin
                Width = Math.Max(50, desiredSize.Width + 20);
                Height = Math.Max(20, desiredSize.Height + 10);
            }
        }

        private static void OnStrokeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleTextItem item && item._border != null)
            {
                item._border.BorderBrush = item.StrokeBrush ?? Brushes.Transparent;
                item._border.BorderThickness = new Thickness(Math.Max(0, item.StrokeThickness));
                item.OnPropertyChanged("Stroke");
            }
        }

        // Event handlers for property changes
        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleTextItem item && item._textBlock != null)
            {
                if (item.IsVerticalStack)
                {
                    item.ApplyTypography();
                    item.ComposeVerticalStack();
                }
                else
                {
                    item.ApplyTypography();
                    item._textBlock.Text = item.ComposeDisplayText(item.Resolve((string)e.NewValue));
                }
                item.UpdateSizeToFitText();
                item.OnPropertyChanged("Text");
            }
        }

        private static void OnFontChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleTextItem item && item._textBlock != null)
            {
                var propertyName = e.Property.Name;

                switch (propertyName)
                {
                    case "FontFamily":
                        item._textBlock.FontFamily = (FontFamily)e.NewValue;
                        break;
                    case "FontSize":
                        item._textBlock.FontSize = (double)e.NewValue;
                        break;
                    case "FontWeight":
                        item._textBlock.FontWeight = (FontWeight)e.NewValue;
                        break;
                    case "FontStyle":
                        item._textBlock.FontStyle = (FontStyle)e.NewValue;
                        break;
                }

                if (item.IsVerticalStack)
                {
                    item.ApplyTypography();
                    item.ComposeVerticalStack();
                }
                else
                {
                    item.ApplyTypography();
                    item._textBlock.Text = item.ComposeDisplayText(item.Resolve(item.Text));
                }
                item.UpdateSizeToFitText();
                item.OnPropertyChanged(propertyName);
            }
        }

        private static void OnTypographyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleTextItem item && item._textBlock != null)
            {
                item.ApplyTypography();
                if (item.IsVerticalStack)
                    item.ComposeVerticalStack();
                else
                    item._textBlock.Text = item.ComposeDisplayText(item.Resolve(item.Text));
                item.UpdateSizeToFitText();
                item.OnPropertyChanged(e.Property.Name);
            }
        }

        private void ApplyTypography()
        {
            // Line height
            if (!double.IsNaN(LineHeight) && LineHeight > 0)
            {
                _textBlock.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                _textBlock.LineHeight = LineHeight;
            }
            else
            {
                _textBlock.LineStackingStrategy = LineStackingStrategy.MaxHeight;
                _textBlock.LineHeight = double.NaN;
            }
        }

        private string ComposeDisplayText(string s)
        {
            double ls = LetterSpacing;
            if (ls <= 0.01 || string.IsNullOrEmpty(s)) return s;
            // Approximate tracking by inserting hair spaces based on desired spacing
            // Map roughly: every ~2px -> 1 hair space
            int count = Math.Max(1, (int)Math.Round(ls / 2.0));
            string spacer = new string('\u200A', count); // hair space(s)
            var chars = s.ToCharArray();
            return string.Join(spacer, chars);
        }

        private static void OnIsVerticalStackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleTextItem item)
            {
                if ((bool)e.NewValue)
                {
                    // Stacked takes precedence over rotate-vertical
                    item.IsVertical = false;
                }
                item.ApplyOrientation();
                item.UpdateSizeToFitText();
                item.OnPropertyChanged("IsVerticalStack");
            }
        }

        private void ComposeVerticalStack()
        {
            if (_textBlock == null) return;
            _textBlock.Inlines.Clear();
            _textBlock.Text = string.Empty;

            var text = GlobalTokenResolver != null ? GlobalTokenResolver(Text ?? string.Empty) : Text ?? string.Empty;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '\n')
                {
                    _textBlock.Inlines.Add(new LineBreak());
                    continue;
                }
                _textBlock.Inlines.Add(new Run(ch.ToString()));
                if (i < text.Length - 1)
                {
                    _textBlock.Inlines.Add(new LineBreak());
                }
            }
        }

        private static void OnTextColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleTextItem item)
            {
                System.Diagnostics.Debug.WriteLine($"SimpleTextItem.OnTextColorChanged: Called with new value {e.NewValue}");

                if (item._textBlock != null)
                {
                    var oldForeground = item._textBlock.Foreground;
                    item._textBlock.Foreground = (Brush)e.NewValue;
                    item.OnPropertyChanged("TextColor");

                    System.Diagnostics.Debug.WriteLine($"SimpleTextItem.OnTextColorChanged: TextBlock.Foreground changed from {oldForeground} to {item._textBlock.Foreground}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("SimpleTextItem.OnTextColorChanged: _textBlock is null!");
                }
            }
        }

        private static void OnTextAlignmentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleTextItem item && item._textBlock != null)
            {
                item._textBlock.TextAlignment = (TextAlignment)e.NewValue;
                item.OnPropertyChanged("TextAlignment");
            }
        }

        // Public methods for external manipulation
        public void SetFont(FontFamily fontFamily, double fontSize, FontWeight fontWeight, FontStyle fontStyle)
        {
            FontFamily = fontFamily;
            FontSize = fontSize;
            FontWeight = fontWeight;
            FontStyle = fontStyle;
        }

        public void SetTextProperties(string text, Brush textColor, TextAlignment alignment)
        {
            Text = text;
            TextColor = textColor;
            TextAlignment = alignment;
        }

        // Override abstract methods
        public override string GetDisplayName()
        {
            var displayText = string.IsNullOrEmpty(Text) ? "Empty Text" : Text;
            if (displayText.Length > 20)
                displayText = displayText.Substring(0, 17) + "...";
            return $"Text: {displayText}";
        }

        public override SimpleCanvasItem Clone()
        {
            var clone = new SimpleTextItem
            {
                Text = this.Text,
                FontFamily = this.FontFamily,
                FontSize = this.FontSize,
                FontWeight = this.FontWeight,
                FontStyle = this.FontStyle,
                TextColor = this.TextColor,
                TextAlignment = this.TextAlignment,
                LineHeight = this.LineHeight,
                LetterSpacing = this.LetterSpacing,
                IsVertical = this.IsVertical,
                IsVerticalStack = this.IsVerticalStack,
                StrokeBrush = this.StrokeBrush,
                StrokeThickness = this.StrokeThickness,
                Width = this.Width,
                Height = this.Height,
                Left = this.Left + 10, // Slight offset for visual clarity
                Top = this.Top + 10,
                ZIndex = this.ZIndex,
                RotationAngle = this.RotationAngle
            };
            // Copy drop shadow or other UI effects if present
            if (this.Effect != null)
            {
                try { clone.Effect = this.Effect.Clone(); } catch { clone.Effect = this.Effect; }
            }
            return clone;
        }

        // Method to enter edit mode (for future implementation)
        public void EnterEditMode()
        {
            // TODO: Replace TextBlock with TextBox for inline editing
            // This can be implemented later for enhanced functionality
        }

        public void ExitEditMode()
        {
            // TODO: Replace TextBox with TextBlock and save changes
            // This can be implemented later for enhanced functionality
        }

        private string Resolve(string s)
        {
            return GlobalTokenResolver != null ? GlobalTokenResolver(s) : s;
        }
    }
}
