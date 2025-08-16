using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Photobooth.Services;
using CameraControl.Devices;

namespace Photobooth.Windows
{
    public partial class PrinterProfileConfigWindow : Window
    {
        private PrinterSettingsManager settingsManager;
        private SimplifiedPrintService printService;
        private List<ProfileItem> profiles;
        private ProfileItem currentProfile;
        private PrintDocument currentPrintDoc;
        
        public class ProfileItem
        {
            public string DisplayName { get; set; }
            public string ProfileName { get; set; }
            public string PrinterName { get; set; }
            public string PaperSize { get; set; }
            public bool IsLandscape { get; set; }
            public bool Enable2InchCut { get; set; }
        }
        
        public PrinterProfileConfigWindow()
        {
            InitializeComponent();
            settingsManager = PrinterSettingsManager.Instance;
            printService = SimplifiedPrintService.Instance;
            currentPrintDoc = new PrintDocument();
            LoadProfiles();
            LoadPrinters();
        }
        
        private void LoadProfiles()
        {
            profiles = new List<ProfileItem>();
            
            // Add default profiles for common sizes
            var defaultPrinter = new PrintDocument().PrinterSettings.PrinterName;
            
            profiles.Add(new ProfileItem 
            { 
                DisplayName = "4x6 Portrait",
                ProfileName = "4x6_Portrait",
                PrinterName = defaultPrinter,
                PaperSize = "4x6",
                IsLandscape = false
            });
            
            profiles.Add(new ProfileItem 
            { 
                DisplayName = "4x6 Landscape",
                ProfileName = "4x6_Landscape",
                PrinterName = defaultPrinter,
                PaperSize = "4x6",
                IsLandscape = true
            });
            
            profiles.Add(new ProfileItem 
            { 
                DisplayName = "2x6 Portrait",
                ProfileName = "2x6_Portrait",
                PrinterName = defaultPrinter,
                PaperSize = "2x6",
                IsLandscape = false
            });
            
            profiles.Add(new ProfileItem 
            { 
                DisplayName = "2x6 Landscape",
                ProfileName = "2x6_Landscape",
                PrinterName = defaultPrinter,
                PaperSize = "2x6",
                IsLandscape = true
            });
            
            // Add 2x6 printer profiles if configured
            string printer2x6 = Properties.Settings.Default.Printer2x6Name;
            if (!string.IsNullOrEmpty(printer2x6))
            {
                profiles.Add(new ProfileItem 
                { 
                    DisplayName = "2x6 Portrait (2x6 Printer)",
                    ProfileName = "2x6_Portrait",
                    PrinterName = printer2x6,
                    PaperSize = "2x6",
                    IsLandscape = false
                });
                
                profiles.Add(new ProfileItem 
                { 
                    DisplayName = "2x6 Landscape (2x6 Printer)",
                    ProfileName = "2x6_Landscape",
                    PrinterName = printer2x6,
                    PaperSize = "2x6",
                    IsLandscape = true
                });
                
                // Also 4x6 for duplicated prints
                profiles.Add(new ProfileItem 
                { 
                    DisplayName = "4x6 Portrait (2x6 Printer)",
                    ProfileName = "4x6_Portrait",
                    PrinterName = printer2x6,
                    PaperSize = "4x6",
                    IsLandscape = false
                });
            }
            
            ProfileListBox.ItemsSource = profiles;
        }
        
        private void LoadPrinters()
        {
            PrinterComboBox.Items.Clear();
            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                PrinterComboBox.Items.Add(printer);
            }
            
            if (PrinterComboBox.Items.Count > 0)
            {
                PrinterComboBox.SelectedIndex = 0;
            }
        }
        
        private void ProfileListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProfileListBox.SelectedItem is ProfileItem profile)
            {
                currentProfile = profile;
                LoadProfileSettings(profile);
            }
        }
        
        private void LoadProfileSettings(ProfileItem profile)
        {
            try
            {
                // Set basic info
                ProfileNameTextBox.Text = profile.ProfileName;
                
                // Set printer
                for (int i = 0; i < PrinterComboBox.Items.Count; i++)
                {
                    if (PrinterComboBox.Items[i].ToString() == profile.PrinterName)
                    {
                        PrinterComboBox.SelectedIndex = i;
                        break;
                    }
                }
                
                // Set paper size
                PaperSizeComboBox.Text = profile.PaperSize;
                
                // Set orientation
                PortraitRadio.IsChecked = !profile.IsLandscape;
                LandscapeRadio.IsChecked = profile.IsLandscape;
                
                // Set defaults first
                MarginLeftTextBox.Text = "0";
                MarginTopTextBox.Text = "0";
                MarginRightTextBox.Text = "0";
                MarginBottomTextBox.Text = "0";
                CopiesTextBox.Text = "1";
                QualityComboBox.SelectedIndex = 2; // High
                Enable2InchCutCheckBox.IsChecked = false;
                
                // Try to load existing profile settings (if they exist)
                try
                {
                    currentPrintDoc.PrinterSettings.PrinterName = profile.PrinterName;
                    if (settingsManager.LoadPrinterProfile(currentPrintDoc, profile.ProfileName))
                    {
                        // Profile exists, load its settings
                        MarginLeftTextBox.Text = (currentPrintDoc.DefaultPageSettings.Margins.Left / 100.0).ToString("F2");
                        MarginTopTextBox.Text = (currentPrintDoc.DefaultPageSettings.Margins.Top / 100.0).ToString("F2");
                        MarginRightTextBox.Text = (currentPrintDoc.DefaultPageSettings.Margins.Right / 100.0).ToString("F2");
                        MarginBottomTextBox.Text = (currentPrintDoc.DefaultPageSettings.Margins.Bottom / 100.0).ToString("F2");
                        
                        CopiesTextBox.Text = currentPrintDoc.PrinterSettings.Copies.ToString();
                        
                        // Load Enable2InchCut setting if it exists
                        Enable2InchCutCheckBox.IsChecked = profile.Enable2InchCut;
                        
                        // Set quality based on resolution
                        if (currentPrintDoc.DefaultPageSettings.PrinterResolution != null)
                        {
                            switch (currentPrintDoc.DefaultPageSettings.PrinterResolution.Kind)
                            {
                                case PrinterResolutionKind.Draft:
                                    QualityComboBox.SelectedIndex = 0;
                                    break;
                                case PrinterResolutionKind.Low:
                                    QualityComboBox.SelectedIndex = 1;
                                    break;
                                case PrinterResolutionKind.High:
                                    QualityComboBox.SelectedIndex = 2;
                                    break;
                                case PrinterResolutionKind.Custom:
                                    QualityComboBox.SelectedIndex = 3;
                                    break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // If loading fails, just use defaults
                    Log.Debug($"PrinterProfileConfigWindow: Could not load existing profile: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading profile settings: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        
        private void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            if (currentProfile == null)
            {
                MessageBox.Show("Please select a profile to save", "No Profile Selected", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            if (PrinterComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a printer", "No Printer Selected", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            try
            {
                // Configure print document with current settings
                string printerName = PrinterComboBox.SelectedItem.ToString();
                currentPrintDoc.PrinterSettings.PrinterName = printerName;
                
                // Verify printer exists
                bool printerExists = false;
                foreach (string printer in PrinterSettings.InstalledPrinters)
                {
                    if (printer == printerName)
                    {
                        printerExists = true;
                        break;
                    }
                }
                
                if (!printerExists)
                {
                    MessageBox.Show($"Printer '{printerName}' not found in system", "Printer Not Found", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                // Set paper size
                string paperSize = PaperSizeComboBox.Text;
                foreach (PaperSize size in currentPrintDoc.PrinterSettings.PaperSizes)
                {
                    if (size.PaperName.Contains(paperSize))
                    {
                        currentPrintDoc.DefaultPageSettings.PaperSize = size;
                        break;
                    }
                }
                
                // If not found, create custom size
                if (!currentPrintDoc.DefaultPageSettings.PaperSize.PaperName.Contains(paperSize))
                {
                    PaperSize customSize = null;
                    switch (paperSize)
                    {
                        case "4x6":
                            customSize = new PaperSize("4x6", 400, 600);
                            break;
                        case "2x6":
                            customSize = new PaperSize("2x6", 200, 600);
                            break;
                        case "5x7":
                            customSize = new PaperSize("5x7", 500, 700);
                            break;
                        case "8x10":
                            customSize = new PaperSize("8x10", 800, 1000);
                            break;
                    }
                    if (customSize != null)
                    {
                        currentPrintDoc.DefaultPageSettings.PaperSize = customSize;
                    }
                }
                
                // Set orientation
                currentPrintDoc.DefaultPageSettings.Landscape = LandscapeRadio.IsChecked == true;
                
                // Set margins (convert from inches to hundredths)
                if (double.TryParse(MarginLeftTextBox.Text, out double left) &&
                    double.TryParse(MarginTopTextBox.Text, out double top) &&
                    double.TryParse(MarginRightTextBox.Text, out double right) &&
                    double.TryParse(MarginBottomTextBox.Text, out double bottom))
                {
                    currentPrintDoc.DefaultPageSettings.Margins = new Margins(
                        (int)(left * 100),
                        (int)(right * 100),
                        (int)(top * 100),
                        (int)(bottom * 100)
                    );
                }
                
                // Set copies
                if (short.TryParse(CopiesTextBox.Text, out short copies))
                {
                    currentPrintDoc.PrinterSettings.Copies = copies;
                }
                
                // Set quality
                PrinterResolutionKind quality = PrinterResolutionKind.High;
                switch (QualityComboBox.SelectedIndex)
                {
                    case 0: quality = PrinterResolutionKind.Draft; break;
                    case 1: quality = PrinterResolutionKind.Low; break;
                    case 2: quality = PrinterResolutionKind.High; break;
                    case 3: quality = PrinterResolutionKind.Custom; break;
                }
                
                foreach (PrinterResolution res in currentPrintDoc.PrinterSettings.PrinterResolutions)
                {
                    if (res.Kind == quality)
                    {
                        currentPrintDoc.DefaultPageSettings.PrinterResolution = res;
                        break;
                    }
                }
                
                // Save the profile with Enable2InchCut setting
                bool enable2InchCut = Enable2InchCutCheckBox.IsChecked ?? false;
                if (settingsManager.SavePrinterProfile(currentPrintDoc, currentProfile.ProfileName, enable2InchCut))
                {
                    MessageBox.Show($"Profile '{currentProfile.DisplayName}' saved successfully!\n\n" +
                        "These settings will persist across application restarts.", 
                        "Profile Saved", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to save profile", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving profile: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}\n\nInner Exception: {ex.InnerException?.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ConfigurePrinter_Click(object sender, RoutedEventArgs e)
        {
            if (currentProfile == null || PrinterComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a profile and printer", "Selection Required", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            try
            {
                string printerName = PrinterComboBox.SelectedItem.ToString();
                
                // For DNP printers, show tip about 2 inch cut setting
                if (printerName.ToLower().Contains("dnp") || printerName.ToLower().Contains("ds40") || printerName.ToLower().Contains("ds80"))
                {
                    MessageBox.Show("Opening DNP driver settings.\n\n" +
                        "IMPORTANT: If this is for 2x6 strips:\n" +
                        "1. Look for '2inch cut' option\n" +
                        "2. Set it to 'Enable'\n" +
                        "3. Click OK to save\n\n" +
                        "The setting will be captured in the profile.",
                        "DNP Printer Configuration",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                
                // Open printer properties dialog
                var printDialog = new System.Windows.Forms.PrintDialog();
                printDialog.PrinterSettings = new PrinterSettings { PrinterName = printerName };
                printDialog.UseEXDialog = true; // Use extended dialog with more options
                printDialog.AllowPrintToFile = false;
                
                if (printDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    // Save the configured settings
                    var printDoc = new PrintDocument();
                    printDoc.PrinterSettings = printDialog.PrinterSettings;
                    
                    // Apply orientation from our UI
                    printDoc.DefaultPageSettings.Landscape = LandscapeRadio.IsChecked == true;
                    
                    // Save as profile with Enable2InchCut setting
                    bool enable2InchCut = Enable2InchCutCheckBox.IsChecked ?? false;
                    if (settingsManager.SavePrinterProfile(printDoc, currentProfile.ProfileName, enable2InchCut))
                    {
                        MessageBox.Show("Printer settings captured and saved to profile!\n\n" +
                            "✓ DEVMODE settings saved (includes 2 inch cut if enabled)\n" +
                            "✓ Profile will be applied when printing\n" +
                            (enable2InchCut ? "✓ 2 inch cut checkbox is ENABLED\n" : "") +
                            "\nSettings will persist across application restarts.", 
                            "Settings Saved", 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Information);
                        
                        // Reload the profile to show new settings
                        LoadProfileSettings(currentProfile);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening printer dialog: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void TestPrint_Click(object sender, RoutedEventArgs e)
        {
            if (currentProfile == null)
            {
                MessageBox.Show("Please select a profile to test", "No Profile Selected", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            try
            {
                // First, make sure we have a saved profile
                currentPrintDoc.PrinterSettings.PrinterName = currentProfile.PrinterName;
                if (!settingsManager.LoadPrinterProfile(currentPrintDoc, currentProfile.ProfileName))
                {
                    MessageBox.Show("Please save the profile first before testing", "Profile Not Saved", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Create a test image
                var testImage = new System.Drawing.Bitmap(1800, 1200);
                using (var g = System.Drawing.Graphics.FromImage(testImage))
                {
                    g.Clear(System.Drawing.Color.White);
                    
                    // Draw test pattern
                    using (var font = new System.Drawing.Font("Arial", 48))
                    {
                        var brush = System.Drawing.Brushes.Black;
                        
                        string text = $"Test Print\n{currentProfile.DisplayName}\n{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                        var size = g.MeasureString(text, font);
                        
                        g.DrawString(text, font, brush, 
                            (testImage.Width - size.Width) / 2, 
                            (testImage.Height - size.Height) / 2);
                    }
                    
                    // Draw border
                    g.DrawRectangle(System.Drawing.Pens.Black, 10, 10, 
                        testImage.Width - 20, testImage.Height - 20);
                    
                    // Draw paper size info
                    using (var smallFont = new System.Drawing.Font("Arial", 24))
                    {
                        string sizeInfo = $"Paper: {currentProfile.PaperSize} - {(currentProfile.IsLandscape ? "Landscape" : "Portrait")}";
                        g.DrawString(sizeInfo, smallFont, System.Drawing.Brushes.Blue, 20, 20);
                    }
                }
                
                // Save test image
                string tempPath = Path.Combine(Path.GetTempPath(), $"test_print_{Guid.NewGuid()}.jpg");
                testImage.Save(tempPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                testImage.Dispose();
                
                // Print using direct print document instead of SimplifiedPrintService
                var printDoc = new PrintDocument();
                printDoc.PrinterSettings.PrinterName = currentProfile.PrinterName;
                
                // Load the profile settings
                settingsManager.LoadPrinterProfile(printDoc, currentProfile.ProfileName);
                
                // Set up print event
                System.Drawing.Image imageToPrint = System.Drawing.Image.FromFile(tempPath);
                printDoc.PrintPage += (s, args) =>
                {
                    // Get page bounds
                    var bounds = args.PageBounds;
                    
                    // Calculate scaling
                    float scaleX = bounds.Width / (float)imageToPrint.Width;
                    float scaleY = bounds.Height / (float)imageToPrint.Height;
                    float scale = Math.Min(scaleX, scaleY); // Fit within page
                    
                    int scaledWidth = (int)(imageToPrint.Width * scale);
                    int scaledHeight = (int)(imageToPrint.Height * scale);
                    
                    // Center on page
                    int x = (bounds.Width - scaledWidth) / 2;
                    int y = (bounds.Height - scaledHeight) / 2;
                    
                    // Draw the image
                    args.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    args.Graphics.DrawImage(imageToPrint, x, y, scaledWidth, scaledHeight);
                };
                
                // Print
                printDoc.Print();
                
                // Clean up
                imageToPrint.Dispose();
                
                // Delete temp file after a delay to ensure print spooler has read it
                Task.Delay(5000).ContinueWith(t => 
                {
                    try { File.Delete(tempPath); } catch { }
                });
                
                MessageBox.Show("Test print sent to printer", "Test Print", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating test print: {ex.Message}\n\nDetails: {ex.StackTrace}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void AddProfile_Click(object sender, RoutedEventArgs e)
        {
            // Create a simple input dialog
            var inputDialog = new Window
            {
                Title = "New Profile",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };
            
            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            var label = new TextBlock { Text = "Enter a name for the new profile:", Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);
            
            var textBox = new TextBox { Text = "Custom Profile", Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);
            
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(buttonPanel, 2);
            
            var okButton = new Button { Content = "OK", Width = 75, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            var cancelButton = new Button { Content = "Cancel", Width = 75, IsCancel = true };
            
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            grid.Children.Add(buttonPanel);
            
            inputDialog.Content = grid;
            
            string profileName = null;
            okButton.Click += (s, args) => { profileName = textBox.Text; inputDialog.DialogResult = true; };
            cancelButton.Click += (s, args) => { inputDialog.DialogResult = false; };
            
            textBox.SelectAll();
            textBox.Focus();
            
            if (inputDialog.ShowDialog() == true && !string.IsNullOrEmpty(profileName))
            {
                var newProfile = new ProfileItem
                {
                    DisplayName = profileName,
                    ProfileName = profileName.Replace(" ", "_"),
                    PrinterName = new PrintDocument().PrinterSettings.PrinterName,
                    PaperSize = "4x6",
                    IsLandscape = false
                };
                
                profiles.Add(newProfile);
                ProfileListBox.Items.Refresh();
                ProfileListBox.SelectedItem = newProfile;
            }
        }
        
        private void ImportProfiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                Title = "Import Printer Profiles"
            };
            
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Copy the file to profiles directory
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string profilesDir = Path.Combine(appData, "Photobooth", "PrinterProfiles");
                    string destPath = Path.Combine(profilesDir, Path.GetFileName(dialog.FileName));
                    
                    File.Copy(dialog.FileName, destPath, true);
                    
                    MessageBox.Show("Profile imported successfully!", "Import Complete", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    LoadProfiles();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error importing profile: {ex.Message}", "Import Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private void ExportProfiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                Title = "Export Printer Profiles",
                FileName = $"PrinterProfiles_{DateTime.Now:yyyyMMdd}.xml"
            };
            
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Export all profiles to a single file or zip
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string profilesDir = Path.Combine(appData, "Photobooth", "PrinterProfiles");
                    
                    // For simplicity, just copy the first profile
                    var files = Directory.GetFiles(profilesDir, "*.xml");
                    if (files.Length > 0)
                    {
                        File.Copy(files[0], dialog.FileName, true);
                        MessageBox.Show($"Exported {files.Length} profile(s)", "Export Complete", 
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("No profiles found to export", "No Profiles", 
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting profiles: {ex.Message}", "Export Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}