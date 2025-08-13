using System.Collections.ObjectModel;
using System.Windows;

namespace DesignerCanvas
{
    public interface IPolyLineCanvasItem : ICanvasItem
    {
        ObservableCollection<Point> Points { get; }

        /// <summary>
        /// Make ajustments to point coordinations and Left, Top,
        /// making all X, Y of points > 0.
        /// </summary>
        void NormalizePositions();
    }
}