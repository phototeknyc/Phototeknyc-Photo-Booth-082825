using System.Windows;

namespace Photobooth
{
	public partial class WelcomeWindow : Window
	{
		public WelcomeWindow()
		{
			InitializeComponent();
		}

		private void Button_Click(object sender, RoutedEventArgs e)
		{
			PhotoBoothWindow photoBoothWindow = new PhotoBoothWindow();
			photoBoothWindow.Show();
			this.Close();
		}
	}
}
