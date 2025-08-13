using CameraControl.Core.Interfaces;
using Photobooth.MVVM.Views.UserControls.Camera;
using Photobooth.MVVM.Views.UserControls.Designer;
using Photobooth.MVVM.Views.UserControls.Home;
using Photobooth.MVVM.Views.UserControls.Settings;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;

namespace Photobooth.MVVM.Views.Windows
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window, IMainWindowPlugin
	{
		private const String HOME = "HOME";
		private const String DESINGER = "DESINGER";
		private const String CAMERA = "CAMERA";
		private const String SETTINGS = "SETTINGS";

		private HomeMain _homeMain;
		private DesignerMain _designerMain;
		private CameraMain _cameraMain;
		private SettingsMain _settingsMain;

		public String _displayName = "Main Window";
		public string DisplayName { get => _displayName; set => _displayName = value; }

		public MainWindow()
		{
			InitializeComponent();
			SidebarControl.HomeButtonClicked += () => ShowRelativePage(HOME);
			SidebarControl.DesingerButtonClicked += () => ShowRelativePage(DESINGER);
			SidebarControl.CameraButtonClicked += () => ShowRelativePage(CAMERA);
			SidebarControl.SettingsButtonClicked += () => ShowRelativePage(SETTINGS);

			// Show home control by default
			// TODO: Show the home control here when the home control is ready.
			_designerMain = new DesignerMain();
			MainContentControl.Content = _designerMain;
		}

		private void ShowRelativePage(string pageName)
		{
			switch (pageName)
			{
				case HOME:
					MainContentControl.Content = _homeMain ?? new HomeMain();
					break;
				case DESINGER:
					MainContentControl.Content = _designerMain ?? new DesignerMain();
					break;
				case CAMERA:
					MainContentControl.Content =  _cameraMain?.Refresh() ?? new CameraMain();
					break;
				case SETTINGS:
					MainContentControl.Content = _settingsMain ?? new SettingsMain();
					break;
				default:
					MainContentControl.Content = _homeMain ?? new HomeMain();
					break;
			}
		}
	}
}
