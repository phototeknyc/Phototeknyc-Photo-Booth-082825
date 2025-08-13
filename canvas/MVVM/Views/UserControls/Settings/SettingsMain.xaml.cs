using Photobooth.MVVM.ViewModels;
using Photobooth.MVVM.Views.UserControls.Settings.Sections;
using System.Collections.Generic;
using System.Windows.Controls;

namespace Photobooth.MVVM.Views.UserControls.Settings
{
	public partial class SettingsMain : UserControl
	{
		public SettingsMain()
		{
			InitializeComponent();
			DataContext = new SettingsVM();
		}
	}
}
