﻿//using System;
//using System.Windows;
//using System.Windows.Controls;
//using System.Windows.Documents;
//using System.Windows.Input;
//using System.Windows.Media;

//namespace Undefined.DesignerCanvas.Controls.Primitives
//{
//    public class RubberbandAdorner : Adorner
//    {
//        private readonly Controls.DesignerCanvas _Canvas;
//        private readonly Action<object, Rect> _Callback;
//        private readonly RubberbandChrome chrome;
//        // Note the adorner layer will not scroll.
//        // However, we don't count in the render transformation of canvas,
//        // which will be handled in the callback function.
//        private Point _StartPoint;
//        private Point _EndPoint;

//        /// <param name="startPoint">Relative to the adorner layer. Note the adorner layer will not scroll.</param>
//        public RubberbandAdorner(Controls.DesignerCanvas canvas, Point startPoint, Action<object, Rect> callback) : base(canvas)
//        {
//            _Canvas = canvas;
//            _Callback = callback;
//            _StartPoint = _EndPoint = startPoint;
//            chrome = new RubberbandChrome()
//            {
//                HorizontalAlignment = HorizontalAlignment.Left,
//                VerticalAlignment = VerticalAlignment.Top,
//                Margin = new Thickness(_StartPoint.X , _StartPoint.Y, 0, 0)
//            };
//            ResizeChrome();
//            this.AddVisualChild(chrome);
//            CaptureMouse();
//        }

//        /// <summary>
//        /// Implements <see cref="M:System.Windows.Media.Visual.HitTestCore(System.Windows.Media.PointHitTestParameters)"/> to supply base element hit testing behavior (returning <see cref="T:System.Windows.Media.HitTestResult"/>). 
//        /// </summary>
//        /// <returns>
//        /// Results of the test, including the evaluated point.
//        /// </returns>
//        /// <param name="hitTestParameters">Describes the hit test to perform, including the initial hit point.</param>
//        protected override System.Windows.Media.HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
//        {
//            var result = base.HitTestCore(hitTestParameters);
//            if (result == null) return new PointHitTestResult(this, hitTestParameters.HitPoint);
//            return result;
//        }

//        private void ResizeChrome()
//        {
//            var rect = new Rect(_StartPoint, _EndPoint);
//            chrome.Margin = new Thickness(rect.X, rect.Y, 0, 0);
//            chrome.Width = rect.Width;
//            chrome.Height = rect.Height;
//        }

//        protected override Size ArrangeOverride(Size arrangeBounds)
//        {
//            chrome.Arrange(new Rect(arrangeBounds));
//            return arrangeBounds;
//        }

//        protected override Visual GetVisualChild(int index)
//        {
//            if (index == 0) return chrome;
//            throw new IndexOutOfRangeException();
//        }

//        protected override int VisualChildrenCount => 1;

//        protected override void OnMouseMove(MouseEventArgs e)
//        {
//            base.OnMouseMove(e);
//            if (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)
//            {
//                CaptureMouse();
//                _EndPoint = e.GetPosition(this);
//                ResizeChrome();
//            }
//            else
//            {
//                ReleaseMouseCapture();
//            }
//            e.Handled = true;
//        }

//        protected override void OnMouseUp(MouseButtonEventArgs e)
//        {
//            base.OnMouseUp(e);
//            ReleaseMouseCapture();
//            _Callback?.Invoke(this, new Rect(_StartPoint, _EndPoint));
//            var adornerLayer = Parent as AdornerLayer;
//            adornerLayer?.Remove(this);
//            e.Handled = true;
//        }
//    }

//    public class RubberbandChrome : Control
//    {
//        static RubberbandChrome()
//        {
//            DefaultStyleKeyProperty.OverrideMetadata(typeof (RubberbandChrome),
//                new FrameworkPropertyMetadata(typeof (RubberbandChrome)));
//        }
//    }
//}
