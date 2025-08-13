using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace DesignerCanvas
{
    public enum ShapeType
    {
        Rectangle,
        Circle,
        Line
    }

    public class ShapeCanvasItem : CanvasItem
    {
        private ShapeType _shapeType;
        private Brush _fill;
        private Brush _stroke;
        private double _strokeThickness;
        private bool _hasShadow;
        private Color _shadowColor;
        private double _shadowOffsetX;
        private double _shadowOffsetY;
        private double _shadowBlurRadius;
        private bool _hasNoFill;
        private bool _hasNoStroke;

        public ShapeType ShapeType
        {
            get { return _shapeType; }
            set { SetProperty(ref _shapeType, value); }
        }

        public Brush Fill
        {
            get { return HasNoFill ? null : _fill; }
            set { SetProperty(ref _fill, value); }
        }

        public Brush Stroke
        {
            get { return HasNoStroke ? null : _stroke; }
            set { SetProperty(ref _stroke, value); }
        }

        public double StrokeThickness
        {
            get { return _strokeThickness; }
            set { SetProperty(ref _strokeThickness, value); }
        }

        public bool HasShadow
        {
            get { return _hasShadow; }
            set { SetProperty(ref _hasShadow, value); }
        }

        public Color ShadowColor
        {
            get { return _shadowColor; }
            set { SetProperty(ref _shadowColor, value); }
        }

        public double ShadowOffsetX
        {
            get { return _shadowOffsetX; }
            set { SetProperty(ref _shadowOffsetX, value); }
        }

        public double ShadowOffsetY
        {
            get { return _shadowOffsetY; }
            set { SetProperty(ref _shadowOffsetY, value); }
        }

        public double ShadowBlurRadius
        {
            get { return _shadowBlurRadius; }
            set { SetProperty(ref _shadowBlurRadius, value); }
        }

        public bool HasNoFill
        {
            get { return _hasNoFill; }
            set 
            { 
                SetProperty(ref _hasNoFill, value);
                OnPropertyChanged(nameof(Fill)); // Notify Fill property changed
            }
        }

        public bool HasNoStroke
        {
            get { return _hasNoStroke; }
            set 
            { 
                SetProperty(ref _hasNoStroke, value);
                OnPropertyChanged(nameof(Stroke)); // Notify Stroke property changed
            }
        }

        public ShapeCanvasItem(double left, double top, ShapeType shapeType)
            : base(left, top, 100, 100, 1.0, 1)
        {
            ShapeType = shapeType;
            Fill = Brushes.LightBlue;
            Stroke = Brushes.DarkBlue;
            StrokeThickness = 2;
            Resizeable = true;
            
            // Default shadow settings
            HasShadow = false;
            ShadowColor = Colors.Gray;
            ShadowOffsetX = 3;
            ShadowOffsetY = 3;
            ShadowBlurRadius = 5;
            
            // Default fill/stroke settings
            HasNoFill = false;
            HasNoStroke = false;
        }
    }
}