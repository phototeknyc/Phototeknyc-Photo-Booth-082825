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

namespace Photobooth.Pages
{
	/// <summary>
	/// Interaction logic for Properties.xaml
	/// </summary>
	public partial class ItemPropertiesPage : Page
	{
		private IBoxCanvasItem canvasItem;

		public int ItemSizeX
		{
			get { return int.Parse(tbSizeX.Text); }
			set { tbSizeX.Text = value.ToString(); }
		}

		public int ItemSizeY
		{
			get { return int.Parse(tbSizeY.Text); }
			set { tbSizeY.Text = value.ToString(); }
		}

		public int ItemRatioX
		{
			get { return int.Parse(tbRatioX.Text); }
			set { tbRatioX.Text = value.ToString(); }
		}

		public int ItemRatioY
		{
			get { return int.Parse(tbRatioY.Text); }
			set { tbRatioY.Text = value.ToString(); }
		}

		private static ItemPropertiesPage _instance;
		public static ItemPropertiesPage Instance
		{
			get
			{
				if (_instance == null)
				{
					_instance = new ItemPropertiesPage();
				}
				return _instance;
			}
		}

		public IBoxCanvasItem CanvasItem
		{
			get { return canvasItem; }
			set
			{
				canvasItem = value;
				// show location and size of new item
				tbSizeX.Text = canvasItem.Width.ToString();
				tbSizeY.Text = canvasItem.Height.ToString();

				tbRatioX.Text = canvasItem.Top.ToString();
				tbRatioY.Text = canvasItem.Left.ToString();
			}
		}

		public ItemPropertiesPage()
		{
			InitializeComponent();
			tbRatioX.Text = MainPage.Instance.dcvs.RatioX.ToString();
			tbRatioY.Text = MainPage.Instance.dcvs.RatioY.ToString();
			_instance = this;
		}

		private void StackPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			ToggleLock(image, tbRatioX, tbRatioY);
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
			ToggleLock(image8, tbSizeX, tbSizeY);
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
			MainPage.Instance.dcvs.BringToTheFront();
		}

		private void Image_MouseLeftButtonDown_1(object sender, MouseButtonEventArgs e)
		{
			MainPage.Instance.dcvs.SendToTheBack();
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
			MainPage.Instance.dcvs.AlignHorizontallyCenter();
		}


        private void Image_MouseLeftButtonDown_8(object sender, MouseButtonEventArgs e)
        {
			MainPage.Instance.dcvs.AlignVerticallyCenter();
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
				if (tbSizeX.Text != "")
				{
					width = int.Parse(tbSizeX.Text);
					MainPage.Instance.dcvs.SetWidthOfSelectedItems(width);
				}
            }
			catch (Exception)
			{ }
		}

		private void tbSizeH_TextChanged(object sender, TextChangedEventArgs e)
		{
			try
			{
				int height = 0;
				if (tbSizeY.Text != "")
				{
					height = int.Parse(tbSizeY.Text);
					MainPage.Instance.dcvs.SetHeightOfSelectedItems(height);
				}
            }
			catch (Exception)
			{ }
		}

		private void tbLocationX_TextChanged(object sender, TextChangedEventArgs e)
		{
			UpdateAspectRatio();
		}

		private void tbLocationY_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateAspectRatio();
        }

        private void UpdateAspectRatio()
        {
            try
            {
                double top = 0;
                double left = 0;
                if (tbRatioY != null && tbRatioY != null && tbRatioX.Text != "" && tbRatioY.Text != "")
                {
                    top = double.Parse(tbRatioX.Text);
                    left = double.Parse(tbRatioY.Text);
                    double[] aspectRatio = MainPage.Instance.dcvs.SetAspectRatioOfSelectedItems(top / left);
                    if (aspectRatio[0] != -1) tbSizeX.Text = aspectRatio[0].ToString();
                    if (aspectRatio[1] != -1) tbSizeY.Text = aspectRatio[1].ToString();
                }
            }
            catch (Exception)
            { }
        }
    }
}
