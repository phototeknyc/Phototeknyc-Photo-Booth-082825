using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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

		private double _ratioX;
		public double RatioX
		{
			get { return _ratioX; }
			set
			{
				if (SetProperty(ref _ratioX, value))
				{
					// When the aspect ratio changes, update the Width or Height accordingly
					if (!LockedAspectRatio)
					{
						if (_ratioX > 0)
						{
							AspectRatio = Convert.ToDouble(_ratioX) / Convert.ToDouble(_ratioY);
						}
						else
						{
							// Handle an invalid aspect ratio (e.g., zero) by resetting it to 1.0 (1:1)
							_ratioX = 1;
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

		private double _ratioY;
		public double RatioY
		{
			get { return _ratioY; }
			set
			{
				if (SetProperty(ref _ratioY, value))
				{
					// When the aspect ratio changes, update the Width or Height accordingly
					if (!LockedAspectRatio)
					{
						if (_ratioY > 0)
						{
							AspectRatio = Convert.ToDouble(_ratioX) / Convert.ToDouble(_ratioY);
						}
						else
						{
							// Handle an invalid aspect ratio (e.g., zero) by resetting it to 1.0 (1:1)
							_ratioY = 1;
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
			get { return Convert.ToDouble(_Size.Width / _Size.Height); }
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
				if (Resizeable && SetProperty(ref _Size, value))
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
				if (Resizeable)
				{
					_Size.Width = value;
					OnPropertyChanged();
					OnPropertyChanged(nameof(Size));
					OnPropertyChanged(nameof(Bounds));
					OnBoundsChanged();
				}
			}
		}

		public double Height
		{
			get { return _Size.Height; }
			set
			{
				if (Resizeable)
				{
					_Size.Height = value;
					OnPropertyChanged();
					OnPropertyChanged(nameof(Size));
					OnPropertyChanged(nameof(Bounds));
					OnBoundsChanged();
				}
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

		}

		public CanvasItem(double left, double top, double width, double height, double ratioX, double ratioY)
		{
			_Location = new Point(left, top);
			RatioX = ratioX;
			RatioY = ratioY;
			_AspectRatio = ratioX / ratioY;
			_Size = new Size(width, height);
			Width = width;
			Height = height;
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
			get => _Image;
			set => SetProperty(ref _Image, value);
		}

		private bool _stretch = false;
		public bool Stretch
		{
			get => _stretch;
			set => SetProperty(ref _stretch, value);
		}

		private String _ImagePath;
		public String ImagePath
		{
			get => _ImagePath;
			set => SetProperty(ref _ImagePath, value);
		}

		public ImageCanvasItem() {
		}

		public ImageCanvasItem(double left, double top, double width, double height, String imagePath, double aspectRatioX, double aspectRatioY)
			: base(left, top, width, height, aspectRatioX, aspectRatioY)
		{
			if(imagePath != null)
			{
				_ImagePath = imagePath;
				_Image = new BitmapImage(new Uri(imagePath));
			}

		}
	}

	/// <summary>
	/// Represents an entity (or an object, node, vertex) in the graph or diagram.
	/// There can be <see cref="Connection"/>s between entities.
	/// This class can be inherited by user to contain more information.
	/// </summary>
	public class PlaceholderCanvasItem : CanvasItem
	{
		private int _placeholderNo;
		private Brush _Background;

		public int PlaceholderNo
		{
			get => _placeholderNo;
			set { SetProperty(ref _placeholderNo, value); }
		}

		public Brush Background
		{
			get { return _Background; }
			set { SetProperty(ref _Background, value); }
		}

		public PlaceholderCanvasItem() { }

		public PlaceholderCanvasItem(double left, double top, double width, double height, double aspectRatioX, double aspectRatioY, int placeHolder = 0, Brush background = null)
			: base(left, top, width, height, aspectRatioX, aspectRatioY)
		{

			_Background = background ?? new SolidColorBrush(GenerateRandomLightColor());
			_placeholderNo = placeHolder;
		}

		public ImageCanvasItem ToImageCanvasItem(string imagePath)
		{
			return new ImageCanvasItem(Left, Top, Width, Height, imagePath, RatioX, RatioY);
		}

		private static Random _random = new Random();
		private Color GenerateRandomLightColor()
		{
			byte r = (byte)_random.Next(150, 256); // Red component between 150 and 255
			byte g = (byte)_random.Next(150, 256); // Green component between 150 and 255
			byte b = (byte)_random.Next(150, 256); // Blue component between 150 and 255

			return Color.FromRgb(r, g, b);
		}
	}
}