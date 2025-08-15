using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Photobooth.MVVM.ViewModels.Designer;
using Photobooth.Services;
using Photobooth.Database;

namespace Photobooth.Pages
{
    /// <summary>
    /// Interaction logic for SideNavbar.xaml
    /// </summary>
    public partial class SideNavbar : Page
    {
        private DesignerVM ViewModel => MainPage.Instance?.ViewModel;

        public SideNavbar()
        {
            InitializeComponent();
            Loaded += SideNavbar_Loaded;
            Unloaded += SideNavbar_Unloaded;
        }
        
        private void SideNavbar_Loaded(object sender, RoutedEventArgs e)
        {
            // Always refresh DataContext on load
            DataContext = ViewModel;
            
            // Force command re-evaluation to ensure button is responsive
            CommandManager.InvalidateRequerySuggested();
            
            // Ensure the photobooth button is always enabled
            if (launchPhotoboothButton != null)
            {
                launchPhotoboothButton.IsEnabled = true;
                DebugService.LogDebug($"SideNavbar_Loaded: Photobooth button enabled explicitly");
            }
            
            // Set initial state of debug checkbox
            if (DebugModeCheckBox != null)
            {
                DebugModeCheckBox.IsChecked = DebugService.Instance.IsDebugEnabled;
            }
            
            DebugService.LogDebug($"SideNavbar_Loaded: DataContext set, ViewModel exists: {ViewModel != null}");
        }
        
        private void SideNavbar_Unloaded(object sender, RoutedEventArgs e)
        {
            // Clean up PhotoboothService window reference if needed
            if (PhotoboothService.PhotoboothWindow != null && !PhotoboothService.PhotoboothWindow.IsVisible)
            {
                PhotoboothService.PhotoboothWindow = null;
            }
            DebugService.LogDebug("SideNavbar_Unloaded: Cleanup completed");
        }

        private void ActionAddPlaceholder(object sender, MouseButtonEventArgs e)
        {
            ViewModel?.AddPlaceholderCmd.Execute(null);
        }

        private void ActionAddText(object sender, MouseButtonEventArgs e)
        {
            ViewModel?.AddTextCmd.Execute(null);
        }

        private void ActionImportImage(object sender, MouseButtonEventArgs e)
        {
            ViewModel?.ImportImageCmd.Execute(null);
        }

        private void ActionCaptureImage(object sender, MouseButtonEventArgs e)
        {
            ViewModel?.AddPlaceholderCmd.Execute(null);
        }

        private void comboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // This is now handled by data binding to SelectedRatio property
        }

        private void comboBox1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string selectedValue = (sender as ComboBox).SelectedValue.ToString();
            //split the string to get the ratio
            string ratio = selectedValue.Split(':')[1];
            //set the orientation
            

        }

        private void ActionClearCanvas(object sender, MouseButtonEventArgs e)
        {
            ViewModel?.ClearCanvasCmd.Execute(null);
        }

        private void ActionChangeOrientation(object sender, MouseButtonEventArgs e)
        {
            ViewModel?.ChangeCanvasOrientationCmd.Execute(null);
        }

        private void ActionOpenPhotobooth(object sender, MouseButtonEventArgs e)
        {
            DebugService.LogDebug("ActionOpenPhotobooth clicked");
            
            // Navigate to the PhotoboothTouch page
            try
            {
                // Check if window is already open using shared PhotoboothService window
                if (PhotoboothService.PhotoboothWindow != null)
                {
                    try
                    {
                        if (PhotoboothService.PhotoboothWindow.IsVisible)
                        {
                            // Bring existing window to front
                            PhotoboothService.PhotoboothWindow.Activate();
                            PhotoboothService.PhotoboothWindow.WindowState = WindowState.Maximized;
                            return;
                        }
                    }
                    catch
                    {
                        // Window was closed, clear the reference
                        PhotoboothService.PhotoboothWindow = null;
                    }
                }
                
                // Create a new window for the modern photobooth touch interface
                PhotoboothService.PhotoboothWindow = new Window
                {
                    Title = "Photobooth Touch Interface",
                    WindowState = WindowState.Maximized,
                    WindowStyle = WindowStyle.None, // Fullscreen for touch
                    Content = new PhotoboothTouchModern(),
                    Background = new SolidColorBrush(Colors.Black)
                };
                
                // Clean up reference when window is closed
                PhotoboothService.PhotoboothWindow.Closed += (s, args) => 
                {
                    DebugService.LogDebug("PhotoboothWindow closed, clearing reference and forcing command re-evaluation");
                    PhotoboothService.PhotoboothWindow = null;
                    
                    // Force command re-evaluation on the main thread
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        CommandManager.InvalidateRequerySuggested();
                        
                        // Force refresh the DataContext to re-evaluate bindings
                        if (MainPage.Instance != null)
                        {
                            var temp = MainPage.Instance.DataContext;
                            MainPage.Instance.DataContext = null;
                            MainPage.Instance.DataContext = temp;
                        }
                    }));
                    
                    // Force garbage collection to clean up resources
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                };
                
                PhotoboothService.PhotoboothWindow.Show();
            }
            catch (Exception ex)
            {
                PhotoboothService.PhotoboothWindow = null; // Clear reference on error
                MessageBox.Show($"Failed to open photobooth interface: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void LaunchPhotoboothButton_Click(object sender, RoutedEventArgs e)
        {
            DebugService.LogDebug("LaunchPhotoboothButton_Click called directly");
            DebugService.LogDebug($"Button IsEnabled: {(sender as Button)?.IsEnabled}");
            DebugService.LogDebug($"PhotoboothWindow exists: {PhotoboothService.PhotoboothWindow != null}");
            
            // Clear any stale window reference first
            if (PhotoboothService.PhotoboothWindow != null)
            {
                try
                {
                    if (!PhotoboothService.PhotoboothWindow.IsVisible)
                    {
                        DebugService.LogDebug("Clearing stale window reference");
                        PhotoboothService.PhotoboothWindow = null;
                    }
                }
                catch
                {
                    PhotoboothService.PhotoboothWindow = null;
                }
            }
            
            try
            {
                // Force refresh DataContext if needed
                if (DataContext == null)
                {
                    DataContext = ViewModel;
                    DebugService.LogDebug("LaunchPhotoboothButton_Click: DataContext was null, refreshed");
                }
                
                // Try to get the ViewModel
                var viewModel = DataContext as MVVM.ViewModels.Designer.DesignerVM ?? ViewModel;
                if (viewModel != null)
                {
                    DebugService.LogDebug($"ViewModel found, SelectedEvent: {viewModel.SelectedEvent?.Name ?? "null"}");
                    
                    if (viewModel.SelectedEvent != null)
                    {
                        // Launch via PhotoboothService
                        var photoboothService = new Services.PhotoboothService();
                        await photoboothService.LaunchPhotoboothAsync(viewModel.SelectedEvent.Id);
                        
                        // Force command re-evaluation after launch
                        CommandManager.InvalidateRequerySuggested();
                    }
                    else
                    {
                        MessageBox.Show("Please select an event first to launch the photobooth.", 
                            "No Event Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    DebugService.LogDebug("ViewModel not found, opening photobooth directly");
                    
                    // Fallback - open photobooth directly without event
                    ActionOpenPhotobooth(sender, null);
                }
            }
            catch (Exception ex)
            {
                DebugService.LogDebug($"LaunchPhotoboothButton_Click failed: {ex.Message}");
                MessageBox.Show($"Failed to launch photobooth: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Always force re-enable the button
                if (sender is Button button)
                {
                    button.IsEnabled = true;
                    DebugService.LogDebug("Button re-enabled in finally block");
                }
            }
        }
        
        private void DebugModeCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            DebugService.Instance.IsDebugEnabled = true;
        }

        private void DebugModeCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            DebugService.Instance.IsDebugEnabled = false;
        }

        private void EventTemplate_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Get the clicked template from the Tag property
                var border = sender as Border;
                var template = border?.Tag as TemplateData;
                
                if (template != null && ViewModel != null)
                {
                    // Set the selected event template
                    ViewModel.SelectedEventTemplate = template;
                    
                    // Load the template into the canvas
                    ViewModel.LoadEventTemplateCmd.Execute(null);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load event template: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
