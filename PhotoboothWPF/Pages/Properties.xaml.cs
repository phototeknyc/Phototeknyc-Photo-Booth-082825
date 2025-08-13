using DesignerCanvas;
using System;
using System.Collections.Generic;
using System.IO;
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

namespace PhotoboothWPF.Pages
{
	/// <summary>
	/// Interaction logic for Properties.xaml
	/// </summary>
	public partial class Properties : Page
	{
		private IBoxCanvasItem canvasItem;

		public IBoxCanvasItem CanvasItem
		{
			get { return canvasItem; }
			set
			{
				canvasItem = value;
				// show location and size of new item
				tbSizeW.Text = canvasItem.Width.ToString();
				tbSizeH.Text = canvasItem.Height.ToString();

				tbLocationX.Text = canvasItem.Top.ToString();
				tbLocationY.Text = canvasItem.Left.ToString();
			}
		}

		public Properties()
		{
			InitializeComponent();
			tbLocationX.Text = MainPage.Instance.dcvs.RatioX.ToString();
			tbLocationY.Text = MainPage.Instance.dcvs.RatioY.ToString();
		}

		private void StackPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			ToggleLock(image, tbLocationX, tbLocationY);
			// check if lock is closed then stop then selected item from moving
			if (image.Source.ToString().Contains("/images/lock.png"))
			{
				MainPage.Instance.dcvs.ChangeAspectRatio(true);
			}
			else
			{
				MainPage.Instance.dcvs.ChangeAspectRatio(false);
			}

		}

		private void ToggleLock(Image img, TextBox txtBox1, TextBox txtBox2)
		{
			string image1 = "/images/Padlock.png";
			string image2 = "/images/lock.png";

			if (img.Source.ToString().Contains(image1))
			{
				img.Source = new BitmapImage(new Uri(image2, UriKind.RelativeOrAbsolute));
				txtBox1.IsEnabled = false;
				txtBox2.IsEnabled = false;
			}
			else
			{
				img.Source = new BitmapImage(new Uri(image1, UriKind.RelativeOrAbsolute));
				txtBox1.IsEnabled = true;
				txtBox2.IsEnabled = true;
			}
		}

		private void StackPanel_MouseLeftButtonDown_1(object sender, MouseButtonEventArgs e)
		{
			ToggleLock(image8, tbSizeW, tbSizeH);
			// check if lock is closed then stop then selected item from moving
			if (image8.Source.ToString().Contains("/images/lock.png"))
			{
				MainPage.Instance.dcvs.LockSize(true);
			}
			else
			{
				MainPage.Instance.dcvs.LockSize(false);
			}
		}

		private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			MainPage.Instance.dcvs.BringToFront();
		}

		private void Image_MouseLeftButtonDown_1(object sender, MouseButtonEventArgs e)
		{
			MainPage.Instance.dcvs.SendToBack();
		}

		private void Image_MouseLeftButtonDown_2(object sender, MouseButtonEventArgs e)
		{
			MainPage.Instance.dcvs.AlignLeft();
		}

		private void Image_MouseLeftButtonDown_3(object sender, MouseButtonEventArgs e)
		{
			MainPage.Instance.dcvs.AlignRight();
		}

		private void Image_MouseLeftButtonDown_4(object sender, MouseButtonEventArgs e)
		{
			MainPage.Instance.dcvs.AlignTop();
		}

		private void Image_MouseLeftButtonDown_5(object sender, MouseButtonEventArgs e)
		{
			MainPage.Instance.dcvs.AlignTop();
		}

		private void Image_MouseLeftButtonDown_6(object sender, MouseButtonEventArgs e)
		{
			MainPage.Instance.dcvs.DuplicateSelected();
		}

		private void Image_MouseLeftButtonDown_7(object sender, MouseButtonEventArgs e)
		{
			MainPage.Instance.dcvs.AlignBottom();
		}

		private void tbSizeW_TextChanged(object sender, TextChangedEventArgs e)
		{
			try
			{
				//parse text to int and set width of selected items
				int width = 0;
				if (tbSizeW.Text != "")
				{
					width = int.Parse(tbSizeW.Text);

				}
				MainPage.Instance.dcvs.SetWidthOfSelectedItems(width);
			}
			catch (Exception)
			{ }
		}

		private void tbSizeH_TextChanged(object sender, TextChangedEventArgs e)
		{
			try
			{
				int height = 0;
				if (tbSizeH.Text != "")
				{
					height = int.Parse(tbSizeH.Text);
				}
				MainPage.Instance.dcvs.SetHeightOfSelectedItems(height);
			}
			catch (Exception)
			{ }
		}

		private void tbLocationX_TextChanged(object sender, TextChangedEventArgs e)
		{
			try
			{
				double top = 0;
				double left = 0;
				if (tbLocationY != null && tbLocationY != null && tbLocationX.Text != "" && tbLocationY.Text != "")
				{
					top = double.Parse(tbLocationX.Text);
					left = double.Parse(tbLocationY.Text);
				}
				MainPage.Instance.dcvs.SetAspectRatioOfSelectedItems(top / left);
			}
			catch (Exception)
			{ }
		}

		private void tbLocationY_TextChanged(object sender, TextChangedEventArgs e)
		{
			try
			{
				double top = 0;
				double left = 0;
				if (tbLocationY != null && tbLocationY != null && tbLocationX.Text != "" && tbLocationY.Text != "")
				{
					top = double.Parse(tbLocationX.Text);
					left = double.Parse(tbLocationY.Text);
				}
				MainPage.Instance.dcvs.SetAspectRatioOfSelectedItems(top / left);
			}
			catch (Exception)
			{ }
		}
	}
}
