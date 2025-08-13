using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DesignerCanvas.Controls.Primitives
{
    /// <summary>
    /// Used for drag &amp; moving objects on the canvas.
    /// </summary>
    public class DragThumb : Thumb
    {
        /// <summary>
        /// If the count of selected items exceeds this value,
        /// all the selected object except this one will be moved
        /// only when user releases the thumb.
        /// </summary>
        public const int InstantPreviewItemsThreshold = 200;

        private ICanvasItem destItem;
        private DesignerCanvas designer;

        public DragThumb()
        {
            DragStarted += DragThumb_DragStarted;
            DragDelta += DragThumb_DragDelta;
            DragCompleted += DragThumb_DragCompleted;
        }

        private bool instantPreview;

        private void DragThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            destItem = DataContext as ICanvasItem;
            if (destItem == null) return;
            designer = DesignerCanvas.FindDesignerCanvas(this);
            if (designer == null) return;
            instantPreview = designer.SelectedItems.Count < InstantPreviewItemsThreshold;
            foreach (var item in designer.SelectedItems)
            {
                item.NotifyUserDraggingStarted();
            }
        }

        private void DragThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (destItem == null) return;
            if (designer == null) return;
            var container = designer.ItemContainerGenerator.ContainerFromItem(destItem) as UIElement;
            if (container == null) return;
            var minLeft = double.MaxValue;
            var minTop = double.MaxValue;
            //var tf = TransformToAncestor(designer);
            var delta = new Point(e.HorizontalChange, e.VerticalChange);
            var tf = container.RenderTransform;
            if (tf != null) delta = tf.Transform(delta);
            foreach (var item in designer.SelectedItems)
            {
                minLeft = Math.Min(item.Left, minLeft);
                minTop = Math.Min(item.Top, minTop);
            }
            
            // Apply constraints to ensure image never goes completely off canvas
            var minOverlap = 50; // Minimum pixels that must remain visible on canvas
            var deltaX = delta.X;
            var deltaY = delta.Y;
            
            // Check constraints for all selected items to prevent complete disappearance
            foreach (var item in designer.SelectedItems.OfType<IBoxCanvasItem>())
            {
                var newLeft = item.Left + deltaX;
                var newTop = item.Top + deltaY;
                var newRight = newLeft + item.Width;
                var newBottom = newTop + item.Height;
                
                // Prevent moving completely off the left edge
                if (newRight < minOverlap)
                    deltaX = minOverlap - item.Left - item.Width;
                
                // Prevent moving completely off the right edge  
                if (newLeft > designer.ActualWidth - minOverlap)
                    deltaX = designer.ActualWidth - minOverlap - item.Left;
                
                // Prevent moving completely off the top edge
                if (newBottom < minOverlap)
                    deltaY = minOverlap - item.Top - item.Height;
                
                // Prevent moving completely off the bottom edge
                if (newTop > designer.ActualHeight - minOverlap)
                    deltaY = designer.ActualHeight - minOverlap - item.Top;
            }
            if (instantPreview)
            {
                // This operation may be slow.
                foreach (var item in designer.SelectedItems)
                {
                    item.NotifyUserDragging(deltaX, deltaY);
                    item.Left += deltaX;
                    item.Top += deltaY;
                }
            }
            else
            {
                destItem.NotifyUserDragging(deltaX, deltaY);
                destItem.Left += deltaX;
                destItem.Top += deltaY;
            }
            e.Handled = true;
        }

        private void DragThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (designer == null) return;
            var container = designer.ItemContainerGenerator.ContainerFromItem(destItem) as UIElement;
            if (container == null) return;
            if (!instantPreview)
            {
                var tf = TransformToAncestor((Visual)Parent);
                var delta = new Point(e.HorizontalChange, e.VerticalChange);
                delta = tf.Transform(delta);
                var minLeft = double.MaxValue;
                var minTop = double.MaxValue;
                foreach (var item in designer.SelectedItems.OfType<IBoxCanvasItem>())
                {
                    var left = item.Left;
                    var top = item.Top;
                    minLeft = double.IsNaN(left) ? 0 : Math.Min(left, minLeft);
                    minTop = double.IsNaN(top) ? 0 : Math.Min(top, minTop);
                }
                // Apply same constraints as in DragDelta to ensure image stays visible
                var minOverlap = 50; // Minimum pixels that must remain visible on canvas
                var deltaHorizontal = delta.X;
                var deltaVertical = delta.Y;
                
                // Check constraints for all selected items to prevent complete disappearance
                foreach (var constraintItem in designer.SelectedItems.OfType<IBoxCanvasItem>())
                {
                    var newLeft = constraintItem.Left + deltaHorizontal;
                    var newTop = constraintItem.Top + deltaVertical;
                    var newRight = newLeft + constraintItem.Width;
                    var newBottom = newTop + constraintItem.Height;
                    
                    // Prevent moving completely off the left edge
                    if (newRight < minOverlap)
                        deltaHorizontal = minOverlap - constraintItem.Left - constraintItem.Width;
                    
                    // Prevent moving completely off the right edge  
                    if (newLeft > designer.ActualWidth - minOverlap)
                        deltaHorizontal = designer.ActualWidth - minOverlap - constraintItem.Left;
                    
                    // Prevent moving completely off the top edge
                    if (newBottom < minOverlap)
                        deltaVertical = minOverlap - constraintItem.Top - constraintItem.Height;
                    
                    // Prevent moving completely off the bottom edge
                    if (newTop > designer.ActualHeight - minOverlap)
                        deltaVertical = designer.ActualHeight - minOverlap - constraintItem.Top;
                }
                foreach (var item in designer.SelectedItems.OfType<IBoxCanvasItem>())
                {
                    if (item == destItem) continue;
                    item.NotifyUserDragging(deltaHorizontal, deltaVertical);
                    item.Left += deltaHorizontal;
                    item.Top += deltaVertical;
                }
            }
            foreach (var item in designer.SelectedItems)
            {
                item.NotifyUserDraggingStarted();
            }
            designer.InvalidateMeasure();
        }
    }
}