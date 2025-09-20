using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Photobooth.Controls
{
    public partial class TouchTemplateDesignerOverlay : UserControl
    {
        public event EventHandler CloseRequested;

        public TouchTemplateDesignerOverlay()
        {
            InitializeComponent();
            
            // Make the overlay responsive to screen size changes
            SizeChanged += OnSizeChanged;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Adapt to initial screen size
            AdaptToScreenSize();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Adapt when screen size changes
            AdaptToScreenSize();
        }

        private void AdaptToScreenSize()
        {
            // The TouchTemplateDesigner will handle its own responsive layout
            // This is just for any overlay-specific adjustments if needed
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            
            // Remove from parent
            if (Parent is Panel parentPanel)
            {
                parentPanel.Children.Remove(this);
            }
            else if (Parent is ContentControl parentControl)
            {
                parentControl.Content = null;
            }
        }

        public void ShowFullScreen()
        {
            // Find the main window or parent grid
            var window = Window.GetWindow(this);
            if (window != null)
            {
                // Add to the window's main grid/panel
                if (window.Content is Panel mainPanel)
                {
                    mainPanel.Children.Add(this);
                }
                else if (window.Content is ContentControl contentControl)
                {
                    var grid = new Grid();
                    var originalContent = window.Content;
                    grid.Children.Add(originalContent as UIElement);
                    grid.Children.Add(this);
                    window.Content = grid;
                }
            }
        }

        public void LoadTemplate(int templateId)
        {
            // Pass the template ID to the TouchTemplateDesigner
            if (TouchDesigner != null)
            {
                TouchDesigner.LoadTemplate(templateId);
            }
        }
    }
}