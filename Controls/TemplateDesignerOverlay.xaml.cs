using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Photobooth.Pages;
using Photobooth.Services;
using CameraControl.Devices;

namespace Photobooth.Controls
{
    /// <summary>
    /// Interaction logic for TemplateDesignerOverlay.xaml
    /// </summary>
    public partial class TemplateDesignerOverlay : UserControl
    {
        private MainPage _designerPage;
        public event EventHandler OverlayClosed;
        public event EventHandler TemplateSaved;

        public TemplateDesignerOverlay()
        {
            InitializeComponent();
            
            // Add keyboard handler for ESC key
            this.PreviewKeyDown += OnPreviewKeyDown;
        }
        
        private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                CloseOverlay_Click(null, null);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Show the template designer overlay
        /// </summary>
        public void ShowOverlay(string templatePath = null)
        {
            try
            {
                Log.Debug($"TemplateDesignerOverlay: Showing overlay {(string.IsNullOrEmpty(templatePath) ? "for new template" : $"for template: {templatePath}")}");
                
                // Create or reuse the designer page
                if (_designerPage == null)
                {
                    _designerPage = new MainPage();
                }
                
                // Load template if provided
                if (!string.IsNullOrEmpty(templatePath))
                {
                    // Load the template into the designer
                    _designerPage.ViewModel?.LoadTemplate(templatePath);
                    OverlayTitle.Text = $"Template Designer - {System.IO.Path.GetFileNameWithoutExtension(templatePath)}";
                }
                else
                {
                    // New template
                    _designerPage.ViewModel?.CreateNewTemplate();
                    OverlayTitle.Text = "Template Designer - New Template";
                }
                
                // Set the designer page in the frame
                DesignerFrame.Content = _designerPage;
                
                // Show the overlay
                this.Visibility = Visibility.Visible;
                
                // Animate in
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                MainOverlay.BeginAnimation(OpacityProperty, fadeIn);
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateDesignerOverlay: Failed to show overlay: {ex.Message}", ex);
                MessageBox.Show($"Failed to open template designer: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Hide the template designer overlay
        /// </summary>
        public void HideOverlay()
        {
            try
            {
                Log.Debug("TemplateDesignerOverlay: Hiding overlay");
                
                // Animate out
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (s, e) =>
                {
                    this.Visibility = Visibility.Collapsed;
                    
                    // Clear the frame content
                    DesignerFrame.Content = null;
                    
                    // Raise closed event
                    OverlayClosed?.Invoke(this, EventArgs.Empty);
                };
                MainOverlay.BeginAnimation(OpacityProperty, fadeOut);
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateDesignerOverlay: Failed to hide overlay: {ex.Message}", ex);
                // Fallback - hide immediately
                this.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Close button click handler
        /// </summary>
        private void CloseOverlay_Click(object sender, RoutedEventArgs e)
        {
            // Check if there are unsaved changes
            if (_designerPage?.ViewModel?.HasUnsavedChanges == true)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Do you want to save before closing?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    // Save and then close
                    SaveTemplate_Click(sender, e);
                    HideOverlay();
                }
                else if (result == MessageBoxResult.No)
                {
                    // Close without saving
                    HideOverlay();
                }
                // If Cancel, do nothing
            }
            else
            {
                HideOverlay();
            }
        }

        /// <summary>
        /// Save template button click handler
        /// </summary>
        private void SaveTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_designerPage?.ViewModel == null)
                {
                    MessageBox.Show("No template to save.", "Save Template", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Save the template
                bool saved = _designerPage.ViewModel.SaveTemplate();
                
                if (saved)
                {
                    MessageBox.Show("Template saved successfully!", "Save Template", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Raise saved event
                    TemplateSaved?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateDesignerOverlay: Failed to save template: {ex.Message}", ex);
                MessageBox.Show($"Failed to save template: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Load a template into the designer
        /// </summary>
        public void LoadTemplate(string templatePath)
        {
            ShowOverlay(templatePath);
        }

        /// <summary>
        /// Create a new template in the designer
        /// </summary>
        public void CreateNewTemplate()
        {
            ShowOverlay(null);
        }
    }
}