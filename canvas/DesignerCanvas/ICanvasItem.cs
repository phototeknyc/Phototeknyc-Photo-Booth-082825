using System;
using System.Windows;

namespace DesignerCanvas
{
    public interface ICanvasItem
    {
        double Left { get; set; }

        double Top { get; set; }

        /// <summary>
        /// Gets the bounding rectangle of the object.
        /// </summary>
        Rect Bounds { get; }

		/// <summary>
		/// Fires when <see cref="Bounds"/> has been changed.
		/// </summary>
		event EventHandler BoundsChanged;

        /// <summary>
        /// Determines whether the object is in the specified region.
        /// </summary>
        HitTestResult HitTest(Rect testRectangle);

        void NotifyUserDraggingStarted();

        /// <summary>
        /// Notifies the item when user dragging the item.
        /// </summary>
        void NotifyUserDragging(double deltaX, double deltaY);

        void NotifyUserDraggingCompleted();
        ICanvasItem Clone();
    }

    public interface IBoxCanvasItem : ICanvasItem
    {

        double Width { get; set; }

        double Height { get; set; }

        // todo : (give option to lock position, give setter)
        bool Resizeable { get; set; }

        /// <summary>
        /// Angle of rotation, in degrees.
        /// </summary>
        double Angle { get; set; }

        // aspect ratio locked
        bool LockedAspectRatio { get; set; }

        bool LockedPosition { get; set; }


		/// <summary>
		/// aspect ratio = width / height
		/// height = width / ratio
		/// width = height * ratio
		/// </summary>
		double AspectRatio { get; set; }
		double RatioX { get; set; }
		double RatioY { get; set; }
	}
}