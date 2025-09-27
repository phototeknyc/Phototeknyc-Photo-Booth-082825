using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DesignerCanvas.Controls.Primitives
{
    /// <summary>
    /// Represents a adorder to a specific CanvasItem.
    /// </summary>
    /// <remarks>This  class is used becaused it won't zoom as CanvasEntityItem is being zoomed.</remarks>
    public class CanvasAdorner : Control
    {
        public ICanvasItem AdornedObject { get; }

        public double Left
        {
            get { return (double)GetValue(LeftProperty); }
            set { SetValue(LeftProperty, value); }
        }

        public static readonly DependencyProperty LeftProperty =
            DependencyProperty.Register("Left", typeof(double), typeof(CanvasAdorner), new PropertyMetadata(0.0));

        public double Top
        {
            get { return (double)GetValue(TopProperty); }
            set { SetValue(TopProperty, value); }
        }

        public static readonly DependencyProperty TopProperty =
            DependencyProperty.Register("Top", typeof(double), typeof(CanvasAdorner), new PropertyMetadata(0.0));

        private DesignerCanvas _ParentCanvas;

        public DesignerCanvas ParentCanvas => _ParentCanvas;

        internal void SetCanvas(Controls.DesignerCanvas canvas)
        {
            if (_ParentCanvas != null) _ParentCanvas.ZoomChanged -= ParentCanvas_ZoomChanged;
            _ParentCanvas = canvas;
            if (canvas != null) canvas.ZoomChanged += ParentCanvas_ZoomChanged;
            if (canvas != null) OnUpdateLayout();
        }

        private void ParentCanvas_ZoomChanged(object sender, EventArgs e)
        {
            Debug.Assert(sender == _ParentCanvas);
            OnUpdateLayout();
        }
        
        protected virtual void OnCanvasScaleChanged()
        {
            // Update handle scale when canvas scale changes - override in derived classes
        }

        public CanvasAdorner(ICanvasItem adornedObject)
        {
            if (adornedObject == null) throw new ArgumentNullException(nameof(adornedObject));
            AdornedObject = adornedObject;
            var npc = adornedObject as INotifyPropertyChanged;
            if (npc != null)
            {
                PropertyChangedEventManager.AddHandler(npc, AdornedObject_PropertyChanged, "");
            }
            SetBinding(Canvas.LeftProperty, new Binding("Left") {RelativeSource = RelativeSource.Self});
            SetBinding(Canvas.TopProperty, new Binding("Top") {RelativeSource = RelativeSource.Self});
        }

        static CanvasAdorner()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(CanvasAdorner), new FrameworkPropertyMetadata(typeof(CanvasAdorner)));
        }

        private void AdornedObject_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            Debug.Assert(sender == AdornedObject);
            OnAdornedObjectPropertyChanged(e.PropertyName);
        }

        protected virtual void OnAdornedObjectPropertyChanged(string propertyName)
        {
            if (_ParentCanvas == null) return;
            // Update on bounds or any fundamental position/size change
            if (propertyName == nameof(AdornedObject.Bounds)
                || propertyName == nameof(ICanvasItem.Left)
                || propertyName == nameof(ICanvasItem.Top)
                || propertyName == "Width"
                || propertyName == "Height")
            {
                OnUpdateLayout();
            }
        }

        protected virtual void OnUpdateLayout()
        {
            
        }
    }

    /// <summary>
    /// Used for rendering a handle for resizing &amp; rotation.
    /// </summary>
    public class ResizeRotateAdorner : CanvasAdorner
    {
        private RotateTransform rotateTransform = new RotateTransform();
        
        // Scale property for handle sizing
        public static readonly DependencyProperty HandleScaleProperty =
            DependencyProperty.Register("HandleScale", typeof(double), typeof(ResizeRotateAdorner), 
                new PropertyMetadata(1.0));

        public double HandleScale
        {
            get { return (double)GetValue(HandleScaleProperty); }
            set { SetValue(HandleScaleProperty, value); }
        }

        public bool CanResize
        {
            get { return (bool)GetValue(CanResizeProperty); }
            set { SetValue(CanResizeProperty, value); }
        }

        public static readonly DependencyProperty CanResizeProperty =
            DependencyProperty.Register("CanResize", typeof(bool), typeof(ResizeRotateAdorner), new PropertyMetadata(true));

        public new IBoxCanvasItem AdornedObject => (IBoxCanvasItem) base.AdornedObject;

        public ResizeRotateAdorner(IBoxCanvasItem adornedObject) : base(adornedObject)
        {
            SnapsToDevicePixels = true;
            this.DataContext = adornedObject;
            this.RenderTransform = rotateTransform;
            this.RenderTransformOrigin = new Point(0.5, 0.5);
            CanResize = adornedObject.Resizeable;
        }

        static ResizeRotateAdorner()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ResizeRotateAdorner), new FrameworkPropertyMetadata(typeof(ResizeRotateAdorner)));
        }

        protected override void OnAdornedObjectPropertyChanged(string propertyName)
        {
            base.OnAdornedObjectPropertyChanged(propertyName);
            if (ParentCanvas == null) return;
            if (propertyName == nameof(IBoxCanvasItem.Angle)) OnUpdateLayout();
        }

        protected override void OnUpdateLayout()
        {
            base.OnUpdateLayout();
            var zoom = ParentCanvas.Zoom/100.0;
            Left = AdornedObject.Left*zoom;
            Top = AdornedObject.Top * zoom;
            Width = AdornedObject.Width*zoom;
            Height = AdornedObject.Height*zoom;
            rotateTransform.Angle = AdornedObject.Angle;

            // Update handle scale based on current canvas scale
            if (ParentCanvas != null)
            {
                var newScale = ParentCanvas.CurrentScale;
                if (Math.Abs(HandleScale - newScale) > 0.001) // Only update if scale actually changed
                {
                    HandleScale = newScale;
                    System.Diagnostics.Debug.WriteLine($"ResizeRotateAdorner HandleScale updated to: {HandleScale}");
                }
            }
        }
    }

    public class SizeAdorner : CanvasAdorner
    {
        private RotateTransform rotateTransform = new RotateTransform();
        
        // Scale property for tooltip sizing
        public static readonly DependencyProperty HandleScaleProperty =
            DependencyProperty.Register("HandleScale", typeof(double), typeof(SizeAdorner), 
                new PropertyMetadata(1.0));

        public double HandleScale
        {
            get { return (double)GetValue(HandleScaleProperty); }
            set { SetValue(HandleScaleProperty, value); }
        }

        public new IBoxCanvasItem AdornedObject => (IBoxCanvasItem)base.AdornedObject;

        public SizeAdorner(IBoxCanvasItem adornedObject) : base(adornedObject)
        {
            SnapsToDevicePixels = true;
            this.DataContext = adornedObject;
            this.RenderTransform = rotateTransform;
            this.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        protected override void OnAdornedObjectPropertyChanged(string propertyName)
        {
            base.OnAdornedObjectPropertyChanged(propertyName);
            if (ParentCanvas == null) return;
            if (propertyName == nameof(IBoxCanvasItem.Angle)) OnUpdateLayout();
        }

        protected override void OnUpdateLayout()
        {
            base.OnUpdateLayout();
            var zoom = ParentCanvas.Zoom / 100.0;
            Left = AdornedObject.Left * zoom;
            Top = AdornedObject.Top * zoom;
            Width = AdornedObject.Width * zoom;
            Height = AdornedObject.Height * zoom;
            rotateTransform.Angle = AdornedObject.Angle;

            // Update handle scale for tooltip sizing
            if (ParentCanvas != null)
            {
                var newScale = ParentCanvas.CurrentScale;
                if (Math.Abs(HandleScale - newScale) > 0.001) // Only update if scale actually changed
                {
                    HandleScale = newScale;
                    System.Diagnostics.Debug.WriteLine($"SizeAdorner HandleScale updated to: {HandleScale}");
                }
            }
        }

        static SizeAdorner()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(SizeAdorner), new FrameworkPropertyMetadata(typeof(SizeAdorner)));
        }
    }
}
