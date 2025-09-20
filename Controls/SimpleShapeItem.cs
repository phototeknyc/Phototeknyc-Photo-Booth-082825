using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Photobooth.Controls
{
    public enum SimpleShapeType
    {
        Rectangle,
        Ellipse,
        Line
    }

    public class SimpleShapeItem : SimpleCanvasItem
    {
        public static readonly DependencyProperty FillProperty =
            DependencyProperty.Register("Fill", typeof(Brush), typeof(SimpleShapeItem),
                new PropertyMetadata(Brushes.LightBlue, OnShapeVisualChanged));

        public static readonly DependencyProperty StrokeProperty =
            DependencyProperty.Register("Stroke", typeof(Brush), typeof(SimpleShapeItem),
                new PropertyMetadata(Brushes.Black, OnShapeVisualChanged));

        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register("StrokeThickness", typeof(double), typeof(SimpleShapeItem),
                new PropertyMetadata(2.0, OnShapeVisualChanged));

        public static readonly DependencyProperty ShapeTypeProperty =
            DependencyProperty.Register("ShapeType", typeof(SimpleShapeType), typeof(SimpleShapeItem),
                new PropertyMetadata(SimpleShapeType.Rectangle, OnShapeTypeChanged));

        public SimpleShapeType ShapeType
        {
            get => (SimpleShapeType)GetValue(ShapeTypeProperty);
            set => SetValue(ShapeTypeProperty, value);
        }

        private FrameworkElement _shapeElement;

        public Brush Fill
        {
            get => (Brush)GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        public Brush Stroke
        {
            get => (Brush)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        protected override void InitializeItem()
        {
            base.InitializeItem();
            Width = 120;
            Height = 80;
            SizeChanged += (s, e) => UpdateShapeVisual();
            BuildShape();
        }

        private static void OnShapeVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleShapeItem item)
            {
                item.UpdateShapeVisual();
                item.OnPropertyChanged(e.Property.Name);
            }
        }

        private static void OnShapeTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleShapeItem item)
            {
                item.BuildShape();
                item.OnPropertyChanged("ShapeType");
            }
        }

        private void BuildShape()
        {
            switch (ShapeType)
            {
                case SimpleShapeType.Rectangle:
                    _shapeElement = new Rectangle
                    {
                        Width = double.IsNaN(Width) ? 100 : Width,
                        Height = double.IsNaN(Height) ? 100 : Height
                    };
                    break;
                case SimpleShapeType.Ellipse:
                    _shapeElement = new Ellipse
                    {
                        Width = double.IsNaN(Width) ? 100 : Width,
                        Height = double.IsNaN(Height) ? 100 : Height
                    };
                    break;
                case SimpleShapeType.Line:
                    var line = new Line
                    {
                        X1 = 0,
                        Y1 = 0,
                        X2 = double.IsNaN(Width) ? 100 : Width,
                        Y2 = double.IsNaN(Height) ? 100 : Height,
                        StrokeThickness = 2
                    };
                    _shapeElement = line;
                    break;
            }

            Content = _shapeElement;
            UpdateShapeVisual();
        }

        private void UpdateShapeVisual()
        {
            if (_shapeElement == null)
                return;

            if (_shapeElement is Shape s)
            {
                s.Fill = ShapeType == SimpleShapeType.Line ? Brushes.Transparent : Fill;
                s.Stroke = Stroke;
                s.StrokeThickness = StrokeThickness;

                // Update size for Rectangle and Ellipse
                if (_shapeElement is Rectangle rect)
                {
                    rect.Width = double.IsNaN(Width) ? 100 : Width;
                    rect.Height = double.IsNaN(Height) ? 100 : Height;
                }
                else if (_shapeElement is Ellipse ellipse)
                {
                    ellipse.Width = double.IsNaN(Width) ? 100 : Width;
                    ellipse.Height = double.IsNaN(Height) ? 100 : Height;
                }
            }

            if (_shapeElement is Line ln)
            {
                ln.X2 = double.IsNaN(Width) ? 100 : Width;
                ln.Y2 = double.IsNaN(Height) ? 100 : Height;
            }
        }

        public override string GetDisplayName()
        {
            return $"Shape: {ShapeType}";
        }

        public override SimpleCanvasItem Clone()
        {
            var clone = new SimpleShapeItem
            {
                ShapeType = this.ShapeType,
                Fill = this.Fill,
                Stroke = this.Stroke,
                StrokeThickness = this.StrokeThickness,
                Width = this.Width,
                Height = this.Height,
                Left = this.Left + 10,
                Top = this.Top + 10,
                ZIndex = this.ZIndex,
                RotationAngle = this.RotationAngle
            };

            // Ensure the shape is built properly after setting all properties
            clone.BuildShape();
            // Copy any visual Effect (e.g., drop shadow)
            if (this.Effect != null)
            {
                try { clone.Effect = this.Effect.Clone(); } catch { clone.Effect = this.Effect; }
            }
            return clone;
        }
    }
}
