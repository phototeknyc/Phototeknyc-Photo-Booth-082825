using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;

namespace PhotoboothWPF.Pages
{
    /// <summary>
    /// Interaction logic for CameraSetting.xaml
    /// </summary>
    public partial class CameraSetting : Page
    {
       
        public CameraSetting()
        {
            InitializeComponent();
            string[] modeOptions = { "M" };
            string[] isoOptions = { "200" };
            string[] shutterSpeedOptions = { "1/80" };
            string[] apertureOptions = { "10.0" };
            string[] whiteBalanceOptions = { "Manual" };
            string[] exposureCompOptions = { "0.0" };
            string[] compressionOptions = { "Large Fine JPEG" };
            string[] meteringModeOptions = { "Evaluative metering" };
            string[] focusModeOptions = { "One-Shot AF" };
            string[] driveModeOptions = { "Single-Frame Shooting" };
            string[] flashCompensationOptions = { "Bracketing" };
            string[] aeBracketingOptions = { };

            Border generatedModeXaml = List.GenerateBorderWithStackPanelAndComboBox("Mode", modeOptions, HandleModeItemChange);
            Border generatedIsoXaml = List.GenerateBorderWithStackPanelAndComboBox("ISO", isoOptions, HandleISOItemChange);
            Border generatedShutterSpeedXaml = List.GenerateBorderWithStackPanelAndComboBox("Shutter Speed", shutterSpeedOptions, HandleShutterSpeedItemChange);
            Border generatedApertureXaml = List.GenerateBorderWithStackPanelAndComboBox("Aperture", apertureOptions, HandleApertureItemChange);
            Border generatedWhiteBalanceXaml = List.GenerateBorderWithStackPanelAndComboBox("White Balance", whiteBalanceOptions, HandleWhiteBalanceItemChange);
            Border generatedExposureCompXaml = List.GenerateBorderWithStackPanelAndComboBox("Exposure Comp.", exposureCompOptions, HandleExposureCompItemChange);
            Border generatedCompressionXaml = List.GenerateBorderWithStackPanelAndComboBox("Compression", compressionOptions, HandleCompressionItemChange);
            Border generatedMeteringModeXaml = List.GenerateBorderWithStackPanelAndComboBox("Metering Mode", meteringModeOptions, HandleMeteringModeItemChange);
            Border generatedFocusModeXaml = List.GenerateBorderWithStackPanelAndComboBox("Focus Mode", focusModeOptions, HandleFocusModeItemChange);
            Border generatedDriveModeXaml = List.GenerateBorderWithStackPanelAndComboBox("Drive Mode", driveModeOptions, HandleDriveModeItemChange);
            Border generatedFlashCompensationXaml = List.GenerateBorderWithStackPanelAndComboBox("Flash Compensation", flashCompensationOptions, HandleFlashCompensationItemChange);
            Border generatedAeBracketingXaml = List.GenerateBorderWithStackPanelAndComboBox("AE Bracketing", aeBracketingOptions, HandleAeBracketingItemChange);

            cameraSettingPanel.Children.Add(generatedModeXaml);
            cameraSettingPanel.Children.Add(generatedIsoXaml);
            cameraSettingPanel.Children.Add(generatedShutterSpeedXaml);
            cameraSettingPanel.Children.Add(generatedApertureXaml);
            cameraSettingPanel.Children.Add(generatedWhiteBalanceXaml);
            cameraSettingPanel.Children.Add(generatedExposureCompXaml);
            cameraSettingPanel.Children.Add(generatedCompressionXaml);
            cameraSettingPanel.Children.Add(generatedMeteringModeXaml);
            cameraSettingPanel.Children.Add(generatedFocusModeXaml);
            cameraSettingPanel.Children.Add(generatedDriveModeXaml);
            cameraSettingPanel.Children.Add(generatedFlashCompensationXaml);
            cameraSettingPanel.Children.Add(generatedAeBracketingXaml);
        }

        private void HandleModeItemChange(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem comboBoxItem = (ComboBoxItem)e.AddedItems[0];
            string selectedMode = (string)comboBoxItem.Content;
            MessageBox.Show($"Selected Mode: {selectedMode}");
        }

        private void HandleISOItemChange(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem comboBoxItem = (ComboBoxItem)e.AddedItems[0];
            string selectedISO = (string)comboBoxItem.Content;
            MessageBox.Show($"Selected ISO: {selectedISO}");
        }

        private void HandleShutterSpeedItemChange(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem comboBoxItem = (ComboBoxItem)e.AddedItems[0];
            string selectedShutterSpeed = (string)comboBoxItem.Content;
            MessageBox.Show($"Selected Shutter Speed: {selectedShutterSpeed}");
        }

        private void HandleApertureItemChange(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem comboBoxItem = (ComboBoxItem)e.AddedItems[0];
            string selectedAperture = (string)comboBoxItem.Content;
            MessageBox.Show($"Selected Aperture: {selectedAperture}");
        }

        private void HandleWhiteBalanceItemChange(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem comboBoxItem = (ComboBoxItem)e.AddedItems[0];
            string selectedWhiteBalance = (string)comboBoxItem.Content;
            MessageBox.Show($"Selected White Balance: {selectedWhiteBalance}");
        }

        private void HandleExposureCompItemChange(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem comboBoxItem = (ComboBoxItem)e.AddedItems[0];
            string selectedExposureComp = (string)comboBoxItem.Content;
            MessageBox.Show($"Selected Exposure Compensation: {selectedExposureComp}");
        }

        private void HandleCompressionItemChange(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem comboBoxItem = (ComboBoxItem)e.AddedItems[0];
            string selectedCompression = (string)comboBoxItem.Content;
            MessageBox.Show($"Selected Compression: {selectedCompression}");
        }

        private void HandleMeteringModeItemChange(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem comboBoxItem = (ComboBoxItem)e.AddedItems[0];
            string selectedMeteringMode = (string)comboBoxItem.Content;
            MessageBox.Show($"Selected Metering Mode: {selectedMeteringMode}");
        }

        private void HandleFocusModeItemChange(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem comboBoxItem = (ComboBoxItem)e.AddedItems[0];
            string selectedFocusMode = (string)comboBoxItem.Content;
            MessageBox.Show($"Selected Focus Mode: {selectedFocusMode}");
        }

        private void HandleDriveModeItemChange(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem comboBoxItem = (ComboBoxItem)e.AddedItems[0];
            string selectedDriveMode = (string)comboBoxItem.Content;
            MessageBox.Show($"Selected Drive Mode: {selectedDriveMode}");
        }

        private void HandleFlashCompensationItemChange(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem comboBoxItem = (ComboBoxItem)e.AddedItems[0];
            string selectedFlashCompensation = (string)comboBoxItem.Content;
            MessageBox.Show($"Selected Flash Compensation: {selectedFlashCompensation}");
        }

        private void HandleAeBracketingItemChange(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem comboBoxItem = (ComboBoxItem)e.AddedItems[0];
            string selectedAeBracketing = (string)comboBoxItem.Content;
            MessageBox.Show($"Selected AE Bracketing: {selectedAeBracketing}");
        }
    }
    }
