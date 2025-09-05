using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Photobooth.Services;
using System.Runtime.InteropServices;
using System.Drawing.Printing;

namespace Photobooth.Pages
{
    public partial class PhotoboothSettingsControl : UserControl
    {
        private readonly SettingsManagementService _settingsService;
        
        // Windows API for accessing printer driver settings
        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int DocumentProperties(IntPtr hWnd, IntPtr hPrinter, string pDeviceName, IntPtr pDevModeOutput, IntPtr pDevModeInput, int fMode);
        
        // DNP-specific DEVMODE constants
        private const int DM_IN_BUFFER = 8;
        private const int DM_OUT_BUFFER = 2;
        private const int DM_IN_PROMPT = 4;
        
        // DEVMODE field constants
        private const int DM_ORIENTATION = 0x00000001;
        private const int DM_PAPERSIZE = 0x00000002;
        private const int DM_PAPERLENGTH = 0x00000004;
        private const int DM_PAPERWIDTH = 0x00000008;
        private const int DM_COLOR = 0x00000800;

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr hMem);

        private const uint GMEM_MOVEABLE = 0x0002;
        
        // DEVMODE structure for printer settings
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public short dmOrientation;
            public short dmPaperSize;
            public short dmPaperLength;
            public short dmPaperWidth;
            public short dmScale;
            public short dmCopies;
            public short dmDefaultSource;
            public short dmPrintQuality;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        public PhotoboothSettingsControl()
        {
            InitializeComponent();
            
            // Initialize settings service and subscribe to changes
            _settingsService = SettingsManagementService.Instance;
            _settingsService.SettingChanged += OnSettingChangedFromService;
            _settingsService.SettingsReset += OnSettingsResetFromService;
            
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                // Load countdown settings
                countdownSlider.Value = Properties.Settings.Default.CountdownSeconds;
                showCountdownCheckBox.IsChecked = Properties.Settings.Default.ShowCountdown;
                
                // Load capture timing settings
                photoDisplayDurationSlider.Value = Properties.Settings.Default.PhotoDisplayDuration;
                photographerModeCheckBox.IsChecked = Properties.Settings.Default.PhotographerMode;
                showSessionPromptsCheckBox.IsChecked = Properties.Settings.Default.ShowSessionPrompts;
                if (delayBetweenPhotosSlider != null)
                    delayBetweenPhotosSlider.Value = Properties.Settings.Default.DelayBetweenPhotos;
                
                // Load auto-clear settings
                autoClearSessionCheckBox.IsChecked = Properties.Settings.Default.AutoClearSession;
                autoClearTimeoutSlider.Value = Properties.Settings.Default.AutoClearTimeout;
                
                // Load photo storage settings
                string photoLocation = Properties.Settings.Default.PhotoLocation;
                if (string.IsNullOrEmpty(photoLocation))
                {
                    photoLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Photobooth");
                    Properties.Settings.Default.PhotoLocation = photoLocation;
                }
                photoLocationTextBox.Text = photoLocation;
                
                // Load Video & Boomerang module settings
                var modulesConfig = PhotoboothModulesConfig.Instance;
                enableVideoModuleCheckBox.IsChecked = modulesConfig.VideoEnabled;
                showVideoButtonCheckBox.IsChecked = modulesConfig.ShowVideoButton;
                videoDurationSlider.Value = modulesConfig.VideoDuration;
                
                // Update the duration text display
                int videoDuration = modulesConfig.VideoDuration;
                if (videoDuration >= 60)
                {
                    int minutes = videoDuration / 60;
                    int remainingSeconds = videoDuration % 60;
                    if (remainingSeconds > 0)
                    {
                        videoDurationValueText.Text = $"{minutes}m {remainingSeconds}s";
                    }
                    else
                    {
                        videoDurationValueText.Text = $"{minutes} minute{(minutes > 1 ? "s" : "")}";
                    }
                }
                else
                {
                    videoDurationValueText.Text = $"{videoDuration} seconds";
                }
                
                enableBoomerangModuleCheckBox.IsChecked = modulesConfig.BoomerangEnabled;
                showBoomerangButtonCheckBox.IsChecked = modulesConfig.ShowBoomerangButton;
                boomerangFramesSlider.Value = modulesConfig.BoomerangFrames;
                boomerangSpeedSlider.Value = modulesConfig.BoomerangSpeed;
                
                enableFlipbookModuleCheckBox.IsChecked = modulesConfig.FlipbookEnabled;
                showFlipbookButtonCheckBox.IsChecked = modulesConfig.ShowFlipbookButton;
                flipbookDurationSlider.Value = modulesConfig.FlipbookDuration;
                
                // Load live view settings
                mirrorLiveViewCheckBox.IsChecked = Properties.Settings.Default.MirrorLiveView;
                enableIdleLiveViewCheckBox.IsChecked = Properties.Settings.Default.EnableIdleLiveView;
                frameRateSlider.Value = Properties.Settings.Default.LiveViewFrameRate;
                
                // Load touch interface settings
                fullscreenCheckBox.IsChecked = Properties.Settings.Default.FullscreenMode;
                hideCursorCheckBox.IsChecked = Properties.Settings.Default.HideCursor;
                buttonSizeSlider.Value = Properties.Settings.Default.ButtonSizeScale;
                
                // Load security settings
                enableLockCheckBox.IsChecked = Properties.Settings.Default.EnableLockFeature;
                autoLockTimeoutSlider.Value = Properties.Settings.Default.AutoLockTimeout;
                UpdateAutoLockTimeoutText();
                pinSettingsPanel.Visibility = Properties.Settings.Default.EnableLockFeature ? Visibility.Visible : Visibility.Collapsed;
                
                // Load retake settings
                enableRetakeCheckBox.IsChecked = Properties.Settings.Default.EnableRetake;
                retakeTimeoutSlider.Value = Properties.Settings.Default.RetakeTimeout;
                allowMultipleRetakesCheckBox.IsChecked = Properties.Settings.Default.AllowMultipleRetakes;
                
                // Load beauty mode settings
                beautyModeEnabledCheckBox.IsChecked = Properties.Settings.Default.BeautyModeEnabled;
                beautyModeIntensitySlider.Value = Properties.Settings.Default.BeautyModeIntensity;
                
                // Load filter settings
                enableFiltersCheckBox.IsChecked = Properties.Settings.Default.EnableFilters;
                defaultFilterComboBox.SelectedIndex = Properties.Settings.Default.DefaultFilter;
                filterIntensitySlider.Value = Properties.Settings.Default.FilterIntensity;
                allowFilterChangeCheckBox.IsChecked = Properties.Settings.Default.AllowFilterChange;
                showFilterPreviewCheckBox.IsChecked = Properties.Settings.Default.ShowFilterPreview;
                autoApplyFilterCheckBox.IsChecked = Properties.Settings.Default.AutoApplyFilter;
                
                // Load enabled filters (default to all enabled if setting doesn't exist)
                LoadEnabledFilters();
                
                // Load GIF animation settings
                enableGifGenerationCheckBox.IsChecked = Properties.Settings.Default.EnableGifGeneration;
                gifFrameDelaySlider.Value = Properties.Settings.Default.GifFrameDelay / 1000.0; // Convert from ms to seconds
                enableGifOverlayCheckBox.IsChecked = Properties.Settings.Default.EnableGifOverlay;
                gifOverlayPathTextBox.Text = Properties.Settings.Default.GifOverlayPath;
                gifQualitySlider.Value = Properties.Settings.Default.GifQuality;
                gifMaxWidthTextBox.Text = Properties.Settings.Default.GifMaxWidth.ToString();
                gifMaxHeightTextBox.Text = Properties.Settings.Default.GifMaxHeight.ToString();
                
                // Load print settings including dual printer configuration
                LoadPrintSettings();
                
                // Load auto-routing setting
                if (autoRoutePrinterCheckBox != null)
                    autoRoutePrinterCheckBox.IsChecked = Properties.Settings.Default.AutoRoutePrinter;
                
                // Update display texts
                UpdateSliderTexts();
                
                // Load cloud settings
                LoadCloudSettings();
                
                // Load debug logging settings
                LoadDebugLoggingSettings();
            }
            catch (Exception)
            {
                // If settings can't be loaded, use defaults
                ResetToDefaults();
            }
        }

        private void UpdateSliderTexts()
        {
            countdownValueText.Text = $"{(int)countdownSlider.Value} seconds";
            photoDisplayDurationValueText.Text = $"{(int)photoDisplayDurationSlider.Value} seconds";
            if (delayBetweenPhotosSlider != null && delayBetweenPhotosValueText != null)
                delayBetweenPhotosValueText.Text = $"{(int)delayBetweenPhotosSlider.Value} seconds";
            frameRateValueText.Text = $"{(int)frameRateSlider.Value} FPS";
            buttonSizeValueText.Text = $"{(int)(buttonSizeSlider.Value * 100)}%";
            if (retakeTimeoutValueText != null)
                retakeTimeoutValueText.Text = $"{(int)retakeTimeoutSlider.Value} seconds";
            if (filterIntensityValueText != null)
                filterIntensityValueText.Text = $"{(int)filterIntensitySlider.Value}%";
        }

        private void CountdownSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (countdownValueText != null)
                countdownValueText.Text = $"{(int)e.NewValue} seconds";
        }

        private void PhotoDisplayDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (photoDisplayDurationValueText != null)
                photoDisplayDurationValueText.Text = $"{(int)e.NewValue} seconds";
        }
        
        private void DelayBetweenPhotosSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (delayBetweenPhotosValueText != null)
                delayBetweenPhotosValueText.Text = $"{(int)e.NewValue} seconds";
        }
        
        private void AutoClearTimeoutSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (autoClearTimeoutValueText != null)
                autoClearTimeoutValueText.Text = $"{(int)e.NewValue} seconds";
        }

        private void FrameRateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (frameRateValueText != null)
                frameRateValueText.Text = $"{(int)e.NewValue} FPS";
        }

        private void ButtonSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (buttonSizeValueText != null)
                buttonSizeValueText.Text = $"{(int)(e.NewValue * 100)}%";
        }

        private void RetakeTimeoutSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (retakeTimeoutValueText != null)
                retakeTimeoutValueText.Text = $"{(int)e.NewValue} seconds";
        }

        private void FilterIntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (filterIntensityValueText != null)
                filterIntensityValueText.Text = $"{(int)e.NewValue}%";
        }
        
        private void BeautyModeIntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (beautyModeIntensityText != null)
                beautyModeIntensityText.Text = $"{(int)e.NewValue}%";
        }

        private void BrowsePhotoLocation_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select folder for saving photobooth photos";
            dialog.SelectedPath = photoLocationTextBox.Text;
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                photoLocationTextBox.Text = dialog.SelectedPath;
            }
        }

        private void SaveAllSettings()
        {
            try
            {
                // This method saves all settings programmatically (called from Advanced Driver Settings)
                SaveSettingsInternal();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SaveAllSettings: {ex.Message}");
            }
        }
        
        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveSettingsInternal();
                SaveCloudSettings(); // Save cloud settings too
                
                // Also force save the video/boomerang module settings
                var modulesConfig = PhotoboothModulesConfig.Instance;
                modulesConfig.SaveAllSettings();
                
                // Force save all application settings
                Properties.Settings.Default.Save();
                
                MessageBox.Show("Settings saved successfully!", "Photobooth Settings", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void SaveSettingsInternal()
        {
                // Save countdown settings
                Properties.Settings.Default.CountdownSeconds = (int)countdownSlider.Value;
                Properties.Settings.Default.ShowCountdown = showCountdownCheckBox.IsChecked ?? true;
                
                // Save capture timing settings
                Properties.Settings.Default.PhotoDisplayDuration = (int)photoDisplayDurationSlider.Value;
                Properties.Settings.Default.PhotographerMode = photographerModeCheckBox.IsChecked ?? false;
                Properties.Settings.Default.ShowSessionPrompts = showSessionPromptsCheckBox.IsChecked ?? false;
                if (delayBetweenPhotosSlider != null)
                    Properties.Settings.Default.DelayBetweenPhotos = (int)delayBetweenPhotosSlider.Value;
                
                // Save auto-clear settings
                Properties.Settings.Default.AutoClearSession = autoClearSessionCheckBox.IsChecked ?? false;
                Properties.Settings.Default.AutoClearTimeout = (int)autoClearTimeoutSlider.Value;
                
                // Save photo storage settings
                Properties.Settings.Default.PhotoLocation = photoLocationTextBox.Text;
                
                // Save live view settings
                Properties.Settings.Default.MirrorLiveView = mirrorLiveViewCheckBox.IsChecked ?? false;
                Properties.Settings.Default.EnableIdleLiveView = enableIdleLiveViewCheckBox.IsChecked ?? true;
                Properties.Settings.Default.LiveViewFrameRate = (int)frameRateSlider.Value;
                
                // Save touch interface settings
                Properties.Settings.Default.FullscreenMode = fullscreenCheckBox.IsChecked ?? false;
                Properties.Settings.Default.HideCursor = hideCursorCheckBox.IsChecked ?? false;
                Properties.Settings.Default.ButtonSizeScale = buttonSizeSlider.Value;
                
                // Save security settings
                Properties.Settings.Default.EnableLockFeature = enableLockCheckBox.IsChecked ?? false;
                Properties.Settings.Default.AutoLockTimeout = (int)autoLockTimeoutSlider.Value;
                
                // Save retake settings  
                Properties.Settings.Default.EnableRetake = enableRetakeCheckBox.IsChecked ?? false;
                Properties.Settings.Default.RetakeTimeout = (int)retakeTimeoutSlider.Value;
                Properties.Settings.Default.AllowMultipleRetakes = allowMultipleRetakesCheckBox.IsChecked ?? true;
                
                // Save beauty mode settings
                Properties.Settings.Default.BeautyModeEnabled = beautyModeEnabledCheckBox.IsChecked ?? false;
                Properties.Settings.Default.BeautyModeIntensity = (int)beautyModeIntensitySlider.Value;
                
                // Save filter settings
                Properties.Settings.Default.EnableFilters = enableFiltersCheckBox.IsChecked ?? true;
                Properties.Settings.Default.DefaultFilter = defaultFilterComboBox.SelectedIndex;
                Properties.Settings.Default.FilterIntensity = (int)filterIntensitySlider.Value;
                Properties.Settings.Default.AllowFilterChange = allowFilterChangeCheckBox.IsChecked ?? true;
                Properties.Settings.Default.ShowFilterPreview = showFilterPreviewCheckBox.IsChecked ?? true;
                Properties.Settings.Default.AutoApplyFilter = autoApplyFilterCheckBox.IsChecked ?? false;
                Properties.Settings.Default.EnabledFilters = GetEnabledFilters();
                
                // Save GIF animation settings
                Properties.Settings.Default.EnableGifGeneration = enableGifGenerationCheckBox.IsChecked ?? false;
                Properties.Settings.Default.GifFrameDelay = (int)(gifFrameDelaySlider.Value * 1000); // Convert to ms
                Properties.Settings.Default.EnableGifOverlay = enableGifOverlayCheckBox.IsChecked ?? false;
                Properties.Settings.Default.GifOverlayPath = gifOverlayPathTextBox.Text;
                Properties.Settings.Default.GifQuality = (int)gifQualitySlider.Value;
                if (int.TryParse(gifMaxWidthTextBox.Text, out int maxWidth))
                    Properties.Settings.Default.GifMaxWidth = maxWidth;
                if (int.TryParse(gifMaxHeightTextBox.Text, out int maxHeight))
                    Properties.Settings.Default.GifMaxHeight = maxHeight;
                
                // Save print settings with null checks
                if (enablePrintingCheckBox != null)
                    Properties.Settings.Default.EnablePrinting = enablePrintingCheckBox.IsChecked ?? true;
                    
                if (showPrintButtonCheckBox != null)
                    Properties.Settings.Default.ShowPrintButton = showPrintButtonCheckBox.IsChecked ?? true;
                    
                if (printOnlyOriginalsCheckBox != null)
                    Properties.Settings.Default.PrintOnlyOriginals = printOnlyOriginalsCheckBox.IsChecked ?? true;
                    
                if (allowReprintsCheckBox != null)
                    Properties.Settings.Default.AllowReprints = allowReprintsCheckBox.IsChecked ?? true;
                    
                if (showPrintDialogCheckBox != null)
                    Properties.Settings.Default.ShowPrintDialog = showPrintDialogCheckBox.IsChecked ?? false;
                    
                if (printLandscapeCheckBox != null)
                    Properties.Settings.Default.PrintLandscape = printLandscapeCheckBox.IsChecked ?? false;
                    
                // Save auto-routing setting
                if (autoRoutePrinterCheckBox != null)
                    Properties.Settings.Default.AutoRoutePrinter = autoRoutePrinterCheckBox.IsChecked ?? true;
                    
                // Save 4x6 printer selection
                if (printer4x6ComboBox != null)
                {
                    string selectedText = printer4x6ComboBox.SelectedItem?.ToString() ?? "";
                    string printerName = ExtractPrinterName(selectedText);
                    Properties.Settings.Default.Printer4x6Name = printerName;
                }
                
                // Save 2x6 printer selection
                if (printer2x6ComboBox != null)
                {
                    string selectedText = printer2x6ComboBox.SelectedItem?.ToString() ?? "";
                    string printerName = ExtractPrinterName(selectedText);
                    Properties.Settings.Default.Printer2x6Name = printerName;
                }
                
                // Save legacy printer if visible
                if (printerComboBox != null)
                {
                    string selectedText = printerComboBox.SelectedItem?.ToString() ?? "";
                    string printerName = ExtractPrinterName(selectedText);
                    Properties.Settings.Default.PrinterName = printerName;
                }
                    
                if (paperSizeComboBox != null)
                    Properties.Settings.Default.PrintPaperSize = ((ComboBoxItem)paperSizeComboBox.SelectedItem)?.Content?.ToString() ?? "4x6";

                // Parse numeric print settings with validation
                if (maxSessionPrintsTextBox != null && int.TryParse(maxSessionPrintsTextBox.Text, out int maxSession))
                    Properties.Settings.Default.MaxSessionPrints = Math.Max(0, maxSession);
                
                if (maxEventPrintsTextBox != null && int.TryParse(maxEventPrintsTextBox.Text, out int maxEvent))
                    Properties.Settings.Default.MaxEventPrints = Math.Max(0, maxEvent);
                    
                if (defaultCopiesTextBox != null && int.TryParse(defaultCopiesTextBox.Text, out int defaultCopies))
                    Properties.Settings.Default.DefaultPrintCopies = Math.Max(1, defaultCopies);

                // Save print copies modal settings using PrintSettingsService
                if (showPrintCopiesModalCheckBox != null)
                    PrintSettingsService.Instance.ShowPrintCopiesModal = showPrintCopiesModalCheckBox.IsChecked ?? false;

                if (maxCopiesComboBox != null && maxCopiesComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    if (int.TryParse(selectedItem.Content.ToString(), out int maxCopies))
                    {
                        PrintSettingsService.Instance.MaxCopiesInModal = maxCopies;
                    }
                }
                
                // Save advanced driver settings
                /*if (colorModeComboBox?.SelectedItem is ComboBoxItem colorModeItem)
                    Properties.Settings.Default.PrintColorMode = colorModeItem.Content.ToString();
                    
                if (resolutionComboBox?.SelectedItem is ComboBoxItem resolutionItem)
                    Properties.Settings.Default.PrinterResolution = resolutionItem.Content.ToString();
                    
                if (mediaTypeComboBox?.SelectedItem is ComboBoxItem mediaTypeItem)
                    Properties.Settings.Default.PrintMediaType = mediaTypeItem.Content.ToString();
                    
                if (duplexModeComboBox?.SelectedItem is ComboBoxItem duplexModeItem)
                    Properties.Settings.Default.PrintDuplexMode = duplexModeItem.Content.ToString();
                    
                if (borderlessCheckBox != null)
                    Properties.Settings.Default.PrintBorderless = borderlessCheckBox.IsChecked ?? false;
                    
                if (fitToPageCheckBox != null)
                    Properties.Settings.Default.PrintFitToPage = fitToPageCheckBox.IsChecked ?? true;
                
                // Save DNP settings (only DEVMODE-compatible settings)
                if (dnp2InchCutCheckBox != null)
                    Properties.Settings.Default.Dnp2InchCut = dnp2InchCutCheckBox.IsChecked ?? false;*/
                
                // Save to disk
                Properties.Settings.Default.Save();
                
                // Refresh settings service to sync with overlays
                _settingsService.LoadSettings();
        }

        private void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to reset all photobooth settings to defaults?", 
                "Reset Settings", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                // Use the service to reset settings - this will also notify overlays
                _settingsService.ResetToDefaults();
                
                // Also reset local UI elements that aren't managed by the service
                ResetToDefaults();
            }
        }

        private void ResetToDefaults()
        {
            countdownSlider.Value = 5;
            showCountdownCheckBox.IsChecked = true;
            photoDisplayDurationSlider.Value = 4;
            photographerModeCheckBox.IsChecked = false;
            photoLocationTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Photobooth");
            mirrorLiveViewCheckBox.IsChecked = false;
            frameRateSlider.Value = 30;
            fullscreenCheckBox.IsChecked = false;
            hideCursorCheckBox.IsChecked = false;
            buttonSizeSlider.Value = 1.0;
            enableRetakeCheckBox.IsChecked = false;
            retakeTimeoutSlider.Value = 15;
            allowMultipleRetakesCheckBox.IsChecked = true;
            beautyModeEnabledCheckBox.IsChecked = false;
            beautyModeIntensitySlider.Value = 50;
            enableFiltersCheckBox.IsChecked = true;
            defaultFilterComboBox.SelectedIndex = 0; // None
            filterIntensitySlider.Value = 100;
            allowFilterChangeCheckBox.IsChecked = true;
            showFilterPreviewCheckBox.IsChecked = true;
            autoApplyFilterCheckBox.IsChecked = false;
            
            UpdateSliderTexts();
        }

        private void LoadEnabledFilters()
        {
            // Load enabled filters from settings or default to all enabled
            string enabledFilters = Properties.Settings.Default.EnabledFilters;
            if (string.IsNullOrEmpty(enabledFilters))
            {
                // Default: all filters enabled
                filterBlackWhiteCheckBox.IsChecked = true;
                filterSepiaCheckBox.IsChecked = true;
                filterVintageCheckBox.IsChecked = true;
                filterGlamourCheckBox.IsChecked = true;
                filterCoolCheckBox.IsChecked = true;
                filterWarmCheckBox.IsChecked = true;
                filterHighContrastCheckBox.IsChecked = true;
                filterVividCheckBox.IsChecked = true;
            }
            else
            {
                // Parse saved filter settings
                var filters = enabledFilters.Split(',');
                filterBlackWhiteCheckBox.IsChecked = filters.Contains("BlackWhite");
                filterSepiaCheckBox.IsChecked = filters.Contains("Sepia");
                filterVintageCheckBox.IsChecked = filters.Contains("Vintage");
                filterGlamourCheckBox.IsChecked = filters.Contains("Glamour");
                filterCoolCheckBox.IsChecked = filters.Contains("Cool");
                filterWarmCheckBox.IsChecked = filters.Contains("Warm");
                filterHighContrastCheckBox.IsChecked = filters.Contains("HighContrast");
                filterVividCheckBox.IsChecked = filters.Contains("Vivid");
            }
        }
        
        private string GetEnabledFilters()
        {
            var enabledFilters = new List<string>();
            
            if (filterBlackWhiteCheckBox.IsChecked == true)
                enabledFilters.Add("BlackWhite");
            if (filterSepiaCheckBox.IsChecked == true)
                enabledFilters.Add("Sepia");
            if (filterVintageCheckBox.IsChecked == true)
                enabledFilters.Add("Vintage");
            if (filterGlamourCheckBox.IsChecked == true)
                enabledFilters.Add("Glamour");
            if (filterCoolCheckBox.IsChecked == true)
                enabledFilters.Add("Cool");
            if (filterWarmCheckBox.IsChecked == true)
                enabledFilters.Add("Warm");
            if (filterHighContrastCheckBox.IsChecked == true)
                enabledFilters.Add("HighContrast");
            if (filterVividCheckBox.IsChecked == true)
                enabledFilters.Add("Vivid");
                
            return string.Join(",", enabledFilters);
        }
        
        private void SelectAllFilters_Click(object sender, RoutedEventArgs e)
        {
            filterBlackWhiteCheckBox.IsChecked = true;
            filterSepiaCheckBox.IsChecked = true;
            filterVintageCheckBox.IsChecked = true;
            filterGlamourCheckBox.IsChecked = true;
            filterCoolCheckBox.IsChecked = true;
            filterWarmCheckBox.IsChecked = true;
            filterHighContrastCheckBox.IsChecked = true;
            filterVividCheckBox.IsChecked = true;
        }
        
        private void DeselectAllFilters_Click(object sender, RoutedEventArgs e)
        {
            filterBlackWhiteCheckBox.IsChecked = false;
            filterSepiaCheckBox.IsChecked = false;
            filterVintageCheckBox.IsChecked = false;
            filterGlamourCheckBox.IsChecked = false;
            filterCoolCheckBox.IsChecked = false;
            filterWarmCheckBox.IsChecked = false;
            filterHighContrastCheckBox.IsChecked = false;
            filterVividCheckBox.IsChecked = false;
        }
        
        private void OpenPhotobooth_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // First save current settings
                SaveSettings_Click(sender, e);
                
                // Check if we're in the new Surface window or ModernSettingsWindow
                var parentWindow = Window.GetWindow(this);
                
                if (parentWindow is SurfacePhotoBoothWindow surfaceWindow)
                {
                    // Navigate to Event Selection within the same window
                    surfaceWindow.NavigateToPage(new EventSelectionPage(), "Select Event");
                }
                else if (parentWindow is ModernSettingsWindow)
                {
                    // Open the main Surface window
                    var mainWindow = new SurfacePhotoBoothWindow();
                    mainWindow.Show();
                    
                    // Navigate to Event Selection after window loads
                    mainWindow.Loaded += (s, args) =>
                    {
                        mainWindow.NavigateToPage(new EventSelectionPage(), "Select Event");
                    };
                    
                    // Minimize settings window
                    parentWindow.WindowState = WindowState.Minimized;
                }
                else
                {
                    // Fallback - open PhotoBooth in new Surface window
                    var photoboothWindow = new SurfacePhotoBoothWindow();
                    photoboothWindow.Show();
                    
                    photoboothWindow.Loaded += (s, args) =>
                    {
                        photoboothWindow.NavigateToPage(new PhotoboothTouchModern(), "Photo Booth");
                    };
                    
                    // Close or minimize parent window if it exists
                    if (parentWindow != null)
                    {
                        parentWindow.WindowState = WindowState.Minimized;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open photobooth: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DefaultPooling_Changed(object sender, RoutedEventArgs e)
        {
            if (defaultPoolPanel != null && defaultPoolingCheckBox != null)
            {
                defaultPoolPanel.Visibility = defaultPoolingCheckBox.IsChecked == true ? 
                    System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                    
                // Populate pool printer dropdowns if visible
                if (defaultPoolingCheckBox.IsChecked == true)
                {
                    PopulatePoolDropdowns();
                }
            }
        }
        
        private void PopulatePoolDropdowns()
        {
            var printers = PrintService.GetAvailablePrinters();
            
            // Populate default pool dropdowns
            if (defaultPool2ComboBox != null)
            {
                defaultPool2ComboBox.Items.Clear();
                defaultPool2ComboBox.Items.Add(""); // Empty option
                foreach (var printer in printers)
                    defaultPool2ComboBox.Items.Add(printer);
            }
            
            if (defaultPool3ComboBox != null)
            {
                defaultPool3ComboBox.Items.Clear();
                defaultPool3ComboBox.Items.Add(""); // Empty option
                foreach (var printer in printers)
                    defaultPool3ComboBox.Items.Add(printer);
            }
            
            if (defaultPool4ComboBox != null)
            {
                defaultPool4ComboBox.Items.Clear();
                defaultPool4ComboBox.Items.Add(""); // Empty option
                foreach (var printer in printers)
                    defaultPool4ComboBox.Items.Add(printer);
            }
        }
        
        private void LoadPrintSettings()
        {
            try
            {
                // Load print settings with null checks
                if (enablePrintingCheckBox != null)
                    enablePrintingCheckBox.IsChecked = Properties.Settings.Default.EnablePrinting;
                    
                if (showPrintButtonCheckBox != null)
                    showPrintButtonCheckBox.IsChecked = Properties.Settings.Default.ShowPrintButton;
                    
                if (printOnlyOriginalsCheckBox != null)
                    printOnlyOriginalsCheckBox.IsChecked = Properties.Settings.Default.PrintOnlyOriginals;
                    
                if (allowReprintsCheckBox != null)
                    allowReprintsCheckBox.IsChecked = Properties.Settings.Default.AllowReprints;
                    
                if (showPrintDialogCheckBox != null)
                    showPrintDialogCheckBox.IsChecked = Properties.Settings.Default.ShowPrintDialog;
                    
                if (printLandscapeCheckBox != null)
                    printLandscapeCheckBox.IsChecked = Properties.Settings.Default.PrintLandscape;
                    
                if (maxSessionPrintsTextBox != null)
                    maxSessionPrintsTextBox.Text = Properties.Settings.Default.MaxSessionPrints.ToString();
                    
                if (maxEventPrintsTextBox != null)
                    maxEventPrintsTextBox.Text = Properties.Settings.Default.MaxEventPrints.ToString();
                    
                if (defaultCopiesTextBox != null)
                    defaultCopiesTextBox.Text = Properties.Settings.Default.DefaultPrintCopies.ToString();

                // Load print copies modal settings from PrintSettingsService
                if (showPrintCopiesModalCheckBox != null)
                {
                    showPrintCopiesModalCheckBox.IsChecked = PrintSettingsService.Instance.ShowPrintCopiesModal;
                }

                if (maxCopiesComboBox != null)
                {
                    int maxCopies = PrintSettingsService.Instance.MaxCopiesInModal;
                    foreach (ComboBoxItem item in maxCopiesComboBox.Items)
                    {
                        if (item.Content.ToString() == maxCopies.ToString())
                        {
                            maxCopiesComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
                
                // Load advanced driver settings with null checks
                /*if (colorModeComboBox != null)
                {
                    var colorMode = Properties.Settings.Default.PrintColorMode;
                    foreach (ComboBoxItem item in colorModeComboBox.Items)
                    {
                        if (item.Content.ToString() == colorMode)
                        {
                            colorModeComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
                
                if (resolutionComboBox != null)
                {
                    var resolution = Properties.Settings.Default.PrinterResolution;
                    foreach (ComboBoxItem item in resolutionComboBox.Items)
                    {
                        if (item.Content.ToString() == resolution)
                        {
                            resolutionComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
                
                if (mediaTypeComboBox != null)
                {
                    var mediaType = Properties.Settings.Default.PrintMediaType;
                    foreach (ComboBoxItem item in mediaTypeComboBox.Items)
                    {
                        if (item.Content.ToString() == mediaType)
                        {
                            mediaTypeComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
                
                if (duplexModeComboBox != null)
                {
                    var duplexMode = Properties.Settings.Default.PrintDuplexMode;
                    foreach (ComboBoxItem item in duplexModeComboBox.Items)
                    {
                        if (item.Content.ToString() == duplexMode)
                        {
                            duplexModeComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
                
                if (borderlessCheckBox != null)
                    borderlessCheckBox.IsChecked = Properties.Settings.Default.PrintBorderless;
                    
                if (fitToPageCheckBox != null)
                    fitToPageCheckBox.IsChecked = Properties.Settings.Default.PrintFitToPage;*/
                
                // Load DNP settings
                LoadDnpSettings();

                // Load printers
                RefreshPrinters();

                // Set paper size
                if (paperSizeComboBox != null)
                {
                    var paperSize = Properties.Settings.Default.PrintPaperSize;
                    foreach (ComboBoxItem item in paperSizeComboBox.Items)
                    {
                        if (item.Content.ToString() == paperSize)
                        {
                            paperSizeComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading print settings: {ex.Message}");
            }
        }

        private void RefreshPrinters()
        {
            try
            {
                // Get USB printers with detailed info and all printers
                var usbPrinters = PrintService.GetUSBPrinters();
                var allPrinters = PrintService.GetAvailablePrinters();
                
                // Create a dictionary to store printer status info
                var printerStatusMap = usbPrinters.ToDictionary(p => p.Name, p => p);
                
                // Refresh 4x6 printer combo box
                if (printer4x6ComboBox != null)
                {
                    printer4x6ComboBox.Items.Clear();
                    
                    foreach (var printer in allPrinters)
                    {
                        if (printerStatusMap.ContainsKey(printer))
                        {
                            printer4x6ComboBox.Items.Add(printerStatusMap[printer].DisplayText);
                        }
                        else
                        {
                            printer4x6ComboBox.Items.Add(printer);
                        }
                    }
                    
                    // Select current 4x6 printer
                    var current4x6 = Properties.Settings.Default.Printer4x6Name;
                    if (!string.IsNullOrEmpty(current4x6))
                    {
                        foreach (var item in printer4x6ComboBox.Items)
                        {
                            var itemText = item.ToString();
                            if (itemText.Contains(current4x6) || itemText == current4x6)
                            {
                                printer4x6ComboBox.SelectedItem = item;
                                break;
                            }
                        }
                    }
                }
                
                // Refresh 2x6 printer combo box
                if (printer2x6ComboBox != null)
                {
                    printer2x6ComboBox.Items.Clear();
                    
                    foreach (var printer in allPrinters)
                    {
                        if (printerStatusMap.ContainsKey(printer))
                        {
                            printer2x6ComboBox.Items.Add(printerStatusMap[printer].DisplayText);
                        }
                        else
                        {
                            printer2x6ComboBox.Items.Add(printer);
                        }
                    }
                    
                    // Select current 2x6 printer
                    var current2x6 = Properties.Settings.Default.Printer2x6Name;
                    if (!string.IsNullOrEmpty(current2x6))
                    {
                        foreach (var item in printer2x6ComboBox.Items)
                        {
                            var itemText = item.ToString();
                            if (itemText.Contains(current2x6) || itemText == current2x6)
                            {
                                printer2x6ComboBox.SelectedItem = item;
                                break;
                            }
                        }
                    }
                }
                
                // Also refresh legacy printer combo box (if visible)
                if (printerComboBox != null)
                {
                    printerComboBox.Items.Clear();
                    
                    foreach (var printer in allPrinters)
                    {
                        if (printerStatusMap.ContainsKey(printer))
                        {
                            printerComboBox.Items.Add(printerStatusMap[printer].DisplayText);
                        }
                        else
                        {
                            printerComboBox.Items.Add(printer);
                        }
                    }

                    var currentPrinter = Properties.Settings.Default.PrinterName;
                    if (!string.IsNullOrEmpty(currentPrinter))
                    {
                        foreach (var item in printerComboBox.Items)
                        {
                            var itemText = item.ToString();
                            if (itemText.Contains(currentPrinter) || itemText == currentPrinter)
                            {
                                printerComboBox.SelectedItem = item;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing printers: {ex.Message}");
            }
        }

        private void RefreshPrinters_Click(object sender, RoutedEventArgs e)
        {
            RefreshPrinters();
        }

        private void AutoSelectUSB_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string autoSelectedPrinter = PrintService.AutoSelectUSBPrinter();
                
                if (!string.IsNullOrEmpty(autoSelectedPrinter))
                {
                    Properties.Settings.Default.PrinterName = autoSelectedPrinter;
                    Properties.Settings.Default.Save();
                    
                    // Refresh the printer list to show the new selection
                    RefreshPrinters();
                    
                    MessageBox.Show($"Auto-selected USB printer: {autoSelectedPrinter}", 
                        "USB Printer Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("No USB printers found. Please check printer connections.", 
                        "USB Printer Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error auto-selecting USB printer: {ex.Message}", 
                    "USB Printer Selection", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadDnpSettings()
        {
            try
            {
                // Load DNP 2-inch cut setting (DEVMODE-compatible)
                /*if (dnp2InchCutCheckBox != null)
                    dnp2InchCutCheckBox.IsChecked = Properties.Settings.Default.Dnp2InchCut;*/
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading DNP settings: {ex.Message}");
            }
        }

        // Removed DNP slider event handlers - not stored in DEVMODE

        private void VerifyDnp2InchCutSetting(string printerName)
        {
            IntPtr hPrinter = IntPtr.Zero;
            IntPtr pDevMode = IntPtr.Zero;
            
            try
            {
                // Open the printer
                if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                {
                    MessageBox.Show($"Cannot open printer: {printerName}", "Verify Settings", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                // Get the size of DEVMODE structure
                int bytesNeeded = DocumentProperties(IntPtr.Zero, hPrinter, printerName, IntPtr.Zero, IntPtr.Zero, 0);
                if (bytesNeeded <= 0)
                {
                    MessageBox.Show("Cannot get printer settings size.", "Verify Settings", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                // Allocate memory for DEVMODE
                pDevMode = Marshal.AllocHGlobal(bytesNeeded);
                
                // Get current settings
                int result = DocumentProperties(IntPtr.Zero, hPrinter, printerName, pDevMode, IntPtr.Zero, DM_OUT_BUFFER);
                if (result < 0)
                {
                    MessageBox.Show("Cannot get current printer settings.", "Verify Settings", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                // Convert DEVMODE to byte array for inspection
                byte[] devModeBytes = new byte[bytesNeeded];
                Marshal.Copy(pDevMode, devModeBytes, 0, bytesNeeded);
                
                // Convert to Base64 for display
                string base64Current = Convert.ToBase64String(devModeBytes);
                
                // Compare with saved settings
                string savedBase64 = Properties.Settings.Default.PrinterDriverSettings;
                
                // Create detailed report
                var report = new System.Text.StringBuilder();
                report.AppendLine("=== DNP 2-Inch Cut Setting Verification ===");
                report.AppendLine($"Printer: {printerName}");
                report.AppendLine($"DEVMODE Size: {bytesNeeded} bytes");
                /*report.AppendLine($"App Checkbox: {(dnp2InchCutCheckBox?.IsChecked ?? false ? "ENABLED" : "DISABLED")}");*/
                report.AppendLine($"Settings Saved: {(Properties.Settings.Default.Dnp2InchCut ? "ENABLED" : "DISABLED")}");
                report.AppendLine();
                
                // Check if we have saved settings
                if (!string.IsNullOrEmpty(savedBase64))
                {
                    report.AppendLine("Saved DEVMODE: YES");
                    report.AppendLine($"Saved Size: {savedBase64.Length} chars (Base64)");
                    
                    // Check if current matches saved
                    if (base64Current == savedBase64)
                    {
                        report.AppendLine("Status: CURRENT SETTINGS MATCH SAVED");
                    }
                    else
                    {
                        report.AppendLine("Status: CURRENT SETTINGS DIFFER FROM SAVED");
                        report.AppendLine("This means the driver settings have changed since last save.");
                    }
                }
                else
                {
                    report.AppendLine("Saved DEVMODE: NONE");
                    report.AppendLine("No driver settings have been saved yet.");
                }
                
                report.AppendLine();
                report.AppendLine("To verify 2-inch cut in driver:");
                report.AppendLine("1. Click 'Advanced Driver Settings'");
                report.AppendLine("2. Check if '2inch cut' shows 'Enable'");
                report.AppendLine("3. If not, enable it and click OK");
                report.AppendLine("4. Click 'Save Profile' to capture the setting");
                
                // Show first few bytes of DEVMODE in hex (where DNP settings might be)
                report.AppendLine();
                report.AppendLine("DEVMODE Header (first 200 bytes in hex):");
                for (int i = 0; i < Math.Min(200, devModeBytes.Length); i += 16)
                {
                    report.Append($"{i:X4}: ");
                    for (int j = 0; j < 16 && i + j < devModeBytes.Length; j++)
                    {
                        report.Append($"{devModeBytes[i + j]:X2} ");
                    }
                    report.AppendLine();
                }
                
                // Display the report
                var resultWindow = new Window
                {
                    Title = "DNP 2-Inch Cut Verification",
                    Width = 700,
                    Height = 600,
                    Content = new ScrollViewer
                    {
                        Content = new TextBox
                        {
                            Text = report.ToString(),
                            IsReadOnly = true,
                            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                            Margin = new Thickness(10)
                        }
                    }
                };
                resultWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error verifying settings: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (pDevMode != IntPtr.Zero)
                    Marshal.FreeHGlobal(pDevMode);
                    
                if (hPrinter != IntPtr.Zero)
                    ClosePrinter(hPrinter);
            }
        }
        
        private void AdvancedDriver4x6_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (printer4x6ComboBox?.SelectedItem != null)
                {
                    string printerName = ExtractPrinterName(printer4x6ComboBox.SelectedItem.ToString());
                    ConfigureDriverSettings(printerName, false, "4x6");
                }
                else
                {
                    MessageBox.Show("Please select a 4x6 printer first.", "4x6 Printer Settings", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening 4x6 printer settings: {ex.Message}", "4x6 Printer Settings", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void PrinterAlignment_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var alignmentDialog = new PrinterAlignmentDialog();
                alignmentDialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening printer alignment dialog: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AdvancedDriver2x6_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (printer2x6ComboBox?.SelectedItem != null)
                {
                    string printerName = ExtractPrinterName(printer2x6ComboBox.SelectedItem.ToString());
                    ConfigureDriverSettings(printerName, true, "2x6");
                }
                else
                {
                    MessageBox.Show("Please select a 2x6 printer first.", "2x6 Printer Settings", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening 2x6 printer settings: {ex.Message}", "2x6 Printer Settings", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ConfigureDriverSettings(string printerName, bool is2x6Format, string formatType)
        {
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(
                Application.Current.MainWindow).Handle;
            IntPtr hPrinter = IntPtr.Zero;
            IntPtr pDevMode = IntPtr.Zero;
            
            try
            {
                // Open printer
                if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                {
                    MessageBox.Show($"Failed to open printer: {printerName}", $"{formatType} Printer Settings", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                // Get size of DEVMODE structure
                int size = DocumentProperties(IntPtr.Zero, hPrinter, printerName, IntPtr.Zero, IntPtr.Zero, 0);
                if (size <= 0)
                {
                    MessageBox.Show("Failed to get printer settings size", $"{formatType} Printer Settings", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                // Allocate memory for DEVMODE
                pDevMode = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)size);
                IntPtr pDevModeData = GlobalLock(pDevMode);
                
                // Get current settings
                int result = DocumentProperties(IntPtr.Zero, hPrinter, printerName, pDevModeData, IntPtr.Zero, DM_OUT_BUFFER);
                if (result < 0)
                {
                    MessageBox.Show("Failed to get current printer settings", $"{formatType} Printer Settings", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                // Show the printer properties dialog
                result = DocumentProperties(hwnd, hPrinter, printerName, pDevModeData, pDevModeData, DM_IN_PROMPT | DM_IN_BUFFER | DM_OUT_BUFFER);
                
                if (result > 0)  // User clicked OK
                {
                    // Convert to Base64 and save
                    byte[] devModeBytes = new byte[size];
                    Marshal.Copy(pDevModeData, devModeBytes, 0, size);
                    string base64Settings = Convert.ToBase64String(devModeBytes);
                    
                    // Save to appropriate settings property
                    if (formatType == "4x6")
                    {
                        Properties.Settings.Default.Printer4x6DriverSettings = base64Settings;
                        Properties.Settings.Default.Printer4x6Name = printerName;
                    }
                    else if (formatType == "2x6")
                    {
                        Properties.Settings.Default.Printer2x6DriverSettings = base64Settings;
                        Properties.Settings.Default.Printer2x6Name = printerName;
                        
                        // For DNP printers, ask about 2-inch cut
                        if (printerName.ToLower().Contains("dnp"))
                        {
                            var result2 = MessageBox.Show(
                                "Did you enable '2inch cut' in the driver settings?\n\n" +
                                "Click 'Yes' if you enabled it for 2x6 strips.\n" +
                                "Click 'No' if you disabled it or left it unchanged.",
                                $"DNP 2-Inch Cut Setting for {formatType}",
                                MessageBoxButton.YesNoCancel,
                                MessageBoxImage.Question);
                            
                            if (result2 == MessageBoxResult.Yes)
                            {
                                Properties.Settings.Default.Dnp2InchCut = true;
                                /*if (dnp2InchCutCheckBox != null)
                                    dnp2InchCutCheckBox.IsChecked = true;*/
                            }
                            else if (result2 == MessageBoxResult.No)
                            {
                                Properties.Settings.Default.Dnp2InchCut = false;
                                /*if (dnp2InchCutCheckBox != null)
                                    dnp2InchCutCheckBox.IsChecked = false;*/
                            }
                        }
                    }
                    
                    // Save all settings
                    Properties.Settings.Default.Save();
                    
                    MessageBox.Show($"{formatType} printer settings have been saved!\n\n" +
                        $"✓ DEVMODE settings captured for {printerName}\n" +
                        (is2x6Format && printerName.ToLower().Contains("dnp") ? 
                            "✓ Remember to enable 2-inch cut for 2x6 strips\n" : "") +
                        $"✓ Settings will be applied when printing {formatType} images", 
                        $"{formatType} Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error configuring {formatType} printer settings: {ex.Message}", 
                    $"{formatType} Printer Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (pDevMode != IntPtr.Zero)
                {
                    GlobalUnlock(pDevMode);
                    GlobalFree(pDevMode);
                }
                if (hPrinter != IntPtr.Zero)
                {
                    ClosePrinter(hPrinter);
                }
            }
        }
        
        private void AdvancedDriverSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (printerComboBox?.SelectedItem == null)
                {
                    MessageBox.Show("Please select a printer first.", "No Printer Selected", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                string printerName = ExtractPrinterName(printerComboBox.SelectedItem.ToString());
                
                // First, apply our 2-inch cut setting if it's a DNP printer
                /*if (printerName.ToLower().Contains("dnp") && dnp2InchCutCheckBox?.IsChecked == true)
                {
                    ApplyDnp2InchCutSetting(printerName, true);
                }*/
                
                // Create a PrintDialog to access printer settings
                var printDialog = new System.Windows.Forms.PrintDialog();
                var printDocument = new System.Drawing.Printing.PrintDocument();
                printDocument.PrinterSettings.PrinterName = printerName;
                printDialog.Document = printDocument;
                printDialog.AllowPrintToFile = false;
                printDialog.AllowSelection = false;
                printDialog.AllowSomePages = false;
                
                // IMPORTANT: Show the driver dialog with IN_PROMPT to allow user to change settings
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(Window.GetWindow(this)).Handle;
                
                // Open printer for DEVMODE access
                IntPtr hPrinter = IntPtr.Zero;
                IntPtr pDevMode = IntPtr.Zero;
                
                try
                {
                    if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                    {
                        MessageBox.Show($"Cannot open printer: {printerName}", "Error", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    
                    // Get DEVMODE size
                    int size = DocumentProperties(IntPtr.Zero, hPrinter, printerName, IntPtr.Zero, IntPtr.Zero, 0);
                    if (size <= 0)
                    {
                        MessageBox.Show("Cannot get printer settings size.", "Error", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    
                    // Allocate memory
                    pDevMode = Marshal.AllocHGlobal(size);
                    
                    // Get current DEVMODE
                    int result = DocumentProperties(IntPtr.Zero, hPrinter, printerName, pDevMode, IntPtr.Zero, DM_OUT_BUFFER);
                    if (result < 0)
                    {
                        MessageBox.Show("Cannot get current settings.", "Error", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    
                    // Show the printer properties dialog - user can change 2-inch cut here
                    result = DocumentProperties(hwnd, hPrinter, printerName, pDevMode, pDevMode, DM_IN_PROMPT | DM_IN_BUFFER | DM_OUT_BUFFER);
                    
                    if (result > 0)  // User clicked OK
                    {
                        // The DEVMODE now contains the user's changes including 2-inch cut
                        // Convert to Base64 and save
                        byte[] devModeBytes = new byte[size];
                        Marshal.Copy(pDevMode, devModeBytes, 0, size);
                        string base64Settings = Convert.ToBase64String(devModeBytes);
                        
                        // Auto-save the driver settings
                        Properties.Settings.Default.PrinterDriverSettings = base64Settings;
                        
                        // Also save the current printer name to ensure consistency
                        Properties.Settings.Default.PrinterName = printerName;
                        
                        // Auto-detect if this is a DNP printer and 2-inch cut might be enabled
                        // Note: We can't directly detect if 2-inch cut is enabled from DEVMODE,
                        // but we can prompt the user
                        if (printerName.ToLower().Contains("dnp"))
                        {
                            var result2 = MessageBox.Show(
                                "Did you enable '2inch cut' in the driver settings?\n\n" +
                                "Click 'Yes' to automatically enable it in the application.\n" +
                                "Click 'No' if you disabled it or left it unchanged.",
                                "DNP 2-Inch Cut Setting",
                                MessageBoxButton.YesNoCancel,
                                MessageBoxImage.Question);
                            
                            if (result2 == MessageBoxResult.Yes)
                            {
                                // Auto-check the 2-inch cut checkbox
                                /*if (dnp2InchCutCheckBox != null)
                                {
                                    dnp2InchCutCheckBox.IsChecked = true;
                                }*/
                                Properties.Settings.Default.Dnp2InchCut = true;
                            }
                            else if (result2 == MessageBoxResult.No)
                            {
                                // Auto-uncheck the 2-inch cut checkbox
                                /*if (dnp2InchCutCheckBox != null)
                                {
                                    dnp2InchCutCheckBox.IsChecked = false;
                                }*/
                                Properties.Settings.Default.Dnp2InchCut = false;
                            }
                            // If Cancel, leave the checkbox unchanged
                        }
                        
                        // Auto-save ALL settings
                        SaveAllSettings();
                        
                        MessageBox.Show("Driver settings have been automatically saved!\n\n" +
                            "✓ DEVMODE settings captured\n" +
                            "✓ All settings saved to application\n" +
                            (printerName.ToLower().Contains("dnp") ? "✓ DNP 2-inch cut preference updated\n\n" : "\n") +
                            "You can now print with these settings.", 
                            "Settings Auto-Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                finally
                {
                    if (pDevMode != IntPtr.Zero)
                        Marshal.FreeHGlobal(pDevMode);
                    if (hPrinter != IntPtr.Zero)
                        ClosePrinter(hPrinter);
                }
                
                return;  // Skip the old PrintDialog code
                
                // Show printer properties
                if (printDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    // IMPORTANT: Capture the DEVMODE immediately after the user clicks OK
                    // This should include the 2-inch cut setting if they enabled it
                    string rawSettings = CaptureRawDriverSettings(printerName);
                    if (!string.IsNullOrEmpty(rawSettings))
                    {
                        Properties.Settings.Default.PrinterDriverSettings = rawSettings;
                        Properties.Settings.Default.Save();
                        
                        // Verify what was captured
                        var message = "Printer settings updated successfully.\n\n";
                        message += "IMPORTANT: If you enabled '2inch cut' in the DNP dialog:\n";
                        message += "1. The setting has been captured\n";
                        message += "2. Click 'Verify 2\" Cut' to confirm it's saved\n";
                        message += "3. The 2-inch cut will be applied when printing\n\n";
                        message += "Note: The checkbox in our app does NOT directly control the driver setting.\n";
                        message += "You must enable it in the DNP driver dialog.";
                        
                        MessageBox.Show(message, "Print Settings", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening printer settings: {ex.Message}", "Print Settings", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"Advanced driver settings error: {ex}");
            }
        }

        private void Verify2InchCut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (printerComboBox?.SelectedItem != null)
                {
                    string printerName = ExtractPrinterName(printerComboBox.SelectedItem.ToString());
                    VerifyDnp2InchCutSetting(printerName);
                }
                else
                {
                    MessageBox.Show("Please select a printer first.", "Verify Settings", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error verifying settings: {ex.Message}", "Verify Settings", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void Test4x6Print_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (printer4x6ComboBox?.SelectedItem != null)
                {
                    string printerName = ExtractPrinterName(printer4x6ComboBox.SelectedItem.ToString());
                    
                    // Get the selected paper size from settings
                    string paperSize = Properties.Settings.Default.PrintPaperSize;
                    if (string.IsNullOrEmpty(paperSize))
                        paperSize = "4x6"; // Default
                    
                    // Create a test image for the selected paper size
                    string testImagePath = CreateDefaultPrinterTestImage(printerName, paperSize);
                    
                    if (!string.IsNullOrEmpty(testImagePath))
                    {
                        // Temporarily set to use 4x6 printer and its DEVMODE
                        bool originalAutoRoute = Properties.Settings.Default.AutoRoutePrinter;
                        string originalPrinter = Properties.Settings.Default.PrinterName;
                        string originalDevMode = Properties.Settings.Default.PrinterDriverSettings;
                        
                        Properties.Settings.Default.AutoRoutePrinter = true; // Enable to use format-specific DEVMODE
                        Properties.Settings.Default.PrinterName = printerName;
                        Properties.Settings.Default.Printer4x6Name = printerName;
                        Properties.Settings.Default.Dnp2InchCut = false; // Disable 2-inch cut for 4x6
                        
                        var printService = PrintService.Instance;
                        var result = printService.PrintPhotos(new List<string> { testImagePath }, "TEST_4x6", 1);
                        
                        // Restore original settings
                        Properties.Settings.Default.AutoRoutePrinter = originalAutoRoute;
                        Properties.Settings.Default.PrinterName = originalPrinter;
                        
                        if (result.Success)
                        {
                            MessageBox.Show($"4x6 test print sent successfully to:\n{printerName}", "Test Print 4x6", 
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show($"4x6 test print failed: {result.Message}", "Test Print 4x6", 
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        
                        // Clean up test image
                        if (File.Exists(testImagePath))
                            File.Delete(testImagePath);
                    }
                }
                else
                {
                    MessageBox.Show("Please select a 4x6 printer first.", "Test Print 4x6", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during 4x6 test print: {ex.Message}", "Test Print 4x6", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void Test2x6Print_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (printer2x6ComboBox?.SelectedItem != null)
                {
                    string printerName = ExtractPrinterName(printer2x6ComboBox.SelectedItem.ToString());
                    
                    // Create a 2x6 test image
                    string testImagePath = Create2x6TestImage(printerName);
                    
                    if (!string.IsNullOrEmpty(testImagePath))
                    {
                        // Temporarily set to use 2x6 printer and its DEVMODE
                        bool originalAutoRoute = Properties.Settings.Default.AutoRoutePrinter;
                        string originalPrinter = Properties.Settings.Default.PrinterName;
                        string originalDevMode = Properties.Settings.Default.PrinterDriverSettings;
                        
                        Properties.Settings.Default.AutoRoutePrinter = true; // Enable to use format-specific DEVMODE
                        Properties.Settings.Default.PrinterName = printerName;
                        Properties.Settings.Default.Printer2x6Name = printerName;
                        Properties.Settings.Default.Dnp2InchCut = true; // Enable 2-inch cut for 2x6
                        
                        var printService = PrintService.Instance;
                        var result = printService.PrintPhotos(new List<string> { testImagePath }, "TEST_2x6", 1);
                        
                        // Restore original settings
                        Properties.Settings.Default.AutoRoutePrinter = originalAutoRoute;
                        Properties.Settings.Default.PrinterName = originalPrinter;
                        
                        if (result.Success)
                        {
                            MessageBox.Show($"2x6 test print sent successfully to:\n{printerName}\n\nNote: 2-inch cut is enabled for this print.", 
                                "Test Print 2x6", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show($"2x6 test print failed: {result.Message}", "Test Print 2x6", 
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        
                        // Clean up test image
                        if (File.Exists(testImagePath))
                            File.Delete(testImagePath);
                    }
                }
                else
                {
                    MessageBox.Show("Please select a 2x6 printer first.", "Test Print 2x6", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during 2x6 test print: {ex.Message}", "Test Print 2x6", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void TestPrint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (printerComboBox?.SelectedItem != null)
                {
                    string printerName = ExtractPrinterName(printerComboBox.SelectedItem.ToString());
                    var printService = PrintService.Instance;
                    
                    // Create a simple test image path (you might want to include a test image in resources)
                    string testImagePath = CreateTestImage();
                    
                    if (!string.IsNullOrEmpty(testImagePath))
                    {
                        var result = printService.PrintPhotos(new List<string> { testImagePath }, "TEST_SESSION", 1);
                        if (result.Success)
                        {
                            MessageBox.Show("Test print sent successfully!", "Test Print", 
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show($"Test print failed: {result.Message}", "Test Print", 
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        
                        // Clean up test image
                        if (File.Exists(testImagePath))
                            File.Delete(testImagePath);
                    }
                }
                else
                {
                    MessageBox.Show("Please select a printer first.", "Test Print", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during test print: {ex.Message}", "Test Print", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"Test print error: {ex}");
            }
        }

        private string CreateDefaultPrinterTestImage(string printerName, string paperSize)
        {
            try
            {
                // Determine dimensions based on paper size (at 300 DPI)
                int width, height;
                string sizeLabel;
                
                switch (paperSize.ToLower().Replace(" ", ""))
                {
                    case "5x7":
                        width = 1500; height = 2100;
                        sizeLabel = "5x7";
                        break;
                    case "8x10":
                        width = 2400; height = 3000;
                        sizeLabel = "8x10";
                        break;
                    case "a4":
                        width = 2480; height = 3508;
                        sizeLabel = "A4";
                        break;
                    case "letter":
                        width = 2550; height = 3300;
                        sizeLabel = "Letter";
                        break;
                    case "4x6":
                    default:
                        width = 1200; height = 1800;
                        sizeLabel = "4x6";
                        break;
                }
                
                // Create test image with appropriate dimensions
                using (var bitmap = new System.Drawing.Bitmap(width, height))
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    // Fill with white background
                    graphics.Clear(System.Drawing.Color.White);
                    
                    // Scale font sizes based on image size
                    float scaleFactor = (float)width / 1200f;
                    
                    // Add border
                    using (var pen = new System.Drawing.Pen(System.Drawing.Color.Black, 5 * scaleFactor))
                    {
                        graphics.DrawRectangle(pen, 10, 10, bitmap.Width - 20, bitmap.Height - 20);
                    }
                    
                    // Add test content
                    using (var titleFont = new System.Drawing.Font("Arial", 48 * scaleFactor, System.Drawing.FontStyle.Bold))
                    using (var font = new System.Drawing.Font("Arial", 32 * scaleFactor))
                    using (var smallFont = new System.Drawing.Font("Arial", 24 * scaleFactor))
                    using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Black))
                    {
                        // Title
                        graphics.DrawString($"{sizeLabel} TEST PRINT", titleFont, brush, 200 * scaleFactor, 100 * scaleFactor);
                        
                        // Format indicator
                        using (var goldBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Gold))
                        {
                            graphics.FillRectangle(goldBrush, 100 * scaleFactor, 250 * scaleFactor, 
                                (width - 200 * scaleFactor), 100 * scaleFactor);
                        }
                        graphics.DrawString($"Default Printer - {sizeLabel} Format", font, brush, 
                            150 * scaleFactor, 280 * scaleFactor);
                        
                        // Print details
                        float y = 450 * scaleFactor;
                        graphics.DrawString($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", smallFont, brush, 100 * scaleFactor, y);
                        y += 70 * scaleFactor;
                        graphics.DrawString($"Printer: {printerName}", smallFont, brush, 100 * scaleFactor, y);
                        y += 70 * scaleFactor;
                        graphics.DrawString($"Paper Size: {sizeLabel}", smallFont, brush, 100 * scaleFactor, y);
                        y += 70 * scaleFactor;
                        graphics.DrawString($"Dimensions: {width} x {height} px", smallFont, brush, 100 * scaleFactor, y);
                        y += 70 * scaleFactor;
                        graphics.DrawString($"Aspect Ratio: {((float)width/height):F2}", smallFont, brush, 100 * scaleFactor, y);
                        y += 70 * scaleFactor;
                        graphics.DrawString("2-inch cut: DISABLED", smallFont, brush, 100 * scaleFactor, y);
                    }
                    
                    // Add color test bars (scaled)
                    int barWidth = (int)(200 * scaleFactor);
                    int barHeight = (int)(600 * scaleFactor);
                    int startY = height - (int)(900 * scaleFactor);
                    
                    using (var redBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Red))
                    using (var greenBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Green))
                    using (var blueBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Blue))
                    using (var yellowBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Yellow))
                    using (var magentaBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Magenta))
                    using (var cyanBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Cyan))
                    {
                        int x = (int)(100 * scaleFactor);
                        graphics.FillRectangle(redBrush, x, startY, barWidth, barHeight);
                        x += barWidth;
                        graphics.FillRectangle(greenBrush, x, startY, barWidth, barHeight);
                        x += barWidth;
                        graphics.FillRectangle(blueBrush, x, startY, barWidth, barHeight);
                        x += barWidth;
                        if (width > 1800) // Add more bars for larger formats
                        {
                            graphics.FillRectangle(yellowBrush, x, startY, barWidth, barHeight);
                            x += barWidth;
                            graphics.FillRectangle(magentaBrush, x, startY, barWidth, barHeight);
                        }
                    }
                    
                    // Save to temp file
                    string tempPath = Path.Combine(Path.GetTempPath(), 
                        $"photobooth_test_{sizeLabel}_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                    bitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                    return tempPath;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating test image: {ex.Message}");
                return null;
            }
        }
        
        private string Create4x6TestImage(string printerName)
        {
            try
            {
                // Create a 4x6 test image (1200x1800 pixels at 300 DPI = 4x6 inches)
                using (var bitmap = new System.Drawing.Bitmap(1200, 1800))
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    // Fill with white background
                    graphics.Clear(System.Drawing.Color.White);
                    
                    // Add border
                    using (var pen = new System.Drawing.Pen(System.Drawing.Color.Black, 5))
                    {
                        graphics.DrawRectangle(pen, 10, 10, bitmap.Width - 20, bitmap.Height - 20);
                    }
                    
                    // Add test content
                    using (var titleFont = new System.Drawing.Font("Arial", 48, System.Drawing.FontStyle.Bold))
                    using (var font = new System.Drawing.Font("Arial", 32))
                    using (var smallFont = new System.Drawing.Font("Arial", 24))
                    using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Black))
                    {
                        // Title
                        graphics.DrawString("4x6 TEST PRINT", titleFont, brush, 200, 100);
                        
                        // Format indicator
                        using (var goldBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Gold))
                        {
                            graphics.FillRectangle(goldBrush, 100, 250, 1000, 100);
                        }
                        graphics.DrawString("Standard 4x6 Photo Format", font, brush, 250, 280);
                        
                        // Print details
                        graphics.DrawString($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", smallFont, brush, 100, 450);
                        graphics.DrawString($"Printer: {printerName}", smallFont, brush, 100, 520);
                        graphics.DrawString("Dimensions: 4\" x 6\" (1200 x 1800 px)", smallFont, brush, 100, 590);
                        graphics.DrawString("Aspect Ratio: 0.67", smallFont, brush, 100, 660);
                        graphics.DrawString("2-inch cut: DISABLED", smallFont, brush, 100, 730);
                    }
                    
                    // Add color test bars
                    int barWidth = 200;
                    int barHeight = 600;
                    int startY = 900;
                    
                    using (var redBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Red))
                    using (var greenBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Green))
                    using (var blueBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Blue))
                    using (var yellowBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Yellow))
                    using (var magentaBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Magenta))
                    using (var cyanBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Cyan))
                    {
                        graphics.FillRectangle(redBrush, 100, startY, barWidth, barHeight);
                        graphics.FillRectangle(greenBrush, 300, startY, barWidth, barHeight);
                        graphics.FillRectangle(blueBrush, 500, startY, barWidth, barHeight);
                        graphics.FillRectangle(yellowBrush, 700, startY, barWidth, barHeight);
                        graphics.FillRectangle(magentaBrush, 900, startY, barWidth, barHeight);
                        graphics.FillRectangle(cyanBrush, 100, startY + barHeight, 1000, 200);
                    }
                    
                    // Save to temp file
                    string tempPath = Path.Combine(Path.GetTempPath(), $"photobooth_test_4x6_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                    bitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                    return tempPath;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating 4x6 test image: {ex.Message}");
                return null;
            }
        }
        
        private string Create2x6TestImage(string printerName)
        {
            try
            {
                // Create a 2x6 test image (600x1800 pixels at 300 DPI = 2x6 inches)
                using (var bitmap = new System.Drawing.Bitmap(600, 1800))
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    // Fill with white background
                    graphics.Clear(System.Drawing.Color.White);
                    
                    // Add border
                    using (var pen = new System.Drawing.Pen(System.Drawing.Color.Black, 3))
                    {
                        graphics.DrawRectangle(pen, 5, 5, bitmap.Width - 10, bitmap.Height - 10);
                    }
                    
                    // Add test content for strip format
                    using (var titleFont = new System.Drawing.Font("Arial", 28, System.Drawing.FontStyle.Bold))
                    using (var font = new System.Drawing.Font("Arial", 18))
                    using (var smallFont = new System.Drawing.Font("Arial", 14))
                    using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Black))
                    {
                        // Title
                        graphics.DrawString("2x6 STRIP", titleFont, brush, 150, 50);
                        graphics.DrawString("TEST", titleFont, brush, 200, 100);
                        
                        // Format indicator
                        using (var cyanBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Cyan))
                        {
                            graphics.FillRectangle(cyanBrush, 50, 200, 500, 60);
                        }
                        graphics.DrawString("Photo Strip Format", font, brush, 150, 220);
                        
                        // Add 2-inch cut markers
                        using (var dashedPen = new System.Drawing.Pen(System.Drawing.Color.Red, 2))
                        {
                            dashedPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                            // Mark at 2 inches (600 pixels)
                            graphics.DrawLine(dashedPen, 0, 600, bitmap.Width, 600);
                            graphics.DrawString("← 2\" CUT →", smallFont, brush, 230, 580);
                            
                            // Mark at 4 inches (1200 pixels)
                            graphics.DrawLine(dashedPen, 0, 1200, bitmap.Width, 1200);
                            graphics.DrawString("← 2\" CUT →", smallFont, brush, 230, 1180);
                        }
                        
                        // First panel (0-600px)
                        graphics.DrawString("Panel 1", font, brush, 250, 350);
                        graphics.DrawString($"Date: {DateTime.Now:MM/dd}", smallFont, brush, 200, 450);
                        
                        // Second panel (600-1200px)
                        graphics.DrawString("Panel 2", font, brush, 250, 750);
                        graphics.DrawString($"Time: {DateTime.Now:HH:mm}", smallFont, brush, 200, 850);
                        graphics.DrawString($"Printer:", smallFont, brush, 50, 950);
                        graphics.DrawString($"{printerName}", smallFont, brush, 50, 980);
                        
                        // Third panel (1200-1800px)
                        graphics.DrawString("Panel 3", font, brush, 250, 1350);
                        graphics.DrawString("2x6 inches", smallFont, brush, 200, 1450);
                        graphics.DrawString("Aspect: 0.33", smallFont, brush, 200, 1500);
                        graphics.DrawString("2\" cut: ON", smallFont, brush, 200, 1550);
                        
                        // Add color bars in third panel
                        int barHeight = 100;
                        using (var redBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Red))
                        using (var greenBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Green))
                        using (var blueBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Blue))
                        {
                            graphics.FillRectangle(redBrush, 50, 1650, 150, barHeight);
                            graphics.FillRectangle(greenBrush, 225, 1650, 150, barHeight);
                            graphics.FillRectangle(blueBrush, 400, 1650, 150, barHeight);
                        }
                    }
                    
                    // Save to temp file
                    string tempPath = Path.Combine(Path.GetTempPath(), $"photobooth_test_2x6_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                    bitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                    return tempPath;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating 2x6 test image: {ex.Message}");
                return null;
            }
        }
        
        private string CreateTestImage()
        {
            try
            {
                // Create a simple test image
                using (var bitmap = new System.Drawing.Bitmap(600, 400))
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    // Fill with white background
                    graphics.Clear(System.Drawing.Color.White);
                    
                    // Add test content
                    using (var font = new System.Drawing.Font("Arial", 24, System.Drawing.FontStyle.Bold))
                    using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Black))
                    {
                        graphics.DrawString("PHOTOBOOTH TEST PRINT", font, brush, 50, 50);
                        graphics.DrawString($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", new System.Drawing.Font("Arial", 16), brush, 50, 150);
                        graphics.DrawString("Print Settings:", new System.Drawing.Font("Arial", 16), brush, 50, 200);
                        
                        if (printerComboBox?.SelectedItem != null)
                            graphics.DrawString($"Printer: {printerComboBox.SelectedItem}", new System.Drawing.Font("Arial", 12), brush, 50, 250);
                        
                        if (paperSizeComboBox?.SelectedItem is ComboBoxItem paperItem)
                            graphics.DrawString($"Paper: {paperItem.Content}", new System.Drawing.Font("Arial", 12), brush, 50, 280);
                        
                        /*if (colorModeComboBox?.SelectedItem is ComboBoxItem colorItem)
                            graphics.DrawString($"Color: {colorItem.Content}", new System.Drawing.Font("Arial", 12), brush, 50, 310);*/
                    }
                    
                    // Add color test bars
                    using (var redBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Red))
                    using (var greenBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Green))
                    using (var blueBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Blue))
                    {
                        graphics.FillRectangle(redBrush, 400, 100, 50, 200);
                        graphics.FillRectangle(greenBrush, 460, 100, 50, 200);
                        graphics.FillRectangle(blueBrush, 520, 100, 50, 200);
                    }
                    
                    // Save to temp file
                    string tempPath = Path.Combine(Path.GetTempPath(), $"photobooth_test_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                    bitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                    return tempPath;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating test image: {ex.Message}");
                return null;
            }
        }

        private string ExtractPrinterName(string displayText)
        {
            if (string.IsNullOrEmpty(displayText))
                return "";
                
            // If display text contains status info in parentheses, extract just the printer name
            int parenIndex = displayText.IndexOf(" (");
            if (parenIndex > 0)
            {
                return displayText.Substring(0, parenIndex);
            }
            
            return displayText;
        }

        private string SerializePrinterSettings(System.Drawing.Printing.PrinterSettings settings)
        {
            try
            {
                // Get raw driver settings (DEVMODE) for advanced settings
                string rawDriverData = CaptureRawDriverSettings(settings.PrinterName);
                
                // Comprehensive serialization of printer settings
                var settingsData = new
                {
                    PrinterName = settings.PrinterName,
                    Copies = settings.Copies,
                    Duplex = settings.Duplex.ToString(),
                    PrintRange = settings.PrintRange.ToString(),
                    Collate = settings.Collate,
                    FromPage = settings.FromPage,
                    ToPage = settings.ToPage,
                    MaximumCopies = settings.MaximumCopies,
                    MaximumPage = settings.MaximumPage,
                    MinimumPage = settings.MinimumPage,
                    PrintToFile = settings.PrintToFile,
                    PrintFileName = settings.PrintFileName,
                    SupportsColor = settings.SupportsColor,
                    
                    // Page Settings
                    PageSettings = new
                    {
                        Landscape = settings.DefaultPageSettings.Landscape,
                        Color = settings.DefaultPageSettings.Color,
                        PaperSize = new
                        {
                            PaperName = settings.DefaultPageSettings.PaperSize?.PaperName,
                            Width = settings.DefaultPageSettings.PaperSize?.Width,
                            Height = settings.DefaultPageSettings.PaperSize?.Height,
                            Kind = settings.DefaultPageSettings.PaperSize?.Kind.ToString()
                        },
                        PaperSource = new
                        {
                            SourceName = settings.DefaultPageSettings.PaperSource?.SourceName,
                            Kind = settings.DefaultPageSettings.PaperSource?.Kind.ToString()
                        },
                        PrinterResolution = new
                        {
                            X = settings.DefaultPageSettings.PrinterResolution?.X,
                            Y = settings.DefaultPageSettings.PrinterResolution?.Y,
                            Kind = settings.DefaultPageSettings.PrinterResolution?.Kind.ToString()
                        },
                        Margins = new
                        {
                            Left = settings.DefaultPageSettings.Margins?.Left,
                            Right = settings.DefaultPageSettings.Margins?.Right,
                            Top = settings.DefaultPageSettings.Margins?.Top,
                            Bottom = settings.DefaultPageSettings.Margins?.Bottom
                        }
                    },
                    
                    // Available capabilities
                    AvailablePaperSizes = settings.PaperSizes?.Cast<System.Drawing.Printing.PaperSize>()
                        .Select(ps => new { ps.PaperName, ps.Width, ps.Height, Kind = ps.Kind.ToString() })
                        .ToList(),
                        
                    AvailablePaperSources = settings.PaperSources?.Cast<System.Drawing.Printing.PaperSource>()
                        .Select(ps => new { ps.SourceName, Kind = ps.Kind.ToString() })
                        .ToList(),
                        
                    AvailableResolutions = settings.PrinterResolutions?.Cast<System.Drawing.Printing.PrinterResolution>()
                        .Select(pr => new { pr.X, pr.Y, Kind = pr.Kind.ToString() })
                        .ToList(),
                    
                    // Raw driver settings (includes advanced settings like "Enable 2" cut")
                    RawDriverSettings = rawDriverData,
                        
                    // Timestamp for settings
                    SavedAt = DateTime.Now
                };
                
                return Newtonsoft.Json.JsonConvert.SerializeObject(settingsData, Newtonsoft.Json.Formatting.Indented);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error serializing printer settings: {ex.Message}");
                return "";
            }
        }

        private string CaptureRawDriverSettings(string printerName)
        {
            IntPtr hPrinter = IntPtr.Zero;
            IntPtr pDevMode = IntPtr.Zero;
            
            try
            {
                // Open printer
                if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to open printer: {printerName}");
                    return "";
                }

                // Get size of DEVMODE structure
                int bytesNeeded = DocumentProperties(IntPtr.Zero, hPrinter, printerName, IntPtr.Zero, IntPtr.Zero, 0);
                if (bytesNeeded <= 0)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to get DEVMODE size");
                    return "";
                }

                // Allocate memory for DEVMODE
                pDevMode = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytesNeeded);
                if (pDevMode == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to allocate memory for DEVMODE");
                    return "";
                }

                IntPtr pDevModeData = GlobalLock(pDevMode);
                if (pDevModeData == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to lock DEVMODE memory");
                    return "";
                }

                // Get current printer settings
                int result = DocumentProperties(IntPtr.Zero, hPrinter, printerName, pDevModeData, IntPtr.Zero, DM_OUT_BUFFER);
                if (result < 0)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to get current printer settings");
                    return "";
                }

                // Convert DEVMODE to byte array
                byte[] devModeBytes = new byte[bytesNeeded];
                Marshal.Copy(pDevModeData, devModeBytes, 0, bytesNeeded);

                // Parse the DEVMODE to check orientation
                var devMode = (DEVMODE)Marshal.PtrToStructure(pDevModeData, typeof(DEVMODE));
                System.Diagnostics.Debug.WriteLine($"DEVMODE CAPTURE: dmOrientation={devMode.dmOrientation}, dmFields=0x{devMode.dmFields:X8}");
                System.Diagnostics.Debug.WriteLine($"DEVMODE CAPTURE: Orientation field set={(devMode.dmFields & DM_ORIENTATION) != 0}");
                System.Diagnostics.Debug.WriteLine($"DEVMODE CAPTURE: Paper size={devMode.dmPaperSize}, Width={devMode.dmPaperWidth}, Height={devMode.dmPaperLength}");
                
                // Ensure orientation field is set
                if ((devMode.dmFields & DM_ORIENTATION) == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"WARNING: Orientation not set in DEVMODE, will need to determine from paper dimensions");
                }

                // Convert to Base64 for storage
                string base64Data = Convert.ToBase64String(devModeBytes);
                
                System.Diagnostics.Debug.WriteLine($"Captured {bytesNeeded} bytes of driver settings for {printerName}");
                return base64Data;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error capturing raw driver settings: {ex.Message}");
                return "";
            }
            finally
            {
                if (pDevMode != IntPtr.Zero)
                {
                    GlobalUnlock(pDevMode);
                    GlobalFree(pDevMode);
                }
                if (hPrinter != IntPtr.Zero)
                {
                    ClosePrinter(hPrinter);
                }
            }
        }

        public static bool RestorePrinterSettings(string printerName, string savedSettingsJson)
        {
            try
            {
                if (string.IsNullOrEmpty(savedSettingsJson))
                    return false;

                var savedSettings = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(savedSettingsJson);
                
                // Create new printer settings for the specified printer
                var settings = new System.Drawing.Printing.PrinterSettings();
                settings.PrinterName = printerName;
                
                if (!settings.IsValid)
                {
                    System.Diagnostics.Debug.WriteLine($"Cannot restore settings: Printer {printerName} is not valid");
                    return false;
                }

                // Restore basic printer settings
                if (savedSettings.Copies != null)
                    settings.Copies = (short)savedSettings.Copies;
                    
                if (savedSettings.Collate != null)
                    settings.Collate = savedSettings.Collate;
                    
                if (savedSettings.PrintToFile != null)
                    settings.PrintToFile = savedSettings.PrintToFile;
                    
                if (savedSettings.PrintFileName != null)
                    settings.PrintFileName = savedSettings.PrintFileName;

                // Restore duplex setting
                if (savedSettings.Duplex != null)
                {
                    if (Enum.TryParse<System.Drawing.Printing.Duplex>(savedSettings.Duplex.ToString(), out System.Drawing.Printing.Duplex duplex))
                        settings.Duplex = duplex;
                }

                // Restore print range
                if (savedSettings.PrintRange != null)
                {
                    if (Enum.TryParse<System.Drawing.Printing.PrintRange>(savedSettings.PrintRange.ToString(), out System.Drawing.Printing.PrintRange printRange))
                        settings.PrintRange = printRange;
                }

                // Restore page settings
                if (savedSettings.PageSettings != null)
                {
                    if (savedSettings.PageSettings.Landscape != null)
                        settings.DefaultPageSettings.Landscape = savedSettings.PageSettings.Landscape;
                        
                    if (savedSettings.PageSettings.Color != null)
                        settings.DefaultPageSettings.Color = savedSettings.PageSettings.Color;

                    // Restore paper size
                    if (savedSettings.PageSettings.PaperSize?.PaperName != null)
                    {
                        string paperName = savedSettings.PageSettings.PaperSize.PaperName;
                        foreach (System.Drawing.Printing.PaperSize size in settings.PaperSizes)
                        {
                            if (size.PaperName == paperName)
                            {
                                settings.DefaultPageSettings.PaperSize = size;
                                break;
                            }
                        }
                    }

                    // Restore paper source
                    if (savedSettings.PageSettings.PaperSource?.SourceName != null)
                    {
                        string sourceName = savedSettings.PageSettings.PaperSource.SourceName;
                        foreach (System.Drawing.Printing.PaperSource source in settings.PaperSources)
                        {
                            if (source.SourceName == sourceName)
                            {
                                settings.DefaultPageSettings.PaperSource = source;
                                break;
                            }
                        }
                    }

                    // Restore printer resolution
                    if (savedSettings.PageSettings.PrinterResolution?.X != null && 
                        savedSettings.PageSettings.PrinterResolution?.Y != null)
                    {
                        try
                        {
                            int x = Convert.ToInt32(savedSettings.PageSettings.PrinterResolution.X);
                            int y = Convert.ToInt32(savedSettings.PageSettings.PrinterResolution.Y);
                            
                            foreach (System.Drawing.Printing.PrinterResolution resolution in settings.PrinterResolutions)
                            {
                                if (resolution.X == x && resolution.Y == y)
                                {
                                    settings.DefaultPageSettings.PrinterResolution = resolution;
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error restoring printer resolution: {ex.Message}");
                        }
                    }

                    // Restore margins
                    if (savedSettings.PageSettings.Margins != null)
                    {
                        try
                        {
                            int left = Convert.ToInt32(savedSettings.PageSettings.Margins.Left ?? 100);
                            int right = Convert.ToInt32(savedSettings.PageSettings.Margins.Right ?? 100);
                            int top = Convert.ToInt32(savedSettings.PageSettings.Margins.Top ?? 100);
                            int bottom = Convert.ToInt32(savedSettings.PageSettings.Margins.Bottom ?? 100);
                            
                            var margins = new System.Drawing.Printing.Margins(left, right, top, bottom);
                            settings.DefaultPageSettings.Margins = margins;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error restoring margins: {ex.Message}");
                        }
                    }
                }

                // Restore raw driver settings (includes advanced settings like "Enable 2" cut")
                if (savedSettings.RawDriverSettings != null)
                {
                    bool rawRestoreSuccess = RestoreRawDriverSettings(printerName, savedSettings.RawDriverSettings.ToString());
                    if (rawRestoreSuccess)
                    {
                        System.Diagnostics.Debug.WriteLine($"Successfully restored raw driver settings for {printerName}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to restore raw driver settings for {printerName}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Successfully restored printer settings for {printerName}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restoring printer settings: {ex.Message}");
                return false;
            }
        }

        public static bool RestoreRawDriverSettings(string printerName, string base64Data)
        {
            IntPtr hPrinter = IntPtr.Zero;
            IntPtr pDevMode = IntPtr.Zero;
            
            try
            {
                if (string.IsNullOrEmpty(base64Data))
                    return false;

                // Convert Base64 back to byte array
                byte[] devModeBytes = Convert.FromBase64String(base64Data);

                // Open printer
                if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to open printer for restore: {printerName}");
                    return false;
                }

                // Allocate memory for DEVMODE
                pDevMode = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)devModeBytes.Length);
                if (pDevMode == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to allocate memory for DEVMODE restore");
                    return false;
                }

                IntPtr pDevModeData = GlobalLock(pDevMode);
                if (pDevModeData == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to lock DEVMODE memory for restore");
                    return false;
                }

                // Copy saved DEVMODE data
                Marshal.Copy(devModeBytes, 0, pDevModeData, devModeBytes.Length);

                // CRITICAL: Use both DM_IN_BUFFER and DM_OUT_BUFFER to ensure settings stick
                // This tells Windows to apply the input DEVMODE and return the validated result
                int result = DocumentProperties(IntPtr.Zero, hPrinter, printerName, pDevModeData, pDevModeData, DM_IN_BUFFER | DM_OUT_BUFFER);
                
                if (result >= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully applied raw driver settings to {printerName}");
                    
                    // Also try to update the registry Default DevMode for persistence
                    try
                    {
                        string regPath = $@"System\CurrentControlSet\Control\Print\Printers\{printerName}";
                        using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regPath, true))
                        {
                            if (key != null)
                            {
                                key.SetValue("Default DevMode", devModeBytes, Microsoft.Win32.RegistryValueKind.Binary);
                                System.Diagnostics.Debug.WriteLine($"Updated registry Default DevMode for {printerName}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Registry update failed (may need admin rights): {ex.Message}");
                        // Continue - the in-memory update should work for this print session
                    }
                    
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to apply raw driver settings to {printerName}, error: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restoring raw driver settings: {ex.Message}");
                return false;
            }
            finally
            {
                if (pDevMode != IntPtr.Zero)
                {
                    GlobalUnlock(pDevMode);
                    GlobalFree(pDevMode);
                }
                if (hPrinter != IntPtr.Zero)
                {
                    ClosePrinter(hPrinter);
                }
            }
        }

        private void SavePrinterProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (printerComboBox?.SelectedItem != null)
                {
                    string printerName = ExtractPrinterName(printerComboBox.SelectedItem.ToString());
                    
                    var printDocument = new System.Drawing.Printing.PrintDocument();
                    printDocument.PrinterSettings.PrinterName = printerName;
                    
                    if (printDocument.PrinterSettings.IsValid)
                    {
                        // Prompt for profile name
                        string profileName = Microsoft.VisualBasic.Interaction.InputBox(
                            "Enter a name for this printer profile:", 
                            "Save Printer Profile", 
                            $"{printerName}_Profile_{DateTime.Now:yyyyMMdd}");
                            
                        if (!string.IsNullOrEmpty(profileName))
                        {
                            // Capture current driver settings including 2-inch cut
                            string rawSettings = CaptureRawDriverSettings(printerName);
                            
                            // Create profile object with all settings
                            var profile = new
                            {
                                ProfileName = profileName,
                                PrinterName = printerName,
                                PrinterSettings = SerializePrinterSettings(printDocument.PrinterSettings),
                                RawDriverSettings = rawSettings,
                                /*Dnp2InchCut = dnp2InchCutCheckBox?.IsChecked ?? false,*/
                                CreatedDate = DateTime.Now
                            };
                            
                            // Load existing profiles
                            var profiles = LoadPrinterProfiles();
                            
                            // Add or update this profile
                            profiles[profileName] = Newtonsoft.Json.JsonConvert.SerializeObject(profile);
                            
                            // Save all profiles back
                            Properties.Settings.Default.PrinterProfiles = Newtonsoft.Json.JsonConvert.SerializeObject(profiles);
                            Properties.Settings.Default.Save();
                            
                            MessageBox.Show($"Printer profile '{profileName}' saved successfully!\n\nThis profile includes the 2-inch cut setting and all driver configurations.", 
                                "Save Profile", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    else
                    {
                        MessageBox.Show("Selected printer is not valid.", "Save Profile", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Please select a printer first.", "Save Profile", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving printer profile: {ex.Message}", "Save Profile", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Dictionary<string, string> LoadPrinterProfiles()
        {
            try
            {
                string profilesJson = Properties.Settings.Default.PrinterProfiles;
                if (!string.IsNullOrEmpty(profilesJson))
                {
                    return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(profilesJson) 
                           ?? new Dictionary<string, string>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading printer profiles: {ex.Message}");
            }
            return new Dictionary<string, string>();
        }
        
        private void LoadPrinterProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get all saved printer profiles
                var profiles = LoadPrinterProfiles();
                
                if (profiles.Count == 0)
                {
                    MessageBox.Show("No saved printer profiles found.", "Load Profile", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                var profileNames = profiles.Keys.ToList();

                // Show profile selection dialog (simple approach)
                string selectedProfile = "";
                var profileDialog = new System.Windows.Forms.Form()
                {
                    Text = "Select Printer Profile",
                    Size = new System.Drawing.Size(400, 200),
                    StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
                };
                
                var listBox = new System.Windows.Forms.ListBox()
                {
                    Dock = System.Windows.Forms.DockStyle.Fill,
                    DataSource = profileNames
                };
                
                var okButton = new System.Windows.Forms.Button()
                {
                    Text = "Load",
                    DialogResult = System.Windows.Forms.DialogResult.OK,
                    Dock = System.Windows.Forms.DockStyle.Bottom
                };
                
                profileDialog.Controls.Add(listBox);
                profileDialog.Controls.Add(okButton);
                
                if (profileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && 
                    listBox.SelectedItem != null)
                {
                    selectedProfile = listBox.SelectedItem.ToString();
                    
                    // Load the selected profile from the dictionary
                    if (profiles.ContainsKey(selectedProfile))
                    {
                        string profileJson = profiles[selectedProfile];
                        var profile = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(profileJson);
                        
                        if (printerComboBox?.SelectedItem != null)
                        {
                            string currentPrinter = ExtractPrinterName(printerComboBox.SelectedItem.ToString());
                            
                            // Restore the printer settings
                            bool success = false;
                            if (profile.PrinterSettings != null)
                            {
                                success = RestorePrinterSettings(currentPrinter, profile.PrinterSettings.ToString());
                            }
                            
                            // Restore raw driver settings if available
                            if (profile.RawDriverSettings != null)
                            {
                                RestoreRawDriverSettings(currentPrinter, profile.RawDriverSettings.ToString());
                            }
                            
                            // Restore 2-inch cut setting
                            /*if (profile.Dnp2InchCut != null && dnp2InchCutCheckBox != null)
                            {
                                dnp2InchCutCheckBox.IsChecked = (bool)profile.Dnp2InchCut;
                                Properties.Settings.Default.Dnp2InchCut = (bool)profile.Dnp2InchCut;
                                Properties.Settings.Default.Save();
                            }*/
                            
                            if (success)
                            {
                                MessageBox.Show($"Printer profile '{selectedProfile}' loaded successfully!\n\n2-inch cut setting has been restored.", 
                                    "Load Profile", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            else
                            {
                                MessageBox.Show("Failed to load printer profile.", "Load Profile", 
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        else
                        {
                            MessageBox.Show("Please select a printer first.", "Load Profile", 
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading printer profile: {ex.Message}", "Load Profile", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void ApplyDnp2InchCutSetting(string printerName, bool enable2InchCut)
        {
            // The only reliable way to set DNP 2-inch cut is through the driver dialog
            // We'll open it programmatically with the setting pre-configured
            
            IntPtr hPrinter = IntPtr.Zero;
            IntPtr pDevMode = IntPtr.Zero;
            
            try
            {
                // Open the printer
                if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to open printer: {printerName}");
                    return;
                }
                
                // Get the size of DEVMODE structure
                int bytesNeeded = DocumentProperties(IntPtr.Zero, hPrinter, printerName, IntPtr.Zero, IntPtr.Zero, 0);
                if (bytesNeeded <= 0)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to get DEVMODE size");
                    return;
                }
                
                // Allocate memory for DEVMODE
                pDevMode = Marshal.AllocHGlobal(bytesNeeded);
                
                // Get current settings
                int result = DocumentProperties(IntPtr.Zero, hPrinter, printerName, pDevMode, IntPtr.Zero, DM_OUT_BUFFER);
                if (result < 0)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to get current DEVMODE");
                    return;
                }
                
                // Important: The 2-inch cut setting in DNP drivers is not directly modifiable via DEVMODE
                // It requires using the driver's proprietary interface
                // The setting is stored in the driver's private data area but the structure is undocumented
                
                // Show a message to the user about what needs to be done
                if (enable2InchCut)
                {
                    System.Diagnostics.Debug.WriteLine("IMPORTANT: To enable 2-inch cut, you must:");
                    System.Diagnostics.Debug.WriteLine("1. Click 'Advanced Driver Settings'");
                    System.Diagnostics.Debug.WriteLine("2. Set '2inch cut' to 'Enable' in the DNP dialog");
                    System.Diagnostics.Debug.WriteLine("3. Click OK to save");
                    System.Diagnostics.Debug.WriteLine("4. Click 'Save Profile' to capture the setting");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error with DNP 2-inch cut setting: {ex.Message}");
            }
            finally
            {
                if (pDevMode != IntPtr.Zero)
                    Marshal.FreeHGlobal(pDevMode);
                    
                if (hPrinter != IntPtr.Zero)
                    ClosePrinter(hPrinter);
            }
        }
        
        public static bool ApplyDevModeToPrintDocument(System.Drawing.Printing.PrintDocument printDocument, string base64DevMode)
        {
            IntPtr hPrinter = IntPtr.Zero;
            IntPtr pDevMode = IntPtr.Zero;
            
            try
            {
                if (string.IsNullOrEmpty(base64DevMode))
                    return false;
                    
                string printerName = printDocument.PrinterSettings.PrinterName;
                
                // Convert Base64 to byte array
                byte[] devModeBytes = Convert.FromBase64String(base64DevMode);
                
                // Open printer
                if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to open printer for DEVMODE application: {printerName}");
                    return false;
                }
                
                // Allocate memory for DEVMODE
                pDevMode = Marshal.AllocHGlobal(devModeBytes.Length);
                
                // Copy saved DEVMODE data
                Marshal.Copy(devModeBytes, 0, pDevMode, devModeBytes.Length);
                
                // Apply DEVMODE using DocumentProperties with IN_BUFFER | OUT_BUFFER
                int result = DocumentProperties(IntPtr.Zero, hPrinter, printerName, pDevMode, pDevMode, DM_IN_BUFFER | DM_OUT_BUFFER);
                
                if (result >= 0)
                {
                    // Now apply this DEVMODE to the PrintDocument's PageSettings
                    // This is the critical step that was missing
                    
                    // Get the DEVMODE structure from the pointer
                    var devMode = (DEVMODE)Marshal.PtrToStructure(pDevMode, typeof(DEVMODE));
                    
                    // Apply to PrintDocument's DefaultPageSettings
                    if ((devMode.dmFields & DM_ORIENTATION) != 0)
                    {
                        bool isLandscape = (devMode.dmOrientation == 2); // 2 = DMORIENT_LANDSCAPE, 1 = DMORIENT_PORTRAIT
                        printDocument.DefaultPageSettings.Landscape = isLandscape;
                        System.Diagnostics.Debug.WriteLine($"DEVMODE ORIENTATION: dmOrientation={devMode.dmOrientation}, Setting Landscape={isLandscape}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"DEVMODE ORIENTATION: DM_ORIENTATION flag not set in dmFields (0x{devMode.dmFields:X8})");
                    }
                    
                    if ((devMode.dmFields & DM_PAPERSIZE) != 0)
                    {
                        foreach (System.Drawing.Printing.PaperSize ps in printDocument.PrinterSettings.PaperSizes)
                        {
                            if (ps.RawKind == devMode.dmPaperSize)
                            {
                                printDocument.DefaultPageSettings.PaperSize = ps;
                                break;
                            }
                        }
                    }
                    
                    if ((devMode.dmFields & DM_COLOR) != 0)
                    {
                        printDocument.DefaultPageSettings.Color = (devMode.dmColor > 1);
                    }
                    
                    // Apply the entire DEVMODE to the PrinterSettings
                    // This includes all driver-specific settings like 2-inch cut
                    printDocument.PrinterSettings.SetHdevmode(pDevMode);
                    printDocument.DefaultPageSettings.SetHdevmode(pDevMode);
                    
                    System.Diagnostics.Debug.WriteLine($"Successfully applied DEVMODE to PrintDocument for {printerName}");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to apply DEVMODE to printer: {printerName}, error: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying DEVMODE to PrintDocument: {ex.Message}");
                return false;
            }
            finally
            {
                if (pDevMode != IntPtr.Zero)
                    Marshal.FreeHGlobal(pDevMode);
                    
                if (hPrinter != IntPtr.Zero)
                    ClosePrinter(hPrinter);
            }
        }
        
        private void ExportPrinterProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (printerComboBox?.SelectedItem != null)
                {
                    string printerName = ExtractPrinterName(printerComboBox.SelectedItem.ToString());
                    
                    var printDocument = new System.Drawing.Printing.PrintDocument();
                    printDocument.PrinterSettings.PrinterName = printerName;
                    
                    if (printDocument.PrinterSettings.IsValid)
                    {
                        string settingsJson = SerializePrinterSettings(printDocument.PrinterSettings);
                        
                        var saveDialog = new Microsoft.Win32.SaveFileDialog()
                        {
                            Title = "Export Printer Profile",
                            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                            FileName = $"{printerName}_Profile_{DateTime.Now:yyyyMMdd}.json"
                        };
                        
                        if (saveDialog.ShowDialog() == true)
                        {
                            System.IO.File.WriteAllText(saveDialog.FileName, settingsJson);
                            MessageBox.Show($"Printer profile exported to:\n{saveDialog.FileName}", 
                                "Export Profile", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    else
                    {
                        MessageBox.Show("Selected printer is not valid.", "Export Profile", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Please select a printer first.", "Export Profile", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting printer profile: {ex.Message}", "Export Profile", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ConfigurePrinterProfiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var profileWindow = new Windows.PrinterProfileConfigWindow();
                profileWindow.Owner = Window.GetWindow(this);
                profileWindow.ShowDialog();
                
                // Refresh printer lists after configuration
                RefreshPrinters_Click(sender, e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening printer profile configuration: {ex.Message}", 
                    "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                ScrollViewer scrollViewer = sender as ScrollViewer;
                if (scrollViewer != null)
                {
                    // Handle mouse wheel scrolling
                    double offset = scrollViewer.VerticalOffset;
                    
                    // Adjust scroll speed (multiply delta for faster scrolling)
                    double scrollAmount = e.Delta * 0.5; // Adjust multiplier for scroll speed
                    
                    scrollViewer.ScrollToVerticalOffset(offset - scrollAmount);
                    
                    // Mark event as handled to prevent bubbling
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ScrollViewer mouse wheel error: {ex.Message}");
            }
        }

        private void InnerScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                ScrollViewer innerScrollViewer = sender as ScrollViewer;
                if (innerScrollViewer != null)
                {
                    double offset = innerScrollViewer.VerticalOffset;
                    double scrollAmount = e.Delta * 0.3; // Slower scroll for inner viewers
                    
                    // Check if we can scroll in the inner viewer
                    bool canScrollUp = offset > 0;
                    bool canScrollDown = offset < innerScrollViewer.ScrollableHeight;
                    
                    if ((e.Delta > 0 && canScrollUp) || (e.Delta < 0 && canScrollDown))
                    {
                        // Scroll the inner viewer
                        innerScrollViewer.ScrollToVerticalOffset(offset - scrollAmount);
                        e.Handled = true;
                    }
                    // If inner can't scroll, let the event bubble to parent
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Inner ScrollViewer mouse wheel error: {ex.Message}");
            }
        }

        #region GIF Animation Settings Event Handlers

        private void EnableGifGeneration_Changed(object sender, RoutedEventArgs e)
        {
            if (enableGifGenerationCheckBox != null)
            {
                Properties.Settings.Default.EnableGifGeneration = enableGifGenerationCheckBox.IsChecked ?? false;
                Properties.Settings.Default.Save();
            }
        }

        private void GifFrameDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (gifFrameDelaySlider != null && gifFrameDelayValueText != null)
            {
                double seconds = Math.Round(gifFrameDelaySlider.Value, 1);
                gifFrameDelayValueText.Text = $"{seconds} seconds";
                
                // Convert to milliseconds for storage
                Properties.Settings.Default.GifFrameDelay = (int)(seconds * 1000);
                Properties.Settings.Default.Save();
            }
        }

        private void EnableGifOverlay_Changed(object sender, RoutedEventArgs e)
        {
            if (enableGifOverlayCheckBox != null)
            {
                Properties.Settings.Default.EnableGifOverlay = enableGifOverlayCheckBox.IsChecked ?? false;
                Properties.Settings.Default.Save();
            }
        }

        private void GifOverlayPath_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (gifOverlayPathTextBox != null && !string.IsNullOrEmpty(gifOverlayPathTextBox.Text))
            {
                Properties.Settings.Default.GifOverlayPath = gifOverlayPathTextBox.Text;
                Properties.Settings.Default.Save();
            }
        }

        private void BrowseGifOverlay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Overlay Image",
                    Filter = "Image Files (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All Files (*.*)|*.*",
                    CheckFileExists = true
                };

                if (dialog.ShowDialog() == true)
                {
                    gifOverlayPathTextBox.Text = dialog.FileName;
                    Properties.Settings.Default.GifOverlayPath = dialog.FileName;
                    Properties.Settings.Default.Save();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error selecting overlay image: {ex.Message}", "Browse Overlay", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GifQualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (gifQualitySlider != null && gifQualityValueText != null)
            {
                int quality = (int)gifQualitySlider.Value;
                gifQualityValueText.Text = $"{quality}%";
                
                Properties.Settings.Default.GifQuality = quality;
                Properties.Settings.Default.Save();
            }
        }

        private void GifMaxWidth_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (gifMaxWidthTextBox != null && int.TryParse(gifMaxWidthTextBox.Text, out int width))
            {
                if (width > 0 && width <= 4096)
                {
                    Properties.Settings.Default.GifMaxWidth = width;
                    Properties.Settings.Default.Save();
                }
            }
        }

        private void GifMaxHeight_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (gifMaxHeightTextBox != null && int.TryParse(gifMaxHeightTextBox.Text, out int height))
            {
                if (height > 0 && height <= 4096)
                {
                    Properties.Settings.Default.GifMaxHeight = height;
                    Properties.Settings.Default.Save();
                }
            }
        }

        #endregion
        
        #region Security Settings
        
        private void EnableLockCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            pinSettingsPanel.Visibility = Visibility.Visible;
        }
        
        private void EnableLockCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            pinSettingsPanel.Visibility = Visibility.Collapsed;
        }
        
        private void CurrentPinBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            // Verify current PIN
            string currentPin = Properties.Settings.Default.LockPin;
            bool isValid = currentPinBox.Password == currentPin;
            
            if (isValid)
            {
                newPinBox.IsEnabled = true;
                confirmPinBox.IsEnabled = true;
                pinStatusText.Text = "✓ Current PIN verified";
                pinStatusText.Foreground = new SolidColorBrush(Colors.LightGreen);
            }
            else if (currentPinBox.Password.Length > 0)
            {
                newPinBox.IsEnabled = false;
                confirmPinBox.IsEnabled = false;
                changePinButton.IsEnabled = false;
                pinStatusText.Text = "✗ Incorrect PIN";
                pinStatusText.Foreground = new SolidColorBrush(Colors.LightCoral);
            }
            else
            {
                newPinBox.IsEnabled = false;
                confirmPinBox.IsEnabled = false;
                changePinButton.IsEnabled = false;
                pinStatusText.Text = "";
            }
        }
        
        private void NewPinBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            string newPin = newPinBox.Password;
            
            // Validate PIN (4-6 digits)
            if (newPin.Length >= 4 && newPin.Length <= 6 && newPin.All(char.IsDigit))
            {
                pinStrengthText.Text = "✓ Valid";
                pinStrengthText.Foreground = new SolidColorBrush(Colors.LightGreen);
                ValidatePinMatch();
            }
            else if (newPin.Length > 0)
            {
                if (newPin.Length < 4)
                {
                    pinStrengthText.Text = "Too short";
                    pinStrengthText.Foreground = new SolidColorBrush(Colors.Orange);
                }
                else if (!newPin.All(char.IsDigit))
                {
                    pinStrengthText.Text = "Digits only";
                    pinStrengthText.Foreground = new SolidColorBrush(Colors.Orange);
                }
                changePinButton.IsEnabled = false;
            }
            else
            {
                pinStrengthText.Text = "";
                changePinButton.IsEnabled = false;
            }
        }
        
        private void ConfirmPinBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            ValidatePinMatch();
        }
        
        private void ValidatePinMatch()
        {
            string newPin = newPinBox.Password;
            string confirmPin = confirmPinBox.Password;
            
            if (confirmPin.Length > 0)
            {
                if (newPin == confirmPin && newPin.Length >= 4 && newPin.Length <= 6 && newPin.All(char.IsDigit))
                {
                    pinMatchText.Text = "✓ Match";
                    pinMatchText.Foreground = new SolidColorBrush(Colors.LightGreen);
                    changePinButton.IsEnabled = true;
                }
                else
                {
                    pinMatchText.Text = "✗ No match";
                    pinMatchText.Foreground = new SolidColorBrush(Colors.LightCoral);
                    changePinButton.IsEnabled = false;
                }
            }
            else
            {
                pinMatchText.Text = "";
                changePinButton.IsEnabled = false;
            }
        }
        
        private void ChangePinButton_Click(object sender, RoutedEventArgs e)
        {
            string newPin = newPinBox.Password;
            
            // Save new PIN
            Properties.Settings.Default.LockPin = newPin;
            Properties.Settings.Default.Save();
            
            // Clear fields
            currentPinBox.Clear();
            newPinBox.Clear();
            confirmPinBox.Clear();
            newPinBox.IsEnabled = false;
            confirmPinBox.IsEnabled = false;
            changePinButton.IsEnabled = false;
            
            // Show success message
            pinStatusText.Text = "✓ PIN changed successfully!";
            pinStatusText.Foreground = new SolidColorBrush(Colors.LightGreen);
            pinStrengthText.Text = "";
            pinMatchText.Text = "";
        }
        
        private void AutoLockTimeoutSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateAutoLockTimeoutText();
        }
        
        private void UpdateAutoLockTimeoutText()
        {
            if (autoLockTimeoutText != null)
            {
                int minutes = (int)autoLockTimeoutSlider.Value;
                if (minutes == 0)
                {
                    autoLockTimeoutText.Text = "Disabled";
                }
                else if (minutes == 1)
                {
                    autoLockTimeoutText.Text = "1 minute";
                }
                else
                {
                    autoLockTimeoutText.Text = $"{minutes} minutes";
                }
            }
        }
        
        #endregion

        #region Cloud Settings Event Handlers

        private void EnableCloudSharing_Changed(object sender, RoutedEventArgs e)
        {
            if (cloudSettingsPanel != null)
            {
                cloudSettingsPanel.IsEnabled = enableCloudSharingCheckBox.IsChecked == true;
            }
        }

        private void EnableSms_Changed(object sender, RoutedEventArgs e)
        {
            if (smsSettingsPanel != null)
            {
                smsSettingsPanel.IsEnabled = enableSmsCheckBox.IsChecked == true;
            }
        }

        private async void TestTwilio_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate phone number
                if (string.IsNullOrWhiteSpace(testPhoneNumberBox.Text))
                {
                    MessageBox.Show("Please enter a phone number to send the test SMS to.", 
                        "Phone Number Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                testTwilioButton.IsEnabled = false;
                testTwilioButton.Content = "📤 Sending...";

                // Save Twilio credentials temporarily - use User level to match SendSMSAsync
                Environment.SetEnvironmentVariable("TWILIO_ACCOUNT_SID", twilioSidBox.Text, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("TWILIO_AUTH_TOKEN", twilioAuthTokenBox.Password, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("TWILIO_PHONE_NUMBER", twilioPhoneBox.Text, EnvironmentVariableTarget.User);

                // Reset the provider to reload with new credentials
                Services.CloudShareProvider.Reset();
                
                // Debug logging
                System.Diagnostics.Debug.WriteLine($"Test SMS: Account SID = {twilioSidBox.Text}");
                System.Diagnostics.Debug.WriteLine($"Test SMS: From Number = {twilioPhoneBox.Text}");
                System.Diagnostics.Debug.WriteLine($"Test SMS: To Number = {testPhoneNumberBox.Text}");
                
                // Test SMS
                var shareService = Services.CloudShareProvider.GetShareService();
                var testMessage = $"🎉 Photobooth Test SMS\n\nThis is a test message from your photobooth app.\nTime: {DateTime.Now:g}\n\nTwilio integration is working!";
                
                bool sent = await shareService.SendSMSAsync(testPhoneNumberBox.Text, testMessage);

                if (sent)
                {
                    MessageBox.Show($"✅ Test SMS sent successfully to {testPhoneNumberBox.Text}!\n\nCheck your phone for the message.", 
                        "SMS Test Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("⚠️ SMS test failed.\n\nPlease check:\n• Account SID is correct\n• Auth Token is correct\n• Phone number format (+1234567890)\n• Twilio account has SMS credits", 
                        "SMS Test Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ SMS test error:\n\n{ex.Message}", 
                    "SMS Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                testTwilioButton.IsEnabled = true;
                testTwilioButton.Content = "📱 Test SMS";
            }
        }

        private async void TestCloudConnection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                testCloudConnectionButton.IsEnabled = false;
                testCloudConnectionButton.Content = "⏳ Testing...";

                // Save credentials to environment variables temporarily for testing
                Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", awsAccessKeyBox.Text);
                Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", awsSecretKeyBox.Password);
                Environment.SetEnvironmentVariable("S3_BUCKET_NAME", s3BucketNameBox.Text);
                Environment.SetEnvironmentVariable("GALLERY_BASE_URL", galleryBaseUrlBox.Text);
                
                if (enableSmsCheckBox.IsChecked == true)
                {
                    Environment.SetEnvironmentVariable("TWILIO_ACCOUNT_SID", twilioSidBox.Text);
                    Environment.SetEnvironmentVariable("TWILIO_AUTH_TOKEN", twilioAuthTokenBox.Password);
                    Environment.SetEnvironmentVariable("TWILIO_PHONE_NUMBER", twilioPhoneBox.Text);
                }

                // Reset the provider to force reload with new credentials
                Services.CloudShareProvider.Reset();
                System.Diagnostics.Debug.WriteLine("TestCloudConnection: Reset CloudShareProvider before testing");

                // Test connection
                var shareService = Services.CloudShareProvider.GetShareService();
                
                // Try to create a test session
                var testResult = await shareService.CreateShareableGalleryAsync(
                    "test-" + Guid.NewGuid().ToString().Substring(0, 8),
                    new List<string>(),
                    "test-event");

                if (testResult != null)
                {
                    MessageBox.Show("✅ Cloud connection successful!\n\nYour AWS credentials are valid.", 
                        "Connection Test", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("⚠️ Connection test failed.\n\nPlease check your credentials.", 
                        "Connection Test", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Connection test failed:\n\n{ex.Message}", 
                    "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                testCloudConnectionButton.IsEnabled = true;
                testCloudConnectionButton.Content = "☁️ Test AWS Connection";
            }
        }

        private void SaveCloudSettings()
        {
            // Save cloud settings to app settings or registry
            try
            {
                // You could save to Properties.Settings.Default or registry
                // For now, we'll use environment variables
                if (enableCloudSharingCheckBox.IsChecked == true)
                {
                    Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", awsAccessKeyBox.Text, EnvironmentVariableTarget.User);
                    Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", awsSecretKeyBox.Password, EnvironmentVariableTarget.User);
                    Environment.SetEnvironmentVariable("S3_BUCKET_NAME", s3BucketNameBox.Text, EnvironmentVariableTarget.User);
                    Environment.SetEnvironmentVariable("GALLERY_BASE_URL", galleryBaseUrlBox.Text, EnvironmentVariableTarget.User);
                    
                    if (enableSmsCheckBox.IsChecked == true)
                    {
                        Environment.SetEnvironmentVariable("TWILIO_ACCOUNT_SID", twilioSidBox.Text, EnvironmentVariableTarget.User);
                        Environment.SetEnvironmentVariable("TWILIO_AUTH_TOKEN", twilioAuthTokenBox.Password, EnvironmentVariableTarget.User);
                        Environment.SetEnvironmentVariable("TWILIO_PHONE_NUMBER", twilioPhoneBox.Text, EnvironmentVariableTarget.User);
                    }
                    
                    Environment.SetEnvironmentVariable("CLOUD_AUTO_SHARE", autoShareCheckBox.IsChecked.ToString(), EnvironmentVariableTarget.User);
                    Environment.SetEnvironmentVariable("CLOUD_OPTIMIZE_PHOTOS", optimizePhotosCheckBox.IsChecked.ToString(), EnvironmentVariableTarget.User);
                    Environment.SetEnvironmentVariable("CLOUD_CLEANUP_AFTER", cleanupAfterShareCheckBox.IsChecked.ToString(), EnvironmentVariableTarget.User);
                }
                
                Environment.SetEnvironmentVariable("CLOUD_SHARING_ENABLED", enableCloudSharingCheckBox.IsChecked.ToString(), EnvironmentVariableTarget.User);
                
                // Reset the cloud share provider to force reload with new settings
                Services.CloudShareProvider.Reset();
                System.Diagnostics.Debug.WriteLine("SaveCloudSettings: Reset CloudShareProvider to reload with new settings");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving cloud settings: {ex.Message}");
            }
        }

        private void LoadCloudSettings()
        {
            try
            {
                // Load cloud settings from environment variables
                var cloudEnabled = Environment.GetEnvironmentVariable("CLOUD_SHARING_ENABLED", EnvironmentVariableTarget.User);
                enableCloudSharingCheckBox.IsChecked = cloudEnabled == "True";
                
                awsAccessKeyBox.Text = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID", EnvironmentVariableTarget.User) ?? "";
                awsSecretKeyBox.Password = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", EnvironmentVariableTarget.User) ?? "";
                s3BucketNameBox.Text = Environment.GetEnvironmentVariable("S3_BUCKET_NAME", EnvironmentVariableTarget.User) ?? "photobooth-shares";
                galleryBaseUrlBox.Text = Environment.GetEnvironmentVariable("GALLERY_BASE_URL", EnvironmentVariableTarget.User) ?? "https://photos.yourapp.com";
                
                twilioSidBox.Text = Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID", EnvironmentVariableTarget.User) ?? "";
                twilioAuthTokenBox.Password = Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN", EnvironmentVariableTarget.User) ?? "";
                twilioPhoneBox.Text = Environment.GetEnvironmentVariable("TWILIO_PHONE_NUMBER", EnvironmentVariableTarget.User) ?? "";
                
                enableSmsCheckBox.IsChecked = !string.IsNullOrEmpty(twilioSidBox.Text);
                
                autoShareCheckBox.IsChecked = Environment.GetEnvironmentVariable("CLOUD_AUTO_SHARE", EnvironmentVariableTarget.User) == "True";
                optimizePhotosCheckBox.IsChecked = Environment.GetEnvironmentVariable("CLOUD_OPTIMIZE_PHOTOS", EnvironmentVariableTarget.User) != "False";
                cleanupAfterShareCheckBox.IsChecked = Environment.GetEnvironmentVariable("CLOUD_CLEANUP_AFTER", EnvironmentVariableTarget.User) == "True";
                
                // Update UI state
                EnableCloudSharing_Changed(null, null);
                EnableSms_Changed(null, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading cloud settings: {ex.Message}");
            }
        }

        #endregion
        
        #region Video & Boomerang Module Event Handlers
        
        private void EnableVideoModule_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var modulesConfig = PhotoboothModulesConfig.Instance;
                modulesConfig.VideoEnabled = enableVideoModuleCheckBox.IsChecked == true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating video module setting: {ex.Message}");
            }
        }
        
        private void ShowVideoButton_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var modulesConfig = PhotoboothModulesConfig.Instance;
                modulesConfig.ShowVideoButton = showVideoButtonCheckBox.IsChecked == true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating video button visibility: {ex.Message}");
            }
        }
        
        private void VideoDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (videoDurationValueText != null && videoDurationSlider != null)
                {
                    int seconds = (int)videoDurationSlider.Value;
                    
                    // Update display text
                    if (seconds >= 60)
                    {
                        int minutes = seconds / 60;
                        int remainingSeconds = seconds % 60;
                        if (remainingSeconds > 0)
                        {
                            videoDurationValueText.Text = $"{minutes}m {remainingSeconds}s";
                        }
                        else
                        {
                            videoDurationValueText.Text = $"{minutes} minute{(minutes > 1 ? "s" : "")}";
                        }
                    }
                    else
                    {
                        videoDurationValueText.Text = $"{seconds} seconds";
                    }
                    
                    // Save to config
                    var modulesConfig = PhotoboothModulesConfig.Instance;
                    modulesConfig.VideoDuration = seconds;
                    
                    // Force save to settings
                    Properties.Settings.Default.VideoDuration = seconds;
                    Properties.Settings.Default.Save();
                    
                    System.Diagnostics.Debug.WriteLine($"[SETTINGS] Video duration updated to {seconds} seconds");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating video duration: {ex.Message}");
            }
        }
        
        private void EnableBoomerangModule_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var modulesConfig = PhotoboothModulesConfig.Instance;
                modulesConfig.BoomerangEnabled = enableBoomerangModuleCheckBox.IsChecked == true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating boomerang module setting: {ex.Message}");
            }
        }
        
        private void ShowBoomerangButton_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var modulesConfig = PhotoboothModulesConfig.Instance;
                modulesConfig.ShowBoomerangButton = showBoomerangButtonCheckBox.IsChecked == true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating boomerang button visibility: {ex.Message}");
            }
        }
        
        private void BoomerangFramesSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (boomerangFramesValueText != null)
                {
                    int frames = (int)boomerangFramesSlider.Value;
                    boomerangFramesValueText.Text = $"{frames} frames";
                    
                    var modulesConfig = PhotoboothModulesConfig.Instance;
                    modulesConfig.BoomerangFrames = frames;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating boomerang frames: {ex.Message}");
            }
        }
        
        private void BoomerangSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (boomerangSpeedValueText != null)
                {
                    int speed = (int)boomerangSpeedSlider.Value;
                    boomerangSpeedValueText.Text = $"{speed} ms";
                    
                    var modulesConfig = PhotoboothModulesConfig.Instance;
                    modulesConfig.BoomerangSpeed = speed;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating boomerang speed: {ex.Message}");
            }
        }
        
        private void EnableFlipbookModule_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var modulesConfig = PhotoboothModulesConfig.Instance;
                modulesConfig.FlipbookEnabled = enableFlipbookModuleCheckBox.IsChecked == true;
                Properties.Settings.Default.FlipbookEnabled = enableFlipbookModuleCheckBox.IsChecked == true;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating flipbook module setting: {ex.Message}");
            }
        }
        
        private void ShowFlipbookButton_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var modulesConfig = PhotoboothModulesConfig.Instance;
                modulesConfig.ShowFlipbookButton = showFlipbookButtonCheckBox.IsChecked == true;
                Properties.Settings.Default.ShowFlipbookButton = showFlipbookButtonCheckBox.IsChecked == true;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating flipbook button visibility: {ex.Message}");
            }
        }
        
        private void FlipbookDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (flipbookDurationValueText != null)
                {
                    int duration = (int)flipbookDurationSlider.Value;
                    flipbookDurationValueText.Text = $"{duration} seconds";
                    
                    var modulesConfig = PhotoboothModulesConfig.Instance;
                    modulesConfig.FlipbookDuration = duration;
                    Properties.Settings.Default.FlipbookDuration = duration;
                    Properties.Settings.Default.Save();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating flipbook duration: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Settings Service Event Handlers
        
        /// <summary>
        /// Handle setting changes from the overlay service to keep UI in sync
        /// </summary>
        private void OnSettingChangedFromService(object sender, SettingChangedEventArgs e)
        {
            try
            {
                // Refresh the UI to reflect changes made in overlays
                Dispatcher.Invoke(() => LoadSettings());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error syncing setting change from service: {ex.Message}");
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
                Dispatcher.Invoke(() => LoadSettings());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error syncing settings reset from service: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Debug Logging Settings
        
        /// <summary>
        /// Load debug logging settings and initialize UI
        /// </summary>
        private void LoadDebugLoggingSettings()
        {
            try
            {
                // Set the toggle state based on current debug service state
                if (DebugLoggingToggle != null)
                {
                    DebugLoggingToggle.IsChecked = DebugService.Instance.IsDebugEnabled;
                }
                
                // Update the status display
                UpdateDebugStatus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading debug settings: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Debug Settings Event Handlers
        
        /// <summary>
        /// Handle debug logging toggle checked event
        /// </summary>
        private void DebugLoggingToggle_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                DebugService.Instance.IsDebugEnabled = true;
                UpdateDebugStatus();
                DebugService.LogDebug("Debug logging enabled via Settings Control");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error enabling debug logging: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handle debug logging toggle unchecked event
        /// </summary>
        private void DebugLoggingToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                DebugService.LogDebug("Debug logging disabled via Settings Control");
                DebugService.Instance.IsDebugEnabled = false;
                UpdateDebugStatus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disabling debug logging: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Update debug status display
        /// </summary>
        private void UpdateDebugStatus()
        {
            try
            {
                bool isEnabled = DebugService.Instance.IsDebugEnabled;
                DebugStatusText.Text = isEnabled 
                    ? "Debug logging is currently enabled. Messages appear in Debug Output window."
                    : "Debug logging is currently disabled.";
                DebugStatusText.Foreground = new SolidColorBrush(isEnabled 
                    ? Color.FromRgb(76, 175, 80)    // Green when enabled
                    : Color.FromRgb(176, 176, 176)); // Gray when disabled
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating debug status: {ex.Message}");
            }
        }

        private void DefaultCaptureModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Save the selected default capture mode
            if (DefaultCaptureModeCombo?.SelectedItem is ComboBoxItem selectedItem)
            {
                string modeName = selectedItem.Content.ToString();
                Properties.Settings.Default.DefaultCaptureMode = modeName;
                Properties.Settings.Default.Save();
                
                // Update the service
                var service = Photobooth.Services.CaptureModesService.Instance;
                if (Enum.TryParse<Photobooth.Services.CaptureMode>(modeName, out var mode))
                {
                    service.CurrentMode = mode;
                }
            }
        }
        
        #endregion
    }
}