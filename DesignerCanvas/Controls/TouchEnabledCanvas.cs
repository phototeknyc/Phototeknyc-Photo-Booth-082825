using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesignerCanvas.Controls.Primitives;

namespace DesignerCanvas.Controls
{
    /// <summary>
    /// Touch-enabled extension of DesignerCanvas with multi-touch support
    /// </summary>
    public class TouchEnabledCanvas : DesignerCanvas
    {
        private readonly Dictionary<int, TouchPoint> _activeTouches = new Dictionary<int, TouchPoint>();
        private readonly Dictionary<int, IBoxCanvasItem> _touchTargets = new Dictionary<int, IBoxCanvasItem>();
        private Point? _lastPanPoint;
        private double _initialDistance;
        private Point _initialCenter;
        private bool _isMultiTouch;
        
        public TouchEnabledCanvas()
        {
            // Enable touch support
            IsManipulationEnabled = true;
            
            // Subscribe to touch events
            TouchDown += TouchEnabledCanvas_TouchDown;
            TouchMove += TouchEnabledCanvas_TouchMove;
            TouchUp += TouchEnabledCanvas_TouchUp;
            
            // Subscribe to manipulation events for gestures
            ManipulationStarting += TouchEnabledCanvas_ManipulationStarting;
            ManipulationDelta += TouchEnabledCanvas_ManipulationDelta;
            ManipulationCompleted += TouchEnabledCanvas_ManipulationCompleted;
        }

        #region Touch Events

        private void TouchEnabledCanvas_TouchDown(object sender, TouchEventArgs e)
        {
            var touchPoint = e.GetTouchPoint(this);
            var touchId = e.TouchDevice.Id;
            
            _activeTouches[touchId] = touchPoint;
            
            // Find the canvas item under the touch point
            var hitItem = GetCanvasItemAt(touchPoint.Position);
            if (hitItem != null)
            {
                _touchTargets[touchId] = hitItem;
                
                // Select the item if not already selected
                if (!SelectedItems.Contains(hitItem))
                {
                    if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
                    {
                        SelectedItems.Clear();
                    }
                    SelectedItems.Add(hitItem);
                }
            }
            else
            {
                // Touch on empty canvas - prepare for panning
                _lastPanPoint = touchPoint.Position;
            }
            
            // Capture the touch
            CaptureTouch(e.TouchDevice);
            e.Handled = true;
        }

        private void TouchEnabledCanvas_TouchMove(object sender, TouchEventArgs e)
        {
            var touchPoint = e.GetTouchPoint(this);
            var touchId = e.TouchDevice.Id;
            
            if (!_activeTouches.ContainsKey(touchId))
                return;
                
            var previousPoint = _activeTouches[touchId].Position;
            _activeTouches[touchId] = touchPoint;
            
            // Handle single touch movement
            if (_activeTouches.Count == 1 && _touchTargets.ContainsKey(touchId))
            {
                var item = _touchTargets[touchId];
                var deltaX = touchPoint.Position.X - previousPoint.X;
                var deltaY = touchPoint.Position.Y - previousPoint.Y;
                
                MoveItem(item, deltaX, deltaY);
            }
            // Handle canvas panning for single touch on empty area
            else if (_activeTouches.Count == 1 && !_touchTargets.ContainsKey(touchId))
            {
                if (_lastPanPoint.HasValue)
                {
                    var deltaX = touchPoint.Position.X - _lastPanPoint.Value.X;
                    var deltaY = touchPoint.Position.Y - _lastPanPoint.Value.Y;
                    
                    PanCanvas(deltaX, deltaY);
                    _lastPanPoint = touchPoint.Position;
                }
            }
            
            e.Handled = true;
        }

        private void TouchEnabledCanvas_TouchUp(object sender, TouchEventArgs e)
        {
            var touchId = e.TouchDevice.Id;
            
            _activeTouches.Remove(touchId);
            _touchTargets.Remove(touchId);
            
            if (_activeTouches.Count == 0)
            {
                _lastPanPoint = null;
                _isMultiTouch = false;
            }
            
            ReleaseTouchCapture(e.TouchDevice);
            e.Handled = true;
        }

        #endregion

        #region Manipulation Events (Multi-touch gestures)

        private void TouchEnabledCanvas_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.ManipulationContainer = this;
            e.Handled = true;
        }

        private void TouchEnabledCanvas_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            // Handle translation (panning)
            if (e.DeltaManipulation.Translation.Length > 0)
            {
                var deltaX = e.DeltaManipulation.Translation.X;
                var deltaY = e.DeltaManipulation.Translation.Y;
                
                if (SelectedItems.Any())
                {
                    // Move selected items
                    foreach (var item in SelectedItems.ToList())
                    {
                        if (item is IBoxCanvasItem boxItem)
                        {
                            MoveItem(boxItem, deltaX, deltaY);
                        }
                    }
                }
                else
                {
                    // Pan the canvas
                    PanCanvas(deltaX, deltaY);
                }
            }
            
            // Handle scaling (pinch to zoom)
            if (Math.Abs(e.DeltaManipulation.Scale.X - 1.0) > 0.01 || Math.Abs(e.DeltaManipulation.Scale.Y - 1.0) > 0.01)
            {
                var scaleX = e.DeltaManipulation.Scale.X;
                var scaleY = e.DeltaManipulation.Scale.Y;
                var center = e.ManipulationOrigin;
                
                if (SelectedItems.Any())
                {
                    // Scale selected items
                    ScaleSelectedItems(scaleX, scaleY, center);
                }
                else
                {
                    // Zoom the canvas
                    ZoomCanvas(scaleX, center);
                }
            }
            
            // Handle rotation
            if (Math.Abs(e.DeltaManipulation.Rotation) > 1)
            {
                var rotation = e.DeltaManipulation.Rotation;
                var center = e.ManipulationOrigin;
                
                if (SelectedItems.Any())
                {
                    RotateSelectedItems(rotation, center);
                }
            }
            
            e.Handled = true;
        }

        private void TouchEnabledCanvas_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
            e.Handled = true;
        }

        #endregion

        #region Touch Helper Methods

        private IBoxCanvasItem GetCanvasItemAt(Point position)
        {
            // Hit test to find the topmost canvas item at the given position
            var hitTestResults = new List<DependencyObject>();
            VisualTreeHelper.HitTest(this, 
                null,
                (result) => {
                    hitTestResults.Add(result.VisualHit);
                    return HitTestResultBehavior.Continue;
                },
                new PointHitTestParameters(position));
            
            foreach (var element in hitTestResults)
            {
                var container = FindAncestor<DesignerCanvasItemContainer>(element as DependencyObject);
                if (container?.Content is IBoxCanvasItem item)
                {
                    return item;
                }
            }
            
            return null;
        }

        private T FindAncestor<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            
            if (parent == null)
                return null;
                
            if (parent is T ancestor)
                return ancestor;
                
            return FindAncestor<T>(parent);
        }

        private void MoveItem(IBoxCanvasItem item, double deltaX, double deltaY)
        {
            if (item == null) return;
            
            var newLeft = item.Left + deltaX;
            var newTop = item.Top + deltaY;
            
            // Apply movement constraints to keep item visible
            var minOverlap = 50; // Minimum pixels that must remain visible
            var maxLeft = ActualWidth - minOverlap;
            var maxTop = ActualHeight - minOverlap;
            
            newLeft = Math.Max(-item.Width + minOverlap, Math.Min(maxLeft, newLeft));
            newTop = Math.Max(-item.Height + minOverlap, Math.Min(maxTop, newTop));
            
            item.Left = newLeft;
            item.Top = newTop;
        }

        private void PanCanvas(double deltaX, double deltaY)
        {
            // Implement canvas panning - move all items
            foreach (var item in Items.ToList())
            {
                item.Left += deltaX;
                item.Top += deltaY;
            }
        }

        private void ZoomCanvas(double scaleFactor, Point center)
        {
            // Implement canvas zooming
            var transform = RenderTransform as ScaleTransform ?? new ScaleTransform();
            
            var newScaleX = Math.Max(0.1, Math.Min(5.0, transform.ScaleX * scaleFactor));
            var newScaleY = Math.Max(0.1, Math.Min(5.0, transform.ScaleY * scaleFactor));
            
            RenderTransform = new ScaleTransform(newScaleX, newScaleY, center.X, center.Y);
        }

        private void ScaleSelectedItems(double scaleX, double scaleY, Point center)
        {
            foreach (var item in SelectedItems.ToList())
            {
                if (!(item is IBoxCanvasItem boxItem) || boxItem.LockedPosition) continue;
                
                // Calculate new dimensions
                var newWidth = Math.Max(10, boxItem.Width * scaleX);
                var newHeight = Math.Max(10, boxItem.Height * scaleY);
                
                // Maintain aspect ratio if required
                if (boxItem.AspectRatio > 0)
                {
                    var aspectRatio = boxItem.Width / boxItem.Height;
                    if (Math.Abs(scaleX) > Math.Abs(scaleY))
                    {
                        newHeight = newWidth / aspectRatio;
                    }
                    else
                    {
                        newWidth = newHeight * aspectRatio;
                    }
                }
                
                // Calculate position adjustment to scale around center
                var itemCenter = new Point(boxItem.Left + boxItem.Width / 2, boxItem.Top + boxItem.Height / 2);
                var deltaX = (itemCenter.X - center.X) * (scaleX - 1);
                var deltaY = (itemCenter.Y - center.Y) * (scaleY - 1);
                
                boxItem.Width = newWidth;
                boxItem.Height = newHeight;
                boxItem.Left += deltaX;
                boxItem.Top += deltaY;
            }
        }

        private void RotateSelectedItems(double rotation, Point center)
        {
            foreach (var item in SelectedItems.ToList())
            {
                if (!(item is IBoxCanvasItem boxItem) || boxItem.LockedPosition) continue;
                
                // Apply rotation
                var currentRotation = boxItem.Angle;
                boxItem.Angle = (currentRotation + rotation) % 360;
            }
        }

        #endregion

        #region Touch-Friendly UI Properties

        /// <summary>
        /// Minimum touch target size for accessibility (44x44 pixels recommended)
        /// </summary>
        public static readonly DependencyProperty MinTouchTargetSizeProperty =
            DependencyProperty.Register(nameof(MinTouchTargetSize), typeof(Size), typeof(TouchEnabledCanvas),
                new PropertyMetadata(new Size(44, 44)));

        public Size MinTouchTargetSize
        {
            get => (Size)GetValue(MinTouchTargetSizeProperty);
            set => SetValue(MinTouchTargetSizeProperty, value);
        }

        /// <summary>
        /// Touch sensitivity for gesture recognition
        /// </summary>
        public static readonly DependencyProperty TouchSensitivityProperty =
            DependencyProperty.Register(nameof(TouchSensitivity), typeof(double), typeof(TouchEnabledCanvas),
                new PropertyMetadata(10.0));

        public double TouchSensitivity
        {
            get => (double)GetValue(TouchSensitivityProperty);
            set => SetValue(TouchSensitivityProperty, value);
        }

        #endregion
    }
}