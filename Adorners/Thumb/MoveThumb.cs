using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Photobooth.MVVM.ViewModels.Designer;

namespace Photobooth.Adorners.Thumb
{
    public class MoveThumb : System.Windows.Controls.Primitives.Thumb
	{
        private RotateTransform rotateTransform;
        private ContentControl designerItem;

        public MoveThumb()
        {
            DragStarted += new DragStartedEventHandler(this.MoveThumb_DragStarted);
            DragDelta += new DragDeltaEventHandler(this.MoveThumb_DragDelta);
        }

        private void MoveThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            this.designerItem = DataContext as ContentControl;

            if (this.designerItem != null)
            {
                this.rotateTransform = this.designerItem.RenderTransform as RotateTransform;
            }
        }

        private void MoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            Console.WriteLine("MoveThumb_DragDelta called!");
            System.Diagnostics.Debug.WriteLine("MoveThumb_DragDelta called!");
            if (this.designerItem != null)
            {
                Point dragDelta = new Point(e.HorizontalChange, e.VerticalChange);

                if (this.rotateTransform != null)
                {
                    dragDelta = this.rotateTransform.Transform(dragDelta);
                }

                Canvas.SetLeft(this.designerItem, Canvas.GetLeft(this.designerItem) + dragDelta.X);
                Canvas.SetTop(this.designerItem, Canvas.GetTop(this.designerItem) + dragDelta.Y);
                
                // Update ViewModel properties in real-time
                UpdateViewModelProperties();
            }
        }
        
        private void UpdateViewModelProperties()
        {
            Console.WriteLine("MoveThumb: UpdateViewModelProperties called");
            System.Diagnostics.Debug.WriteLine("MoveThumb: UpdateViewModelProperties called");
            
            // Try multiple approaches to find the DesignerVM
            DesignerVM designerVM = null;
            var canvas = VisualTreeHelper.GetParent(this.designerItem) as Canvas;
            
            // Approach 1: Check the canvas DataContext
            if (canvas?.DataContext is DesignerVM vm1)
            {
                designerVM = vm1;
                Console.WriteLine("MoveThumb: Found DesignerVM on canvas");
            }
            // Approach 2: Check the designerItem's DataContext
            else if (this.designerItem?.DataContext is DesignerVM vm2)
            {
                designerVM = vm2;
                Console.WriteLine("MoveThumb: Found DesignerVM on designerItem");
            }
            // Approach 3: Traverse up the visual tree
            else
            {
                FrameworkElement element = canvas != null ? (FrameworkElement)canvas : this.designerItem;
                while (element != null && designerVM == null)
                {
                    if (element.DataContext is DesignerVM vm)
                    {
                        designerVM = vm;
                        Console.WriteLine($"MoveThumb: Found DesignerVM on {element.GetType().Name}");
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
                    Console.WriteLine("MoveThumb: Found DesignerVM from MainPage.Instance");
                }
            }
            
            if (designerVM != null)
            {
                double left = Canvas.GetLeft(this.designerItem);
                double top = Canvas.GetTop(this.designerItem);
                double width = this.designerItem.Width;
                double height = this.designerItem.Height;
                
                Console.WriteLine($"MoveThumb: Updating properties - L:{left:F0}, T:{top:F0}, W:{width:F0}, H:{height:F0}");
                System.Diagnostics.Debug.WriteLine($"MoveThumb: Updating properties - L:{left:F0}, T:{top:F0}, W:{width:F0}, H:{height:F0}");
                
                // Update position and size properties using the safe method
                designerVM.UpdatePropertyDisplayValues(left, top, width, height);
            }
            else
            {
                // Debug: Log if DesignerVM not found anywhere!
                System.Diagnostics.Debug.WriteLine("MoveThumb: DesignerVM not found anywhere!");
                Console.WriteLine("MoveThumb: DesignerVM not found anywhere!");
            }
        }
    }
}
