using Photobooth.MVVM.ViewModels.Settings;
using System.Windows;

namespace Photobooth.MVVM.Views.Windows
{
	public partial class StartupWindow : Window
	{
		public StartupWindow()
		{
			InitializeComponent();
			DataContext = SessionsVM.Instance;
			Application.Current.Exit += (s, e) => { };
			Loaded += (s, e) => cbxSessions.SelectedIndex = cbxSessions.Items.Count - 1;
		}

		private void Button_Click(object sender, RoutedEventArgs e)
		{
			if (SessionsVM.Instance.CurrentSession != null)
				ShowMainWindow();
			else
				MessageBox.Show("Session not selected. Either create a new session or select from the dropdown.");
		}

		private void Button_NewSessionClick(object sender, RoutedEventArgs e)
		{
			if (new PopUpWindow(PopUpWindow.PopUpType.Session).ShowDialog() == true)
				ShowMainWindow();
		}

		private void ShowMainWindow()
		{
			new MainWindow().Show();
			Close();
		}
	}
}