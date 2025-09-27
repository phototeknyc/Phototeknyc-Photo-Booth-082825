using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DesignerCanvas.Controls.Primitives
{
    /// <summary>
    /// Touch-enabled version of DragThumb with multi-touch support
    /// </summary>
    public class TouchDragThumb : DragThumb
    {
        private readonly Dictionary<int, TouchPoint> _activeTouches = new Dictionary<int, TouchPoint>();
        private IBoxCanvasItem _targetItem;
        private DesignerCanvas _designer;
        private bool _isDragging;

        public TouchDragThumb()
        {
            // Enable touch
            IsManipulationEnabled = true;
            
            // Increase touch target size for better usability
            MinWidth = 44;
            MinHeight = 44;
            
            // Touch events
            TouchDown += TouchDragThumb_TouchDown;
            TouchMove += TouchDragThumb_TouchMove;
            TouchUp += TouchDragThumb_TouchUp;
            
            // Manipulation events for gestures
            ManipulationStarting += TouchDragThumb_ManipulationStarting;
            ManipulationDelta += TouchDragThumb_ManipulationDelta;
            ManipulationCompleted += TouchDragThumb_ManipulationCompleted;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            
            // Find the target item and designer canvas when the template is applied
            Loaded += (s, e) => {
                _targetItem = VisualTreeHelperExtensions.FindAncestor<DesignerCanvasItemContainer>(this)?.Content as IBoxCanvasItem;
                _designer = VisualTreeHelperExtensions.FindAncestor<DesignerCanvas>(this);
            };
        }

        #region Touch Events

        private void TouchDragThumb_TouchDown(object sender, TouchEventArgs e)
        {
            // Track touch in designer coordinate space to keep deltas consistent
            var touchPoint = _designer != null ? e.GetTouchPoint(_designer) : e.GetTouchPoint(this);
            var touchId = e.TouchDevice.Id;
            
            _activeTouches[touchId] = touchPoint;
            
            if (!_isDragging && _targetItem != null)
            {
                _isDragging = true;
                
                // Select the item if not already selected
                if (_designer != null && !_designer.SelectedItems.Contains(_targetItem))
                {
                    if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
                    {
                        _designer.SelectedItems.Clear();
                    }
                    _designer.SelectedItems.Add(_targetItem);
                }
                _designer?.BeginBatchDrag();
                
                // Provide haptic feedback if available
                ProvideHapticFeedback();
            }
            
            CaptureTouch(e.TouchDevice);
            e.Handled = true;
        }

        private void TouchDragThumb_TouchMove(object sender, TouchEventArgs e)
        {
            if (!_isDragging || _targetItem == null)
                return;
                
            var relativeTo = (System.Windows.IInputElement)_designer ?? (System.Windows.IInputElement)this;
            var touchPoint = e.GetTouchPoint(relativeTo);
            var touchId = e.TouchDevice.Id;
            
            if (!_activeTouches.ContainsKey(touchId))
                return;
                
            var previousPoint = _activeTouches[touchId].Position;
            _activeTouches[touchId] = touchPoint;
            
            var deltaX = touchPoint.Position.X - previousPoint.X;
            var deltaY = touchPoint.Position.Y - previousPoint.Y;
            // Ignore tiny jitters
            if (Math.Abs(deltaX) < 0.25 && Math.Abs(deltaY) < 0.25)
            {
                e.Handled = true;
                return;
            }
            // Convert to canvas virtual units (account for zoom and external scale)
            var z = _designer?.Zoom / 100.0 ?? 1.0;
            var s = _designer?.CurrentScale ?? 1.0;
            var scale = z * s;
            if (scale <= 0) scale = 1.0;

            // Apply movement with constraints in virtual coordinates
            MoveItemWithConstraints(_targetItem, deltaX / scale, deltaY / scale);
            
            e.Handled = true;
        }

        private void TouchDragThumb_TouchUp(object sender, TouchEventArgs e)
        {
            var touchId = e.TouchDevice.Id;
            _activeTouches.Remove(touchId);
            
            if (_activeTouches.Count == 0)
            {
                _isDragging = false;
                _designer?.EndBatchDrag();
            }
            
            ReleaseTouchCapture(e.TouchDevice);
            e.Handled = true;
        }

        #endregion

        #region Manipulation Events

        private void TouchDragThumb_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.ManipulationContainer = _designer;
            e.Handled = true;
        }

        private void TouchDragThumb_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            if (_targetItem == null || _designer == null)
                return;
                
            // Handle translation
            var deltaX = e.DeltaManipulation.Translation.X;
            var deltaY = e.DeltaManipulation.Translation.Y;
            
            // Avoid duplicate single-finger translation (TouchMove already handles it). Use manipulations for multi-touch only.
            if (_activeTouches.Count >= 2 && (Math.Abs(deltaX) > 0.5 || Math.Abs(deltaY) > 0.5))
            {
                // Convert to virtual units
                var z = _designer?.Zoom / 100.0 ?? 1.0;
                var s = _designer?.CurrentScale ?? 1.0;
                var scale = z * s;
                if (scale <= 0) scale = 1.0;
                var vx = deltaX / scale;
                var vy = deltaY / scale;
                // Move all selected items
                foreach (var item in _designer.SelectedItems.ToList())
                {
                    if (item is IBoxCanvasItem boxItem)
                    {
                        MoveItemWithConstraints(boxItem, vx, vy);
                    }
                }
            }
            
            e.Handled = true;
        }

        private void TouchDragThumb_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
            _designer?.EndBatchDrag();
            e.Handled = true;
        }

        #endregion

        #region Helper Methods

        private void MoveItemWithConstraints(IBoxCanvasItem item, double deltaX, double deltaY)
        {
            if (item == null || item.LockedPosition)
                return;
                
            var newLeft = item.Left + deltaX;
            var newTop = item.Top + deltaY;
            
            // Apply movement constraints to keep item visible
            var minOverlap = 50; // Minimum pixels that must remain visible
            // Use viewport size in virtual units for constraints
            var canvasWidth = _designer?.ViewPortRect.Width ?? 800;
            var canvasHeight = _designer?.ViewPortRect.Height ?? 600;
            
            var minLeft = -item.Width + minOverlap;
            var maxLeft = canvasWidth - minOverlap;
            var minTop = -item.Height + minOverlap;
            var maxTop = canvasHeight - minOverlap;
            
            newLeft = Math.Max(minLeft, Math.Min(maxLeft, newLeft));
            newTop = Math.Max(minTop, Math.Min(maxTop, newTop));
            
            if (item is CanvasItem ci)
            {
                ci.Location = new Point(newLeft, newTop);
            }
            else
            {
                item.Left = newLeft;
                item.Top = newTop;
            }
            
            // Trigger bounds changed event
            // Trigger bounds changed event - this will be handled by the CanvasItem implementation
        }

        private void ProvideHapticFeedback()
        {
            // For touch devices, we can provide visual feedback since haptic feedback
            // is not directly available in WPF. This could be enhanced with third-party libraries.
            
            // Visual feedback: briefly change opacity or scale
            var originalOpacity = Opacity;
            Opacity = 0.7;
            
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            
            timer.Tick += (s, e) =>
            {
                Opacity = originalOpacity;
                timer.Stop();
            };
            
            timer.Start();
        }

        #endregion
    }

    /// <summary>
    /// Touch-enabled resize thumb
    /// </summary>
    public class TouchResizeThumb : ResizeThumb
    {
        private readonly Dictionary<int, TouchPoint> _activeTouches = new Dictionary<int, TouchPoint>();
        private bool _isResizing;

        public TouchResizeThumb()
        {
            // Enable touch and increase size for better touch targets
            IsManipulationEnabled = true;
            MinWidth = 44;
            MinHeight = 44;
            
            // Touch events
            TouchDown += TouchResizeThumb_TouchDown;
            TouchMove += TouchResizeThumb_TouchMove;
            TouchUp += TouchResizeThumb_TouchUp;
            
            // Manipulation for pinch-to-resize
            ManipulationStarting += TouchResizeThumb_ManipulationStarting;
            ManipulationDelta += TouchResizeThumb_ManipulationDelta;
        }

        private void TouchResizeThumb_TouchDown(object sender, TouchEventArgs e)
        {
            var touchPoint = e.GetTouchPoint(this);
            var touchId = e.TouchDevice.Id;
            
            _activeTouches[touchId] = touchPoint;
            _isResizing = true;
            
            CaptureTouch(e.TouchDevice);
            e.Handled = true;
        }

        private void TouchResizeThumb_TouchMove(object sender, TouchEventArgs e)
        {
            if (!_isResizing)
                return;
                
            var touchPoint = e.GetTouchPoint(this);
            var touchId = e.TouchDevice.Id;
            
            if (!_activeTouches.ContainsKey(touchId))
                return;
                
            var previousPoint = _activeTouches[touchId].Position;
            _activeTouches[touchId] = touchPoint;
            
            var deltaX = touchPoint.Position.X - previousPoint.X;
            var deltaY = touchPoint.Position.Y - previousPoint.Y;
            
            // Perform resize operation
            OnResizeDelta(deltaX, deltaY);
            
            e.Handled = true;
        }

        private void TouchResizeThumb_TouchUp(object sender, TouchEventArgs e)
        {
            var touchId = e.TouchDevice.Id;
            _activeTouches.Remove(touchId);
            
            if (_activeTouches.Count == 0)
            {
                _isResizing = false;
            }
            
            ReleaseTouchCapture(e.TouchDevice);
            e.Handled = true;
        }

        private void TouchResizeThumb_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.ManipulationContainer = this;
            e.Handled = true;
        }

        private void TouchResizeThumb_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            // Handle pinch-to-resize
            var scaleX = e.DeltaManipulation.Scale.X;
            var scaleY = e.DeltaManipulation.Scale.Y;
            
            if (Math.Abs(scaleX - 1.0) > 0.01 || Math.Abs(scaleY - 1.0) > 0.01)
            {
                OnResizeScale(scaleX, scaleY);
            }
            
            e.Handled = true;
        }

        protected virtual void OnResizeDelta(double deltaX, double deltaY)
        {
            // Override in derived classes or use existing resize logic
            // This would typically call the base ResizeThumb functionality
        }

        protected virtual void OnResizeScale(double scaleX, double scaleY)
        {
            // Handle scale-based resizing for touch gestures
        }
    }

    /// <summary>
    /// Touch-enabled rotate thumb
    /// </summary>
    public class TouchRotateThumb : RotateThumb
    {
        private readonly Dictionary<int, TouchPoint> _activeTouches = new Dictionary<int, TouchPoint>();
        private double _initialAngle;
        private bool _isRotating;

        public TouchRotateThumb()
        {
            // Enable touch and increase size
            IsManipulationEnabled = true;
            MinWidth = 44;
            MinHeight = 44;
            
            // Touch events
            TouchDown += TouchRotateThumb_TouchDown;
            TouchMove += TouchRotateThumb_TouchMove;
            TouchUp += TouchRotateThumb_TouchUp;
            
            // Manipulation for rotation gestures
            ManipulationStarting += TouchRotateThumb_ManipulationStarting;
            ManipulationDelta += TouchRotateThumb_ManipulationDelta;
        }

        private void TouchRotateThumb_TouchDown(object sender, TouchEventArgs e)
        {
            var touchPoint = e.GetTouchPoint(this);
            var touchId = e.TouchDevice.Id;
            
            _activeTouches[touchId] = touchPoint;
            _isRotating = true;
            _initialAngle = GetAngleFromCenter(touchPoint.Position);
            
            CaptureTouch(e.TouchDevice);
            e.Handled = true;
        }

        private void TouchRotateThumb_TouchMove(object sender, TouchEventArgs e)
        {
            if (!_isRotating)
                return;
                
            var touchPoint = e.GetTouchPoint(this);
            var touchId = e.TouchDevice.Id;
            
            if (!_activeTouches.ContainsKey(touchId))
                return;
                
            _activeTouches[touchId] = touchPoint;
            
            var currentAngle = GetAngleFromCenter(touchPoint.Position);
            var deltaAngle = currentAngle - _initialAngle;
            
            OnRotationDelta(deltaAngle);
            _initialAngle = currentAngle;
            
            e.Handled = true;
        }

        private void TouchRotateThumb_TouchUp(object sender, TouchEventArgs e)
        {
            var touchId = e.TouchDevice.Id;
            _activeTouches.Remove(touchId);
            
            if (_activeTouches.Count == 0)
            {
                _isRotating = false;
            }
            
            ReleaseTouchCapture(e.TouchDevice);
            e.Handled = true;
        }

        private void TouchRotateThumb_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.ManipulationContainer = this;
            e.Handled = true;
        }

        private void TouchRotateThumb_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            // Handle rotation gestures
            var rotation = e.DeltaManipulation.Rotation;
            
            if (Math.Abs(rotation) > 1)
            {
                OnRotationDelta(rotation);
            }
            
            e.Handled = true;
        }

        private double GetAngleFromCenter(Point point)
        {
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            var vector = point - center;
            return Math.Atan2(vector.Y, vector.X) * 180.0 / Math.PI;
        }

        protected virtual void OnRotationDelta(double deltaAngle)
        {
            // Override in derived classes or use existing rotation logic
        }
    }
}

/// <summary>
/// Helper class for visual tree operations
/// </summary>
public static class VisualTreeHelperExtensions
{
    public static T FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        
        if (parent == null)
            return null;
            
        if (parent is T ancestor)
            return ancestor;
            
        return FindAncestor<T>(parent);
    }

    public static T FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null)
            return null;
            
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            
            if (child is T result)
                return result;
                
            var childOfChild = FindChild<T>(child);
            if (childOfChild != null)
                return childOfChild;
        }
        
        return null;
    }
}
