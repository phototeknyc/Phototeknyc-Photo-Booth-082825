using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Photobooth.Database;
using Photobooth.Windows;

namespace Photobooth.Services
{
    public class PhotoboothService
    {
        private readonly TemplateDatabase database;
        private readonly EventService eventService;
        
        public PhotoboothService()
        {
            database = new TemplateDatabase();
            eventService = new EventService();
        }
        
        /// <summary>
        /// Launch photobooth using existing touch interface with event workflow
        /// </summary>
        public async Task<bool> LaunchPhotoboothAsync(int eventId)
        {
            DebugService.LogDebug($"PhotoboothService.LaunchPhotoboothAsync called with eventId: {eventId}");
            
            try
            {
                // Get event details
                var eventData = database.GetEvent(eventId);
                if (eventData == null)
                {
                    DebugService.LogDebug("Event not found in database");
                    MessageBox.Show("Event not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                
                DebugService.LogDebug($"Found event: {eventData.Name}");
                
                // Get event templates
                var templates = eventService.GetEventTemplates(eventId);
                if (!templates.Any())
                {
                    DebugService.LogDebug($"No templates found for event {eventData.Name}");
                    MessageBox.Show($"No templates assigned to event '{eventData.Name}'. Please assign at least one template.", 
                        "No Templates", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                
                DebugService.LogDebug($"Found {templates.Count} templates for event");
                
                // Don't pre-select template anymore - let PhotoboothTouch handle it
                // Store event data for PhotoboothTouch to use
                CurrentEvent = eventData;
                CurrentTemplate = null; // Let PhotoboothTouch handle template selection
                
                // Navigate to photobooth touch interface
                DebugService.LogDebug("Navigating to photobooth window");
                NavigateToPhotobooth();
                
                DebugService.LogDebug("LaunchPhotoboothAsync completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                DebugService.LogDebug($"LaunchPhotoboothAsync failed with exception: {ex.Message}");
                MessageBox.Show($"Failed to launch photobooth: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        
        // Static properties to share event and template data with PhotoboothTouch
        public static EventData CurrentEvent { get; set; }
        public static TemplateData CurrentTemplate { get; set; }
        
        // Make this public so it can be shared with Sidebar
        public static Window PhotoboothWindow { get; set; } = null;
        
        private void NavigateToPhotobooth()
        {
            try
            {
                DebugService.LogDebug($"NavigateToPhotobooth: Checking existing window - PhotoboothWindow is null: {PhotoboothWindow == null}");
                
                // Check if window is already open
                if (PhotoboothWindow != null)
                {
                    try
                    {
                        if (PhotoboothWindow.IsVisible)
                        {
                            DebugService.LogDebug("NavigateToPhotobooth: Window exists and is visible, bringing to front");
                            // Bring existing window to front
                            PhotoboothWindow.Activate();
                            PhotoboothWindow.WindowState = WindowState.Maximized;
                            return;
                        }
                        else
                        {
                            DebugService.LogDebug("NavigateToPhotobooth: Window exists but not visible, clearing reference");
                            PhotoboothWindow = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugService.LogDebug($"NavigateToPhotobooth: Error checking window state: {ex.Message}");
                        // Window was closed, clear the reference
                        PhotoboothWindow = null;
                    }
                }
                
                DebugService.LogDebug("NavigateToPhotobooth: Creating new window");
                
                // Create a new window for the photobooth touch interface
                PhotoboothWindow = new Window
                {
                    Title = "Photobooth Session",
                    WindowState = WindowState.Maximized,
                    WindowStyle = WindowStyle.None, // Fullscreen for touch
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black)
                };
                
                DebugService.LogDebug("NavigateToPhotobooth: Creating PhotoboothTouch page");
                
                // Create the PhotoboothTouch page with event and template data
                var photoboothPage = new Pages.PhotoboothTouch();
                PhotoboothWindow.Content = photoboothPage;
                
                // Clean up reference when window is closed
                PhotoboothWindow.Closed += (s, args) => 
                {
                    DebugService.LogDebug("NavigateToPhotobooth: Window closed event fired, clearing reference and forcing command re-evaluation");
                    PhotoboothWindow = null;
                    
                    // Force command re-evaluation on the main thread
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                        
                        // Force refresh the DataContext to re-evaluate bindings
                        if (Pages.MainPage.Instance != null)
                        {
                            var temp = Pages.MainPage.Instance.DataContext;
                            Pages.MainPage.Instance.DataContext = null;
                            Pages.MainPage.Instance.DataContext = temp;
                        }
                    }));
                };
                
                DebugService.LogDebug("NavigateToPhotobooth: Showing window");
                
                // Show as a non-modal window
                PhotoboothWindow.Show();
                
                DebugService.LogDebug("NavigateToPhotobooth: Window shown successfully");
            }
            catch (Exception ex)
            {
                DebugService.LogDebug($"NavigateToPhotobooth: Exception occurred: {ex.Message}");
                PhotoboothWindow = null; // Clear reference on error
                MessageBox.Show($"Failed to open photobooth interface: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// Get photo count from template by counting placeholder canvas items
        /// </summary>
        public int GetTemplatePhotoCount(TemplateData template)
        {
            try
            {
                // Get canvas items from database
                var canvasItems = database.GetCanvasItems(template.Id);
                
                // Count placeholder items (these are for photos)
                var photoPlaceholders = canvasItems.Where(item => 
                    string.Equals(item.ItemType, "Placeholder", StringComparison.OrdinalIgnoreCase)).ToList();
                
                return Math.Max(1, photoPlaceholders.Count); // At least 1 photo
            }
            catch
            {
                return 1; // Default fallback
            }
        }
        
        /// <summary>
        /// Get placeholder positions and sizes for photo insertion
        /// </summary>
        public List<PhotoPlaceholder> GetPhotoPlaceholders(TemplateData template)
        {
            var placeholders = new List<PhotoPlaceholder>();
            
            try
            {
                // Get canvas items from database
                var canvasItems = database.GetCanvasItems(template.Id);
                
                var photoItems = canvasItems.Where(item => 
                    string.Equals(item.ItemType, "Placeholder", StringComparison.OrdinalIgnoreCase)).ToList();
                
                for (int i = 0; i < photoItems.Count; i++)
                {
                    var item = photoItems[i];
                    placeholders.Add(new PhotoPlaceholder
                    {
                        Index = i,
                        X = item.X,
                        Y = item.Y,
                        Width = item.Width,
                        Height = item.Height,
                        MaintainAspectRatio = item.LockedAspectRatio
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing placeholders: {ex.Message}");
            }
            
            return placeholders;
        }
    }
    
    /// <summary>
    /// Represents a photo placeholder in the template
    /// </summary>
    public class PhotoPlaceholder
    {
        public int Index { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool MaintainAspectRatio { get; set; }
    }
}