using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Photobooth.Services;

namespace Photobooth
{
    public partial class PrinterAlignmentDialog : Window
    {
        private PrintService printService;
        
        public PrinterAlignmentDialog()
        {
            InitializeComponent();
            printService = PrintService.Instance;
            LoadCurrentSettings();
            UpdatePrinterNames();
        }
        
        private void LoadCurrentSettings()
        {
            // Load Default Printer settings - use ScaleX as the unified scale (for backwards compatibility)
            DefaultScaleSlider.Value = Properties.Settings.Default.DefaultPrinterScaleX;
            DefaultXSlider.Value = Properties.Settings.Default.DefaultPrinterOffsetX;
            DefaultYSlider.Value = Properties.Settings.Default.DefaultPrinterOffsetY;
            
            // Load 2x6 Printer settings - use ScaleX as the unified scale
            Strip2x6ScaleSlider.Value = Properties.Settings.Default.Printer2x6ScaleX;
            Strip2x6XSlider.Value = Properties.Settings.Default.Printer2x6OffsetX;
            Strip2x6YSlider.Value = Properties.Settings.Default.Printer2x6OffsetY;
            
            UpdateDisplayValues();
        }
        
        private void UpdatePrinterNames()
        {
            // Display selected printer names
            string defaultPrinter = Properties.Settings.Default.Printer4x6Name;
            if (string.IsNullOrEmpty(defaultPrinter))
                defaultPrinter = Properties.Settings.Default.PrinterName;
            
            DefaultPrinterName.Text = string.IsNullOrEmpty(defaultPrinter) ? "Not Selected" : defaultPrinter;
            
            string strip2x6Printer = Properties.Settings.Default.Printer2x6Name;
            Printer2x6Name.Text = string.IsNullOrEmpty(strip2x6Printer) ? "Not Selected" : strip2x6Printer;
        }
        
        private void UpdateDisplayValues()
        {
            // Update Default Printer display values
            DefaultScaleValue.Text = $"{(DefaultScaleSlider.Value * 100):F0}%";
            DefaultXValue.Text = $"{(int)DefaultXSlider.Value} px";
            DefaultYValue.Text = $"{(int)DefaultYSlider.Value} px";
            
            // Update 2x6 Printer display values (for 4x6 print)
            Strip2x6ScaleValue.Text = $"{(Strip2x6ScaleSlider.Value * 100):F0}%";
            Strip2x6XValue.Text = $"{(int)Strip2x6XSlider.Value} px";
            Strip2x6YValue.Text = $"{(int)Strip2x6YSlider.Value} px";
            
            // Update visual previews
            UpdateDefaultPreview();
            UpdateStrip2x6Preview();
        }
        
        private void UpdateDefaultPreview()
        {
            if (DefaultImagePreview == null) return;
            
            // Base dimensions for 4x6 portrait
            double baseWidth = 180;
            double baseHeight = 270;
            double baseLeft = 10;
            double baseTop = 15;
            
            // Apply uniform scale adjustments (maintains aspect ratio)
            double scale = DefaultScaleSlider.Value;
            double newWidth = baseWidth * scale;
            double newHeight = baseHeight * scale;
            
            // Apply position adjustments (scaled to preview size)
            double offsetXScaled = DefaultXSlider.Value * 0.5; // Scale down for preview
            double offsetYScaled = DefaultYSlider.Value * 0.5;
            
            // Calculate new position to keep centered with offsets
            double newLeft = baseLeft + (baseWidth - newWidth) / 2 + offsetXScaled;
            double newTop = baseTop + (baseHeight - newHeight) / 2 + offsetYScaled;
            
            // Update the preview
            DefaultImagePreview.Width = newWidth;
            DefaultImagePreview.Height = newHeight;
            Canvas.SetLeft(DefaultImagePreview, newLeft);
            Canvas.SetTop(DefaultImagePreview, newTop);
        }
        
        private void UpdateStrip2x6Preview()
        {
            if (Strip2x6ImagePreview == null) return;
            
            // Base dimensions for 4x6 portrait (with 2 strips stacked) - SAME as default 4x6
            double baseWidth = 180;
            double baseHeight = 270;
            double baseLeft = 10;
            double baseTop = 15;
            
            // Apply uniform scale adjustments (maintains aspect ratio)
            double scale = Strip2x6ScaleSlider.Value;
            double newWidth = baseWidth * scale;
            double newHeight = baseHeight * scale;
            
            // Apply position adjustments (scaled to preview size)
            double offsetXScaled = Strip2x6XSlider.Value * 0.5; // Scale down for preview
            double offsetYScaled = Strip2x6YSlider.Value * 0.5;
            
            // Calculate new position to keep centered with offsets
            double newLeft = baseLeft + (baseWidth - newWidth) / 2 + offsetXScaled;
            double newTop = baseTop + (baseHeight - newHeight) / 2 + offsetYScaled;
            
            // Update the preview
            Strip2x6ImagePreview.Width = newWidth;
            Strip2x6ImagePreview.Height = newHeight;
            Canvas.SetLeft(Strip2x6ImagePreview, newLeft);
            Canvas.SetTop(Strip2x6ImagePreview, newTop);
        }
        
        #region Default Printer Controls
        
        private void DefaultScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DefaultScaleValue != null)
                DefaultScaleValue.Text = $"{(e.NewValue * 100):F0}%";
            UpdateDefaultPreview();
        }
        
        private void DefaultXSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DefaultXValue != null)
                DefaultXValue.Text = $"{(int)e.NewValue} px";
            UpdateDefaultPreview();
        }
        
        private void DefaultYSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DefaultYValue != null)
                DefaultYValue.Text = $"{(int)e.NewValue} px";
            UpdateDefaultPreview();
        }
        
        private void DefaultScaleMinus_Click(object sender, RoutedEventArgs e)
        {
            DefaultScaleSlider.Value = Math.Max(DefaultScaleSlider.Minimum, DefaultScaleSlider.Value - 0.01);
        }
        
        private void DefaultScalePlus_Click(object sender, RoutedEventArgs e)
        {
            DefaultScaleSlider.Value = Math.Min(DefaultScaleSlider.Maximum, DefaultScaleSlider.Value + 0.01);
        }
        
        private void DefaultXMinus_Click(object sender, RoutedEventArgs e)
        {
            DefaultXSlider.Value = Math.Max(DefaultXSlider.Minimum, DefaultXSlider.Value - 1);
        }
        
        private void DefaultXPlus_Click(object sender, RoutedEventArgs e)
        {
            DefaultXSlider.Value = Math.Min(DefaultXSlider.Maximum, DefaultXSlider.Value + 1);
        }
        
        private void DefaultYMinus_Click(object sender, RoutedEventArgs e)
        {
            DefaultYSlider.Value = Math.Max(DefaultYSlider.Minimum, DefaultYSlider.Value - 1);
        }
        
        private void DefaultYPlus_Click(object sender, RoutedEventArgs e)
        {
            DefaultYSlider.Value = Math.Min(DefaultYSlider.Maximum, DefaultYSlider.Value + 1);
        }
        
        #endregion
        
        #region 2x6 Printer Controls
        
        private void Strip2x6ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Strip2x6ScaleValue != null)
                Strip2x6ScaleValue.Text = $"{(e.NewValue * 100):F0}%";
            UpdateStrip2x6Preview();
        }
        
        private void Strip2x6XSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Strip2x6XValue != null)
                Strip2x6XValue.Text = $"{(int)e.NewValue} px";
            UpdateStrip2x6Preview();
        }
        
        private void Strip2x6YSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Strip2x6YValue != null)
                Strip2x6YValue.Text = $"{(int)e.NewValue} px";
            UpdateStrip2x6Preview();
        }
        
        private void Strip2x6ScaleMinus_Click(object sender, RoutedEventArgs e)
        {
            Strip2x6ScaleSlider.Value = Math.Max(Strip2x6ScaleSlider.Minimum, Strip2x6ScaleSlider.Value - 0.01);
        }
        
        private void Strip2x6ScalePlus_Click(object sender, RoutedEventArgs e)
        {
            Strip2x6ScaleSlider.Value = Math.Min(Strip2x6ScaleSlider.Maximum, Strip2x6ScaleSlider.Value + 0.01);
        }
        
        private void Strip2x6XMinus_Click(object sender, RoutedEventArgs e)
        {
            Strip2x6XSlider.Value = Math.Max(Strip2x6XSlider.Minimum, Strip2x6XSlider.Value - 1);
        }
        
        private void Strip2x6XPlus_Click(object sender, RoutedEventArgs e)
        {
            Strip2x6XSlider.Value = Math.Min(Strip2x6XSlider.Maximum, Strip2x6XSlider.Value + 1);
        }
        
        private void Strip2x6YMinus_Click(object sender, RoutedEventArgs e)
        {
            Strip2x6YSlider.Value = Math.Max(Strip2x6YSlider.Minimum, Strip2x6YSlider.Value - 1);
        }
        
        private void Strip2x6YPlus_Click(object sender, RoutedEventArgs e)
        {
            Strip2x6YSlider.Value = Math.Min(Strip2x6YSlider.Maximum, Strip2x6YSlider.Value + 1);
        }
        
        #endregion
        
        #region Test Print Functions
        
        private void TestPrintDefault_Click(object sender, RoutedEventArgs e)
        {
            // Create a test 4x6 image with alignment grid
            string testImagePath = CreateTestImage(false);
            
            // Apply current alignment settings temporarily
            var originalScaleX = Properties.Settings.Default.DefaultPrinterScaleX;
            var originalScaleY = Properties.Settings.Default.DefaultPrinterScaleY;
            var originalOffsetX = Properties.Settings.Default.DefaultPrinterOffsetX;
            var originalOffsetY = Properties.Settings.Default.DefaultPrinterOffsetY;
            
            try
            {
                // Apply test settings (use scale for both X and Y to maintain aspect ratio)
                Properties.Settings.Default.DefaultPrinterScaleX = DefaultScaleSlider.Value;
                Properties.Settings.Default.DefaultPrinterScaleY = DefaultScaleSlider.Value;
                Properties.Settings.Default.DefaultPrinterOffsetX = (int)DefaultXSlider.Value;
                Properties.Settings.Default.DefaultPrinterOffsetY = (int)DefaultYSlider.Value;
                
                // Print test image
                var result = printService.PrintPhotos(new System.Collections.Generic.List<string> { testImagePath }, "alignment_test", 1, false);
                
                if (result.Success)
                {
                    MessageBox.Show("Test print sent successfully!", "Test Print", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Test print failed: {result.Message}", "Test Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                // Restore original settings
                Properties.Settings.Default.DefaultPrinterScaleX = originalScaleX;
                Properties.Settings.Default.DefaultPrinterScaleY = originalScaleY;
                Properties.Settings.Default.DefaultPrinterOffsetX = originalOffsetX;
                Properties.Settings.Default.DefaultPrinterOffsetY = originalOffsetY;
                
                // Clean up test image
                if (File.Exists(testImagePath))
                    File.Delete(testImagePath);
            }
        }
        
        private void TestPrint2x6_Click(object sender, RoutedEventArgs e)
        {
            // Create a test 4x6 image (duplicated 2x6) with alignment grid
            // Remember: 2x6 prints are actually 4x6 with two strips side by side
            string testImagePath = CreateTestImage(true);
            
            // Apply current alignment settings temporarily
            var originalScaleX = Properties.Settings.Default.Printer2x6ScaleX;
            var originalScaleY = Properties.Settings.Default.Printer2x6ScaleY;
            var originalOffsetX = Properties.Settings.Default.Printer2x6OffsetX;
            var originalOffsetY = Properties.Settings.Default.Printer2x6OffsetY;
            
            try
            {
                // Apply test settings (use scale for both X and Y to maintain aspect ratio)
                Properties.Settings.Default.Printer2x6ScaleX = Strip2x6ScaleSlider.Value;
                Properties.Settings.Default.Printer2x6ScaleY = Strip2x6ScaleSlider.Value;
                Properties.Settings.Default.Printer2x6OffsetX = (int)Strip2x6XSlider.Value;
                Properties.Settings.Default.Printer2x6OffsetY = (int)Strip2x6YSlider.Value;
                
                // Print test image as 2x6 (which will be printed as 4x6)
                var result = printService.PrintPhotos(new System.Collections.Generic.List<string> { testImagePath }, "alignment_test_2x6", 1, true);
                
                if (result.Success)
                {
                    MessageBox.Show("Test print sent successfully! (4x6 with 2 strips)", "Test Print", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Test print failed: {result.Message}", "Test Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                // Restore original settings
                Properties.Settings.Default.Printer2x6ScaleX = originalScaleX;
                Properties.Settings.Default.Printer2x6ScaleY = originalScaleY;
                Properties.Settings.Default.Printer2x6OffsetX = originalOffsetX;
                Properties.Settings.Default.Printer2x6OffsetY = originalOffsetY;
                
                // Clean up test image
                if (File.Exists(testImagePath))
                    File.Delete(testImagePath);
            }
        }
        
        private string CreateTestImage(bool is2x6Format)
        {
            // Create a test image with alignment grid and markers
            int width, height;
            string filename;
            
            if (is2x6Format)
            {
                // Create a 4x6 portrait image (1200x1800 at 300 DPI) with two 2x6 strips side by side - SAME orientation as regular 4x6
                width = 1200;  // 4 inches at 300 DPI
                height = 1800; // 6 inches at 300 DPI
                filename = "test_alignment_4x6_strips.jpg";
            }
            else
            {
                // Create a standard 4x6 portrait image (1200x1800 at 300 DPI)
                width = 1200;  // 4 inches at 300 DPI
                height = 1800; // 6 inches at 300 DPI
                filename = "test_alignment_4x6.jpg";
            }
            
            using (var bitmap = new Bitmap(width, height))
            {
                bitmap.SetResolution(300, 300);
                
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    
                    // Draw grid lines
                    using (var pen = new Pen(Color.LightGray, 1))
                    {
                        // Vertical lines every 0.5 inch (150 pixels)
                        for (int x = 0; x <= width; x += 150)
                        {
                            graphics.DrawLine(pen, x, 0, x, height);
                        }
                        
                        // Horizontal lines every 0.5 inch (150 pixels)
                        for (int y = 0; y <= height; y += 150)
                        {
                            graphics.DrawLine(pen, 0, y, width, y);
                        }
                    }
                    
                    // Draw border with margin indicators
                    using (var borderPen = new Pen(Color.Red, 3))
                    {
                        graphics.DrawRectangle(borderPen, 10, 10, width - 20, height - 20);
                    }
                    
                    // Draw center lines
                    using (var centerPen = new Pen(Color.Blue, 2))
                    {
                        graphics.DrawLine(centerPen, width / 2, 0, width / 2, height);
                        graphics.DrawLine(centerPen, 0, height / 2, width, height / 2);
                    }
                    
                    // Add text labels
                    using (var font = new Font(new FontFamily("Arial"), 24, System.Drawing.FontStyle.Bold))
                    using (var brush = new SolidBrush(Color.Black))
                    {
                        string label = is2x6Format ? "4x6 STRIPS TEST" : "4x6 TEST";
                        var textSize = graphics.MeasureString(label, font);
                        graphics.DrawString(label, font, brush, (width - textSize.Width) / 2, 50);
                        
                        // Add dimensions text
                        string dimensions = $"{width}x{height} px @ 300 DPI";
                        graphics.DrawString(dimensions, font, brush, (width - graphics.MeasureString(dimensions, font).Width) / 2, height - 100);
                        
                        if (is2x6Format)
                        {
                            // Draw strip separation line with cut indicators (horizontal line for portrait orientation)
                            using (var stripPen = new Pen(Color.Green, 4))
                            {
                                // For portrait 4x6, draw horizontal line to show two strips stacked
                                graphics.DrawLine(stripPen, 0, height / 2, width, height / 2);
                            }
                            
                            // Draw cut line indicators (dashed red lines)
                            using (var cutPen = new Pen(Color.Red, 2) { DashStyle = DashStyle.Dash })
                            {
                                graphics.DrawLine(cutPen, 0, height / 2 - 15, width, height / 2 - 15);
                                graphics.DrawLine(cutPen, 0, height / 2 + 15, width, height / 2 + 15);
                            }
                            
                            // Label each strip with cut indicators (top and bottom)
                            graphics.DrawString("STRIP 1", font, brush, width / 2 - 50, height / 4 - 30);
                            graphics.DrawString("(CUT)", font, brush, width / 2 - 25, height / 4);
                            graphics.DrawString("STRIP 2", font, brush, width / 2 - 50, 3 * height / 4 - 30);
                            graphics.DrawString("(CUT)", font, brush, width / 2 - 25, 3 * height / 4);
                            
                            // Add rotation indicator text
                            graphics.DrawString("↕ Same orientation as 4x6 ↕", font, brush, (width - graphics.MeasureString("↕ Same orientation as 4x6 ↕", font).Width) / 2, height - 150);
                        }
                    }
                    
                    // Draw corner markers
                    using (var markerBrush = new SolidBrush(Color.Black))
                    {
                        int markerSize = 20;
                        graphics.FillRectangle(markerBrush, 0, 0, markerSize, markerSize);
                        graphics.FillRectangle(markerBrush, width - markerSize, 0, markerSize, markerSize);
                        graphics.FillRectangle(markerBrush, 0, height - markerSize, markerSize, markerSize);
                        graphics.FillRectangle(markerBrush, width - markerSize, height - markerSize, markerSize, markerSize);
                    }
                }
                
                // Save to temp path
                string tempPath = Path.Combine(Path.GetTempPath(), filename);
                bitmap.Save(tempPath, ImageFormat.Jpeg);
                return tempPath;
            }
        }
        
        #endregion
        
        #region Dialog Controls
        
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Save Default Printer settings (use single scale for both X and Y to maintain aspect ratio)
            Properties.Settings.Default.DefaultPrinterScaleX = DefaultScaleSlider.Value;
            Properties.Settings.Default.DefaultPrinterScaleY = DefaultScaleSlider.Value;
            Properties.Settings.Default.DefaultPrinterOffsetX = (int)DefaultXSlider.Value;
            Properties.Settings.Default.DefaultPrinterOffsetY = (int)DefaultYSlider.Value;
            
            // Save 2x6 Printer settings (use single scale for both X and Y to maintain aspect ratio)
            Properties.Settings.Default.Printer2x6ScaleX = Strip2x6ScaleSlider.Value;
            Properties.Settings.Default.Printer2x6ScaleY = Strip2x6ScaleSlider.Value;
            Properties.Settings.Default.Printer2x6OffsetX = (int)Strip2x6XSlider.Value;
            Properties.Settings.Default.Printer2x6OffsetY = (int)Strip2x6YSlider.Value;
            
            Properties.Settings.Default.Save();
            
            MessageBox.Show("Alignment settings saved successfully!", "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Reset all alignment settings to default values?", "Reset Settings", 
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                // Reset to default values
                DefaultScaleSlider.Value = 1.0;
                DefaultXSlider.Value = 0;
                DefaultYSlider.Value = 0;
                
                Strip2x6ScaleSlider.Value = 1.0;
                Strip2x6XSlider.Value = 0;
                Strip2x6YSlider.Value = 0;
                
                UpdateDisplayValues();
            }
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        #endregion
    }
}