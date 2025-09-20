using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Photobooth.Controls
{
    /// <summary>
    /// In-app eyedropper implemented as an Adorner overlay.
    /// Captures input above the target element, samples pixel color at the click point,
    /// and returns it via callbacks without creating a top-level window.
    /// </summary>
    public class EyedropperAdorner : Adorner
    {
        private readonly VisualCollection _visuals;
        private readonly Grid _root;
        private readonly Border _instruction;
        private readonly Ellipse _cursorDot;
        private readonly Action<Color> _onPicked;
        private readonly Action _onCancelled;
        private bool _active;

        private EyedropperAdorner(UIElement adornedElement, Action<Color> onPicked, Action onCancelled)
            : base(adornedElement)
        {
            _onPicked = onPicked;
            _onCancelled = onCancelled;
            _visuals = new VisualCollection(this);

            _root = new Grid
            {
                Background = Brushes.Transparent, // eat input everywhere
                Focusable = true
            };

            // Instruction bar inside the app window
            _instruction = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 12, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(220, 45, 45, 48)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 8, 16, 8),
                Child = new TextBlock
                {
                    Text = "Click/Touch to sample color • ESC to cancel",
                    Foreground = Brushes.White,
                    FontSize = 13
                }
            };

            _root.Children.Add(_instruction);

            // Small dot to indicate sampling cursor position
            _cursorDot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.Transparent,
                Stroke = Brushes.Orange,
                StrokeThickness = 2,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            _root.Children.Add(_cursorDot);

            // Input handlers
            _root.PreviewMouseMove += (s, e) => UpdateCursorDot(e.GetPosition(_root));
            _root.PreviewMouseLeftButtonDown += (s, e) => { PickAtPoint(e.GetPosition(_root)); e.Handled = true; };
            _root.PreviewTouchDown += (s, e) => { PickAtPoint(e.GetTouchPoint(_root).Position); e.Handled = true; };
            _root.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Cancel(); };

            _visuals.Add(_root);

            Loaded += (s, e) =>
            {
                try { Keyboard.Focus(_root); } catch { }
                _active = true;
            };
        }

        public static EyedropperAdorner Start(UIElement target, Action<Color> onPicked, Action onCancelled)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var layer = AdornerLayer.GetAdornerLayer(target);
            if (layer == null) throw new InvalidOperationException("AdornerLayer not found for target element.");

            var adorner = new EyedropperAdorner(target, onPicked, onCancelled);
            layer.Add(adorner);
            try { Mouse.OverrideCursor = Cursors.Pen; } catch { }
            return adorner;
        }

        private void Finish()
        {
            if (!_active) return;
            _active = false;
            var layer = Parent as AdornerLayer ?? AdornerLayer.GetAdornerLayer(AdornedElement);
            if (layer != null)
            {
                layer.Remove(this);
            }
            try { Mouse.OverrideCursor = null; } catch { }
        }

        private void Cancel()
        {
            Finish();
            _onCancelled?.Invoke();
        }

        private void UpdateCursorDot(Point p)
        {
            if (!_active) return;
            _cursorDot.Visibility = Visibility.Visible;
            // Position using a TranslateTransform for simplicity
            _cursorDot.RenderTransform = new TranslateTransform(p.X - _cursorDot.Width / 2, p.Y - _cursorDot.Height / 2);
        }

        private void PickAtPoint(Point point)
        {
            try
            {
                var color = SampleColorAt(point);
                Finish();
                _onPicked?.Invoke(color);
            }
            catch
            {
                Cancel();
            }
        }

        private Color SampleColorAt(Point localPoint)
        {
            // Render the adorned element to a bitmap and sample the pixel under the click
            if (!(AdornedElement is FrameworkElement fe))
                throw new InvalidOperationException("Adorned element must be a FrameworkElement.") ;

            if (fe.ActualWidth <= 0 || fe.ActualHeight <= 0)
                return Colors.Black;

            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(fe.ActualWidth),
                (int)Math.Ceiling(fe.ActualHeight),
                96, 96, PixelFormats.Pbgra32);

            // Temporarily hide the adorner visuals while rendering so we sample underlying content only
            var prevVisibility = _root.Visibility;
            _root.Visibility = Visibility.Collapsed;
            rtb.Render(AdornedElement);
            _root.Visibility = prevVisibility;

            int x = Math.Max(0, Math.Min(rtb.PixelWidth - 1, (int)Math.Round(localPoint.X)));
            int y = Math.Max(0, Math.Min(rtb.PixelHeight - 1, (int)Math.Round(localPoint.Y)));

            var cb = new CroppedBitmap(rtb, new Int32Rect(x, y, 1, 1));
            var pixels = new byte[4];
            cb.CopyPixels(pixels, 4, 0);
            // BGRA to Color
            return Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
        }

        protected override int VisualChildrenCount => _visuals.Count;
        protected override Visual GetVisualChild(int index) => _visuals[index];

        protected override Size ArrangeOverride(Size finalSize)
        {
            _root.Arrange(new Rect(new Point(0, 0), finalSize));
            return finalSize;
        }
    }
}
