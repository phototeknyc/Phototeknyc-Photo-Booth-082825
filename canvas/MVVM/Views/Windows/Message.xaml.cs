using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Photobooth.MVVM.Views.Windows
{
	/// <summary>
	/// Interaction logic for Message.xaml
	/// </summary>
	public partial class Message : Window
	{
		private DispatcherTimer _timer;

		public Message(string message, int autoCloseTime = 5000)
		{
			InitializeComponent();
			MessageTextBlock.Text = message;

			_timer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(autoCloseTime)
			};
			_timer.Tick += Timer_Tick;
			_timer.Start();
		}

		private void Timer_Tick(object sender, EventArgs e)
		{
			_timer.Stop();
			this.Close();
		}

		public static void Show(string message, int autoCloseTime = 5000)
		{
			Thread thread = new Thread(() =>
			{
				Message messageBox = new Message(message, autoCloseTime);
				messageBox.ShowDialog();
			});
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
		}
	}
}
