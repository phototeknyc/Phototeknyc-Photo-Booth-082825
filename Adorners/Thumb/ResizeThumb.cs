using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Collections.Generic;
using Photobooth.MVVM.ViewModels.Designer;

namespace Photobooth.Adorners.Thumb
{
    public class ResizeThumb : System.Windows.Controls.Primitives.Thumb
	{
        private RotateTransform rotateTransform;
        private double angle;
        private Adorner adorner;
        private Point transformOrigin;
        private ContentControl designerItem;
        private Canvas canvas;

        public ResizeThumb()
        {
            DragStarted += new DragStartedEventHandler(this.ResizeThumb_DragStarted);
            DragDelta += new DragDeltaEventHandler(this.ResizeThumb_DragDelta);
            DragCompleted += new DragCompletedEventHandler(this.ResizeThumb_DragCompleted);
        }

        private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ResizeThumb_DragStarted triggered!");
            Console.WriteLine("ResizeThumb_DragStarted triggered!");
            
            this.designerItem = this.DataContext as ContentControl;

            if (this.designerItem != null)
            {
                System.Diagnostics.Debug.WriteLine($"DesignerItem found: Width={designerItem.Width}, Height={designerItem.Height}");
                Console.WriteLine($"DesignerItem found: Width={designerItem.Width}, Height={designerItem.Height}");
                
                this.canvas = VisualTreeHelper.GetParent(this.designerItem) as Canvas;

                if (this.canvas != null)
                {
                    this.transformOrigin = this.designerItem.RenderTransformOrigin;

                    this.rotateTransform = this.designerItem.RenderTransform as RotateTransform;
                    if (this.rotateTransform != null)
                    {
                        this.angle = this.rotateTransform.Angle * Math.PI / 180.0;
                    }
                    else
                    {
                        this.angle = 0.0d;
                    }

                    AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(this.canvas);
                    if (adornerLayer != null)
                    {
                        this.adorner = new SizeAdorner(this.designerItem);
                        adornerLayer.Add(this.adorner);
                        System.Diagnostics.Debug.WriteLine("SizeAdorner created and added to layer");
                        Console.WriteLine("SizeAdorner created and added to layer");
                    }
                }
            }
        }

        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            Console.WriteLine("ResizeThumb_DragDelta called!");
            System.Diagnostics.Debug.WriteLine("ResizeThumb_DragDelta called!");
            if (this.designerItem != null)
            {
                double deltaVertical, deltaHorizontal;

                switch (VerticalAlignment)
                {
                    case System.Windows.VerticalAlignment.Bottom:
                        deltaVertical = Math.Min(-e.VerticalChange, this.designerItem.ActualHeight - this.designerItem.MinHeight);
                        Canvas.SetTop(this.designerItem, Canvas.GetTop(this.designerItem) + (this.transformOrigin.Y * deltaVertical * (1 - Math.Cos(-this.angle))));
                        Canvas.SetLeft(this.designerItem, Canvas.GetLeft(this.designerItem) - deltaVertical * this.transformOrigin.Y * Math.Sin(-this.angle));
                        this.designerItem.Height -= deltaVertical;
                        break;
                    case System.Windows.VerticalAlignment.Top:
                        deltaVertical = Math.Min(e.VerticalChange, this.designerItem.ActualHeight - this.designerItem.MinHeight);
                        Canvas.SetTop(this.designerItem, Canvas.GetTop(this.designerItem) + deltaVertical * Math.Cos(-this.angle) + (this.transformOrigin.Y * deltaVertical * (1 - Math.Cos(-this.angle))));
                        Canvas.SetLeft(this.designerItem, Canvas.GetLeft(this.designerItem) + deltaVertical * Math.Sin(-this.angle) - (this.transformOrigin.Y * deltaVertical * Math.Sin(-this.angle)));
                        this.designerItem.Height -= deltaVertical;
                        break;
                    default:
                        break;
                }

                switch (HorizontalAlignment)
                {
                    case System.Windows.HorizontalAlignment.Left:
                        deltaHorizontal = Math.Min(e.HorizontalChange, this.designerItem.ActualWidth - this.designerItem.MinWidth);
                        Canvas.SetTop(this.designerItem, Canvas.GetTop(this.designerItem) + deltaHorizontal * Math.Sin(this.angle) - this.transformOrigin.X * deltaHorizontal * Math.Sin(this.angle));
                        Canvas.SetLeft(this.designerItem, Canvas.GetLeft(this.designerItem) + deltaHorizontal * Math.Cos(this.angle) + (this.transformOrigin.X * deltaHorizontal * (1 - Math.Cos(this.angle))));
                        this.designerItem.Width -= deltaHorizontal;
                        break;
                    case System.Windows.HorizontalAlignment.Right:
                        deltaHorizontal = Math.Min(-e.HorizontalChange, this.designerItem.ActualWidth - this.designerItem.MinWidth);
                        Canvas.SetTop(this.designerItem, Canvas.GetTop(this.designerItem) - this.transformOrigin.X * deltaHorizontal * Math.Sin(this.angle));
                        Canvas.SetLeft(this.designerItem, Canvas.GetLeft(this.designerItem) + (deltaHorizontal * this.transformOrigin.X * (1 - Math.Cos(this.angle))));
                        this.designerItem.Width -= deltaHorizontal;
                        break;
                    default:
                        break;
                }
                
                // Update ViewModel properties in real-time
                UpdateViewModelProperties();
            }

            e.Handled = true;
        }
        
        private void UpdateViewModelProperties()
        {
            Console.WriteLine("ResizeThumb: UpdateViewModelProperties called");
            System.Diagnostics.Debug.WriteLine("ResizeThumb: UpdateViewModelProperties called");
            
            // Try multiple approaches to find the DesignerVM
            DesignerVM designerVM = null;
            
            // Approach 1: Check the canvas DataContext directly
            if (this.canvas?.DataContext is DesignerVM vm1)
            {
                designerVM = vm1;
                Console.WriteLine("ResizeThumb: Found DesignerVM directly on canvas");
            }
            // Approach 2: Check the designerItem's DataContext
            else if (this.designerItem?.DataContext is DesignerVM vm2)
            {
                designerVM = vm2;
                Console.WriteLine("ResizeThumb: Found DesignerVM on designerItem");
            }
            // Approach 3: Traverse up the visual tree
            else
            {
                FrameworkElement element = this.canvas;
                while (element != null && designerVM == null)
                {
                    if (element.DataContext is DesignerVM vm)
                    {
                        designerVM = vm;
                        Console.WriteLine($"ResizeThumb: Found DesignerVM on {element.GetType().Name}");
                        break;
                    }
                    element = VisualTreeHelper.GetParent(element) as FrameworkElement;
                }
            }
            
            // Approach 4: Try to get from MainPage as fallback
            if (designerVM == null)
            {
                var mainPage = Photobooth.Pages.MainPage.Instance;
                if (mainPage?.ViewModel is DesignerVM vm)
                {
                    designerVM = vm;
                    Console.WriteLine("ResizeThumb: Found DesignerVM from MainPage.Instance");
                }
            }
            
            if (designerVM != null)
            {
                double left = Canvas.GetLeft(this.designerItem);
                double top = Canvas.GetTop(this.designerItem);
                double width = this.designerItem.Width;
                double height = this.designerItem.Height;
                
                Console.WriteLine($"ResizeThumb: Updating properties - L:{left:F0}, T:{top:F0}, W:{width:F0}, H:{height:F0}");
                System.Diagnostics.Debug.WriteLine($"ResizeThumb: Updating properties - L:{left:F0}, T:{top:F0}, W:{width:F0}, H:{height:F0}");
                
                // Update position and size properties using the safe method
                designerVM.UpdatePropertyDisplayValues(left, top, width, height);
            }
            else
            {
                // Debug: Log if DesignerVM not found anywhere!");
                System.Diagnostics.Debug.WriteLine("ResizeThumb: DesignerVM not found anywhere!");
                Console.WriteLine("ResizeThumb: DesignerVM not found anywhere!");
            }
        }

        private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (this.adorner != null)
            {
                AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(this.canvas);
                if (adornerLayer != null)
                {
                    adornerLayer.Remove(this.adorner);
                }

                this.adorner = null;
            }
        }
    }
}
