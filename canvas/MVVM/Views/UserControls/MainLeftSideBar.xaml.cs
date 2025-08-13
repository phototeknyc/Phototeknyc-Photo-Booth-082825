using Photobooth.Resources.Controls;
using System;
using System.Windows;
using System.Windows.Controls;

namespace Photobooth.MVVM.Views.UserControls
{
	/// <summary>
	/// Interaction logic for MainLeftSideBar.xaml
	/// </summary>
	public partial class MainLeftSideBar : UserControl
	{
		public event Action HomeButtonClicked;
		public event Action DesingerButtonClicked;
		public event Action CameraButtonClicked;
		public event Action SettingsButtonClicked;

		private object _selectedItem = null;

		public MainLeftSideBar()
		{
			InitializeComponent();
			DesingerButton_Click(null, null);
		}

		private void HomeButton_Click(object sender, RoutedEventArgs e)
		{
			SetSelectedItem(lbiHome);
			HomeButtonClicked?.Invoke();
		}

		private void DesingerButton_Click(object sender, RoutedEventArgs e)
		{
			SetSelectedItem(lbiDesinger);
			DesingerButtonClicked?.Invoke();
		}

		private void CameraButton_Click(object sender, RoutedEventArgs e)
		{
			SetSelectedItem(lbiCamera);
			CameraButtonClicked?.Invoke();
		}

		private void SettingsButton_Click(object sender, RoutedEventArgs e)
		{
			SetSelectedItem(lbiSettings);
			SettingsButtonClicked?.Invoke();
		}

		private void SetSelectedItem(ListBoxItem item)
		{
			sidebar.SelectedItem = item;
			_selectedItem = item;
		}

		private void sidebar_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (sidebar.SelectedItem != _selectedItem && sidebar.SelectedItem is ListBoxItem selectedItem)
			{
				if (selectedItem.Content is NavButton navButton)
				{
					navButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
				}
			}
		}
	}
}
