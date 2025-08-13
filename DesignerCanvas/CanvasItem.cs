using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace DesignerCanvas
{
    public class CanvasItem : INotifyPropertyChanged, IBoxCanvasItem, ISizeConstraint
    {
        protected Point _Location;
        protected Size _Size;
        private double _Angle;
        private double _AspectRatio;
        public virtual event EventHandler BoundsChanged;


        public bool LockedPosition { get; set; }

        public bool LockedAspectRatio { get; set; }
        public bool Resizeable { get; set; }


        /// <summary>
        /// aspect ratio = width / height
        /// height = width / ratio
        /// width = height * ratio
        /// </summary>
        public double AspectRatio
        {
            get { return Convert.ToDouble(_Size.Width/_Size.Height); }
            set
            {
                if (SetProperty(ref _AspectRatio, value))
                {
                    // When the aspect ratio changes, update the Width or Height accordingly
                    if (!LockedAspectRatio)
					{
						if (_AspectRatio > 0)
						{
							// Adjust the width while maintaining the new aspect ratio
							Width = Height * _AspectRatio;
						}
						else
						{
							// Handle an invalid aspect ratio (e.g., zero) by resetting it to 1.0 (1:1)
							_AspectRatio = 1.0;
							// Alternatively, you can throw an exception or handle it in another way.
							// throw new ArgumentException("Aspect ratio must be greater than zero.");
						}
                    }
                    else
                    {
                        throw new Exception("Aspect ratio is locked");
                    }
                }
            }
        }



        public Point Location
        {
            get { return _Location; }
            set
            {
                if (!LockedPosition && SetProperty(ref _Location, value))
                {
                    OnPropertyChanged(nameof(Left));
                    OnPropertyChanged(nameof(Top));
                    OnPropertyChanged(nameof(Bounds));
                    OnBoundsChanged();
                }
            }
        }

        public Size Size
        {
            get { return _Size; }
            set
            {
                if (SetProperty(ref _Size, value))
                {
                    OnPropertyChanged(nameof(Width));
                    OnPropertyChanged(nameof(Height));
                    OnPropertyChanged(nameof(Bounds));
                    OnBoundsChanged();
                }
            }
        }

        public double Left
        {
            get { return _Location.X; }
            set
            {
                if (!LockedPosition)
                {
                    _Location.X = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Location));
                    OnPropertyChanged(nameof(Bounds));
                    OnBoundsChanged();
                }
            }
        }

        public double Top
        {
            get { return _Location.Y; }
            set
            {
                if (!LockedPosition)
                {
                    _Location.Y = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Location));
                    OnPropertyChanged(nameof(Bounds));
                    OnBoundsChanged();
                }
            }
        }

        public double Width
        {
            get { return _Size.Width; }
            set
            {
                if (value <= 0) return; // Prevent invalid dimensions
                
                var oldWidth = _Size.Width;
                _Size.Width = value;
                
                // If aspect ratio is locked, adjust height to maintain ratio
                if (LockedAspectRatio && AspectRatio > 0)
                {
                    _Size.Height = value / AspectRatio;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(Size));
                OnPropertyChanged(nameof(Height)); // Notify height change if aspect ratio is locked
                OnPropertyChanged(nameof(Bounds));
                OnBoundsChanged();
            }
        }

        public double Height
        {
            get { return _Size.Height; }
            set
            {
                if (value <= 0) return; // Prevent invalid dimensions
                
                var oldHeight = _Size.Height;
                _Size.Height = value;
                
                // If aspect ratio is locked, adjust width to maintain ratio
                if (LockedAspectRatio && AspectRatio > 0)
                {
                    _Size.Width = value * AspectRatio;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(Size));
                OnPropertyChanged(nameof(Width)); // Notify width change if aspect ratio is locked
                OnPropertyChanged(nameof(Bounds));
                OnBoundsChanged();
            }
        }

        public double Angle
        {
            get { return _Angle; }
            set
            {
                SetProperty(ref _Angle, value);
                OnPropertyChanged(nameof(Bounds));
                OnBoundsChanged();
            }
        }

        public Rect Bounds
        {
            get
            {
                var angle = _Angle * Math.PI / 180.0;
                var sa = Math.Abs(Math.Abs(angle) < 0.01 ? angle : Math.Sin(angle));
                var ca = Math.Abs(Math.Abs(angle) < 0.01 ? 1 - angle * angle / 2 : Math.Cos(angle));
                var centerX = _Location.X + _Size.Width / 2;
                var centerY = _Location.Y + _Size.Height / 2;
                // bounding rectangle
                var width = _Size.Width * ca + _Size.Height * sa;
                var height = _Size.Width * sa + _Size.Height * ca;
                return new Rect(centerX - width / 2, centerY - height / 2, width, height);
            }
        }


        public virtual double MinWidth => 10;

        public virtual double MinHeight => 10;

        /// <summary>
        /// Determines whether the object is in the specified region.
        /// </summary>
        public HitTestResult HitTest(Rect testRectangle)
        {
            var b = Bounds;
            if (testRectangle.Contains(b)) return HitTestResult.Inside;
            if (b.Contains(testRectangle)) return HitTestResult.Contains;
            if (b.IntersectsWith(testRectangle)) return HitTestResult.Intersects;
            return HitTestResult.None;
        }

        public virtual void NotifyUserDraggingStarted()
        {

        }

        /// <summary>
        /// Notifies the item when user dragging the item.
        /// </summary>
        public virtual void NotifyUserDragging(double deltaX, double deltaY)
        {

        }

        public virtual void NotifyUserDraggingCompleted()
        {
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (object.Equals(storage, value) == false)
            {
                storage = value;
                OnPropertyChanged(propertyName);
                return true;
            }
            else
            {
                return false;
            }
        }

        protected virtual void OnBoundsChanged()
        {
            BoundsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void reverseAspectRatio()
        {
            // reverse aspect ratio

            SetProperty(ref _AspectRatio, 1 / this.AspectRatio);

            // swap width and height
            double temp = this.Width;
            this.Width = this.Height;
            this.Height = temp;
        }

        public ICanvasItem Clone()
        {
            // create a clone of this object
            return (ICanvasItem)this.MemberwiseClone();
        }

        public CanvasItem()
        {
            Resizeable = true;
            LockedAspectRatio = false;
        }

        public CanvasItem(double left, double top, double width, double height, double aspectRatioX, double aspectRatioY)
        {
            _Location = new Point(left, top);
            _Size = new Size(width, height);
            AspectRatio = aspectRatioX / aspectRatioY;
            Resizeable = true;
            LockedAspectRatio = false;
        }
    }
    /// <summary>
    /// Enables size constraint for a specific type of <see cref="ICanvasItem"/> 。
    /// </summary>
    public interface ISizeConstraint
    {
        double MinWidth { get; }
        double MinHeight { get; }
    }

    /// <summary>
    /// Represents an entity (or an object, node, vertex) in the graph or diagram.
    /// There can be <see cref="Connection"/>s between entities.
    /// This class can be inherited by user to contain more information.
    /// </summary>
    public class ImageCanvasItem : CanvasItem
    {
        private ImageSource _Image;

        public ImageSource Image
        {
            get { return _Image; }
            set
            {
                SetProperty(ref _Image, value);
            }
        }

        public ImageCanvasItem()
        {
            Resizeable = true;
            LockedAspectRatio = true; // Images should default to locked aspect ratio
        }

        public ImageCanvasItem(double left, double top, double width, double height, ImageSource image, double aspectRatioX, int aspectRatioY)
            : base(left, top, width, height, aspectRatioX, aspectRatioY)
        {
            _Image = image;
            Resizeable = true;
            LockedAspectRatio = true; // Images should default to locked aspect ratio
        }
    }

    /// <summary>
    /// Represents an entity (or an object, node, vertex) in the graph or diagram.
    /// There can be <see cref="Connection"/>s between entities.
    /// This class can be inherited by user to contain more information.
    /// </summary>
    public class PlaceholderCanvasItem : CanvasItem
    {
        private static int TotalPlaceholders = 0;
        private int _placeholderNo;
        private Brush _Background;

        // Predefined color palette for placeholders - professional and visually distinct colors
        private static readonly Color[] ColorPalette = new Color[]
        {
            Color.FromRgb(255, 182, 193), // Light Pink
            Color.FromRgb(173, 216, 230), // Light Blue
            Color.FromRgb(144, 238, 144), // Light Green
            Color.FromRgb(255, 218, 185), // Peach
            Color.FromRgb(221, 160, 221), // Plum
            Color.FromRgb(255, 255, 224), // Light Yellow
            Color.FromRgb(176, 224, 230), // Powder Blue
            Color.FromRgb(255, 228, 196), // Bisque
            Color.FromRgb(216, 191, 216), // Thistle
            Color.FromRgb(240, 230, 140), // Khaki
            Color.FromRgb(255, 192, 203), // Pink
            Color.FromRgb(230, 230, 250), // Lavender
            Color.FromRgb(250, 240, 230), // Linen
            Color.FromRgb(255, 228, 225), // Misty Rose
            Color.FromRgb(224, 255, 255), // Light Cyan
            Color.FromRgb(240, 255, 240), // Honeydew
        };

        public int PlaceholderNo
        {
            get => _placeholderNo;
            set 
            { 
                SetProperty(ref _placeholderNo, value);
                // Update background color when placeholder number changes
                Background = new SolidColorBrush(GetColorForPlaceholder(value));
            }
        }

        public Brush Background
        {
            get { return _Background; }
            set { SetProperty(ref _Background, value); }
        }


        public PlaceholderCanvasItem()
        {
            // Don't auto-increment here, let it be set explicitly
            _placeholderNo = 1; // Default to 1
            _Background = new SolidColorBrush(GetColorForPlaceholder(_placeholderNo));
            Resizeable = true;
            LockedAspectRatio = false; // Placeholders can be freely resized by default
        }

        public PlaceholderCanvasItem(double left, double top, double width, double height, double aspectRatioX, double aspectRatioY)
            : base(left, top, width, height, aspectRatioX, aspectRatioY)
        {
            // Don't auto-increment here, let it be set explicitly
            _placeholderNo = 1; // Default to 1
            _Background = new SolidColorBrush(GetColorForPlaceholder(_placeholderNo));
            Resizeable = true;
            LockedAspectRatio = false; // Placeholders can be freely resized by default
        }

        private static Color GetColorForPlaceholder(int placeholderNumber)
        {
            // Use modulo to cycle through the palette if we have more placeholders than colors
            int colorIndex = (placeholderNumber - 1) % ColorPalette.Length;
            return ColorPalette[colorIndex];
        }
        
        public new ICanvasItem Clone()
        {
            var cloned = (PlaceholderCanvasItem)base.Clone();
            
            // Increment the total count and assign new number
            TotalPlaceholders += 1;
            cloned._placeholderNo = TotalPlaceholders;
            
            // Assign color from the palette based on the new number
            cloned._Background = new SolidColorBrush(GetColorForPlaceholder(cloned._placeholderNo));
            
            return cloned;
        }
    }
}