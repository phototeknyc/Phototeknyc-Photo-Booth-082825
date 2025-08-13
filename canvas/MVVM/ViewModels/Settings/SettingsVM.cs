using Photobooth.MVVM.Models;
using Photobooth.MVVM.Views.UserControls.Settings.Sections;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace Photobooth.MVVM.ViewModels
{
	public class SettingsVM : BaseViewModel
	{
		private const string DarkThemePath = "/MVVM/Resources/Themes/Dark.xaml";
		private const string LightThemePath = "/MVVM/Resources/Themes/Light.xaml";
		private bool _isLightTheme;
		private ObservableCollection<SettingsSection> _settingsSections;

		public ICommand ChangeTheme { get; set; }

		public ObservableCollection<SettingsSection> SettingsSections
		{
			get => _settingsSections;
			set => SetProperty<ObservableCollection<SettingsSection>>(ref _settingsSections, value);
		}

		public SettingsVM()
		{
			//SettingsSections = new ObservableCollection<SettingsSection>
			//									{
			//										new SettingsSection
			//										{
			//											Header = "Session Management",
			//											Content = new Sessions() { DataContext = this }
			//										},
			//										new SettingsSection
			//										{
			//											Header = "Theme",
			//											Content = new Theme() { DataContext = this }
			//										}
			//									};

			ChangeTheme = new RelayCommand(param => OnChangeTheme());
		}

		private void OnChangeTheme()
		{
			_isLightTheme = !_isLightTheme;
			ResourceDictionary resourceDictionary = new ResourceDictionary
			{
				Source = new Uri(_isLightTheme ? LightThemePath : DarkThemePath, UriKind.Relative)
			};
			App.Current.Resources.MergedDictionaries.Clear();
			App.Current.Resources.MergedDictionaries.Add(resourceDictionary);
		}
	}
}
