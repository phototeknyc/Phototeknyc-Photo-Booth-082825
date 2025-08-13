using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesignerCanvas.Controls.Primitives;
using Microsoft.Win32;

namespace DesignerCanvas.Controls
{
	public class DesigningAdornerGeneratingEventArgs : EventArgs
	{
		public DesigningAdornerGeneratingEventArgs(ICanvasItem item)
		{
			if (item == null) throw new ArgumentNullException(nameof(item));
			Item = item;
		}

		public ICanvasItem Item { get; }

		public CanvasAdorner Adorder { get; set; }

	}

	/// <summary>
	/// Hosts a canvas that supports diagram designing.
	/// </summary>
	[TemplatePart(Name = "PART_Canvas", Type = typeof(Canvas))]
	[TemplatePart(Name = "PART_AdornerCanvas", Type = typeof(Canvas))]
	[TemplatePart(Name = "PART_HorizontalScrollBar", Type = typeof(ScrollBar))]
	[TemplatePart(Name = "PART_VerticalScrollBar", Type = typeof(ScrollBar))]
	public class DesignerCanvas : Control
	{
		private readonly GraphicalObjectCollection _Items = new GraphicalObjectCollection();
		private readonly GraphicalObjectCollection _SelectedItems = new GraphicalObjectCollection();

		private readonly GraphicalObjectContainerGenerator _ItemContainerGenerator =
			new GraphicalObjectContainerGenerator();

		#region Properties

		// ItemTemplate & ItemTemplateSelector affect both the Entity & the Connection.

		public DataTemplate EntityItemTemplate
		{
			get { return (DataTemplate)GetValue(EntityItemTemplateProperty); }
			set { SetValue(EntityItemTemplateProperty, value); }
		}

		public static readonly DependencyProperty EntityItemTemplateProperty =
			DependencyProperty.Register("EntityItemTemplate", typeof(DataTemplate),
				typeof(DesignerCanvas), new PropertyMetadata(null));

		public DataTemplateSelector ItemTemplateSelector
		{
			get { return (DataTemplateSelector)GetValue(ItemTemplateSelectorProperty); }
			set { SetValue(ItemTemplateSelectorProperty, value); }
		}

		public static readonly DependencyProperty ItemTemplateSelectorProperty =
			DependencyProperty.Register("ItemTemplateSelector", typeof(DataTemplateSelector),
				typeof(DesignerCanvas), new PropertyMetadata(null));


		/// <summary>
		/// Decides whether to show boundaries for each item. This functionality is
		/// used for debugging.
		/// </summary>
		public bool ShowBoundaries
		{
			get { return (bool)GetValue(ShowBoundariesProperty); }
			set { SetValue(ShowBoundariesProperty, value); }
		}

		public static readonly DependencyProperty ShowBoundariesProperty = DependencyProperty.Register("ShowBoundaries",
			typeof(bool), typeof(DesignerCanvas), new PropertyMetadata(false));

		/// <summary>
		/// The horizontal viewport offset from the origin.
		/// </summary>
		public double HorizontalScrollOffset
		{
			get { return (double)GetValue(HorizontalScrollOffsetProperty); }
			set { SetValue(HorizontalScrollOffsetProperty, value); }
		}

		public static readonly DependencyProperty HorizontalScrollOffsetProperty =
			DependencyProperty.Register("HorizontalScrollOffset", typeof(double), typeof(DesignerCanvas),
				new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
					(d, e) => ((DesignerCanvas)d).InvalidateViewPortRect()));

		/// <summary>
		/// The vertical viewport offset from the origin.
		/// </summary>
		public double VerticalScrollOffset
		{
			get { return (double)GetValue(VerticalScrollOffsetProperty); }
			set { SetValue(VerticalScrollOffsetProperty, value); }
		}

		public static readonly DependencyProperty VerticalScrollOffsetProperty =
			DependencyProperty.Register("VerticalScrollOffset", typeof(double), typeof(DesignerCanvas),
				new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
					(d, e) => ((DesignerCanvas)d).InvalidateViewPortRect()));

		#endregion

		#region Events

		/// <summary>
		/// Raised when <see cref="SelectedItems"/> has been changed.
		/// </summary>
		/// <remarks>
		/// You may modify <see cref="SelectedItems"/> in the event handler,
		/// because the event is raised asynchronously.
		/// </remarks>
		public event EventHandler SelectionChanged;

		protected virtual void OnSelectionChanged()
		{
			SelectionChanged?.Invoke(this, EventArgs.Empty);
		}

		public event EventHandler<DesigningAdornerGeneratingEventArgs> DesigningAdornerGenerating;

		protected virtual void OnDesigningAdornerGenerating(DesigningAdornerGeneratingEventArgs e)
		{
			DesigningAdornerGenerating?.Invoke(this, e);
		}

		internal CanvasAdorner GenerateDesigningAdornerFormItem(ICanvasItem obj)
		{
			if (obj == null) throw new ArgumentNullException(nameof(obj));
			var entity = obj as IBoxCanvasItem;
			var polyLine = obj as IPolyLineCanvasItem;
			var e = new DesigningAdornerGeneratingEventArgs(obj);
			// Initialize defaults.
			if (entity != null)
				e.Adorder = new ResizeRotateAdorner(entity);
			else if (polyLine != null)
				e.Adorder = new PolyLineVerticesAdorner(polyLine);
			OnDesigningAdornerGenerating(e);
			return e.Adorder;
		}

		#endregion

		#region Items & States

		public GraphicalObjectCollection Items => _Items;

		public GraphicalObjectCollection SelectedItems => _SelectedItems;

		public GraphicalObjectContainerGenerator ItemContainerGenerator => _ItemContainerGenerator;

		// Indicates whether containers' IsSelected property is beging changed to
		// correspond with SelectedItems collection.
		private bool isSelectedContainersSynchronizing = false;

		private bool selectionChangedRaised = false;

		private void _SelectedItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			try
			{
				if (isSelectedContainersSynchronizing) throw new InvalidOperationException("This function does not support recursive calls。");
				isSelectedContainersSynchronizing = true;
				selectionChangedRaised = true;
				Dispatcher.CurrentDispatcher.BeginInvoke((Action)(() =>
				{
					if (selectionChangedRaised)
					{
						selectionChangedRaised = false;
						OnSelectionChanged();
					}
				}));
				switch (e.Action)
				{
					case NotifyCollectionChangedAction.Add:
					case NotifyCollectionChangedAction.Remove:
					case NotifyCollectionChangedAction.Replace:
						if (e.OldItems != null)
						{
							foreach (var item in e.OldItems)
							{
								var container = _ItemContainerGenerator.ContainerFromItem((ICanvasItem)item);
								container?.SetValue(Selector.IsSelectedProperty, false);
							}
						}
						if (e.NewItems != null)
						{
							foreach (var item in e.NewItems)
							{
								var container = _ItemContainerGenerator.ContainerFromItem((ICanvasItem)item);
								container?.SetValue(Selector.IsSelectedProperty, true);
							}
						}
						break;
					case NotifyCollectionChangedAction.Reset:
						var unselectedContainers = partCanvas.Children.Cast<DependencyObject>().ToList();
						foreach (var item in SelectedItems)
						{
							var container = _ItemContainerGenerator.ContainerFromItem(item);
							if (container != null)
							{
								container.SetValue(Selector.IsSelectedProperty, true);
								unselectedContainers.Remove(container);
							}
						}
						foreach (var item in unselectedContainers)
						{
							item.SetValue(Selector.IsSelectedProperty, false);
						}
						break;
				}
			}
			finally
			{
				isSelectedContainersSynchronizing = false;
			}
		}

		private void _Items_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (partCanvas == null)
			{
				// The control may have not been loaded.
				return;
			}
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
				case NotifyCollectionChangedAction.Remove:
				case NotifyCollectionChangedAction.Replace:
					if (e.OldItems != null)
					{
						foreach (ICanvasItem item in e.OldItems)
						{
							item.BoundsChanged -= Item_BoundsChanged;
							SetContainerVisibility(item, false);
							_SelectedItems.Remove(item);
						}
					}
					if (e.NewItems != null)
					{
						foreach (ICanvasItem item in e.NewItems)
						{
							if (item == null) continue;
							if (_ViewPortRect.IntersectsWith(item.Bounds))
							{
								SetContainerVisibility(item, true);
							}
							item.BoundsChanged += Item_BoundsChanged;
						}
						UnionExtendRect(e.NewItems.Cast<ICanvasItem>().GetBounds());
					}
					break;
				case NotifyCollectionChangedAction.Reset:
					_ItemContainerGenerator.RecycleAll();
					partCanvas.Children.Clear();
					_SelectedItems.Clear();
					foreach (
						var item in _Items.ObjectsInRegion(_ViewPortRect, ItemSelectionOptions.IncludePartialSelection))
						_ItemContainerGenerator.CreateContainer(item);
					RefreshExtendRect();
					break;
			}
			this.InvalidateMeasure();
		}

		private void Item_BoundsChanged(object sender, EventArgs e)
		{
			var obj = (ICanvasItem)sender;
			// Bring the container into view if the bounds has been moved into viewport, vice versa.
			SetContainerVisibility(obj, _ViewPortRect.IntersectsWith(obj.Bounds));
			UnionExtendRect(obj.Bounds);
		}

		private void changeZIndex(ICanvasItem item, int index)
		{
			var container = _ItemContainerGenerator.ContainerFromItem(item);
			if (container != null)
			{
				partCanvas.Children.Remove((UIElement)container);
				partCanvas.Children.Insert(index, (UIElement)container);
			}
		}


		#endregion

		#region UI

		private Canvas partCanvas, adornerCanvas;
		private ScrollBar horizontalScrollBar, verticalScrollBar;
		private readonly TranslateTransform canvasTranslateTransform = new TranslateTransform();
		private readonly ScaleTransform canvasScaleTransform = new ScaleTransform();
		private readonly TranslateTransform adornerCanvasTranslateTransform = new TranslateTransform();

		internal static DesignerCanvas FindDesignerCanvas(DependencyObject childContainer)
		{
			while (childContainer != null)
			{
				childContainer = VisualTreeHelper.GetParent(childContainer);
				var dc = childContainer as DesignerCanvas;
				if (dc != null) return dc;
			}
			return null;
		}

		internal void AddAdorner(CanvasAdorner adorner)
		{
			if (adorner == null) throw new ArgumentNullException(nameof(adorner));
			adorner.SetCanvas(this);
			adornerCanvas.Children.Add(adorner);
		}

		internal void RemoveAdorner(CanvasAdorner adorner)
		{
			if (adorner == null) throw new ArgumentNullException(nameof(adorner));
			if (adorner.ParentCanvas != this) throw new InvalidOperationException("Invalid ParentCanvas.");
			adorner.SetCanvas(null);
			adornerCanvas.Children.Remove(adorner);
		}

		private DrawingVisual debuggingVisual;

		public override void OnApplyTemplate()
		{
			base.OnApplyTemplate();
			partCanvas = (Canvas)GetTemplateChild("PART_Canvas");
			adornerCanvas = (Canvas)GetTemplateChild("PART_AdornerCanvas");
			horizontalScrollBar = (ScrollBar)GetTemplateChild("PART_HorizontalScrollBar");
			verticalScrollBar = (ScrollBar)GetTemplateChild("PART_VerticalScrollBar");
			var ct = new TransformGroup();
			ct.Children.Add(canvasTranslateTransform);
			ct.Children.Add(canvasScaleTransform);
			partCanvas.RenderTransform = ct;
			adornerCanvas.RenderTransform = adornerCanvasTranslateTransform;
			//horizontalScrollBar.SetBinding(RangeBase.ValueProperty,
			//new Binding("HorizontalScrollOffset") {Source = this});
			//verticalScrollBar.SetBinding(RangeBase.ValueProperty,
			//    new Binding("VerticalScrollOffset") {Source = this});
		}

		/// <summary>
		/// Update ViewPort rectangle & its children when needed.
		/// </summary>
		private void InvalidateViewPortRect()
		{
			Dispatcher.InvokeAsync(() =>
			{
				var vp = ViewPortRect;
				if (_ViewPortRect != vp)
				{
					// Generate / Recycle Items
					OnViewPortChanged(_ViewPortRect, vp);
					var z = Zoom / 100.0;
					canvasTranslateTransform.X = -vp.Left;
					canvasTranslateTransform.Y = -vp.Top;
					adornerCanvasTranslateTransform.X = -vp.Left * z;
					adornerCanvasTranslateTransform.Y = -vp.Top * z;
				}
				_ViewPortRect = vp;
			}, DispatcherPriority.Render);
		}

		private void OnViewPortChanged(Rect oldViewPort, Rect newViewPort)
		{
			double delta;
			const double safetyMargin = 10;

			delta = newViewPort.X - oldViewPort.X;
			if (delta > 0)
				SetContainerVisibility(new Rect(_ExtendRect.X - safetyMargin, _ExtendRect.Y - safetyMargin,
					newViewPort.X - (_ExtendRect.X - safetyMargin), _ExtendRect.Height - safetyMargin * 2), false);
			else if (delta < 0)
				SetContainerVisibility(new Rect(newViewPort.X, newViewPort.Y, -delta, newViewPort.Height), true);

			delta = newViewPort.Y - oldViewPort.Y;
			if (delta > 0)
				SetContainerVisibility(new Rect(_ExtendRect.X - safetyMargin, _ExtendRect.Y - safetyMargin,
					_ExtendRect.Width + safetyMargin * 2, newViewPort.Y - (_ExtendRect.Y - safetyMargin)), false);
			else if (delta < 0)
				SetContainerVisibility(new Rect(newViewPort.X, newViewPort.Y, newViewPort.Width, -delta), true);

			delta = newViewPort.Right - oldViewPort.Right;
			if (delta > 0)
				SetContainerVisibility(new Rect(oldViewPort.Right, newViewPort.Y, delta, newViewPort.Height), true);
			else if (delta < 0)
				SetContainerVisibility(new Rect(newViewPort.Right, double.MinValue / 2,
					double.MaxValue, double.MaxValue), false);

			delta = newViewPort.Bottom - oldViewPort.Bottom;
			if (delta > 0)
				SetContainerVisibility(new Rect(newViewPort.X, oldViewPort.Bottom, newViewPort.Width, delta), true);
			else if (delta < 0)
				SetContainerVisibility(new Rect(double.MinValue / 2, newViewPort.Bottom,
					double.MaxValue, double.MaxValue), false);
		}

		private void SetContainerVisibility(ICanvasItem item, bool visible)
		{
			if (item == null) throw new ArgumentNullException(nameof(item));
			if (visible)
			{
				if (_ItemContainerGenerator.ContainerFromItem(item) == null)
				{
					var container = (UIElement)_ItemContainerGenerator.CreateContainer(item);
					// Note these 2 statements shouldn't be swapped,
					// so the container will not unnecessarily fire NotifyItemIsSelectedChanged().
					container.SetValue(Selector.IsSelectedProperty, _SelectedItems.Contains(item));
					partCanvas.Children.Add(container);
				}
			}
			else
			{
				var container = _ItemContainerGenerator.ContainerFromItem(item);
				if (container != null)
				{
					partCanvas.Children.Remove((UIElement)container);
					_ItemContainerGenerator.Recycle(container);
				}
			}
		}

		/// <summary>
		/// Decides whether to render childen in certain rectangle.
		/// </summary>
		private void SetContainerVisibility(Rect rect, bool visible)
		{
			// Allow partial shown containers.
			// Hide when the container is contained in rect.
			foreach (var obj in _Items.ObjectsInRegion(rect, visible
				? ItemSelectionOptions.IncludePartialSelection
				: ItemSelectionOptions.None))
				SetContainerVisibility(obj, visible);
		}

		internal void RenderImage(RenderTargetBitmap target)
		{
			if (target == null) throw new ArgumentNullException(nameof(target));
			partCanvas.HorizontalAlignment = HorizontalAlignment.Left;
			partCanvas.VerticalAlignment = VerticalAlignment.Top;
			try
			{
				ShowContainers();
				// Wait for item rendering.
				CanvasImageExporter.DoEvents();
				target.Render(partCanvas);
			}
			finally
			{
				HideCoveredContainers();
				partCanvas.HorizontalAlignment = HorizontalAlignment.Stretch;
				partCanvas.VerticalAlignment = VerticalAlignment.Stretch;
			}
		}

		internal void ShowContainers()
		{
			// SLOW!
			partCanvas.Width = ExtentWidth;
			partCanvas.Height = ExtentHeight;
			foreach (var item in Items)
			{
				SetContainerVisibility(item, true);
			}
		}

		internal void HideCoveredContainers()
		{
			double delta;
			// Top
			SetContainerVisibility(new Rect(_ExtendRect.Left, _ExtendRect.Top,
				_ExtendRect.Width, _ViewPortRect.Top), false);
			// Bottom
			delta = _ExtendRect.Bottom - _ViewPortRect.Bottom;
			if (delta > 0)
			{
				SetContainerVisibility(new Rect(_ExtendRect.Left, _ViewPortRect.Bottom, _ExtendRect.Width,
					delta), false);
			}
			// Left
			SetContainerVisibility(new Rect(_ExtendRect.Left, _ViewPortRect.Top, _ViewPortRect.Left - _ExtendRect.Left,
				_ViewPortRect.Height), false);
			// Right
			delta = _ExtendRect.Right - _ViewPortRect.Right;
			if (delta > 0)
			{
				SetContainerVisibility(new Rect(_ViewPortRect.Right, _ViewPortRect.Top,
					delta, _ViewPortRect.Height), false);
			}
			partCanvas.Width = double.NaN;
			partCanvas.Height = double.NaN;
		}

		#endregion

		#region Interactive

		private Point? RubberbandStartPoint = null;

		/// <summary>
		/// Invoked when an unhandled <see cref="E:System.Windows.Input.Mouse.MouseDown"/> attached event reaches an element in its route that is derived from this class. Implement this method to add class handling for this event. 
		/// </summary>
		/// <param name="e">The <see cref="T:System.Windows.Input.MouseButtonEventArgs"/> that contains the event data. This event data reports details about the mouse button that was pressed and the handled state.</param>
		protected override void OnMouseDown(MouseButtonEventArgs e)
		{
			if (IsInBackground(e.OriginalSource as DependencyObject))
			{
				RubberbandStartPoint = e.GetPosition(this);
				Focus();
			}
			base.OnMouseDown(e);
		}

		/// <summary>
		/// Invoked when an unhandled <see cref="E:System.Windows.Input.Mouse.MouseMove"/> attached event reaches an element in its route that is derived from this class. Implement this method to add class handling for this event. 
		/// </summary>
		/// <param name="e">The <see cref="T:System.Windows.Input.MouseEventArgs"/> that contains the event data.</param>
		protected override void OnMouseMove(MouseEventArgs e)
		{
			if (RubberbandStartPoint != null)
			{
				if (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)
				{
					if ((e.GetPosition(this) - RubberbandStartPoint.Value).Length > 2)
					{
						var adornerLayer = AdornerLayer.GetAdornerLayer(partCanvas);
						if (adornerLayer != null)
						{
							var adorner = new RubberbandAdorner(this, RubberbandStartPoint.Value, Rubberband_Callback);
							adornerLayer.Add(adorner);
							RubberbandStartPoint = null;
						}
					}
				}
			}
			base.OnMouseMove(e);
		}

		/// <summary>
		/// Invoked when an unhandled <see cref="E:System.Windows.Input.Mouse.MouseUp"/> routed event reaches an element in its route that is derived from this class. Implement this method to add class handling for this event. 
		/// </summary>
		/// <param name="e">The <see cref="T:System.Windows.Input.MouseButtonEventArgs"/> that contains the event data. The event data reports that the mouse button was released.</param>
		protected override void OnMouseUp(MouseButtonEventArgs e)
		{
			RubberbandStartPoint = null;
			// Note we should determine whether the mouse is in the blank canvas area.
			// This event handler will be fired even when mouse is in the child UIElement.
			if (IsInBackground(e.OriginalSource as DependencyObject))
			{
				_SelectedItems.Clear();
				Focus();
			}
			base.OnMouseUp(e);
		}

		/// <summary>
		/// Determines whether the specified element is in the DesignerCanvas
		/// instead of any objects (Entity/Connection) on the canvas.
		/// </summary>
		/// <param name="element"></param>
		/// <returns></returns>
		private bool IsInBackground(DependencyObject element)
		{
			while (element != null)
			{
				if (element is DesignerCanvas) return true;
				if (element is DesignerCanvasItemContainer) return false;
				element = VisualTreeHelper.GetParent(element);
			}
			return false;
		}

		private static readonly double[] standardZoomValues =
		{
			0.1, 1, 5, 12.5, 25, 30, 35, 50, 75, 100, 200, 400, 800, 1200, 1600, 2000
		};

		/// <summary>
		/// Invoked when an unhandled <see cref="E:System.Windows.Input.Mouse.PreviewMouseWheel"/> attached event reaches an element in its route that is derived from this class. Implement this method to add class handling for this event. 
		/// </summary>
		/// <param name="e">The <see cref="T:System.Windows.Input.MouseWheelEventArgs"/> that contains the event data.</param>
		protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
		{
			var mod = Keyboard.Modifiers;
			// Automatic Zoom
			if ((mod & ModifierKeys.Control) == ModifierKeys.Control)
			{
				//If value is not found and value is less than one or more elements in array,
				// a negative number which is the bitwise complement of the index of the first
				// element that is larger than value. 
				var stdZoomIndex = Array.BinarySearch(standardZoomValues, Zoom);
				var step = Math.Sign(e.Delta);
				if (stdZoomIndex >= 0)
				{
					stdZoomIndex += step;
				}
				else
				{
					stdZoomIndex = ~stdZoomIndex;
					if (step > 0)
						stdZoomIndex += step - 1;
					else
						stdZoomIndex += step;
				}
				if (stdZoomIndex >= 0 && stdZoomIndex < standardZoomValues.Length)
				{
					Zoom = standardZoomValues[stdZoomIndex];
				}
			}
			base.OnPreviewMouseWheel(e);
		}

		private void Rubberband_Callback(object o, Rect rect)
		{
			rect = new Rect(PointToCanvas(rect.TopLeft), PointToCanvas(rect.BottomRight));
			var mod = Keyboard.Modifiers;
			if ((mod & (ModifierKeys.Shift | ModifierKeys.Control)) == ModifierKeys.None)
			{
				_SelectedItems.Clear();
				_SelectedItems.AddRange(Items.ObjectsInRegion(rect));
			}
			else
			{
				var newItems = new HashSet<ICanvasItem>(Items.ObjectsInRegion(rect));
				if ((mod & ModifierKeys.Shift) == ModifierKeys.Shift)
				{
					// Switch
					var intersection = _SelectedItems.Where(i => newItems.Contains(i)).ToList();
					foreach (var item in intersection)
					{
						_SelectedItems.Remove(item);
						newItems.Remove(item);
					}
					_SelectedItems.AddRange(newItems);
				}
				else if ((mod & ModifierKeys.Control) == ModifierKeys.Control)
				{
					// Merge
					foreach (var item in _SelectedItems) newItems.Remove(item);
					_SelectedItems.AddRange(newItems);
				}
			}
			Focus();
		}

		#endregion

		#region Public UI

		public event EventHandler ZoomChanged;

		public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register("Zoom",
			typeof(double), typeof(DesignerCanvas),
			new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsMeasure, ZoomChangedCallback),
			v =>
			{
				var value = (double)v;
				return value >= 0.1 && value <= 50000;
			});

		private static void ZoomChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var dc = (DesignerCanvas)d;
			var z = (double)e.NewValue / 100;
			var vp = dc.ViewPortRect;
			dc.canvasScaleTransform.ScaleX = dc.canvasScaleTransform.ScaleY = z;
			dc.adornerCanvasTranslateTransform.X = -vp.Left * z;
			dc.adornerCanvasTranslateTransform.Y = -vp.Top * z;
			dc.OnZoomChanged();
		}

		/// <summary>
		/// Gets / sets the canvas zoom percentage. 
		/// </summary>
		/// <value>
		/// The zoom percentage expressed as a value,
		/// 0.1 to 50000.0. The default is 100.0, which corresponds to 100.0%.
		/// </value>
		public double Zoom
		{
			get { return (double)GetValue(ZoomProperty); }
			set { /*SetValue(ZoomProperty, value);*/ }    ///////// Uncomment this to enable zooming./////////////
		}

		/// <summary>
		/// Translates a point from <see cref="DesignerCanvas"/> to 
		/// its internal Canvas panel.
		/// </summary>
		/// <param name="point">The point to be translated, relative to <see cref="DesignerCanvas"/>.</param>
		public Point PointToCanvas(Point point)
		{
			return this.TranslatePoint(point, partCanvas);
		}

		/// <summary>
		/// Translates a point to <see cref="DesignerCanvas"/> from 
		/// its internal Canvas panel.
		/// </summary>
		/// <param name="point">The point to be translated, relative to the internal panel of <see cref="DesignerCanvas"/>.</param>
		public Point PointFromCanvas(Point point)
		{
			return partCanvas.TranslatePoint(point, this);
		}

		/// <summary>
		/// Scrolls the canvas so that the specific rectangle on
		/// canvas can be shown in the viewport.
		/// </summary>
		public void ScrollIntoView(Rect rect)
		{
			var vp = ViewPortRect;
			double dx = 0.0, dy = 0.0;
			if (vp.Contains(rect)) return;
			if (rect.Left > vp.Right) dx = rect.Right - vp.Right;
			else if (rect.Right < vp.Left) dx = rect.Left - vp.Left;
			if (rect.Top > vp.Bottom) dy = rect.Bottom - vp.Bottom;
			else if (rect.Bottom < vp.Top) dy = rect.Top - vp.Top;
			HorizontalScrollOffset += dx;
			VerticalScrollOffset += dy;
		}

		/// <summary>
		/// Scrolls the canvas so that the specific canvas item 
		/// can be shown in the viewport.
		/// </summary>
		public void ScrollIntoView(ICanvasItem item)
		{
			if (item == null) throw new ArgumentNullException(nameof(item));
			ScrollIntoView(item.Bounds);
		}

		/// <summary>
		/// Exports the image of canvas to the specified <see cref="Stream"/>.
		/// </summary>
		public void ExportImage(Stream s, BitmapEncoder encoder)
		{
			ExportImage(s, encoder, CanvasImageExporter.WpfDpi, CanvasImageExporter.WpfDpi);
		}

		/// <summary>
		/// Exports the image of canvas to the specified <see cref="Stream"/>.
		/// </summary>
		public void ExportImage(Stream s, BitmapEncoder encoder, double dpiX, double dpiY)
		{
			CanvasImageExporter.ExportImage(this, s, encoder, dpiX, dpiY);
		}

		/// <summary>
		/// Exports the image of canvas to the specified file.
		/// </summary>
		public void ExportImage(string fileName)
		{
			ExportImage(fileName, 300, 300);
		}

		/// <summary>
		/// Exports the image of canvas to the specified file.
		/// </summary>
		public void ExportImage(string fileName, double dpiX, double dpiY)
		{
			CanvasImageExporter.ExportImage(this, fileName, dpiX, dpiY);
		}
		#endregion

		#region Notifications from Children

		internal void NotifyItemMouseDown(DesignerCanvasItemContainer container)
		{
			Debug.Assert(container != null);
			//Debug.Print("NotifyItemMouseDown");
			if (container.IsSelected == false)
			{
				// Left click to selecte an object.
				if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == ModifierKeys.None)
				{
					// Unselect other items first.
					_SelectedItems.Clear();
				}
				_SelectedItems.Add(_ItemContainerGenerator.ItemFromContainer(container));
			}
		}

		internal void NotifyItemIsSelectedChanged(DependencyObject container)
		{
			Debug.Assert(container != null);
			// Show / Hides the adorner

			// Do not update SelectedItems when SelectedItems are being updated.
			if (isSelectedContainersSynchronizing) return;
			var item = _ItemContainerGenerator.ItemFromContainer(container);
			if (item == null) return;
			if (Selector.GetIsSelected(container))
			{
				Debug.Assert(!SelectedItems.Contains(item));
				SelectedItems.Add(item);
			}
			else
			{
				var reuslt = SelectedItems.Remove(item);
				Debug.Assert(reuslt);
			}
		}

		#endregion

		#region Custom Methods
		private static List<PlaceholderCanvasItem> placeholdersList = new List<PlaceholderCanvasItem>();

		public PlaceholderCanvasItem AddPlaceholder()
		{
			// Create a new PlaceholderCanvasItem with specified properties
			var placeholderItem = new PlaceholderCanvasItem(0, 0, 160, 120, 2, 6);
			placeholderItem.LockedAspectRatio = false;
			placeholderItem.LockedPosition = false;
			placeholderItem.Resizeable = true;

			// Add the PlaceholderCanvasItem to your canvas or UI element
			placeholdersList.Add(placeholderItem);
			placeholderItem.PlaceholderNo = placeholdersList.Count;
			Items.Add(placeholderItem);
			SelectedItems.Clear();
			SelectedItems.Add(placeholderItem);

			return placeholderItem;
		}

		public void ClearCanvas()
		{
			// Check if the _Items, _SelectedItems, or placeholdersList are null before proceeding
			if (_Items == null || _SelectedItems == null || placeholdersList == null)
			{
				return; // Return early to avoid null reference exceptions
			}

			if (_SelectedItems.Count == 0)
			{
				_Items.Clear();
				placeholdersList.Clear();
			}
			else
			{
				while (_SelectedItems.Count > 0)
				{
					var selectedItem = _SelectedItems.ElementAt(0);

					if (selectedItem is PlaceholderCanvasItem)
					{
						for (int i = 0; i < placeholdersList.Count; i++)
						{
							if (placeholdersList[i] == selectedItem)
							{
								placeholdersList.RemoveAt(i);
								i--; // Adjust index after removing the item to avoid skipping the next element
							}
							else
							{
								placeholdersList[i].PlaceholderNo = i + 1;
							}
						}
					}

					// Remove the selected item from _Items and _SelectedItems safely
					_Items.Remove(selectedItem);
				}
			}
		}


		public ImageCanvasItem ImportImage()
		{
			try
			{
				Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
				openFileDialog.Filter = "Image files (*.jpg, *.jpeg, *.jpe, *.jfif, *.png, *.bmp, *.dib, *.gif, *.tif, *.tiff, *.ico, *.svg, *.svgz, *.wmf, *.emf, *.webp)|*.jpg; *.jpeg; *.jpe; *.jfif; *.png; *.bmp; *.dib; *.gif; *.tif; *.tiff; *.ico; *.svg; *.svgz; *.wmf; *.emf; *.webp|All files (*.*)|*.*";
				openFileDialog.Title = "Select a background image";
				if (openFileDialog.ShowDialog() == true)
				{
					// always use for aspect ratio
					var img = new ImageCanvasItem(0, 0, Width, Height, openFileDialog.FileName, RatioX, RatioY);
					img.LockedAspectRatio = false;
					img.LockedPosition = false;
					img.Resizeable = true;
					Items.Add(img);
					SelectedItems.Clear();
					SelectedItems.Add(img);
					return img;
				}
			}
			catch (FileNotFoundException ex)
			{
				MessageBox.Show(ex.Message, "File not found", MessageBoxButton.OK, MessageBoxImage.Error);
			}
			return null;
		}

		public void PrintImage()
		{
			try
			{
				BitmapSource image = CanvasImageExporter.CreateImage(this, 300, 300);
				// print as 4x6 image and also show the preview image
				var printDialog = new PrintDialog();
				if (printDialog.ShowDialog() == true)
				{
					var printCapabilities = printDialog.PrintQueue.GetPrintCapabilities(printDialog.PrintTicket);
					var scale = Math.Min(printCapabilities.PageImageableArea.ExtentWidth / image.Width, printCapabilities.PageImageableArea.ExtentHeight / image.Height);
					var printImage = new Image
					{
						Source = image,
						Stretch = Stretch.Uniform,
						StretchDirection = StretchDirection.DownOnly,
						Width = image.Width * scale,
						Height = image.Height * scale
					};
					var printCanvas = new Canvas
					{
						Width = printImage.Width,
						Height = printImage.Height
					};
					printCanvas.Children.Add(printImage);
					printDialog.PrintVisual(printCanvas, "Photobooth");
				}
			}
			catch (Exception)
			{ }
		}

		public void ExportImage()
		{
			try
			{
				// save file
				Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
				saveFileDialog.Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp|TIFF Image (*.tif)|*.tif|GIF Image (*.gif)|*.gif|All files (*.*)|*.*";
				saveFileDialog.Title = "Save an image";
				if (saveFileDialog.ShowDialog() == true)
				{
					var encoder = CanvasImageExporter.EncoderFromFileName(saveFileDialog.FileName);
					using (var s = File.Open(saveFileDialog.FileName, FileMode.Create))
					{
						CanvasImageExporter.ExportImage(this, s, encoder, 300, 300);
					}
				}
			}
			catch (Exception)
			{ }
		}

		public void ChangeOrientationOfSelectedItems()
		{
			// for each selected item
			foreach (IBoxCanvasItem item in _SelectedItems)
			{
				//swap the width and height according to positon
				var temp = item.Width;
				item.Width = item.Height;
				item.Height = temp;
			}
		}

		public void AlignLeft()
		{
			// there must be multiple items selected
			//if (_SelectedItems.Count <= 1) { return; }

			// store the smallest value of X
			double smallestX = double.MaxValue;
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				if (_SelectedItems.ElementAt(i).Left < smallestX)
				{
					smallestX = _SelectedItems.ElementAt(i).Left;
				}
			}

			// set all items to the smallest value of X
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				_SelectedItems.ElementAt(i).Left = smallestX;
			}
		}

		public void AlignTop()
		{
			//if (_SelectedItems.Count <= 1) { return; }

			// store the smallest value of Y
			double smallestY = double.MaxValue;
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				if (_SelectedItems.ElementAt(i).Top < smallestY)
				{
					smallestY = _SelectedItems.ElementAt(i).Top;
				}
			}

			// set all items to the smallest value of Y
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				_SelectedItems.ElementAt(i).Top = smallestY;
			}
		}

		public void AlignHorizontallyCenter()
		{
			// there must be multiple items selected
			//if (_SelectedItems.Count <= 1) { return; }

			// store the largest value of X
			double largestX = double.MinValue;
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				var element = _SelectedItems.ElementAt(i);
				if (element.Bounds.Right > largestX)
				{
					largestX = element.Bounds.Right;
				}
			}

			// store the smallest value of X
			double smallestX = double.MaxValue;
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				var element = _SelectedItems.ElementAt(i);
				if (element.Left < smallestX)
				{
					smallestX = element.Left;
				}
			}

			// calculate the center
			double center = (largestX - smallestX) / 2 + smallestX;

			// set all items to the center
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				_SelectedItems.ElementAt(i).Left = center - _SelectedItems.ElementAt(i).Bounds.Width / 2;
			}
		}

		public void AlignStretchHorizontal()
		{
			double smallestX = double.MaxValue;
			double largestWidth = double.MinValue;
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				IBoxCanvasItem item = _SelectedItems[i] as IBoxCanvasItem;
				if (item.Left < smallestX)
				{
					smallestX = item.Left;
				}

				if (item.Width > largestWidth)
				{
					largestWidth = item.Width;
				}
			}

			// set all items to the smallest value of X
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				IBoxCanvasItem item = _SelectedItems[i] as IBoxCanvasItem;
				item.Left = smallestX;
				item.Width = largestWidth;

			}
		}

		public void AlignStretchVertical()
		{
			double smallestY = double.MaxValue;
			double largestHeight = double.MinValue;
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				IBoxCanvasItem item = _SelectedItems[i] as IBoxCanvasItem;
				if (item.Top < smallestY)
				{
					smallestY = item.Top;
				}

				if (item.Height > largestHeight)
				{
					largestHeight = item.Height;
				}
			}

			// set all items to the smallest value of X
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				IBoxCanvasItem item = _SelectedItems[i] as IBoxCanvasItem;
				item.Top = smallestY;
				item.Height = largestHeight;

			}
		}

		// align vertically center
		public void AlignVerticallyCenter()
		{
			// there must be multiple items selected
			//if (_SelectedItems.Count <= 1) { return; }
			double largestY = double.MinValue;
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				var element = _SelectedItems.ElementAt(i);
				if (element.Bounds.Bottom > largestY)
				{
					largestY = element.Bounds.Bottom;
				}
			}

			// store the smallest value of Y
			double smallestY = double.MaxValue;
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				var element = _SelectedItems.ElementAt(i);
				if (element.Top < smallestY)
				{
					smallestY = element.Top;
				}
			}

			// calculate the center
			double center = (largestY - smallestY) / 2 + smallestY;

			// set all items to the center
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				_SelectedItems.ElementAt(i).Top = center - _SelectedItems.ElementAt(i).Bounds.Height / 2;
			}
		}

		public void AlignRight()
		{
			// there must be multiple items selected
			//if (_SelectedItems.Count <= 1) { return; }

			// store the largest value of X
			double largestX = double.MinValue;
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				var element = _SelectedItems.ElementAt(i);
				if (element.Bounds.Right > largestX)
				{
					largestX = element.Bounds.Right;
				}
			}

			// set all items to the largest value of X
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				_SelectedItems.ElementAt(i).Left = largestX - _SelectedItems.ElementAt(i).Bounds.Width;
			}
		}

		public void AlignBottom()
		{
			double largestY = double.MinValue;
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				var element = _SelectedItems.ElementAt(i);
				if (element.Bounds.Bottom > largestY)
				{
					largestY = element.Bounds.Bottom;
				}
			}

			// set all items to the largest value of Y
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				_SelectedItems.ElementAt(i).Top = largestY - _SelectedItems.ElementAt(i).Bounds.Height;
			}
		}

		public void ChangeCanvasOrientation()
		{
			//swap the width and height according to positon
			var temp = Width;
			Width = Height;
			Height = temp;
		}

		public void DuplicateSelected()
		{
			int selectedCount = _SelectedItems.Count;

			for (int i = 0; i < selectedCount; i++)
			{
				var item = _SelectedItems.ElementAt(i);
				var newItem = item.Clone();
				newItem.Left += 10;
				newItem.Top += 10;
				_Items.Add(newItem);
				_SelectedItems.Add(newItem);
			}
		}

		// lock aspect Ratio of selected items
		public void ChangeAspectRatio(bool locked)
		{
			// for each selected item
			foreach (IBoxCanvasItem item in _SelectedItems)
			{
				// set the aspect ratio to the current ratio
				item.LockedAspectRatio = locked;
			}
		}

		// lock position
		public void ChangePositionLock(bool locked)
		{
			foreach (IBoxCanvasItem item in _SelectedItems)
			{
				item.LockedPosition = locked;
			}
		}

		// lock size
		public void LockSize(bool locked)
		{
			GraphicalObjectCollection canvasItems = new GraphicalObjectCollection();
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				IBoxCanvasItem item = (IBoxCanvasItem)_SelectedItems.ElementAt(i);
				item.Resizeable = !locked;
				Items.Remove(item);
				Items.Add(item);
				canvasItems.Add(item);
			}
			_SelectedItems.AddRange(canvasItems);
		}

		// return width and height of selected items (null, if multiple selected, and have different size) 
		public int[] GetSizeOfSelectedItems()
		{
			int width = -1;
			int height = -1;

			foreach (IBoxCanvasItem item in _SelectedItems)
			{
				if (width == -1)
				{
					width = (int)item.Width;
					height = (int)item.Height;
				}
				else if (width != (int)item.Width || height != (int)item.Height)
				{
					width = -1;
					break;
				}
			}
			if (width == -1)
			{
				return null;
			}

			return new int[] { width, height };
		}

		public void SetWidthOfSelectedItems(int width)
		{
			foreach (IBoxCanvasItem item in _SelectedItems)
			{
				item.Width = width;
			}
		}

		public void SetHeightOfSelectedItems(int height)
		{
			foreach (IBoxCanvasItem item in _SelectedItems)
			{
				item.Height = height;
			}
		}

		// return position of selected items (null, if multiple selected, and have different position)
		public int[] GetPositionOfSelectedItems()
		{
			int left = -1;
			int top = -1;

			foreach (IBoxCanvasItem item in _SelectedItems)
			{
				if (left == -1)
				{
					left = (int)item.Left;
					top = (int)item.Top;
				}
				else if (left != (int)item.Left || top != (int)item.Top)
				{
					left = -1;
					break;
				}
			}
			if (left == -1)
			{
				return null;
			}

			return new int[] { left, top };
		}

		public void SetLeftOfSelectedItems(int left)
		{
			foreach (IBoxCanvasItem item in _SelectedItems)
			{
				item.Left = left;
			}
		}

		public void SetTopOfSelectedItems(int top)
		{
			foreach (IBoxCanvasItem item in _SelectedItems)
			{
				item.Top = top;
			}
		}

		// returns {width, height} of selected items (-1,-1) if multiple selected, and have different size
		public double[] SetAspectRatioOfSelectedItems(double ratio)
		{
			if (_SelectedItems.Count == 0) return new double[] { -1, -1 };
			double width = _SelectedItems.ElementAt(0).Bounds.Width;
			double height = _SelectedItems.ElementAt(0).Bounds.Height;

			foreach (IBoxCanvasItem item in _SelectedItems)
			{
				item.AspectRatio = ratio;
				if (item.Bounds.Width != width) width = -1;
				if (item.Bounds.Height != height) height = -1;
			}
			// return {width, height}
			return new double[] { width, height };
		}

		// bring to front
		public void BringToTheFront()
		{
			ChangeZIndexOfSelectedItems(partCanvas.Children.Count - 1);
		}

		public void SendToTheBack()
		{
			ChangeZIndexOfSelectedItems(0);
		}

		private void ChangeZIndexOfSelectedItems(int index)
		{
			for (int i = _Items.Count - 1; i >= 0; i--)
			{
				if (_SelectedItems.Contains(_Items.ElementAt(i)))
				{
					changeZIndex(_Items.ElementAt(i), index);
				}
			}
		}

		public void DeleteSelected()
		{
			// todo implement in next milestone
			int selectedCount = _SelectedItems.Count;

			for (int i = 0; i < selectedCount; i++)
			{
				var item = _SelectedItems.ElementAt(i);
				_Items.Remove(item);
				_SelectedItems.Remove(item);
			}
		}

		public void SetExplicitSelectedElementsPositionX(double xDiff)
		{


			foreach (IBoxCanvasItem item in _SelectedItems)
			{
				// Disable the bounds changed event and set the new value then re-enable it.
				item.Left += xDiff;
			}
		}

		public void SetExplicitSelectedElementsPositionY(double yDiff)
		{
			foreach (IBoxCanvasItem item in _SelectedItems)
			{
				// Disable the bounds changed event and set the new value then re-enable it.
				item.Top += yDiff;
			}
		}

		public void SetExplicitSelectedElementsWidth(double widthDiff)
		{
			foreach (IBoxCanvasItem item in _SelectedItems)
			{
				item.Width += widthDiff;
			}
		}

		public void SetExplicitSelectedElementsHeight(double heightDiff)
		{
			foreach (IBoxCanvasItem item in _SelectedItems)
			{
				item.Height += heightDiff;
			}
		}



		public string FileName { get; set; }
		public string FilePath { get; set; }

		public void SaveFile()
		{
			try 
			{
				FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoBooth", "Prints", Guid.NewGuid().ToString() + ".png");
				if (string.IsNullOrEmpty(FilePath))
				{
					SaveAsFile();
				}
				else
				{
					CanvasImageExporter.ExportImage(this, FilePath, 300, 300);
				}
			}
			catch (Exception)
			{ }
		}

		public void SaveAsFile()
		{
			try
			{
				// save file
				Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
				saveFileDialog.Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp|TIFF Image (*.tif)|*.tif|GIF Image (*.gif)|*.gif|All files (*.*)|*.*";
				saveFileDialog.Title = "Save an image";
				if (saveFileDialog.ShowDialog() == true)
				{
					CanvasImageExporter.ExportImage(this, saveFileDialog.FileName, 300, 300);
					FileName = saveFileDialog.SafeFileName;
					FilePath = saveFileDialog.FileName;
				}
			}
			catch (Exception)
			{ }
		}

		public void PrintFile()
		{
			try
			{
				BitmapSource image = CanvasImageExporter.CreateImage(this, 300, 300);
				// print as 4x6 image and also show the preview image
				var printDialog = new PrintDialog();
				if (printDialog.ShowDialog() == true)
				{
					var printCapabilities = printDialog.PrintQueue.GetPrintCapabilities(printDialog.PrintTicket);
					var scale = Math.Min(printCapabilities.PageImageableArea.ExtentWidth / image.Width, printCapabilities.PageImageableArea.ExtentHeight / image.Height);
					var printImage = new Image
					{
						Source = image,
						Stretch = Stretch.Uniform,
						StretchDirection = StretchDirection.DownOnly,
						Width = image.Width * scale,
						Height = image.Height * scale
					};
					var printCanvas = new Canvas
					{
						Width = printImage.Width,
						Height = printImage.Height
					};
					printCanvas.Children.Add(printImage);
					printDialog.PrintVisual(printCanvas, "Photobooth");
				}
			}
			catch (Exception)
			{ }
		}

		public void ViewFileInImageViewer()
		{
			try
			{
				var tempFile = Path.GetTempFileName();
				CanvasImageExporter.ExportImage(this, tempFile, 300, 300);
				Process.Start(tempFile);
			}
			catch (Exception)
			{ }
		}

		public void LockSelectedItemsPosition()
		{
			foreach (var item in _SelectedItems)
			{
				(item as IBoxCanvasItem).LockedPosition = true;
			}
		}

		public void FreeSelectedItemsPosition()
		{
			foreach (var item in _SelectedItems)
			{
				(item as IBoxCanvasItem).LockedPosition = false;
			}
		}

		public void LockSelectedItemsSize()
		{
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				IBoxCanvasItem item = (IBoxCanvasItem)_SelectedItems.ElementAt(i);
				item.Resizeable = false;
			}
		}

		public void FreeSelectedItemsSize()
		{
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				IBoxCanvasItem item = (IBoxCanvasItem)_SelectedItems.ElementAt(i);
				item.Resizeable = true;
			}
		}

		public void SetImageToCanvas()
		{
			for (int i = 0; i < _SelectedItems.Count; i++)
			{
				// Set image to canvas size and position it at start 0 ,0
				IBoxCanvasItem item = (IBoxCanvasItem)_SelectedItems.ElementAt(i);
				item.Width = Width;
				item.Height = Height;
				item.Left = 0;
				item.Top = 0;
			}
		}


		#endregion


		public int RatioX { get; private set; } = 4;
		public int RatioY { get; private set; } = 4;

		public void SetRatio(int RatioX, int RatioY)
		{
			this.RatioX = RatioX;
			this.RatioY = RatioY;

			// update width and height
			this.Width = RatioX * 100;
			this.Height = RatioY * 100;
		}



		public DesignerCanvas()
		{
			// Note PartCanvas property returns null here.
			_Items.CollectionChanged += _Items_CollectionChanged;
			_SelectedItems.CollectionChanged += _SelectedItems_CollectionChanged;
			_ItemContainerGenerator.ContainerPreparing += ItemContainerGenerator_ContainerPreparing;
		}

		private void ItemContainerGenerator_ContainerPreparing(object sender, ContainerPreparingEventArgs e)
		{
			if (EntityItemTemplate != null || ItemTemplateSelector != null)
			{
				e.Container.SetValue(ContentControl.ContentTemplateProperty, EntityItemTemplate);
				e.Container.SetValue(ContentControl.ContentTemplateSelectorProperty, ItemTemplateSelector);
			}
			else
			{
				e.Container.ClearValue(ContentControl.ContentTemplateProperty);
				e.Container.ClearValue(ContentControl.ContentTemplateSelectorProperty);
			}
		}

		static DesignerCanvas()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(DesignerCanvas),
				new FrameworkPropertyMetadata(typeof(DesignerCanvas)));
		}

		#region IScrollInfo

		private Rect _ExtendRect; // Boundary of virtual canvas, regardless of translation & scaling.
		private Rect _ViewPortRect = Rect.Empty; // Boundary of view port, relative to virtual canvas.

		/// <summary>
		/// Forces content to scroll until the coordinate space of a <see cref="T:System.Windows.Media.Visual"/> object is visible. 
		/// </summary>
		/// <returns>
		/// A <see cref="T:System.Windows.Rect"/> that is visible.
		/// </returns>
		/// <param name="visual">A <see cref="T:System.Windows.Media.Visual"/> that becomes visible.</param><param name="rectangle">A bounding rectangle that identifies the coordinate space to make visible.</param>
		public Rect MakeVisible(Visual visual, Rect rectangle)
		{
			if (visual == null) throw new ArgumentNullException(nameof(visual));
			// Determine the point to be shown.
			// Visual doesn't have TranslatePoint function.
			var focusPoint = new Point(0, 0);
			var fe = visual as FrameworkElement;
			if (fe != null)
			{
				// Make sure the center of the visual will be shown.
				focusPoint = fe.TranslatePoint(new Point(fe.ActualWidth / 2, fe.ActualHeight / 2), partCanvas);
			}
			else
			{
				focusPoint = visual.PointToScreen(new Point(0, 0));
				focusPoint = partCanvas.PointFromScreen(focusPoint);
			}
			// Move the view to the specific point.
			// Now the coordinate of visual is relative to the canvas.
			if (!_ViewPortRect.Contains(focusPoint))
			{
				//SetHorizontalOffset(focusPoint.X);
				//SetVerticalOffset(focusPoint.Y);
			}
			return rectangle;
		}

		/// <summary>
		/// Gets or sets a value that indicates whether scrolling on the vertical axis is possible. 
		/// </summary>
		/// <returns>
		/// true if scrolling is possible; otherwise, false. This property has no default value.
		/// </returns>
		public bool CanVerticallyScroll { get; set; }

		/// <summary>
		/// Gets or sets a value that indicates whether scrolling on the horizontal axis is possible.
		/// </summary>
		/// <returns>
		/// true if scrolling is possible; otherwise, false. This property has no default value.
		/// </returns>
		public bool CanHorizontallyScroll { get; set; }

		/// <summary>
		/// Gets the horizontal size of the extent.
		/// </summary>
		/// <returns>
		/// A <see cref="T:System.Double"/> that represents, in device independent pixels, the horizontal size of the extent. This property has no default value.
		/// </returns>
		public double ExtentWidth => _ExtendRect.Width * Zoom / 100.0;

		/// <summary>
		/// Gets the vertical size of the extent.
		/// </summary>
		/// <returns>
		/// A <see cref="T:System.Double"/> that represents, in device independent pixels, the vertical size of the extent.This property has no default value.
		/// </returns>
		public double ExtentHeight => _ExtendRect.Height * Zoom / 100.0;

		/// <summary>
		/// Gets the horizontal size of the viewport for this content.
		/// </summary>
		/// <returns>
		/// A <see cref="T:System.Double"/> that represents, in device independent pixels, the horizontal size of the viewport for this content. This property has no default value.
		/// </returns>
		public double ViewportWidth => _ViewPortRect.Width * Zoom / 100.0;

		/// <summary>
		/// Gets the vertical size of the viewport for this content.
		/// </summary>
		/// <returns>
		/// A <see cref="T:System.Double"/> that represents, in device independent pixels, the vertical size of the viewport for this content. This property has no default value.
		/// </returns>
		public double ViewportHeight => _ViewPortRect.Height * Zoom / 100.0;

		/// <summary>
		/// Gets the horizontal offset of the scrolled content.
		/// </summary>
		/// <returns>
		/// A <see cref="T:System.Double"/> that represents, in device independent pixels, the horizontal offset. This property has no default value.
		/// </returns>
		public double HorizontalOffset => _ViewPortRect.X * Zoom / 100.0;

		/// <summary>
		/// Gets the vertical offset of the scrolled content.
		/// </summary>
		/// <returns>
		/// A <see cref="T:System.Double"/> that represents, in device independent pixels, the vertical offset of the scrolled content. Valid values are between zero and the <see cref="P:System.Windows.Controls.Primitives.IScrollInfo.ExtentHeight"/> minus the <see cref="P:System.Windows.Controls.Primitives.IScrollInfo.ViewportHeight"/>. This property has no default value.
		/// </returns>
		public double VerticalOffset => _ViewPortRect.Y * Zoom / 100.0;

		/// <summary>
		/// Gets or sets a <see cref="T:System.Windows.Controls.ScrollViewer"/> element that controls scrolling behavior.
		/// </summary>
		/// <returns>
		/// A <see cref="T:System.Windows.Controls.ScrollViewer"/> element that controls scrolling behavior. This property has no default value.
		/// </returns>
		public ScrollViewer ScrollOwner { get; set; }

		#endregion

		#region Debug Support

#if DEBUG
		public int RenderedChildrenCount => partCanvas.Children.Count;
#endif

		#endregion

		#region 2016 July Remaster

		private const double ScrollStepIncrement = 10;
		private const double ScrollPageStepPreservation = 10;
		private const double ScrollWheelStepIncrementRel = 1.0 / 3;

		/// <summary>
		/// Called to remeasure a control. 
		/// </summary>
		/// <returns>
		/// The size of the control, up to the maximum specified by <paramref name="constraint"/>.
		/// </returns>
		/// <param name="constraint">The maximum size that the method can return.</param>
		protected override Size MeasureOverride(Size constraint)
		{
			var sz = base.MeasureOverride(constraint);
			if (double.IsInfinity(constraint.Width)) sz.Width = Math.Max(sz.Width, _ExtendRect.Width);
			if (double.IsInfinity(constraint.Height)) sz.Height = Math.Max(sz.Height, _ExtendRect.Height);
			return sz;
		}

		/// <summary>
		/// Called to arrange and size the content of a <see cref="T:System.Windows.Controls.Control"/> object. 
		/// </summary>
		/// <returns>
		/// The size of the control.
		/// </returns>
		/// <param name="arrangeBounds">The computed size that is used to arrange the content.</param>
		protected override Size ArrangeOverride(Size arrangeBounds)
		{
			RefreshScrollBarLimits();
			InvalidateViewPortRect();
			return base.ArrangeOverride(arrangeBounds);
		}

		/// <summary>
		/// Evaluate the virtual boundary.
		/// </summary>
		private void RefreshExtendRect()
		{
			_ExtendRect = _Items.Bounds;
			RefreshScrollBarLimits();
		}

		private void UnionExtendRect(Rect boundary)
		{
			var er = _ExtendRect;
			_ExtendRect.Union(boundary);
			if (_ExtendRect != er) RefreshScrollBarLimits();
		}

		private void RefreshScrollBarLimits()
		{
			if (_ExtendRect.IsEmpty)
			{
				//horizontalScrollBar.Minimum = horizontalScrollBar.Maximum = 0;
				//verticalScrollBar.Minimum = verticalScrollBar.Maximum = 0;
			}
			else
			{
				//var vp = ViewPortSize;
				//horizontalScrollBar.Minimum = _ExtendRect.Left;
				//horizontalScrollBar.Maximum = _ExtendRect.Right;
				//horizontalScrollBar.ViewportSize = vp.Width;
				//verticalScrollBar.Minimum = _ExtendRect.Top;
				//verticalScrollBar.Maximum = _ExtendRect.Bottom;
				//verticalScrollBar.ViewportSize = vp.Height;
			}
		}

		public Rect ViewPortRect
		{
			get
			{
				var ps = ViewPortSize;
				return new Rect(HorizontalScrollOffset, VerticalScrollOffset, ps.Width, ps.Height);
			}
		}

		public Size ViewPortSize
		{
			get
			{
				var z = Zoom / 100.0;
				return new Size(partCanvas.ActualWidth / z, partCanvas.ActualHeight / z);
			}
		}
		#endregion

		protected virtual void OnZoomChanged()
		{
			ZoomChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	/// <summary>
	/// Generates <see cref="UIElement"/>s for GraphicalObjects.
	/// Note <see cref="GraphicalObjectCollection"/> has no indexer.
	/// </summary>
	public class GraphicalObjectContainerGenerator // aka. Factory
	{
		/// <summary>
		/// An attached property for item container, set to the corrspoinding source item.
		/// </summary>
		private static readonly DependencyProperty DataItemProperty =
			DependencyProperty.RegisterAttached("DataItem", typeof(object), typeof(GraphicalObjectContainerGenerator),
				new FrameworkPropertyMetadata(null));

		private readonly Dictionary<ICanvasItem, DependencyObject> containerDict =
			new Dictionary<ICanvasItem, DependencyObject>();

		private readonly ObjectPool<DesignerCanvasItemContainer> entityContainerPool =
			new ObjectPool<DesignerCanvasItemContainer>(() => new DesignerCanvasItemContainer());

		public event EventHandler<ContainerPreparingEventArgs> ContainerPreparing;

		public GraphicalObjectContainerGenerator()
		{
			MaxPooledContainers = 100;
		}

		/// <summary>
		/// Specifies the maximum number of containers that can exist in the pool.
		/// </summary>
		public int MaxPooledContainers
		{
			get { return entityContainerPool.Capacity; }
			set { entityContainerPool.Capacity = value; }
		}

		/// <summary>
		/// Gets a new or pooled container for a specific Entity.
		/// </summary>
		public DependencyObject CreateContainer(ICanvasItem item)
		{
			if (item == null) throw new ArgumentNullException(nameof(item));
			var container = entityContainerPool.Take();
			PrepareContainer(container, item);
			var doContainer = container;
			containerDict.Add(item, doContainer);
			return doContainer;
		}

		private void PrepareContainer(DependencyObject container, ICanvasItem item)
		{
			if (container == null) throw new ArgumentNullException(nameof(container));
			container.SetValue(DataItemProperty, item);
			// Ensure a connection line won't cover any entity.
			container.SetValue(FrameworkElement.DataContextProperty, item);
			container.SetValue(Panel.ZIndexProperty, 10);
			OnContainerPreparing(new ContainerPreparingEventArgs(container, item));
		}

		/// <summary>
		/// Declares a container no longer be used and should be pooled or discarded.
		/// </summary>
		public void Recycle(DependencyObject container)
		{
			Recycle(container, true);
		}

		/// <summary>
		/// Declares a container no longer be used and should be pooled or discarded.
		/// </summary>
		private void Recycle(DependencyObject container, bool removeContainerDictEntry)
		{
			if (container == null) throw new ArgumentNullException(nameof(container));
			var item = ItemFromContainer(container);
			if (item == null) throw new InvalidOperationException("试图回收非列表项目。");
			if (removeContainerDictEntry) containerDict.Remove(item);
			if (container is DesignerCanvasItemContainer)
			{
				entityContainerPool.PutBack((DesignerCanvasItemContainer)container);
			}
			else
			{
				throw new ArgumentException(null, nameof(item));
			}
		}

		public void RecycleAll()
		{
			foreach (var container in containerDict.Values)
				Recycle(container, false);
			containerDict.Clear();
		}

		/// <summary>
		/// Gets the container, if generated, for a specific item.
		/// </summary>
		/// <returns></returns>
		public DependencyObject ContainerFromItem(ICanvasItem item)
		{
			if (item == null) throw new ArgumentNullException(nameof(item));
			DependencyObject container;
			if (containerDict.TryGetValue(item, out container))
				return container;
			return null;
		}

		/// <summary>
		/// Gets the corresponding item, if exists, for a specific container.
		/// </summary>
		/// <returns></returns>
		public ICanvasItem ItemFromContainer(DependencyObject container)
		{
			if (container == null) throw new ArgumentNullException(nameof(container));
			var localValue = container.ReadLocalValue(DataItemProperty);
			if (localValue == DependencyProperty.UnsetValue) localValue = null;
			return (ICanvasItem)localValue;
		}

		protected virtual void OnContainerPreparing(ContainerPreparingEventArgs e)
		{
			ContainerPreparing?.Invoke(this, e);
		}
	}

	public class ContainerPreparingEventArgs : EventArgs
	{
		public DependencyObject Container { get; }
		public ICanvasItem DataContext { get; }

		public ContainerPreparingEventArgs(DependencyObject container, ICanvasItem dataContext)
		{
			Container = container;
			DataContext = dataContext;
		}
	}
}
