using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Photobooth.Controls
{
    public enum BarcodeSymbology { Code39 }

    public class SimpleBarcodeItem : SimpleCanvasItem
    {
        public static Func<string, string> GlobalTokenResolver;

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(string), typeof(SimpleBarcodeItem),
                new PropertyMetadata("12345", OnBarcodePropertyChanged));

        public static readonly DependencyProperty SymbologyProperty =
            DependencyProperty.Register("Symbology", typeof(BarcodeSymbology), typeof(SimpleBarcodeItem),
                new PropertyMetadata(BarcodeSymbology.Code39, OnBarcodePropertyChanged));

        public static readonly DependencyProperty ModuleWidthProperty =
            DependencyProperty.Register("ModuleWidth", typeof(double), typeof(SimpleBarcodeItem),
                new PropertyMetadata(2.0, OnBarcodePropertyChanged));

        public static readonly DependencyProperty IncludeLabelProperty =
            DependencyProperty.Register("IncludeLabel", typeof(bool), typeof(SimpleBarcodeItem),
                new PropertyMetadata(true, OnBarcodePropertyChanged));

        private Canvas _canvas;

        public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public BarcodeSymbology Symbology { get => (BarcodeSymbology)GetValue(SymbologyProperty); set => SetValue(SymbologyProperty, value); }
        public double ModuleWidth { get => (double)GetValue(ModuleWidthProperty); set => SetValue(ModuleWidthProperty, value); }
        public bool IncludeLabel { get => (bool)GetValue(IncludeLabelProperty); set => SetValue(IncludeLabelProperty, value); }

        public SimpleBarcodeItem()
        {
            _canvas = new Canvas();
            Content = _canvas;
            Width = 240; Height = 100;
            RenderBarcode();
        }

        private static void OnBarcodePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleBarcodeItem item)
            {
                item.RenderBarcode();
                item.OnPropertyChanged(e.Property.Name);
            }
        }

        private static readonly Dictionary<char, string> Code39 = new Dictionary<char, string>
        {
            {'0', "nnnwwnwnw"}, {'1', "wnnwnnnnw"}, {'2', "nnwwnnnnw"}, {'3', "wnwwnnnnn"},
            {'4', "nnnwwnnnw"}, {'5', "wnnwwnnnn"}, {'6', "nnwwwnnnn"}, {'7', "nnnwnnwnw"},
            {'8', "wnnwnnwnn"}, {'9', "nnwwnnwnn"}, {'A', "wnnnnwnnw"}, {'B', "nnwnnwnnw"},
            {'C', "wnwnnwnnn"}, {'D', "nnnnwwnnw"}, {'E', "wnnnwwnnn"}, {'F', "nnwnwwnnn"},
            {'G', "nnnnnwwnw"}, {'H', "wnnnnwwnn"}, {'I', "nnwnnwwnn"}, {'J', "nnnnwwwnn"},
            {'K', "wnnnnnnww"}, {'L', "nnwnnnnww"}, {'M', "wnwnnnnwn"}, {'N', "nnnnwnnww"},
            {'O', "wnnnwnnwn"}, {'P', "nnwnwnnwn"}, {'Q', "nnnnnnwww"}, {'R', "wnnnnnwwn"},
            {'S', "nnwnnnwwn"}, {'T', "nnnnwnwwn"}, {'U', "wwnnnnnnw"}, {'V', "nwwnnnnnw"},
            {'W', "wwwnnnnnn"}, {'X', "nwnnwnnnw"}, {'Y', "wwnnwnnnn"}, {'Z', "nwwnwnnnn"},
            {'-', "nwnnnnwnw"}, {'.', "wwnnnnwnn"}, {' ', "nwwnnnwnn"}, {'*', "nwnnwnwnn"},
            {'$', "nwnwnwnnn"}, {'/', "nwnwnnnwn"}, {'+', "nwnnnwnwn"}, {'%', "nnnwnwnwn"}
        };

        private void RenderBarcode()
        {
            if (_canvas == null) return;
            _canvas.Children.Clear();
            string raw = Value ?? string.Empty;
            if (GlobalTokenResolver != null) raw = GlobalTokenResolver(raw);
            raw = raw.ToUpperInvariant();
            // Code39 requires start/stop '*'
            string data = "*" + raw + "*";
            double x = 0;
            double narrow = Math.Max(1, ModuleWidth);
            double wide = narrow * 3;
            double h = Height - (IncludeLabel ? 18 : 0);

            foreach (char ch in data)
            {
                if (!Code39.TryGetValue(ch, out var pattern)) continue;
                bool drawBar = true;
                foreach (char c in pattern)
                {
                    double w = c == 'w' ? wide : narrow;
                    if (drawBar)
                    {
                        var rect = new System.Windows.Shapes.Rectangle { Width = w, Height = h, Fill = Brushes.Black };
                        Canvas.SetLeft(rect, x);
                        Canvas.SetTop(rect, 0);
                        _canvas.Children.Add(rect);
                    }
                    x += w;
                    drawBar = !drawBar;
                }
                // Inter-character narrow space
                x += narrow;
            }

            if (IncludeLabel && !string.IsNullOrEmpty(raw))
            {
                var tb = new TextBlock { Text = raw, Foreground = Brushes.Black, FontSize = 12 };
                Canvas.SetLeft(tb, 0);
                Canvas.SetTop(tb, h);
                _canvas.Children.Add(tb);
            }
        }

        public override string GetDisplayName() => "Barcode";

        public override SimpleCanvasItem Clone()
        {
            var clone = new SimpleBarcodeItem
            {
                Left = Left + 10,
                Top = Top + 10,
                Width = Width,
                Height = Height,
                Value = Value,
                Symbology = Symbology,
                ModuleWidth = ModuleWidth,
                IncludeLabel = IncludeLabel,
                ZIndex = ZIndex,
                RotationAngle = this.RotationAngle
            };
            if (this.Effect != null)
            {
                try { clone.Effect = this.Effect.Clone(); } catch { clone.Effect = this.Effect; }
            }
            return clone;
        }
    }
}
