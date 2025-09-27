using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesignerCanvas.Controls.Primitives
{
    /// <summary>
    /// Used for rendering <see cref="CanvasItem"/> in <see cref="DesignerCanvas" />.
    /// </summary>
    [TemplatePart(Name = "PART_DragThumb", Type = typeof(DragThumb))]
    [TemplatePart(Name = "PART_ContentPresenter", Type = typeof(ContentPresenter))]
    public class DesignerCanvasItemContainer : ContentControl
    {
        private ContentPresenter contentPresenter;

        public bool IsSelected
        {
            get { return (bool)GetValue(IsSelectedProperty); }
            set { SetValue(IsSelectedProperty, value); }
        }

        public static readonly DependencyProperty IsSelectedProperty = Selector.IsSelectedProperty;

        /// <summary>
        /// Determines whether the entity can be resized.
        /// </summary>
        public bool Resizeable
        {
            get { return (bool)GetValue(ResizeableProperty); }
            set { SetValue(ResizeableProperty, value); }
        }

        public static readonly DependencyProperty ResizeableProperty =
            DependencyProperty.Register("Resizeable", typeof(bool), typeof(DesignerCanvasItemContainer), new PropertyMetadata(true));


        public DesignerCanvas ParentDesigner => Controls.DesignerCanvas.FindDesignerCanvas(this);

        /// <summary>
        /// When overridden in a derived class, is invoked whenever application code or internal processes call <see cref="M:System.Windows.FrameworkElement.ApplyTemplate"/>.
        /// </summary>
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            contentPresenter = GetTemplateChild("PART_ContentPresenter") as ContentPresenter;
        }

        /// <summary>
        /// Per-pixel hit testing for images to allow pass-through on transparent pixels.
        /// If the click/touch falls on a fully transparent pixel of an ImageCanvasItem,
        /// ignore this container so items underneath can be selected/resized.
        /// </summary>
        protected override System.Windows.Media.HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        {
            try
            {
                // Only apply per-pixel logic for image items
                if (this.Content is global::DesignerCanvas.ImageCanvasItem imgItem)
                {
                    // Find the Image element rendered by the DataTemplate
                    var searchRoot = (DependencyObject)(contentPresenter as DependencyObject ?? this);
                    var image = FindVisualChild<System.Windows.Controls.Image>(searchRoot);
                    if (image != null && imgItem.Image is BitmapSource bmpSource)
                    {
                        // Map the hit point to the Image's coordinate space
                        var pointInImage = this.TranslatePoint(hitTestParameters.HitPoint, image);

                        // Determine the actual drawn rectangle inside the Image (accounts for Stretch=Uniform)
                        var drawnRect = GetUniformRenderRect(image.ActualWidth, image.ActualHeight, bmpSource.PixelWidth, bmpSource.PixelHeight);

                        // If the point is outside the drawn image area (letterboxing), treat as transparent
                        if (!drawnRect.Contains(pointInImage))
                        {
                            return null; // pass-through to underlying items
                        }

                        // Normalize within the drawn rect and map to source pixel coordinates
                        double nx = (pointInImage.X - drawnRect.X) / Math.Max(1.0, drawnRect.Width);
                        double ny = (pointInImage.Y - drawnRect.Y) / Math.Max(1.0, drawnRect.Height);
                        int px = Math.Min(bmpSource.PixelWidth - 1, Math.Max(0, (int)Math.Floor(nx * bmpSource.PixelWidth)));
                        int py = Math.Min(bmpSource.PixelHeight - 1, Math.Max(0, (int)Math.Floor(ny * bmpSource.PixelHeight)));

                        // Ensure format is BGRA32 for easy alpha read
                        BitmapSource src = bmpSource;
                        if (src.Format != PixelFormats.Bgra32 && src.Format != PixelFormats.Pbgra32)
                        {
                            src = new FormatConvertedBitmap(bmpSource, PixelFormats.Bgra32, null, 0);
                        }

                        byte[] pixel = new byte[4]; // BGRA
                        try
                        {
                            src.CopyPixels(new Int32Rect(px, py, 1, 1), pixel, 4, 0);
                        }
                        catch
                        {
                            // If reading fails, fall back to default behavior
                            return base.HitTestCore(hitTestParameters);
                        }

                        byte alpha = pixel[3];
                        const byte TransparentThreshold = 10; // consider nearly transparent as transparent
                        if (alpha <= TransparentThreshold)
                        {
                            return null; // transparent pixel - let hits pass through
                        }

                        // Opaque pixel - treat as a valid hit on this container
                        return new PointHitTestResult(this, hitTestParameters.HitPoint);
                    }
                }
            }
            catch
            {
                // Swallow and fall back to default hit testing
            }

            return base.HitTestCore(hitTestParameters);
        }

        private static Rect GetUniformRenderRect(double targetWidth, double targetHeight, int sourcePixelWidth, int sourcePixelHeight)
        {
            if (targetWidth <= 0 || targetHeight <= 0 || sourcePixelWidth <= 0 || sourcePixelHeight <= 0)
                return new Rect(0, 0, 0, 0);

            double controlAR = targetWidth / targetHeight;
            double sourceAR = (double)sourcePixelWidth / sourcePixelHeight;

            if (controlAR > sourceAR)
            {
                // Constrained by height
                double drawHeight = targetHeight;
                double drawWidth = drawHeight * sourceAR;
                double offsetX = (targetWidth - drawWidth) / 2.0;
                return new Rect(offsetX, 0, drawWidth, drawHeight);
            }
            else
            {
                // Constrained by width
                double drawWidth = targetWidth;
                double drawHeight = drawWidth / sourceAR;
                double offsetY = (targetHeight - drawHeight) / 2.0;
                return new Rect(0, offsetY, drawWidth, drawHeight);
            }
        }

        // Not supported yet.

        public static Geometry GetContainerClip(DependencyObject obj)
        {
            return (Geometry)obj.GetValue(ContainerClipProperty);
        }

        public static void SetContainerClip(DependencyObject obj, Geometry value)
        {
            obj.SetValue(ContainerClipProperty, value);
        }

        public static readonly DependencyProperty ContainerClipProperty =
            DependencyProperty.RegisterAttached("ContainerClip",
                typeof(Geometry), typeof(DesignerCanvasItemContainer), new FrameworkPropertyMetadata(null, (d, e) =>
                {
                    var container = (d as FrameworkElement)?.Parent as DesignerCanvasItemContainer;
                    container?.NotifyContentContainerClipChanged((Geometry)e.NewValue);
                }));

        /// <summary>
        /// Raises when user attempt to start dragging the item.
        /// </summary>
        public static readonly RoutedEvent BeforeDraggingStartedEvent =
            EventManager.RegisterRoutedEvent("BeforeDraggingStarted", RoutingStrategy.Direct,
                typeof(RoutedEventHandler), typeof(DesignerCanvasItemContainer));

        public static void AddBeforeDraggingStartedHandler(DependencyObject d, RoutedEventHandler handler)
        {
            (d as UIElement)?.AddHandler(BeforeDraggingStartedEvent, handler);
        }

        public static void RemoveBeforeDraggingStartedHandler(DependencyObject d, RoutedEventHandler handler)
        {
            (d as UIElement)?.RemoveHandler(BeforeDraggingStartedEvent, handler);
        }

        #region Interactions

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);
            
            // Handle right-click for context menu
            if (e.RightButton == MouseButtonState.Pressed)
            {
                ParentDesigner?.ShowItemSelectionContextMenu(this, e.GetPosition(ParentDesigner));
                e.Handled = true;
                return;
            }
            
            ParentDesigner?.NotifyItemMouseDown(this);
            Focus();
            if (VisualTreeHelper.GetChildrenCount(contentPresenter) > 0)
            {
                var content = VisualTreeHelper.GetChild(contentPresenter, 0) as UIElement;
                if (content != null)
                {
                    var ee = new RoutedEventArgs(BeforeDraggingStartedEvent, this);
                    content.RaiseEvent(ee);
                }
            }
        }

        protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            
            // Check if this is a text item
            if (DataContext is TextCanvasItem textItem)
            {
                // Stop editing on all other text items first
                ParentDesigner?.StopAllTextEditing();
                
                // Start editing this text item
                textItem.StartEditing();
                
                // Focus the inline text editor after a short delay to ensure the UI has updated
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var textBox = FindVisualChild<TextBox>(this);
                    textBox?.Focus();
                    textBox?.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Render);
                
                e.Handled = true;
            }
        }

        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T)
                    return (T)child;
                
                T childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        #endregion

        private CanvasAdorner designerAdorner;

        private void UpdateDesignerAdorner()
        {
            var pd = ParentDesigner;
            var obj = DataContext as ICanvasItem;
            if (!IsSelected || pd == null || obj == null)
            {
                if (designerAdorner != null)
                {
                    designerAdorner.ParentCanvas.RemoveAdorner(designerAdorner);
                    designerAdorner = null;
                }
            }
            else
            {
                if (designerAdorner == null)
                {
                    designerAdorner = ParentDesigner.GenerateDesigningAdornerFormItem(obj);
                    if (designerAdorner != null)
                    {
                        designerAdorner.SetCanvas(pd);
                        pd.AddAdorner(designerAdorner);
                    }
                }
            }
        }

        private void NotifyContentContainerClipChanged(Geometry newClip)
        {
            this.Clip = newClip;
        }

        /// <summary>
        /// Invoked when the parent of this element in the visual tree is changed. Overrides <see cref="M:System.Windows.UIElement.OnVisualParentChanged(System.Windows.DependencyObject)"/>.
        /// </summary>
        /// <param name="oldParent">The old parent element. May be null to indicate that the element did not have a visual parent previously.</param>
        protected override void OnVisualParentChanged(DependencyObject oldParent)
        {
            base.OnVisualParentChanged(oldParent);
            UpdateDesignerAdorner();
        }

#if DEBUG
        // See OnApplyTemplate()
        private static readonly Brush BoundaryIndicatorBrush =
            new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00));
#endif

        public DesignerCanvasItemContainer()
        {
#if DEBUG
            // Display the boundary of the container. This is used for debugging.
            BorderThickness = new Thickness(1, 1, 1, 1);
            BorderBrush = BoundaryIndicatorBrush;
#endif
        }

        static DesignerCanvasItemContainer()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(DesignerCanvasItemContainer), new FrameworkPropertyMetadata(typeof(DesignerCanvasItemContainer)));
            Selector.IsSelectedProperty.OverrideMetadata(typeof(DesignerCanvasItemContainer),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    (sender, e) =>
                    {
                        var s = (DesignerCanvasItemContainer)sender;
                        s.UpdateDesignerAdorner();
                        s.ParentDesigner?.NotifyItemIsSelectedChanged(s);
                    }));
            FocusableProperty.OverrideMetadata(typeof(DesignerCanvasItemContainer), new FrameworkPropertyMetadata(true));
        }
    }
}
