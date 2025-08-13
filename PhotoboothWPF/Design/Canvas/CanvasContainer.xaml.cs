using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PhotoboothWPF.Design.Canvas
{
	/// <summary>
	/// Interaction logic for CanvasContainer.xaml
	/// </summary>
	public partial class CanvasContainer : UserControl
	{
		public DesignerCanvas.Controls.DesignerCanvas Canvas
		{
			get { return dcvs; }
			set { dcvs = value; }	
		}

		private double _ratio = 1.0;
		public double CanvasRatio
		{
			get { return _ratio; }
			set 
			{
				_ratio = value;
				canvasBorder.Width = _ratio * dcvs.Height;
			}
		}

		public CanvasContainer()
		{
			InitializeComponent();
		}

		private void dcvs_MouseDown(object sender, MouseButtonEventArgs e)
		{

		}

		private void dcvs_MouseMove(object sender, MouseEventArgs e)
		{
		}

		private void Grid_Initialized(object sender, EventArgs e)
		{
		}

		private void Border_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			// Calculate the new width to maintain the specified ratio
			double newWidth = e.NewSize.Height * _ratio;

			// Set the width of the element (e.g., Canvas)
			// Make sure to cast sender to the appropriate type if needed
			if (sender is Border element)
			{
				element.Height = e.NewSize.Height;
				element.Width = newWidth;
			}
		}
	}
}
