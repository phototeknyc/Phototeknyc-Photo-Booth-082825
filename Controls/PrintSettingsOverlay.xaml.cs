using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Photobooth.Controls
{
    public partial class PrintSettingsOverlay : UserControl
    {
        private bool _isInitialized = false;
        private bool _autoSaveEnabled = true;

        public PrintSettingsOverlay()
        {
            InitializeComponent();
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
                // Load enable printing
                EnablePrintingCheckBox.IsChecked = Properties.Settings.Default.EnablePrinting;

                // Load default copies
                DefaultCopiesSlider.Value = Properties.Settings.Default.DefaultPrintCopies;
                UpdateDefaultCopiesText(Properties.Settings.Default.DefaultPrintCopies);

                // Load max copies (using PrintCopies as max copies setting)
                MaxCopiesSlider.Value = Properties.Settings.Default.PrintCopies > 0 ? Properties.Settings.Default.PrintCopies : 5;
                UpdateMaxCopiesText(MaxCopiesSlider.Value);

                // Load max session prints
                MaxSessionPrintsSlider.Value = Properties.Settings.Default.MaxSessionPrints;
                UpdateMaxSessionPrintsText(Properties.Settings.Default.MaxSessionPrints);

                // Load max event prints
                MaxEventPrintsSlider.Value = Properties.Settings.Default.MaxEventPrints;
                UpdateMaxEventPrintsText(Properties.Settings.Default.MaxEventPrints);

                // Load print options
                ShowPrintButtonCheckBox.IsChecked = Properties.Settings.Default.ShowPrintButton;
                ShowPrintCopiesModalCheckBox.IsChecked = Properties.Settings.Default.ShowPrintCopiesModal;
                PrintOnlyOriginalsCheckBox.IsChecked = Properties.Settings.Default.PrintOnlyOriginals;

                // Update UI state based on enable printing
                UpdateControlsEnabledState();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PrintSettingsOverlay] Error loading settings: {ex.Message}");
                SetDefaults();
            }
        }

        private void EnablePrintingCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            
            UpdateControlsEnabledState();
            
            if (_autoSaveEnabled)
            {
                Properties.Settings.Default.EnablePrinting = EnablePrintingCheckBox.IsChecked ?? true;
                Properties.Settings.Default.Save();
            }
        }

        private void UpdateControlsEnabledState()
        {
            bool isEnabled = EnablePrintingCheckBox.IsChecked ?? false;
            
            DefaultCopiesSlider.IsEnabled = isEnabled;
            MaxCopiesSlider.IsEnabled = isEnabled;
            MaxSessionPrintsSlider.IsEnabled = isEnabled;
            MaxEventPrintsSlider.IsEnabled = isEnabled;
            ShowPrintButtonCheckBox.IsEnabled = isEnabled;
            ShowPrintCopiesModalCheckBox.IsEnabled = isEnabled;
            PrintOnlyOriginalsCheckBox.IsEnabled = isEnabled;
        }

        private void DefaultCopiesSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            
            var value = (int)DefaultCopiesSlider.Value;
            UpdateDefaultCopiesText(value);
            
            if (_autoSaveEnabled)
            {
                Properties.Settings.Default.DefaultPrintCopies = value;
                Properties.Settings.Default.Save();
            }
        }

        private void UpdateDefaultCopiesText(double value)
        {
            if (DefaultCopiesValueText != null)
            {
                DefaultCopiesValueText.Text = value == 1 ? "1 copy" : $"{(int)value} copies";
            }
        }

        private void MaxCopiesSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            
            var value = (int)MaxCopiesSlider.Value;
            UpdateMaxCopiesText(value);
            
            if (_autoSaveEnabled)
            {
                Properties.Settings.Default.PrintCopies = value;
                Properties.Settings.Default.Save();
            }
        }

        private void UpdateMaxCopiesText(double value)
        {
            if (MaxCopiesValueText != null)
            {
                MaxCopiesValueText.Text = value == 1 ? "1 copy" : $"{(int)value} copies";
            }
        }

        private void MaxSessionPrintsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            
            var value = (int)MaxSessionPrintsSlider.Value;
            UpdateMaxSessionPrintsText(value);
            
            if (_autoSaveEnabled)
            {
                Properties.Settings.Default.MaxSessionPrints = value;
                Properties.Settings.Default.Save();
            }
        }

        private void UpdateMaxSessionPrintsText(double value)
        {
            if (MaxSessionPrintsValueText != null)
            {
                if (value == 0)
                    MaxSessionPrintsValueText.Text = "Unlimited";
                else if (value == 1)
                    MaxSessionPrintsValueText.Text = "1 print";
                else
                    MaxSessionPrintsValueText.Text = $"{(int)value} prints";
            }
        }

        private void MaxEventPrintsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            
            var value = (int)MaxEventPrintsSlider.Value;
            UpdateMaxEventPrintsText(value);
            
            if (_autoSaveEnabled)
            {
                Properties.Settings.Default.MaxEventPrints = value;
                Properties.Settings.Default.Save();
            }
        }

        private void UpdateMaxEventPrintsText(double value)
        {
            if (MaxEventPrintsValueText != null)
            {
                if (value == 0)
                    MaxEventPrintsValueText.Text = "Unlimited";
                else if (value == 1)
                    MaxEventPrintsValueText.Text = "1 print";
                else
                    MaxEventPrintsValueText.Text = $"{(int)value} prints";
            }
        }

        private void ShowPrintButtonCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            
            if (_autoSaveEnabled)
            {
                Properties.Settings.Default.ShowPrintButton = ShowPrintButtonCheckBox.IsChecked ?? true;
                Properties.Settings.Default.Save();
            }
        }

        private void ShowPrintCopiesModalCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            
            if (_autoSaveEnabled)
            {
                Properties.Settings.Default.ShowPrintCopiesModal = ShowPrintCopiesModalCheckBox.IsChecked ?? true;
                Properties.Settings.Default.Save();
            }
        }

        private void PrintOnlyOriginalsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            
            if (_autoSaveEnabled)
            {
                Properties.Settings.Default.PrintOnlyOriginals = PrintOnlyOriginalsCheckBox.IsChecked ?? false;
                Properties.Settings.Default.Save();
            }
        }

        // Preset Buttons
        private void SinglePrintPresetButton_Click(object sender, RoutedEventArgs e)
        {
            _autoSaveEnabled = false;
            EnablePrintingCheckBox.IsChecked = true;
            DefaultCopiesSlider.Value = 1;
            MaxCopiesSlider.Value = 1;
            MaxSessionPrintsSlider.Value = 1;
            MaxEventPrintsSlider.Value = 0;  // Unlimited event prints
            ShowPrintButtonCheckBox.IsChecked = true;
            ShowPrintCopiesModalCheckBox.IsChecked = false; // No dialog needed for single print
            PrintOnlyOriginalsCheckBox.IsChecked = true;
            _autoSaveEnabled = true;
            SaveAllSettings();
        }

        private void StandardPresetButton_Click(object sender, RoutedEventArgs e)
        {
            _autoSaveEnabled = false;
            EnablePrintingCheckBox.IsChecked = true;
            DefaultCopiesSlider.Value = 2;
            MaxCopiesSlider.Value = 5;
            MaxSessionPrintsSlider.Value = 10;
            MaxEventPrintsSlider.Value = 0;  // Unlimited event prints
            ShowPrintButtonCheckBox.IsChecked = true;
            ShowPrintCopiesModalCheckBox.IsChecked = true;
            PrintOnlyOriginalsCheckBox.IsChecked = false;
            _autoSaveEnabled = true;
            SaveAllSettings();
        }

        private void UnlimitedPresetButton_Click(object sender, RoutedEventArgs e)
        {
            _autoSaveEnabled = false;
            EnablePrintingCheckBox.IsChecked = true;
            DefaultCopiesSlider.Value = 2;
            MaxCopiesSlider.Value = 20;
            MaxSessionPrintsSlider.Value = 0;  // Unlimited per session
            MaxEventPrintsSlider.Value = 0;    // Unlimited per event
            ShowPrintButtonCheckBox.IsChecked = true;
            ShowPrintCopiesModalCheckBox.IsChecked = true;
            PrintOnlyOriginalsCheckBox.IsChecked = false;
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
            EnablePrintingCheckBox.IsChecked = true;
            DefaultCopiesSlider.Value = 1;
            MaxCopiesSlider.Value = 5;
            MaxSessionPrintsSlider.Value = 0;  // Unlimited by default
            MaxEventPrintsSlider.Value = 0;    // Unlimited by default
            ShowPrintButtonCheckBox.IsChecked = true;
            ShowPrintCopiesModalCheckBox.IsChecked = true;
            PrintOnlyOriginalsCheckBox.IsChecked = false;
            _autoSaveEnabled = true;
            UpdateControlsEnabledState();
        }

        private void SaveAllSettings()
        {
            Properties.Settings.Default.EnablePrinting = EnablePrintingCheckBox.IsChecked ?? true;
            Properties.Settings.Default.DefaultPrintCopies = (int)DefaultCopiesSlider.Value;
            Properties.Settings.Default.PrintCopies = (int)MaxCopiesSlider.Value;
            Properties.Settings.Default.MaxSessionPrints = (int)MaxSessionPrintsSlider.Value;
            Properties.Settings.Default.MaxEventPrints = (int)MaxEventPrintsSlider.Value;
            Properties.Settings.Default.ShowPrintButton = ShowPrintButtonCheckBox.IsChecked ?? true;
            Properties.Settings.Default.ShowPrintCopiesModal = ShowPrintCopiesModalCheckBox.IsChecked ?? true;
            Properties.Settings.Default.PrintOnlyOriginals = PrintOnlyOriginalsCheckBox.IsChecked ?? false;
            
            Properties.Settings.Default.Save();
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
    }
}