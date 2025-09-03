using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Photobooth.Services;

namespace Photobooth.Controls
{
    public partial class TimerSettingsOverlay : UserControl
    {
        private bool _isInitialized = false;
        private bool _autoSaveEnabled = true;
        private readonly SettingsManagementService _settingsService;

        public TimerSettingsOverlay()
        {
            InitializeComponent();
            
            // Initialize settings service for sync
            _settingsService = SettingsManagementService.Instance;
            _settingsService.SettingChanged += OnSettingChangedFromService;
            _settingsService.SettingsReset += OnSettingsResetFromService;
            
            this.Loaded += OnControlLoaded;
        }

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                LoadCurrentSettings();
                _isInitialized = true;
            }
        }

        private void LoadCurrentSettings()
        {
            try
            {
                // Load countdown timer value
                if (Properties.Settings.Default.CountdownSeconds > 0)
                {
                    CountdownSlider.Value = Properties.Settings.Default.CountdownSeconds;
                    UpdateCountdownText(Properties.Settings.Default.CountdownSeconds);
                }
                else
                {
                    CountdownSlider.Value = 3;
                    UpdateCountdownText(3);
                }

                // Load show countdown checkbox
                ShowCountdownCheckBox.IsChecked = Properties.Settings.Default.ShowCountdown;

                // Load delay between photos
                DelayBetweenPhotosSlider.Value = (double)Properties.Settings.Default.DelayBetweenPhotos;
                UpdateDelayBetweenPhotosText((double)Properties.Settings.Default.DelayBetweenPhotos);

                // Load auto-clear timeout
                AutoClearTimeoutSlider.Value = Properties.Settings.Default.AutoClearTimeout;
                UpdateAutoClearTimeoutText(Properties.Settings.Default.AutoClearTimeout);

                // Load start delay (property might not exist)
                StartDelaySlider.Value = 0;
                UpdateStartDelayText(0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TimerSettingsOverlay] Error loading settings: {ex.Message}");
                SetDefaults();
            }
        }

        private void CountdownSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            
            var value = (int)CountdownSlider.Value;
            UpdateCountdownText(value);
            
            if (_autoSaveEnabled)
            {
                Properties.Settings.Default.CountdownSeconds = value;
                Properties.Settings.Default.Save();
                _settingsService.LoadSettings();  // Sync with service
            }
        }

        private void UpdateCountdownText(double value)
        {
            if (CountdownValueText != null)
            {
                if (value == 0)
                    CountdownValueText.Text = "No countdown";
                else if (value == 1)
                    CountdownValueText.Text = "1 second";
                else
                    CountdownValueText.Text = $"{(int)value} seconds";
            }
        }

        private void ShowCountdownCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            
            if (_autoSaveEnabled)
            {
                Properties.Settings.Default.ShowCountdown = ShowCountdownCheckBox.IsChecked ?? true;
                Properties.Settings.Default.Save();
                _settingsService.LoadSettings();  // Sync with service
            }
        }

        private void DelayBetweenPhotosSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            
            UpdateDelayBetweenPhotosText(DelayBetweenPhotosSlider.Value);
            
            if (_autoSaveEnabled)
            {
                Properties.Settings.Default.DelayBetweenPhotos = (int)DelayBetweenPhotosSlider.Value;
                Properties.Settings.Default.Save();
                _settingsService.LoadSettings();  // Sync with service
            }
        }

        private void UpdateDelayBetweenPhotosText(double value)
        {
            if (DelayBetweenPhotosValueText != null)
            {
                if (value == 0)
                    DelayBetweenPhotosValueText.Text = "No delay";
                else
                    DelayBetweenPhotosValueText.Text = $"{value:F1} seconds";
            }
        }

        private void AutoClearTimeoutSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            
            var value = (int)AutoClearTimeoutSlider.Value;
            UpdateAutoClearTimeoutText(value);
            
            if (_autoSaveEnabled)
            {
                Properties.Settings.Default.AutoClearTimeout = value;
                Properties.Settings.Default.Save();
                _settingsService.LoadSettings();  // Sync with service
            }
        }

        private void UpdateAutoClearTimeoutText(double value)
        {
            if (AutoClearTimeoutValueText != null)
            {
                if (value >= 60)
                {
                    var minutes = value / 60;
                    AutoClearTimeoutValueText.Text = $"{minutes:F1} minutes";
                }
                else
                {
                    AutoClearTimeoutValueText.Text = $"{(int)value} seconds";
                }
            }
        }

        private void StartDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            
            UpdateStartDelayText(StartDelaySlider.Value);
            
            if (_autoSaveEnabled)
            {
                // StartDelay property might not exist - skip saving
                // Properties.Settings.Default.Save();
            }
        }

        private void UpdateStartDelayText(double value)
        {
            if (StartDelayValueText != null)
            {
                if (value == 0)
                    StartDelayValueText.Text = "No delay";
                else if (value == 1)
                    StartDelayValueText.Text = "1 second";
                else
                    StartDelayValueText.Text = $"{value:F1} seconds";
            }
        }

        // Preset Buttons
        private void FastPresetButton_Click(object sender, RoutedEventArgs e)
        {
            _autoSaveEnabled = false;
            CountdownSlider.Value = 2;
            DelayBetweenPhotosSlider.Value = 1;
            AutoClearTimeoutSlider.Value = 20;
            StartDelaySlider.Value = 0;
            _autoSaveEnabled = true;
            SaveAllSettings();
        }

        private void StandardPresetButton_Click(object sender, RoutedEventArgs e)
        {
            _autoSaveEnabled = false;
            CountdownSlider.Value = 3;
            DelayBetweenPhotosSlider.Value = 2;
            AutoClearTimeoutSlider.Value = 30;
            StartDelaySlider.Value = 0;
            _autoSaveEnabled = true;
            SaveAllSettings();
        }

        private void RelaxedPresetButton_Click(object sender, RoutedEventArgs e)
        {
            _autoSaveEnabled = false;
            CountdownSlider.Value = 5;
            DelayBetweenPhotosSlider.Value = 3;
            AutoClearTimeoutSlider.Value = 60;
            StartDelaySlider.Value = 1;
            _autoSaveEnabled = true;
            SaveAllSettings();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            SetDefaults();
            SaveAllSettings();
        }

        private void SetDefaults()
        {
            _autoSaveEnabled = false;
            CountdownSlider.Value = 3;
            ShowCountdownCheckBox.IsChecked = true;
            DelayBetweenPhotosSlider.Value = 2;
            AutoClearTimeoutSlider.Value = 30;
            StartDelaySlider.Value = 0;
            _autoSaveEnabled = true;
        }

        private void SaveAllSettings()
        {
            Properties.Settings.Default.CountdownSeconds = (int)CountdownSlider.Value;
            Properties.Settings.Default.ShowCountdown = ShowCountdownCheckBox.IsChecked ?? true;
            Properties.Settings.Default.DelayBetweenPhotos = (int)DelayBetweenPhotosSlider.Value;
            Properties.Settings.Default.AutoClearTimeout = (int)AutoClearTimeoutSlider.Value;
            
            try
            {
                // StartDelay might not exist in settings
                // Properties.Settings.Default.StartDelay = StartDelaySlider.Value;
            }
            catch { }
            
            Properties.Settings.Default.Save();
            
            // Refresh settings service to sync with other overlays
            _settingsService.LoadSettings();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideOverlay();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            SaveAllSettings();
            HideOverlay();
        }

        private void HideOverlay()
        {
            // Animate out
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.2));
            fadeOut.Completed += (s, e) => this.Visibility = Visibility.Collapsed;
            this.BeginAnimation(OpacityProperty, fadeOut);
        }

        public void ShowOverlay()
        {
            this.Visibility = Visibility.Visible;
            LoadCurrentSettings();
            
            // Animate in
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3));
            this.BeginAnimation(OpacityProperty, fadeIn);
        }
        
        #region Settings Service Event Handlers
        
        /// <summary>
        /// Handle setting changes from the service to keep UI in sync
        /// </summary>
        private void OnSettingChangedFromService(object sender, SettingChangedEventArgs e)
        {
            try
            {
                // Refresh the UI to reflect changes made in other overlays
                Dispatcher.Invoke(() => 
                {
                    _autoSaveEnabled = false;  // Prevent re-saving while updating
                    LoadCurrentSettings();
                    _autoSaveEnabled = true;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TimerSettingsOverlay] Error syncing setting change from service: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handle settings reset from service
        /// </summary>
        private void OnSettingsResetFromService(object sender, EventArgs e)
        {
            try
            {
                // Refresh the UI to reflect reset settings
                Dispatcher.Invoke(() => 
                {
                    _autoSaveEnabled = false;  // Prevent re-saving while updating
                    LoadCurrentSettings();
                    _autoSaveEnabled = true;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TimerSettingsOverlay] Error syncing settings reset from service: {ex.Message}");
            }
        }
        
        #endregion
    }
}