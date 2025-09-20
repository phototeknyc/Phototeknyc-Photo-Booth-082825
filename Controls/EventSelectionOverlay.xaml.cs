using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
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
        private readonly EventService _eventService;
        private readonly TemplateDatabase _templateDatabase;
        private bool _isSelectMode = false;
        private List<EventItemViewModel> _allEvents;
        private ObservableCollection<EventItemViewModel> _eventItems;
        private List<TemplateData> _currentEventTemplates;
        private EventItemViewModel _selectedEvent;

        public event EventHandler<EventData> EventSelected;
        public event EventHandler SelectionCancelled;
        
        public EventSelectionOverlay()
        {
            InitializeComponent();
            _eventSelectionService = EventSelectionService.Instance;
            _eventService = new EventService();
            _templateDatabase = new TemplateDatabase();
            _allEvents = new List<EventItemViewModel>();
            _eventItems = new ObservableCollection<EventItemViewModel>();
            _currentEventTemplates = new List<TemplateData>();

            // Subscribe to service events
            _eventSelectionService.PropertyChanged += OnServicePropertyChanged;
            _eventSelectionService.EventSelected += OnServiceEventSelected;
            _eventSelectionService.SearchCleared += OnServiceSearchCleared;

            // Handle responsive sizing
            this.Loaded += (s, e) => SetResponsiveHeights();
            this.SizeChanged += (s, e) => SetResponsiveHeights();
        }

        private void SetResponsiveHeights()
        {
            // Get the actual height of the window
            var window = Window.GetWindow(this);
            if (window != null)
            {
                double screenHeight = window.ActualHeight;

                // Set template panel height to 70% of screen height
                if (TemplatePanel != null)
                {
                    TemplatePanel.MaxHeight = screenHeight * 0.7;
                }

                // Set ScrollViewer max height
                if (TemplateScrollViewer != null)
                {
                    TemplateScrollViewer.MaxHeight = screenHeight * 0.6;
                }
            }
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

                // Convert to EventItemViewModel
                LoadEventItems();

                // Bind data to ItemsControl
                EventsListBox.ItemsSource = _eventItems;
                
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

                // Hide management buttons
                HideEventManagementButtons();

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
                        // Refresh the view model collection when events change
                        LoadEventItems();
                        EventsListBox.ItemsSource = _eventItems;
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
                        if (border != null)
                        {
                            // Handle both EventItemViewModel and EventData for compatibility
                            EventData eventData = null;
                            if (border.DataContext is EventItemViewModel viewModel)
                            {
                                eventData = viewModel.EventData;
                            }
                            else if (border.DataContext is EventData directEventData)
                            {
                                eventData = directEventData;
                            }

                            if (eventData != null)
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
            if (sender is Border border)
            {
                var eventItem = border.DataContext as EventItemViewModel;
                if (eventItem == null)
                {
                    // Legacy support for direct EventData binding
                    if (border.DataContext is EventData eventData)
                    {
                        // Convert to view model if needed
                        eventItem = _eventItems.FirstOrDefault(vm => vm.EventData == eventData);
                    }
                    if (eventItem == null) return;
                }

                if (_isSelectMode)
                {
                    // In select mode, toggle selection
                    eventItem.IsSelected = !eventItem.IsSelected;
                    UpdateSelectionStatus();
                }
                else
                {
                    // Not in select mode - original behavior
                    if (e.ClickCount == 2)
                    {
                        // Double click/tap selects the event
                        _eventSelectionService.SelectedEvent = eventItem.EventData;
                        _eventSelectionService.SelectEvent(eventItem.EventData);
                    }
                    else
                    {
                        // Single click/tap shows management buttons
                        _eventSelectionService.SelectedEvent = eventItem.EventData;
                        ShowEventManagementButtons(eventItem.EventData);

                        // Update visual selection
                        UpdateEventSelection(border);
                    }
                }
            }
        }

        private void ShowEventManagementButtons(EventData selectedEvent)
        {
            // Show the management buttons
            EventManagementButtons.Visibility = Visibility.Visible;

            // Store the selected event for the button handlers
            DuplicateEventButton.Tag = selectedEvent;
            RenameEventButton.Tag = selectedEvent;
            DeleteEventButton.Tag = selectedEvent;

            // Load and show templates for this event
            LoadEventTemplates(selectedEvent);
        }

        private void HideEventManagementButtons()
        {
            EventManagementButtons.Visibility = Visibility.Collapsed;
            TemplatePanel.Visibility = Visibility.Collapsed;
        }

        private void LoadEventTemplates(EventData eventData)
        {
            try
            {
                if (eventData == null) return;

                // Get templates for this event
                _currentEventTemplates = _eventService.GetEventTemplates(eventData.Id);

                // Update UI
                if (_currentEventTemplates.Count > 0)
                {
                    TemplatePanel.Visibility = Visibility.Visible;
                    TemplateCountText.Text = $"({_currentEventTemplates.Count} assigned)";
                    NoTemplatesMessage.Visibility = Visibility.Collapsed;
                    TemplateScrollViewer.Visibility = Visibility.Visible;

                    // Populate templates list
                    PopulateTemplatesList();
                }
                else
                {
                    TemplatePanel.Visibility = Visibility.Visible;
                    TemplateCountText.Text = "(0 assigned)";
                    NoTemplatesMessage.Visibility = Visibility.Visible;
                    TemplateScrollViewer.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionOverlay: Failed to load templates: {ex.Message}");
            }
        }

        private void PopulateTemplatesList()
        {
            if (_currentEventTemplates == null) return;

            // Create template view models
            var templateViewModels = new System.Collections.ObjectModel.ObservableCollection<dynamic>();

            for (int i = 0; i < _currentEventTemplates.Count; i++)
            {
                var template = _currentEventTemplates[i];
                var viewModel = new
                {
                    Template = template,
                    Name = template.Name ?? "Untitled Template",
                    Info = $"Template {i + 1}",
                    IsDefault = template.IsDefault,
                    PreviewPath = template.ThumbnailImagePath
                };
                templateViewModels.Add(viewModel);
            }

            TemplatesList.ItemsSource = templateViewModels;

            // Update responsive heights after templates are loaded
            SetResponsiveHeights();
        }

        private void EditTemplate_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.Tag == null) return;

            dynamic templateViewModel = button.Tag;
            var template = templateViewModel.Template as TemplateData;
            if (template == null) return;

            // Create a window to host the TouchTemplateDesignerOverlay
            var designerWindow = new Window
            {
                Title = $"Edit Template - {template.Name}",
                WindowState = WindowState.Maximized,
                WindowStyle = WindowStyle.None,
                Background = System.Windows.Media.Brushes.Black,
                AllowsTransparency = false,
                Topmost = false
            };

            // Create and configure the overlay
            var overlay = new TouchTemplateDesignerOverlay();
            overlay.LoadTemplate(template.Id);

            // Set the overlay as window content
            designerWindow.Content = overlay;

            // Handle close event
            overlay.CloseRequested += (s, args) =>
            {
                designerWindow.Close();
            };

            // Show as modal dialog
            designerWindow.ShowDialog();

            // Refresh templates after editing
            LoadEventTemplates(_eventSelectionService.SelectedEvent);
        }

        private void RemoveTemplate_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.Tag == null) return;

            dynamic templateViewModel = button.Tag;
            var template = templateViewModel.Template as TemplateData;
            if (template == null) return;

            var eventData = _eventSelectionService.SelectedEvent;

            if (MessageBox.Show($"Remove template '{template.Name}' from this event?", "Confirm Remove",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _eventService.RemoveTemplateFromEvent(eventData.Id, template.Id);
                LoadEventTemplates(eventData);
            }
        }

        private void AddTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log.Debug($"EventSelectionOverlay: Opening Template Browser (Event has {_currentEventTemplates.Count} templates)");

                // Show the template browser overlay
                TemplateBrowserOverlay.Visibility = Visibility.Visible;
                TemplateBrowserOverlay.ShowOverlay();

                // Unsubscribe previous handlers to avoid duplicates
                TemplateBrowserOverlay.TemplateSelected -= OnTemplateSelectedForAdd;
                TemplateBrowserOverlay.SelectionCancelled -= OnTemplateBrowserCancelled;

                // Subscribe to events
                TemplateBrowserOverlay.TemplateSelected += OnTemplateSelectedForAdd;
                TemplateBrowserOverlay.SelectionCancelled += OnTemplateBrowserCancelled;
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionOverlay: Failed to open template browser: {ex.Message}");
                MessageBox.Show($"Failed to open template browser: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnTemplateSelectedForAdd(object sender, TemplateData template)
        {
            try
            {
                var eventData = _eventSelectionService.SelectedEvent;
                if (eventData != null && template != null)
                {
                    // Check if template is already assigned to this event
                    bool isAlreadyAssigned = _currentEventTemplates.Any(t => t.Id == template.Id);

                    if (isAlreadyAssigned)
                    {
                        MessageBox.Show($"Template '{template.Name}' is already assigned to this event.",
                            "Template Already Assigned",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        // Add the template to the event
                        _eventService.AssignTemplateToEvent(eventData.Id, template.Id, _currentEventTemplates.Count == 0);
                        LoadEventTemplates(eventData);

                        Log.Debug($"EventSelectionOverlay: Added template '{template.Name}' to event '{eventData.Name}'");
                    }
                }

                // Hide the browser
                TemplateBrowserOverlay.HideOverlay();
                TemplateBrowserOverlay.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionOverlay: Failed to add template: {ex.Message}");
                MessageBox.Show($"Failed to add template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnTemplateBrowserCancelled(object sender, EventArgs e)
        {
            TemplateBrowserOverlay.HideOverlay();
            TemplateBrowserOverlay.Visibility = Visibility.Collapsed;
        }



        private void UpdateEventSelection(Border selectedBorder)
        {
            // Find all event items and update their selection state
            var itemsControl = EventsListBox;
            if (itemsControl != null)
            {
                for (int i = 0; i < itemsControl.Items.Count; i++)
                {
                    var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                    if (container != null)
                    {
                        var border = FindVisualChild<Border>(container, "EventBorder");
                        if (border != null)
                        {
                            if (border == selectedBorder)
                            {
                                // Highlight selected
                                border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                                border.BorderThickness = new Thickness(3);
                            }
                            else
                            {
                                // Reset others
                                border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A3A3A"));
                                border.BorderThickness = new Thickness(1);
                            }
                        }
                    }
                }
            }
        }

        private void DuplicateEvent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sourceEvent = (sender as Button)?.Tag as EventData ?? _eventSelectionService.SelectedEvent;
                if (sourceEvent != null)
                {
                    var newEventName = ShowInputDialog("Duplicate Event",
                                                       "Enter name for the duplicated event:",
                                                       sourceEvent.Name + " (Copy)");

                    if (!string.IsNullOrWhiteSpace(newEventName))
                    {
                        _eventSelectionService.DuplicateEvent(sourceEvent, newEventName);
                        _eventSelectionService.LoadEvents(); // Refresh the list
                        HideEventManagementButtons(); // Hide buttons after action
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionOverlay: Failed to duplicate event: {ex.Message}");
                MessageBox.Show($"Failed to duplicate event: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RenameEvent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var eventToRename = (sender as Button)?.Tag as EventData ?? _eventSelectionService.SelectedEvent;
                if (eventToRename != null)
                {
                    var newName = ShowInputDialog("Rename Event",
                                                  "Enter new name for the event:",
                                                  eventToRename.Name);

                    if (!string.IsNullOrWhiteSpace(newName) && newName != eventToRename.Name)
                    {
                        _eventSelectionService.RenameEvent(eventToRename, newName);
                        _eventSelectionService.LoadEvents(); // Refresh the list
                        HideEventManagementButtons(); // Hide buttons after action
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionOverlay: Failed to rename event: {ex.Message}");
                MessageBox.Show($"Failed to rename event: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteEvent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var eventToDelete = (sender as Button)?.Tag as EventData ?? _eventSelectionService.SelectedEvent;
                if (eventToDelete != null)
                {
                    var result = MessageBox.Show(
                        $"Are you sure you want to delete the event '{eventToDelete.Name}'?\n\nThis will remove all associated templates from this event.",
                        "Delete Event",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        _eventSelectionService.DeleteEvent(eventToDelete);
                        _eventSelectionService.LoadEvents(); // Refresh the list
                        HideEventManagementButtons(); // Hide buttons after action
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionOverlay: Failed to delete event: {ex.Message}");
                MessageBox.Show($"Failed to delete event: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NewEventButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var eventName = ShowInputDialog("Create New Event",
                                               "Enter name for the new event:",
                                               "New Event " + DateTime.Now.ToString("yyyy-MM-dd"));

                if (!string.IsNullOrWhiteSpace(eventName))
                {
                    _eventSelectionService.CreateNewEventFromLast(eventName);
                    _eventSelectionService.LoadEvents(); // Refresh the list
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionOverlay: Failed to create new event: {ex.Message}");
                MessageBox.Show($"Failed to create new event: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Simple input prompt dialog
        /// </summary>
        private string ShowInputDialog(string title, string message, string defaultValue = "")
        {
            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Margin = new Thickness(20);

            var label = new TextBlock
            {
                Text = message,
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            var textBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, 20),
                Padding = new Thickness(5),
                FontSize = 14
            };
            Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            var okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            okButton.Click += (s, e) => { dialog.DialogResult = true; dialog.Close(); };
            buttonPanel.Children.Add(okButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 30,
                IsCancel = true
            };
            cancelButton.Click += (s, e) => { dialog.DialogResult = false; dialog.Close(); };
            buttonPanel.Children.Add(cancelButton);

            dialog.Content = grid;
            textBox.Focus();
            textBox.SelectAll();

            if (dialog.ShowDialog() == true)
            {
                return textBox.Text;
            }

            return null;
        }

        #endregion

        #region Multi-Select Methods

        private void LoadEventItems()
        {
            _allEvents.Clear();
            _eventItems.Clear();

            if (_eventSelectionService.FilteredEvents != null)
            {
                foreach (var eventData in _eventSelectionService.FilteredEvents)
                {
                    var viewModel = new EventItemViewModel(eventData);

                    // Get template count for this event
                    try
                    {
                        var templates = _eventService.GetEventTemplates(eventData.Id);
                        viewModel.TemplateCount = templates.Count;
                    }
                    catch
                    {
                        viewModel.TemplateCount = 0;
                    }

                    _allEvents.Add(viewModel);
                    _eventItems.Add(viewModel);
                }
            }
        }

        private void EnterSelectMode()
        {
            _isSelectMode = true;

            // Show select mode UI
            SelectModePanel.Visibility = Visibility.Visible;
            EventManagementButtons.Visibility = Visibility.Collapsed;

            // Update button text
            SelectModeButton.Content = "✓ Exit Select Mode";

            // Clear any previous selections and make checkboxes visible
            foreach (var eventItem in _allEvents)
            {
                eventItem.IsSelected = false;
                eventItem.ShowCheckbox = true;
            }

            UpdateSelectionStatus();
        }

        private void ExitSelectMode()
        {
            _isSelectMode = false;

            // Hide select mode UI
            SelectModePanel.Visibility = Visibility.Collapsed;

            // Update button text
            SelectModeButton.Content = "☐ Select Mode";

            // Clear selections and hide checkboxes
            foreach (var eventItem in _allEvents)
            {
                eventItem.IsSelected = false;
                eventItem.ShowCheckbox = false;
            }
        }

        private void SelectAll()
        {
            foreach (var eventItem in _eventItems)
            {
                eventItem.IsSelected = true;
            }
            UpdateSelectionStatus();
        }

        private void DeselectAll()
        {
            foreach (var eventItem in _eventItems)
            {
                eventItem.IsSelected = false;
            }
            UpdateSelectionStatus();
        }

        private void UpdateSelectionStatus()
        {
            var selectedCount = _eventItems.Count(e => e.IsSelected);
            SelectionStatusText.Text = $"{selectedCount} selected";
            DeleteSelectedButton.IsEnabled = selectedCount > 0;
        }

        private void DeleteSelectedEvents()
        {
            var selectedEvents = _eventItems.Where(e => e.IsSelected).ToList();

            if (selectedEvents.Count == 0) return;

            var message = selectedEvents.Count == 1
                ? $"Are you sure you want to delete '{selectedEvents[0].EventData.Name}'?"
                : $"Are you sure you want to delete {selectedEvents.Count} events?";

            var result = MessageBox.Show(
                message + "\n\nThis will remove all associated templates from these events.",
                "Delete Events",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    foreach (var eventItem in selectedEvents)
                    {
                        _eventSelectionService.DeleteEvent(eventItem.EventData);
                    }

                    // Reload events
                    _eventSelectionService.LoadEvents();
                    LoadEventItems();

                    // Exit select mode
                    ExitSelectMode();

                    // Show success message
                    var successMessage = selectedEvents.Count == 1
                        ? "Event deleted successfully"
                        : $"{selectedEvents.Count} events deleted successfully";

                    Log.Debug($"EventSelectionOverlay: {successMessage}");
                }
                catch (Exception ex)
                {
                    Log.Error($"EventSelectionOverlay: Failed to delete events: {ex.Message}");
                    MessageBox.Show($"Failed to delete events: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SelectModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSelectMode)
            {
                ExitSelectMode();
            }
            else
            {
                EnterSelectMode();
            }
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            SelectAll();
        }

        private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
        {
            DeselectAll();
        }

        private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedEvents();
        }

        #endregion
    }

    /// <summary>
    /// ViewModel for event items with selection support
    /// </summary>
    public class EventItemViewModel : INotifyPropertyChanged
    {
        public EventData EventData { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        private bool _showCheckbox;
        public bool ShowCheckbox
        {
            get => _showCheckbox;
            set
            {
                _showCheckbox = value;
                OnPropertyChanged();
            }
        }

        private int _templateCount;
        public int TemplateCount
        {
            get => _templateCount;
            set
            {
                _templateCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasTemplates));
                OnPropertyChanged(nameof(TemplatePluralSuffix));
            }
        }

        public bool HasTemplates => TemplateCount > 0;
        public string TemplatePluralSuffix => TemplateCount == 1 ? "" : "s";

        public EventItemViewModel(EventData eventData)
        {
            EventData = eventData;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}