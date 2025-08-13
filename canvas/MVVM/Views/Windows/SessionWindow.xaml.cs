using Photobooth.MVVM.Models;
using Photobooth.MVVM.ViewModels.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace Photobooth.MVVM.Views.Windows
{
	/// <summary>
	/// Interaction logic for SessionWindow.xaml
	/// </summary>
	public partial class PopUpWindow : Window
	{
		public enum PopUpType { Session, Template }

		public PopUpType Type { get; set; }
		public object Args { get; set; }

		public PopUpWindow(PopUpType type, object args = null)
		{
			InitializeComponent();
			this.DataContext = SessionsVM.Instance;
			Args = args;
			Type = type;
			if (type == PopUpType.Session)
			{
				Title = "Create New Session";
				lbName.Text = "Session Name:";
			}
			else
			{
				Title = "Create New Template";
				lbName.Text = "Template Name:";
			}
		}

		private void CreateButton_Click(object sender, RoutedEventArgs e)
		{
			if (Type == PopUpType.Session)
				DialogResult = SessionsVM.Instance.CreateNewSession(NameTextBox.Text);
			else
				DialogResult = SessionsVM.Instance.CreateNewTemplate(NameTextBox.Text, Args);
			Close();
		}

		private void CancelButton_Click(object sender, RoutedEventArgs e)
		{
			DialogResult = false; // Close the dialog and indicate cancellation
			Close();
		}
	}
}
