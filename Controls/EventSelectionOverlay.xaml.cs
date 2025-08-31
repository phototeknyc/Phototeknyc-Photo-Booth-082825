using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Photobooth.Database;
using Photobooth.Models;
using Photobooth.Services;
using CameraControl.Devices;

namespace Photobooth.Controls
{
    /// <summary>
    /// Interaction logic for EventSelectionOverlay.xaml
    /// </summary>
    public partial class EventSelectionOverlay : UserControl
    {
        private readonly EventSelectionService _eventSelectionService;
        
        public event EventHandler<EventData> EventSelected;
        public event EventHandler SelectionCancelled;
        
        public EventSelectionOverlay()
        {
            InitializeComponent();
            _eventSelectionService = EventSelectionService.Instance;
            
            // Subscribe to service events
            _eventSelectionService.PropertyChanged += OnServicePropertyChanged;
            _eventSelectionService.EventSelected += OnServiceEventSelected;
            _eventSelectionService.SearchCleared += OnServiceSearchCleared;
        }
        
        /// <summary>
        /// Show the overlay with animation
        /// </summary>
        public void ShowOverlay()
        {
            try
            {
                Log.Debug("EventSelectionOverlay: Showing overlay");
                
                // Make sure the control itself is visible
                this.Visibility = Visibility.Visible;
                
                // Load events
                _eventSelectionService.LoadEvents();
                
                // Bind data to ItemsControl
                EventsListBox.ItemsSource = _eventSelectionService.FilteredEvents;
                
                // Update UI
                UpdateNoEventsVisibility();
                
                // Load template previews after items are rendered
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    LoadTemplatePreviews();
                }));
                
                // Show overlay
                MainOverlay.Visibility = Visibility.Visible;
                
                // Animate in
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                MainOverlay.BeginAnimation(OpacityProperty, fadeIn);
                
                // Focus search box
                SearchTextBox.Focus();
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionOverlay: Failed to show overlay: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Hide the overlay with animation
        /// </summary>
        public void HideOverlay()
        {
            try
            {
                Log.Debug("EventSelectionOverlay: Hiding overlay");
                
                // Animate out
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (s, e) =>
                {
                    MainOverlay.Visibility = Visibility.Collapsed;
                    
                    // Hide the control itself
                    this.Visibility = Visibility.Collapsed;
                    
                    // Reset service
                    _eventSelectionService.Reset();
                    
                    // Clear UI
                    SearchTextBox.Clear();
                };
                MainOverlay.BeginAnimation(OpacityProperty, fadeOut);
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionOverlay: Failed to hide overlay: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handle property changes from service
        /// </summary>
        private void OnServicePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(EventSelectionService.FilteredEvents):
                        EventsListBox.ItemsSource = _eventSelectionService.FilteredEvents;
                        UpdateNoEventsVisibility();
                        break;
                        
                    case nameof(EventSelectionService.SelectedEvent):
                        UpdateSelectedEvent();
                        break;
                        
                    case nameof(EventSelectionService.TemplatePreviewImage):
                        UpdateTemplatePreview();
                        break;
                        
                    case nameof(EventSelectionService.PreviewTemplate):
                        UpdateTemplateInfo();
                        break;
                }
            });
        }
        
        /// <summary>
        /// Update visibility of no events message
        /// </summary>
        private void UpdateNoEventsVisibility()
        {
            var hasEvents = _eventSelectionService.FilteredEvents?.Count > 0;
            NoEventsMessage.Visibility = hasEvents ? Visibility.Collapsed : Visibility.Visible;
        }
        
        /// <summary>
        /// Update selected event UI
        /// </summary>
        private void UpdateSelectedEvent()
        {
            var hasSelection = _eventSelectionService.SelectedEvent != null;
            SelectEventButton.IsEnabled = hasSelection;
        }
        
        /// <summary>
        /// Update template preview image
        /// </summary>
        private void UpdateTemplatePreview()
        {
            // Template preview is now handled in the data binding of each event item
            // This method is kept for compatibility but doesn't need to update UI directly
        }
        
        /// <summary>
        /// Load template previews for all event items
        /// </summary>
        private void LoadTemplatePreviews()
        {
            try
            {
                // Get all the rendered event items
                var itemsControl = EventsListBox;
                if (itemsControl == null) return;
                
                for (int i = 0; i < itemsControl.Items.Count; i++)
                {
                    var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                    if (container != null)
                    {
                        var border = FindVisualChild<Border>(container);
                        if (border != null && border.DataContext is EventData eventData)
                        {
                            // Find the Image control within the template
                            var imageControl = FindVisualChild<Image>(border, "EventTemplatePreview");
                            var fallbackGrid = FindVisualChild<Grid>(border, "NoPreviewFallback");
                            
                            if (imageControl != null)
                            {
                                // Get the preview image from the service
                                var previewImage = _eventSelectionService.GetEventTemplatePreview(eventData.Id);
                                
                                if (previewImage != null)
                                {
                                    imageControl.Source = previewImage;
                                    if (fallbackGrid != null)
                                        fallbackGrid.Visibility = Visibility.Collapsed;
                                }
                                else
                                {
                                    // Show fallback
                                    if (fallbackGrid != null)
                                        fallbackGrid.Visibility = Visibility.Visible;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionOverlay: Failed to load template previews: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Helper method to find visual child by type
        /// </summary>
        private T FindVisualChild<T>(DependencyObject parent, string name = null) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is T typedChild)
                {
                    if (name == null || (child is FrameworkElement fe && fe.Name == name))
                        return typedChild;
                }
                
                var result = FindVisualChild<T>(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }
        
        /// <summary>
        /// Update template info panel
        /// </summary>
        private void UpdateTemplateInfo()
        {
            // Template info is now shown in each event card
            // This method is kept for compatibility but doesn't need to update UI directly
        }
        
        /// <summary>
        /// Handle service event selected
        /// </summary>
        private void OnServiceEventSelected(object sender, EventData e)
        {
            Dispatcher.Invoke(() =>
            {
                HideOverlay();
                EventSelected?.Invoke(this, e);
            });
        }
        
        /// <summary>
        /// Handle service search cleared
        /// </summary>
        private void OnServiceSearchCleared(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(SearchTextBox.Text))
                {
                    SearchTextBox.Clear();
                }
            });
        }
        
        #region UI Event Handlers
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideOverlay();
            SelectionCancelled?.Invoke(this, EventArgs.Empty);
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            HideOverlay();
            SelectionCancelled?.Invoke(this, EventArgs.Empty);
        }
        
        private void SelectEventButton_Click(object sender, RoutedEventArgs e)
        {
            if (_eventSelectionService.SelectedEvent != null)
            {
                _eventSelectionService.SelectEvent(_eventSelectionService.SelectedEvent);
            }
        }
        
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _eventSelectionService.SearchText = SearchTextBox.Text;
        }
        
        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            _eventSelectionService.ClearSearch();
        }
        
        // EventsListBox_SelectionChanged removed - using ItemsControl with direct click handling now
        
        private void EventItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is EventData eventData)
            {
                // Select the event directly
                _eventSelectionService.SelectedEvent = eventData;
                _eventSelectionService.SelectEvent(eventData);
            }
        }
        
        #endregion
    }
}