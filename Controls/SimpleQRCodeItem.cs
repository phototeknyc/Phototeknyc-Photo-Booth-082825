using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace Photobooth.Controls
{
    public class SimpleQRCodeItem : SimpleCanvasItem
    {
        public static Func<string, string> GlobalTokenResolver;
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(string), typeof(SimpleQRCodeItem),
                new PropertyMetadata("https://example.com", OnValueChanged));

        public static readonly DependencyProperty EccLevelProperty =
            DependencyProperty.Register("EccLevel", typeof(QRCodeGenerator.ECCLevel), typeof(SimpleQRCodeItem),
                new PropertyMetadata(QRCodeGenerator.ECCLevel.Q, OnQrPropertyChanged));

        public static readonly DependencyProperty PixelsPerModuleProperty =
            DependencyProperty.Register("PixelsPerModule", typeof(int), typeof(SimpleQRCodeItem),
                new PropertyMetadata(4, OnQrPropertyChanged));

        private Image _image;
        private Border _border;

        public string Value
        {
            get => (string)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public QRCodeGenerator.ECCLevel EccLevel
        {
            get => (QRCodeGenerator.ECCLevel)GetValue(EccLevelProperty);
            set => SetValue(EccLevelProperty, value);
        }

        public int PixelsPerModule
        {
            get => (int)GetValue(PixelsPerModuleProperty);
            set => SetValue(PixelsPerModuleProperty, value);
        }

        public SimpleQRCodeItem()
        {
            Initialize();
        }

        protected override void InitializeItem()
        {
            base.InitializeItem();
            Width = 160;
            Height = 160;
        }

        private void Initialize()
        {
            _image = new Image
            {
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            _border = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Child = _image
            };

            Content = _border;
            Generate();
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleQRCodeItem item)
            {
                item.Generate();
                item.OnPropertyChanged("Value");
            }
        }

        private static void OnQrPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleQRCodeItem item)
            {
                item.Generate();
                item.OnPropertyChanged(e.Property.Name);
            }
        }

        private void Generate()
        {
            try
            {
                var val = string.IsNullOrWhiteSpace(Value) ? string.Empty : Value;
                if (GlobalTokenResolver != null)
                {
                    try { val = GlobalTokenResolver(val) ?? val; } catch { }
                }

                using (var generator = new QRCodeGenerator())
                using (var data = generator.CreateQrCode(val, EccLevel))
                using (var qr = new QRCode(data))
                {
                    using (var bmp = qr.GetGraphic(Math.Max(1, PixelsPerModule), System.Drawing.Color.Black, System.Drawing.Color.White, true))
                    {
                        _image.Source = ToBitmapImage(bmp);
                    }
                }
            }
            catch
            {
                // Fallback: empty
                _image.Source = null;
            }
        }

        private BitmapImage ToBitmapImage(System.Drawing.Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }

        public override string GetDisplayName()
        {
            return "QR Code";
        }

        public override SimpleCanvasItem Clone()
        {
            var clone = new SimpleQRCodeItem
            {
                Left = Left + 10,
                Top = Top + 10,
                Width = Width,
                Height = Height,
                Value = Value,
                EccLevel = EccLevel,
                PixelsPerModule = PixelsPerModule,
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
