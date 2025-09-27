using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Photobooth.Database;
using Photobooth.Services;
using Photobooth.Models;
using Microsoft.Win32;

namespace Photobooth.Controls
{
    public partial class AITransformationOverlay : UserControl
    {
        private readonly AITemplateService _templateService;
        private readonly AITransformationUIService _uiService;
        private Database.AITransformationTemplate _selectedTemplate;
        private string _currentImagePath;
        private bool _isProcessing;
        private bool _isManagementMode;
        private List<TemplateSelectionItem> _templateItems;
        private EventData _selectedEvent;
        private bool _isUpdatingDropdown;

        public event EventHandler<AITransformationCompletedEventArgs> TransformationCompleted;
        public event EventHandler TransformationSkipped;
        public event EventHandler OverlayClosed;
        public event EventHandler TemplateSelected;

        public AITransformationOverlay()
        {
            InitializeComponent();

            _templateService = AITemplateService.Instance;
            _uiService = AITransformationUIService.Instance;

            LoadCategories();
            LoadAllTemplates();
            InitializeModelSelection();

            // Subscribe to UI service events
            _uiService.ProcessingStateChanged += OnProcessingStateChanged;
            _uiService.TransformationProgress += OnTransformationProgress;
            _uiService.TransformationCompleted += OnTransformationCompleted;
            _uiService.TransformationError += OnTransformationError;
        }

        private void InitializeModelSelection()
        {
            var modelManager = AIModelManager.Instance;
            modelManager.Initialize();

            // Force reload of default models to ensure Seedream-4 is included
            if (!modelManager.AvailableModels.Any(m => m.Id == "seedream-4"))
            {
                modelManager.ResetToDefaults();
            }

            // Populate model dropdown
            ModelSelectionComboBox.ItemsSource = null; // Clear first
            ModelSelectionComboBox.ItemsSource = modelManager.AvailableModels;
            ModelSelectionComboBox.DisplayMemberPath = "Name";
            ModelSelectionComboBox.SelectedValuePath = "Id";

            // Set selected model
            if (!string.IsNullOrEmpty(modelManager.SelectedModelId))
            {
                ModelSelectionComboBox.SelectedValue = modelManager.SelectedModelId;
            }
            else if (modelManager.AvailableModels.Any())
            {
                ModelSelectionComboBox.SelectedIndex = 0;
            }

            Debug.WriteLine($"[AITransformationOverlay] Loaded {modelManager.AvailableModels.Count} models");
            foreach (var model in modelManager.AvailableModels)
            {
                Debug.WriteLine($"  - {model.Id}: {model.Name}");
            }
        }

        public void SetImageForTransformation(string imagePath)
        {
            _currentImagePath = imagePath;
            Debug.WriteLine($"[AITransformationOverlay] Image set for transformation: {imagePath}");
        }

        private void LoadCategories()
        {
            try
            {
                // Get templates based on mode
                var eventTemplateService = EventAITemplateService.Instance;
                var templates = _isManagementMode
                    ? _templateService.GetAllTemplates()  // Show all templates in management mode
                    : eventTemplateService.GetTemplatesForCurrentEvent();  // Show event templates in selection mode

                // Extract unique categories from templates
                var categories = templates
                    .Where(t => t.Category != null)
                    .Select(t => t.Category)
                    .GroupBy(c => c.Id)  // Group by ID to ensure uniqueness
                    .Select(g => g.First())  // Take first of each group
                    .OrderBy(c => c.Name)
                    .ToList();

                // Add "All" category at the beginning
                categories.Insert(0, new AITemplateCategory { Id = -1, Name = "All Templates" });

                CategoryList.ItemsSource = categories;
                Debug.WriteLine($"[AITransformationOverlay] Loaded {categories.Count()} categories for current event");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITransformationOverlay] Error loading categories: {ex.Message}");
            }
        }

        private void LoadAllTemplates()
        {
            try
            {
                var eventTemplateService = EventAITemplateService.Instance;

                if (_isManagementMode)
                {
                    // In management mode, show all templates with selection status
                    var allTemplates = _templateService.GetAllTemplates();
                    var selectedIds = eventTemplateService.GetSelectedTemplateIds();
                    var defaultTemplate = eventTemplateService.GetDefaultTemplate();

                    _templateItems = allTemplates.Select(t => new TemplateSelectionItem(t)
                    {
                        IsSelected = selectedIds.Contains(t.Id),
                        IsDefault = defaultTemplate?.Id == t.Id
                    }).ToList();

                    // Apply filter if the toggle is checked
                    if (FilterSelectedToggle != null && FilterSelectedToggle.IsChecked == true)
                    {
                        // Show only templates that are selected for this event
                        TemplateGrid.ItemsSource = _templateItems.Where(t => t.IsSelected).ToList();
                    }
                    else
                    {
                        // Show all available templates
                        TemplateGrid.ItemsSource = _templateItems;
                    }
                    Debug.WriteLine($"[AITransformationOverlay] Loaded {_templateItems.Count} templates in management mode");
                }
                else
                {
                    // In selection mode, show only event templates
                    var templates = eventTemplateService.GetTemplatesForCurrentEvent();
                    TemplateGrid.ItemsSource = templates;
                    Debug.WriteLine($"[AITransformationOverlay] Loaded {templates.Count()} templates for current event");

                    // Pre-select default template if available
                    var defaultTemplate = eventTemplateService.GetDefaultTemplate();
                    if (defaultTemplate != null && !_isManagementMode)
                    {
                        SelectTemplate(defaultTemplate);
                        Debug.WriteLine($"[AITransformationOverlay] Pre-selected default template: {defaultTemplate.Name}");
                    }
                }

                // Update UI if in management mode
                if (_isManagementMode)
                {
                    // Delay to ensure items are rendered
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                    {
                        UpdateManagementModeUI();
                    }));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITransformationOverlay] Error loading templates: {ex.Message}");
            }
        }

        private void CategoryCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isProcessing) return;

            var border = sender as Border;
            if (border?.Tag is int categoryId)
            {
                // Get templates based on mode (same logic as LoadCategories and LoadAllTemplates)
                var eventTemplateService = EventAITemplateService.Instance;
                var availableTemplates = _isManagementMode
                    ? _templateService.GetAllTemplates()  // Show all templates in management mode
                    : eventTemplateService.GetTemplatesForCurrentEvent();  // Show event templates in selection mode

                // Filter by category
                IEnumerable<object> templates;
                if (_isManagementMode)
                {
                    // In management mode, maintain the TemplateSelectionItem wrapper
                    var selectedIds = eventTemplateService.GetSelectedTemplateIds();
                    var defaultTemplate = eventTemplateService.GetDefaultTemplate();

                    var filteredTemplates = categoryId == -1
                        ? availableTemplates
                        : availableTemplates.Where(t => t.Category?.Id == categoryId);

                    var allItems = filteredTemplates.Select(t => new TemplateSelectionItem(t)
                    {
                        IsSelected = selectedIds.Contains(t.Id),
                        IsDefault = defaultTemplate?.Id == t.Id
                    }).ToList();

                    // Apply filter if the toggle is checked
                    if (FilterSelectedToggle != null && FilterSelectedToggle.IsChecked == true)
                    {
                        // Show only selected templates
                        templates = allItems.Where(t => t.IsSelected).ToList();
                    }
                    else
                    {
                        templates = allItems;
                    }

                    // Store for later updates
                    _templateItems = allItems; // Keep all items for reference
                }
                else
                {
                    // In selection mode, just filter the templates
                    templates = categoryId == -1
                        ? availableTemplates
                        : availableTemplates.Where(t => t.Category?.Id == categoryId).ToList();
                }

                TemplateGrid.ItemsSource = templates;

                // Highlight selected category
                foreach (var item in CategoryList.Items)
                {
                    var container = CategoryList.ItemContainerGenerator.ContainerFromItem(item) as ContentPresenter;
                    if (container != null)
                    {
                        var categoryBorder = FindVisualChild<Border>(container);
                        if (categoryBorder != null)
                        {
                            categoryBorder.BorderBrush = categoryBorder.Tag?.Equals(categoryId) == true
                                ? FindResource("AccentBrush") as Brush
                                : Brushes.Transparent;
                        }
                    }
                }
            }
        }

        private void TemplateCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isProcessing) return;

            var border = sender as Border;
            if (border?.Tag is Database.AITransformationTemplate template)
            {
                SelectTemplate(template);
            }
        }

        private void SelectTemplate(Database.AITransformationTemplate template)
        {
            Debug.WriteLine($"[AITransformationOverlay] SelectTemplate: {template?.Name} (ID: {template?.Id})");
            Debug.WriteLine($"[AITransformationOverlay] Management Mode: {_isManagementMode}");

            _selectedTemplate = template;
            _uiService.SelectedTemplate = template;

            // Update UI
            SelectedTemplateText.Text = $"Selected: {template.Name}";
            ApplyButton.IsEnabled = true;

            Debug.WriteLine($"[AITransformationOverlay] ApplyButton enabled, template set");

            // Highlight selected template
            foreach (var item in TemplateGrid.Items)
            {
                var container = TemplateGrid.ItemContainerGenerator.ContainerFromItem(item) as ContentPresenter;
                if (container != null)
                {
                    var templateBorder = FindVisualChild<Border>(container);
                    if (templateBorder != null)
                    {
                        templateBorder.BorderThickness = templateBorder.Tag == template
                            ? new Thickness(3)
                            : new Thickness(0);
                        templateBorder.BorderBrush = templateBorder.Tag == template
                            ? FindResource("AccentBrush") as Brush
                            : Brushes.Transparent;
                    }
                }
            }
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine($"[AITransformationOverlay] ApplyButton_Click - Template: {_selectedTemplate?.Name}, Mode: {(_isManagementMode ? "Management" : "Selection")}");

            if (_selectedTemplate == null)
            {
                Debug.WriteLine($"[AITransformationOverlay] ApplyButton_Click - No template selected, returning");
                return;
            }

            // In selection mode (not management), just fire the event and close
            if (!_isManagementMode)
            {
                Debug.WriteLine($"[AITransformationOverlay] Selection mode - Template selected for session: {_selectedTemplate.Name} (ID: {_selectedTemplate.Id})");
                Debug.WriteLine($"[AITransformationOverlay] Firing TemplateSelected event");
                TemplateSelected?.Invoke(this, EventArgs.Empty);
                Debug.WriteLine($"[AITransformationOverlay] Closing overlay after selection");
                CloseOverlay();
                return;
            }

            // In management mode, do the transformation preview
            if (string.IsNullOrEmpty(_currentImagePath) || _isProcessing)
                return;

            _isProcessing = true;
            ShowProgress(true);

            try
            {
                var result = await _uiService.ApplyTransformationAsync(_currentImagePath, _selectedTemplate);

                if (!string.IsNullOrEmpty(result))
                {
                    Debug.WriteLine($"[AITransformationOverlay] Transformation completed: {result}");

                    // Fire completed event
                    TransformationCompleted?.Invoke(this, new AITransformationCompletedEventArgs
                    {
                        OriginalPath = _currentImagePath,
                        TransformedPath = result,
                        Template = _selectedTemplate
                    });

                    // Close overlay
                    CloseOverlay();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITransformationOverlay] Transformation failed: {ex.Message}");
                MessageBox.Show($"Transformation failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isProcessing = false;
                ShowProgress(false);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _uiService.CancelTransformation();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            TransformationSkipped?.Invoke(this, EventArgs.Empty);
            CloseOverlay();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing)
            {
                _uiService.CancelTransformation();
            }
            CloseOverlay();
        }

        private void CloseOverlay()
        {
            OverlayClosed?.Invoke(this, EventArgs.Empty);
            HideOverlay();
        }

        #region Public Properties

        /// <summary>
        /// Gets the currently selected template
        /// </summary>
        public Database.AITransformationTemplate SelectedTemplate
        {
            get { return _selectedTemplate; }
        }

        #endregion

        /// <summary>
        /// Show the overlay with animation
        /// </summary>
        public void ShowOverlay(bool managementMode = false)
        {
            try
            {
                Debug.WriteLine($"[AITransformationOverlay] Showing overlay (management mode: {managementMode})");

                _isManagementMode = managementMode;

                // Ensure event is set
                var eventTemplateService = EventAITemplateService.Instance;
                var currentEventId = eventTemplateService.GetCurrentEventId();
                if (!currentEventId.HasValue && _isManagementMode)
                {
                    // In management mode without an event, we can't properly manage templates
                    // The event should be set from the EventSelectionPage or elsewhere before opening this overlay
                    Debug.WriteLine("[AITransformationOverlay] WARNING: No current event set for management mode");
                    Debug.WriteLine("[AITransformationOverlay] Event should be set from EventSelectionPage before opening AI Template Management");
                }

                // Make sure the control itself is visible
                this.Visibility = Visibility.Visible;

                // Show the main overlay
                MainOverlay.Visibility = Visibility.Visible;

                // Configure for management or selection mode
                if (_isManagementMode)
                {
                    HeaderTitle.Text = "AI TEMPLATE MANAGEMENT";
                    HeaderSubtitle.Text = "Configure AI transformation templates for this event";
                    ManagementModeToggle.Visibility = Visibility.Visible;
                    ManagementSettingsPanel.Visibility = Visibility.Visible;
                    ApplyButton.Visibility = Visibility.Collapsed;
                    SkipButton.Visibility = Visibility.Collapsed;
                    LoadManagementSettings();
                }
                else
                {
                    HeaderTitle.Text = "AI PHOTO TRANSFORMATION";
                    HeaderSubtitle.Text = "Select a transformation style to apply to your photo";
                    ManagementModeToggle.Visibility = Visibility.Collapsed;
                    ManagementSettingsPanel.Visibility = Visibility.Collapsed;
                    ApplyButton.Visibility = Visibility.Visible;
                    SkipButton.Visibility = Visibility.Visible;
                }

                // Animate in
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                MainOverlay.BeginAnimation(OpacityProperty, fadeIn);

                // Reset state
                _selectedTemplate = null;
                SelectedTemplateText.Text = "No template selected";
                ApplyButton.IsEnabled = false;

                // Refresh model selection to ensure all models are available
                InitializeModelSelection();

                LoadCategories();
                LoadAllTemplates();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITransformationOverlay] Failed to show overlay: {ex.Message}");
            }
        }

        /// <summary>
        /// Hide the overlay with animation
        /// </summary>
        public void HideOverlay()
        {
            try
            {
                Debug.WriteLine("[AITransformationOverlay] Hiding overlay");

                // Animate out
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (s, e) =>
                {
                    MainOverlay.Visibility = Visibility.Collapsed;
                    this.Visibility = Visibility.Collapsed;
                };
                MainOverlay.BeginAnimation(OpacityProperty, fadeOut);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITransformationOverlay] Failed to hide overlay: {ex.Message}");
            }
        }

        private void ShowProgress(bool show)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                ApplyButton.IsEnabled = !show && _selectedTemplate != null;
                SkipButton.IsEnabled = !show;
                CategoryList.IsEnabled = !show;
                TemplateGrid.IsEnabled = !show;
            });
        }

        private void OnProcessingStateChanged(object sender, ProcessingStateEventArgs e)
        {
            ShowProgress(e.IsProcessing);
        }

        private void OnTransformationProgress(object sender, TransformationUIProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.Progress > 0)
                {
                    TransformationProgress.IsIndeterminate = false;
                    TransformationProgress.Value = e.Progress;
                }
                ProgressStatus.Text = e.Status ?? "Processing...";
            });
        }

        private void OnTransformationCompleted(object sender, TransformationUICompletedEventArgs e)
        {
            Debug.WriteLine($"[AITransformationOverlay] Transformation UI completed: {e.OutputPath}");
        }

        private void OnTransformationError(object sender, TransformationUIErrorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"Error: {e.Error}", "Transformation Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ShowProgress(false);
            });
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;

                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        public void Reset()
        {
            _selectedTemplate = null;
            _currentImagePath = null;
            _isProcessing = false;
            _isManagementMode = false;
            SelectedTemplateText.Text = "No template selected";
            ApplyButton.IsEnabled = false;
            ShowProgress(false);
            LoadAllTemplates();
        }

        #region Management Mode Methods

        private void LoadManagementSettings()
        {
            var eventTemplateService = EventAITemplateService.Instance;
            var settings = eventTemplateService.GetCurrentEventSettings();

            EnableAITransformationCheckBox.IsChecked = settings.EnableAITransformation;
            AutoApplyDefaultCheckBox.IsChecked = settings.AutoApplyDefault;
            ShowSelectionOverlayCheckBox.IsChecked = settings.ShowSelectionOverlay;
        }

        private void ManagementModeToggle_Click(object sender, RoutedEventArgs e)
        {
            // Toggle between management and selection mode
            var toggle = sender as System.Windows.Controls.Primitives.ToggleButton;
            _isManagementMode = toggle.IsChecked == true;

            // Update UI visibility
            UpdateManagementModeUI();
        }

        private void UpdateManagementModeUI()
        {
            var modelManager = AIModelManager.Instance;
            var models = modelManager.AvailableModels;

            if (_isManagementMode)
            {
                // Show management panel
                ManagementSettingsPanel.Visibility = Visibility.Visible;
                // ModeToggleButton.Content = "Management Mode"; // This is handled in XAML
                AddModelButton.Visibility = Visibility.Visible;

                // Initialize model selection
                InitializeModelSelection();

                // Load events into dropdown
                LoadEventsIntoDropdown();
            }
            else
            {
                // Hide management panel
                ManagementSettingsPanel.Visibility = Visibility.Collapsed;
                // ModeToggleButton.Content = "Selection Mode"; // This is handled in XAML
                AddModelButton.Visibility = Visibility.Collapsed;
            }

            // Show/hide management controls in template cards
            foreach (var item in TemplateGrid.Items)
            {
                var container = TemplateGrid.ItemContainerGenerator.ContainerFromItem(item);
                if (container != null)
                {
                    var uploadBtn = FindVisualChild<Button>(container, "UploadThumbnailButton");
                    var checkbox = FindVisualChild<CheckBox>(container, "TemplateSelectionCheckBox");
                    var radioBtn = FindVisualChild<RadioButton>(container, "DefaultTemplateRadio");
                    var managementControls = FindVisualChild<StackPanel>(container, "ManagementControls");
                    var modelCombo = FindVisualChild<ComboBox>(container, "TemplateModelCombo");
                    var editPromptBtn = FindVisualChild<Button>(container, "EditTemplatePromptBtn");

                    if (uploadBtn != null) uploadBtn.Visibility = _isManagementMode ? Visibility.Visible : Visibility.Collapsed;
                    if (checkbox != null)
                    {
                        checkbox.Visibility = _isManagementMode ? Visibility.Visible : Visibility.Collapsed;
                        // Set checkbox state for management mode
                        if (_isManagementMode && item is TemplateSelectionItem selItem)
                        {
                            checkbox.IsChecked = selItem.IsSelected;
                        }
                    }
                    if (radioBtn != null)
                    {
                        radioBtn.Visibility = _isManagementMode ? Visibility.Visible : Visibility.Collapsed;
                        // Set radio state for management mode
                        if (_isManagementMode && item is TemplateSelectionItem selItem)
                        {
                            radioBtn.IsChecked = selItem.IsDefault;
                            radioBtn.IsEnabled = selItem.IsSelected;
                        }
                    }
                    if (managementControls != null) managementControls.Visibility = _isManagementMode ? Visibility.Visible : Visibility.Collapsed;

                    // Populate model combo for this template
                    if (modelCombo != null && _isManagementMode)
                    {
                        modelCombo.ItemsSource = models;
                        modelCombo.DisplayMemberPath = "Name";
                        modelCombo.SelectedValuePath = "Id";

                        // Get template-specific model if stored
                        if (item is TemplateSelectionItem template)
                        {
                            var templateModelId = GetTemplateModelPreference(template.Id);
                            modelCombo.SelectedValue = templateModelId ?? modelManager.SelectedModelId;
                        }
                    }
                }
            }
        }

        private string GetTemplateModelPreference(int templateId)
        {
            var modelManager = AIModelManager.Instance;
            return modelManager.GetTemplateModelPreference(templateId);
        }

        private void LoadEventsIntoDropdown()
        {
            try
            {
                _isUpdatingDropdown = true;

                var eventService = new Services.EventService();
                var events = eventService.GetAllEvents();

                // Sort events by date descending (most recent first)
                var sortedEvents = events.OrderByDescending(e => e.EventDate).ToList();

                EventSelectionComboBox.ItemsSource = sortedEvents;

                // Try to select current event or most recent
                var eventSelectionService = Services.EventSelectionService.Instance;
                var currentEvent = eventSelectionService.SelectedEvent;
                if (currentEvent != null)
                {
                    _selectedEvent = currentEvent;
                    var matchingEvent = sortedEvents.FirstOrDefault(e => e.Id == currentEvent.Id);
                    if (matchingEvent != null)
                    {
                        EventSelectionComboBox.SelectedItem = matchingEvent;
                    }
                }
                else if (sortedEvents.Any())
                {
                    // Select the most recent event if no current event
                    EventSelectionComboBox.SelectedIndex = 0;
                    _selectedEvent = sortedEvents[0];
                }

                Debug.WriteLine($"[AITransformationOverlay] Loaded {sortedEvents.Count()} events into dropdown");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITransformationOverlay] Error loading events: {ex.Message}");
            }
            finally
            {
                _isUpdatingDropdown = false;
            }
        }

        private void EventSelection_Changed(object sender, SelectionChangedEventArgs e)
        {
            // Prevent recursive calls
            if (_isUpdatingDropdown)
                return;

            if (EventSelectionComboBox.SelectedItem is EventData selectedEvent)
            {
                // Only process if the event actually changed
                if (_selectedEvent?.Id == selectedEvent.Id)
                    return;

                _selectedEvent = selectedEvent;

                // Update the event context for template management
                var eventTemplateService = EventAITemplateService.Instance;
                eventTemplateService.SetCurrentEvent(_selectedEvent.Id);

                Debug.WriteLine($"[AITransformationOverlay] Selected event changed to: {_selectedEvent.Name}");

                // Update the enable checkbox state for this event
                UpdateEventSettingsUI();

                // Only reload templates if in management mode and templates are already loaded
                if (_isManagementMode && _templateItems != null)
                {
                    // Just update the selection states for the new event
                    var selectedIds = eventTemplateService.GetSelectedTemplateIds();
                    var defaultTemplate = eventTemplateService.GetDefaultTemplate();

                    foreach (var item in _templateItems)
                    {
                        item.IsSelected = selectedIds.Contains(item.Id);
                        item.IsDefault = defaultTemplate?.Id == item.Id;
                    }

                    // Refresh the UI
                    if (FilterSelectedToggle != null && FilterSelectedToggle.IsChecked == true)
                    {
                        TemplateGrid.ItemsSource = _templateItems.Where(t => t.IsSelected).ToList();
                    }
                    else
                    {
                        TemplateGrid.ItemsSource = _templateItems;
                    }
                }
            }
        }

        private void UpdateEventSettingsUI()
        {
            if (_selectedEvent != null && _isManagementMode)
            {
                var eventTemplateService = EventAITemplateService.Instance;
                // For now, just check the checkbox state since we're managing per-event settings
                var isEnabled = true; // Default to enabled for management mode
                EnableAITransformationCheckBox.IsChecked = isEnabled;

                // Update checkbox text to show event context
                EnableAITransformationCheckBox.Content = $"Enable AI Transformation for {_selectedEvent.Name}";
            }
        }


        private void EnableAITransformation_Changed(object sender, RoutedEventArgs e)
        {
            if (_selectedEvent == null) return;

            var isEnabled = EnableAITransformationCheckBox.IsChecked == true;
            var eventTemplateService = EventAITemplateService.Instance;

            // Update settings for the selected event
            eventTemplateService.SetCurrentEvent(_selectedEvent.Id);
            eventTemplateService.UpdateCurrentEventSettings(enableAITransformation: isEnabled);

            ShowSaveStatus($"AI Transformation {(isEnabled ? "enabled" : "disabled")} for {_selectedEvent.Name}");
        }

        private void AutoApplyDefault_Changed(object sender, RoutedEventArgs e)
        {
            var autoApply = AutoApplyDefaultCheckBox.IsChecked == true;
            var eventTemplateService = EventAITemplateService.Instance;
            eventTemplateService.UpdateCurrentEventSettings(autoApplyDefault: autoApply);
            ShowSaveStatus(autoApply ? "Auto-apply enabled" : "Auto-apply disabled");
        }

        private void ShowSelectionOverlay_Changed(object sender, RoutedEventArgs e)
        {
            var showOverlay = ShowSelectionOverlayCheckBox.IsChecked == true;
            var eventTemplateService = EventAITemplateService.Instance;
            eventTemplateService.UpdateCurrentEventSettings(showSelectionOverlay: showOverlay);
            ShowSaveStatus(showOverlay ? "Selection overlay enabled" : "Selection overlay disabled");
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_templateItems != null)
            {
                var eventTemplateService = EventAITemplateService.Instance;
                foreach (var item in _templateItems)
                {
                    item.IsSelected = true;
                    eventTemplateService.SetTemplateSelected(item.Id, true);
                }
                ShowSaveStatus($"All {_templateItems.Count} templates selected");
            }
        }

        private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_templateItems != null)
            {
                var eventTemplateService = EventAITemplateService.Instance;
                foreach (var item in _templateItems)
                {
                    item.IsSelected = false;
                    item.IsDefault = false;
                    eventTemplateService.SetTemplateSelected(item.Id, false);
                }
                ShowSaveStatus("All templates deselected");
            }
        }

        private void FilterSelected_Changed(object sender, RoutedEventArgs e)
        {
            // Toggle between showing all templates and only those selected for the event
            LoadAllTemplates();
        }

        private void AddTemplate_Click(object sender, RoutedEventArgs e)
        {
            ShowAddTemplateDialog();
        }

        private void ShowAddTemplateDialog()
        {
            // Create dialog window
            var dialog = new Window
            {
                Title = "Create New AI Template",
                Width = 650,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Owner = Window.GetWindow(this),
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                Foreground = Brushes.White
            };

            // Main container
            var mainGrid = new Grid();
            mainGrid.Margin = new Thickness(20);

            // Create scrollable content
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var stackPanel = new StackPanel();

            // Helper function to add labeled input
            Action<string, FrameworkElement> addField = (label, control) =>
            {
                var labelText = new TextBlock
                {
                    Text = label,
                    FontSize = 13,
                    Margin = new Thickness(0, 10, 0, 5),
                    Foreground = Brushes.LightGray
                };
                stackPanel.Children.Add(labelText);
                stackPanel.Children.Add(control);
            };

            // Template Name
            var nameTextBox = new TextBox
            {
                Height = 30,
                Padding = new Thickness(8),
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70))
            };
            addField("Template Name *", nameTextBox);

            // Category ComboBox
            var categoryCombo = new ComboBox
            {
                Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70))
            };

            // Get existing category names
            var categories = _templateService.GetAllTemplates()
                .Where(t => t.Category != null && !string.IsNullOrEmpty(t.Category.Name))
                .Select(t => t.Category.Name)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            // Add default categories if they don't exist
            var defaultCategories = new[] { "Fun", "Professional", "Artistic", "Seasonal", "Custom" };
            foreach (var cat in defaultCategories)
            {
                if (!categories.Contains(cat))
                    categories.Add(cat);
            }

            categoryCombo.ItemsSource = categories.OrderBy(c => c);
            categoryCombo.SelectedIndex = 0;
            categoryCombo.IsEditable = true; // Allow custom categories
            addField("Category", categoryCombo);

            // Model Selection
            var modelCombo = new ComboBox
            {
                Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70))
            };

            var modelManager = AIModelManager.Instance;
            var models = modelManager.AvailableModels;
            modelCombo.ItemsSource = models;
            modelCombo.DisplayMemberPath = "Name";
            modelCombo.SelectedValuePath = "Id";
            if (models.Any())
                modelCombo.SelectedIndex = 0;
            addField("AI Model", modelCombo);

            // Prompt
            var promptTextBox = new TextBox
            {
                Height = 80,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8),
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70))
            };
            addField("Prompt *", promptTextBox);

            // Negative Prompt
            var negativePromptTextBox = new TextBox
            {
                Height = 60,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8),
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70))
            };
            addField("Negative Prompt (Optional)", negativePromptTextBox);

            // Thumbnail Image
            var thumbnailPath = "";
            var thumbnailButton = new Button
            {
                Content = "Browse for Thumbnail Image...",
                Height = 35,
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                Cursor = Cursors.Hand
            };

            var thumbnailLabel = new TextBlock
            {
                Text = "No file selected",
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 5, 0, 0)
            };

            thumbnailButton.Click += (s, args) =>
            {
                var openDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Image files (*.jpg, *.jpeg, *.png)|*.jpg;*.jpeg;*.png",
                    Title = "Select Thumbnail Image"
                };

                if (openDialog.ShowDialog() == true)
                {
                    thumbnailPath = openDialog.FileName;
                    thumbnailLabel.Text = System.IO.Path.GetFileName(thumbnailPath);
                    thumbnailLabel.Foreground = Brushes.LightGreen;
                }
            };

            addField("Thumbnail Image", thumbnailButton);
            stackPanel.Children.Add(thumbnailLabel);

            // Is Active checkbox
            var isActiveCheckBox = new CheckBox
            {
                Content = "Template is Active",
                IsChecked = true,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 15, 0, 0)
            };
            stackPanel.Children.Add(isActiveCheckBox);

            // Button panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 100,
                Height = 35,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                Foreground = Brushes.White,
                Cursor = Cursors.Hand
            };
            cancelButton.Click += (s, args) => dialog.Close();

            var createButton = new Button
            {
                Content = "Create Template",
                Width = 120,
                Height = 35,
                Background = new SolidColorBrush(Color.FromRgb(92, 191, 96)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };

            createButton.Click += (s, args) =>
            {
                // Validate required fields
                if (string.IsNullOrWhiteSpace(nameTextBox.Text))
                {
                    MessageBox.Show("Please enter a template name.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(promptTextBox.Text))
                {
                    MessageBox.Show("Please enter a prompt.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    // Find or create the category
                    var categoryName = categoryCombo.Text ?? "Custom";
                    var database = AITemplateDatabase.Instance;
                    var existingCategory = database.GetCategories()
                        .FirstOrDefault(c => c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));

                    var categoryId = existingCategory?.Id ?? 1; // Default to first category if not found

                    // Create new template
                    var newTemplate = new AITransformationTemplate
                    {
                        Name = nameTextBox.Text.Trim(),
                        Description = $"Custom template created on {DateTime.Now:yyyy-MM-dd}",
                        Category = existingCategory ?? new AITemplateCategory { Id = categoryId, Name = categoryName },
                        Prompt = promptTextBox.Text.Trim(),
                        NegativePrompt = negativePromptTextBox.Text?.Trim(),
                        ThumbnailPath = thumbnailPath,
                        IsActive = isActiveCheckBox.IsChecked ?? true,
                        CreatedDate = DateTime.Now,
                        LastModified = DateTime.Now
                    };

                    // Set model preference if selected
                    var selectedModel = modelCombo.SelectedItem as AIModelDefinition;
                    if (selectedModel != null)
                    {
                        // Store model preference (this would need a new column in the database)
                        // For now, we'll just note it in the prompt metadata
                        newTemplate.Prompt = $"[Model: {selectedModel.Id}] {newTemplate.Prompt}";
                    }

                    // Save to database
                    _templateService.AddTemplate(newTemplate, categoryId);

                    // Close dialog
                    dialog.Close();

                    // Refresh the template grid
                    LoadAllTemplates();

                    MessageBox.Show($"Template '{newTemplate.Name}' created successfully!",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error creating template: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(createButton);
            stackPanel.Children.Add(buttonPanel);

            scrollViewer.Content = stackPanel;
            mainGrid.Children.Add(scrollViewer);
            dialog.Content = mainGrid;

            // Show dialog
            dialog.ShowDialog();
        }

        private void DeleteTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (_templateItems == null || !_templateItems.Any(t => t.IsSelected))
            {
                MessageBox.Show("Please select at least one template to delete.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedCount = _templateItems.Count(t => t.IsSelected);
            var result = MessageBox.Show($"Are you sure you want to delete {selectedCount} selected template(s)?\n\nThis action cannot be undone.",
                "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var templateService = AITemplateService.Instance;
                var deletedCount = 0;

                foreach (var item in _templateItems.Where(t => t.IsSelected).ToList())
                {
                    try
                    {
                        // Delete from database
                        templateService.DeleteTemplate(item.Id);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AITransformationOverlay] Error deleting template {item.Id}: {ex.Message}");
                    }
                }

                if (deletedCount > 0)
                {
                    ShowSaveStatus($"Deleted {deletedCount} template(s)");
                    LoadCategories(); // Refresh categories
                    LoadAllTemplates(); // Refresh templates
                }
            }
        }

        private void RenameTemplate_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.Tag is Database.AITransformationTemplate template)
            {
                ShowRenameTemplateDialog(template);
            }
        }

        private void ShowRenameTemplateDialog(Database.AITransformationTemplate template)
        {
            var dialog = new Window
            {
                Title = "Rename Template",
                Width = 500,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Owner = Window.GetWindow(this),
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                Foreground = Brushes.White,
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Current name label
            var currentLabel = new TextBlock
            {
                Text = "Current Name:",
                FontSize = 12,
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 0, 0, 5)
            };
            Grid.SetRow(currentLabel, 0);
            grid.Children.Add(currentLabel);

            var currentName = new TextBlock
            {
                Text = template.Name,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(currentName, 1);
            grid.Children.Add(currentName);

            // New name input
            var newNameLabel = new TextBlock
            {
                Text = "New Name:",
                FontSize = 12,
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 0, 0, 5)
            };
            Grid.SetRow(newNameLabel, 2);
            grid.Children.Add(newNameLabel);

            var nameTextBox = new TextBox
            {
                Text = template.Name,
                Height = 35,
                Padding = new Thickness(8),
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                FontSize = 14
            };
            Grid.SetRow(nameTextBox, 3);
            grid.Children.Add(nameTextBox);

            // Button panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 100,
                Height = 35,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                Foreground = Brushes.White,
                Cursor = Cursors.Hand
            };
            cancelButton.Click += (s, args) => dialog.Close();

            var saveButton = new Button
            {
                Content = "Rename",
                Width = 100,
                Height = 35,
                Background = new SolidColorBrush(Color.FromRgb(92, 191, 96)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };

            saveButton.Click += (s, args) =>
            {
                var newName = nameTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(newName))
                {
                    MessageBox.Show("Please enter a valid name.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (newName == template.Name)
                {
                    dialog.Close();
                    return;
                }

                try
                {
                    template.Name = newName;
                    template.LastModified = DateTime.Now;

                    var templateService = AITemplateService.Instance;
                    templateService.UpdateTemplate(template);

                    ShowSaveStatus($"Template renamed to '{newName}'");
                    LoadAllTemplates(); // Refresh the grid

                    dialog.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error renaming template: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(saveButton);
            Grid.SetRow(buttonPanel, 4);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;

            // Select all text in the textbox when dialog opens
            nameTextBox.Loaded += (s, e) =>
            {
                nameTextBox.Focus();
                nameTextBox.SelectAll();
            };

            // Allow Enter to save
            nameTextBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            };

            dialog.ShowDialog();
        }

        private void TemplateCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var checkbox = sender as CheckBox;
            if (checkbox?.Tag is int templateId)
            {
                var eventTemplateService = EventAITemplateService.Instance;
                eventTemplateService.SetTemplateSelected(templateId, checkbox.IsChecked == true);

                // If unchecked and was default, clear default
                if (checkbox.IsChecked == false)
                {
                    var item = _templateItems?.FirstOrDefault(t => t.Id == templateId);
                    if (item?.IsDefault == true)
                    {
                        item.IsDefault = false;
                        eventTemplateService.SetDefaultTemplate(-1); // Clear default
                    }
                }

                // Show save feedback
                ShowSaveStatus("Selection saved");
            }
        }

        private void DefaultRadioButton_Changed(object sender, RoutedEventArgs e)
        {
            var radio = sender as RadioButton;
            if (radio?.Tag is int templateId && radio.IsChecked == true)
            {
                var eventTemplateService = EventAITemplateService.Instance;

                // First ensure the template is selected
                eventTemplateService.SetTemplateSelected(templateId, true);

                // Then set it as default
                eventTemplateService.SetDefaultTemplate(templateId);

                // Update all items
                if (_templateItems != null)
                {
                    foreach (var item in _templateItems)
                    {
                        if (item.Id == templateId)
                        {
                            item.IsSelected = true; // Ensure it's selected
                            item.IsDefault = true;
                        }
                        else
                        {
                            item.IsDefault = false;
                        }
                    }
                }

                // Show save feedback
                ShowSaveStatus("Default template set");
            }
        }

        private async void UploadThumbnail_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var template = button?.Tag as Database.AITransformationTemplate;
            if (template == null) return;

            // Open file dialog
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files (*.jpg, *.jpeg, *.png)|*.jpg;*.jpeg;*.png",
                Title = "Select Sample Image"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    // Copy image to thumbnails folder
                    var thumbnailDir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Photobooth", "AITemplates", "Thumbnails"
                    );
                    System.IO.Directory.CreateDirectory(thumbnailDir);

                    var fileName = $"template_{template.Id}_{System.IO.Path.GetFileName(openFileDialog.FileName)}";
                    var destPath = System.IO.Path.Combine(thumbnailDir, fileName);

                    System.IO.File.Copy(openFileDialog.FileName, destPath, true);

                    // Update template
                    template.ThumbnailPath = destPath;
                    var templateService = AITemplateService.Instance;
                    templateService.UpdateTemplate(template);

                    // Refresh UI
                    LoadAllTemplates();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to upload thumbnail: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void UpdateTemplateSelections()
        {
            // Update the event template service with current selections
            var eventTemplateService = EventAITemplateService.Instance;
            if (_templateItems != null)
            {
                foreach (var item in _templateItems)
                {
                    eventTemplateService.SetTemplateSelected(item.Id, item.IsSelected);
                }
            }
        }

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

        private void ModelSelection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (ModelSelectionComboBox.SelectedValue != null)
            {
                var modelManager = AIModelManager.Instance;
                modelManager.SelectedModelId = ModelSelectionComboBox.SelectedValue.ToString();
                ShowSaveStatus($"Model changed to: {modelManager.SelectedModel?.Name}");
            }
        }

        private void EditPromptsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var modelManager = AIModelManager.Instance;
                var currentModel = modelManager.SelectedModel;

                if (currentModel == null)
                {
                    MessageBox.Show("Please select an AI model first", "No Model Selected",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                ShowPromptEditor(currentModel);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening prompt editor: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddModelButton_Click(object sender, RoutedEventArgs e)
        {
            // Show dialog to add custom model
            ShowAddModelDialog();
        }

        private void ShowPromptEditor(AIModelDefinition model)
        {
            var window = new Window
            {
                Title = $"Edit Prompts for {model.Name}",
                Width = 900,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
                Owner = Window.GetWindow(this)
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(20)
            };

            var stackPanel = new StackPanel();

            // Header
            var headerText = new TextBlock
            {
                Text = $"Customize prompts for {model.Name}",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 20)
            };
            stackPanel.Children.Add(headerText);

            // Get all templates
            var templates = _templateService.GetAllTemplates();
            var modelManager = AIModelManager.Instance;

            foreach (var template in templates)
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 0, 15),
                    Padding = new Thickness(15)
                };

                var templateStack = new StackPanel();

                // Template name
                var nameText = new TextBlock
                {
                    Text = template.Name,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                templateStack.Children.Add(nameText);

                // Get existing custom prompt
                var existingPrompt = modelManager.GetPromptForTemplate(model.Id, template.Id.ToString());

                // Prompt
                var promptLabel = new TextBlock
                {
                    Text = "Prompt:",
                    FontSize = 12,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                templateStack.Children.Add(promptLabel);

                var promptBox = new TextBox
                {
                    Text = existingPrompt?.Prompt ?? template.Prompt,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    MinHeight = 80,
                    Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)),
                    Foreground = Brushes.White,
                    Padding = new Thickness(8),
                    Tag = template.Id
                };
                templateStack.Children.Add(promptBox);

                // Negative prompt if supported
                if (model.Capabilities.SupportsNegativePrompt)
                {
                    var negLabel = new TextBlock
                    {
                        Text = "Negative Prompt:",
                        FontSize = 12,
                        Foreground = Brushes.White,
                        Margin = new Thickness(0, 10, 0, 5)
                    };
                    templateStack.Children.Add(negLabel);

                    var negBox = new TextBox
                    {
                        Text = existingPrompt?.NegativePrompt ?? template.NegativePrompt ?? "",
                        TextWrapping = TextWrapping.Wrap,
                        AcceptsReturn = true,
                        MinHeight = 50,
                        Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)),
                        Foreground = Brushes.White,
                        Padding = new Thickness(8),
                        Name = $"Neg_{template.Id}"
                    };
                    templateStack.Children.Add(negBox);
                }

                border.Child = templateStack;
                stackPanel.Children.Add(border);
            }

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var saveButton = new Button
            {
                Content = "Save All",
                Width = 120,
                Height = 40,
                Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                Foreground = Brushes.White,
                FontSize = 14,
                Margin = new Thickness(0, 0, 10, 0)
            };
            saveButton.Click += (s, args) =>
            {
                // Save all prompts
                foreach (var child in stackPanel.Children)
                {
                    if (child is Border b && b.Child is StackPanel ts)
                    {
                        var promptBox = ts.Children.OfType<TextBox>().FirstOrDefault(tb => tb.Tag != null);
                        if (promptBox != null)
                        {
                            var templateId = promptBox.Tag.ToString();
                            var prompt = promptBox.Text;
                            var negBox = ts.Children.OfType<TextBox>().FirstOrDefault(tb => tb.Name?.StartsWith("Neg_") == true);
                            var negPrompt = negBox?.Text ?? "";

                            modelManager.SetPromptForTemplate(model.Id, templateId, prompt, negPrompt);
                        }
                    }
                }
                ShowSaveStatus("Prompts saved successfully");
                window.Close();
            };
            buttonPanel.Children.Add(saveButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 100,
                Height = 40,
                Background = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
                Foreground = Brushes.White,
                FontSize = 14
            };
            cancelButton.Click += (s, args) => window.Close();
            buttonPanel.Children.Add(cancelButton);

            stackPanel.Children.Add(buttonPanel);
            scrollViewer.Content = stackPanel;
            window.Content = scrollViewer;

            window.ShowDialog();
        }

        private void ShowAddModelDialog()
        {
            var window = new Window
            {
                Title = "Add Custom AI Model",
                Width = 600,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
                Owner = Window.GetWindow(this)
            };

            var mainGrid = new Grid();
            mainGrid.Margin = new Thickness(20);

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var stackPanel = new StackPanel();

            // Header
            var headerText = new TextBlock
            {
                Text = "Add Custom AI Model",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 20)
            };
            stackPanel.Children.Add(headerText);

            // Model ID
            AddFormField(stackPanel, "Model ID:", "model-id", "e.g., custom-model-1");

            // Model Name
            AddFormField(stackPanel, "Model Name:", "model-name", "e.g., My Custom Model");

            // Description
            AddFormField(stackPanel, "Description:", "model-description", "Brief description of the model");

            // Provider
            var providerCombo = AddComboField(stackPanel, "Provider:", new[] { "replicate", "huggingface", "custom" });

            // Model Path
            AddFormField(stackPanel, "Model Path:", "model-path", "e.g., owner/model-name");

            // Model Version (optional)
            AddFormField(stackPanel, "Model Version (Optional):", "model-version", "Leave blank for latest");

            // Checkboxes
            var preservesIdentityCheck = AddCheckBox(stackPanel, "Preserves Identity",
                "Check if this model maintains the person's facial features");
            var supportsImageCheck = AddCheckBox(stackPanel, "Supports Image Input",
                "Check if this model accepts image input (uncheck for text-to-image)", true);
            var syncModeCheck = AddCheckBox(stackPanel, "Supports Synchronous Mode",
                "Check if this model can run synchronously");

            // Capabilities section
            stackPanel.Children.Add(new TextBlock
            {
                Text = "Model Capabilities",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 20, 0, 10)
            });

            var negativePromptCheck = AddCheckBox(stackPanel, "Supports Negative Prompt", null, true);
            var strengthCheck = AddCheckBox(stackPanel, "Supports Strength Parameter", null, true);
            var guidanceCheck = AddCheckBox(stackPanel, "Supports Guidance Scale", null, true);
            var stepsCheck = AddCheckBox(stackPanel, "Supports Steps", null, true);
            var seedCheck = AddCheckBox(stackPanel, "Supports Seed", null, true);

            // Button Panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 30, 0, 0)
            };

            var addButton = new Button
            {
                Content = "Add Model",
                Width = 120,
                Height = 40,
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                Foreground = Brushes.White,
                FontSize = 14,
                Margin = new Thickness(0, 0, 10, 0)
            };

            addButton.Click += (s, args) =>
            {
                try
                {
                    var modelId = GetTextBoxValue(stackPanel, "model-id");
                    var modelName = GetTextBoxValue(stackPanel, "model-name");
                    var description = GetTextBoxValue(stackPanel, "model-description");
                    var modelPath = GetTextBoxValue(stackPanel, "model-path");
                    var modelVersion = GetTextBoxValue(stackPanel, "model-version");

                    if (string.IsNullOrWhiteSpace(modelId) || string.IsNullOrWhiteSpace(modelName) ||
                        string.IsNullOrWhiteSpace(modelPath))
                    {
                        MessageBox.Show("Please fill in all required fields.", "Missing Information",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var newModel = new AIModelDefinition
                    {
                        Id = modelId.Trim(),
                        Name = modelName.Trim(),
                        Description = description?.Trim(),
                        Provider = providerCombo.SelectedItem?.ToString() ?? "replicate",
                        ModelPath = modelPath.Trim(),
                        ModelVersion = string.IsNullOrWhiteSpace(modelVersion) ? null : modelVersion.Trim(),
                        PreservesIdentity = preservesIdentityCheck.IsChecked ?? false,
                        SupportsImageInput = supportsImageCheck.IsChecked ?? true,
                        SupportsSynchronousMode = syncModeCheck.IsChecked ?? false,
                        ImageInputFormat = (supportsImageCheck.IsChecked ?? true) ? "dataurl" : "none",
                        Capabilities = new ModelCapabilities
                        {
                            SupportsNegativePrompt = negativePromptCheck.IsChecked ?? true,
                            SupportsStrength = strengthCheck.IsChecked ?? true,
                            SupportsGuidanceScale = guidanceCheck.IsChecked ?? true,
                            SupportsSteps = stepsCheck.IsChecked ?? true,
                            SupportsSeed = seedCheck.IsChecked ?? true
                        },
                        DefaultParameters = new ModelParameters
                        {
                            Strength = 0.7,
                            GuidanceScale = 7.5,
                            Steps = 30,
                            OutputFormat = "png"
                        },
                        IsActive = true
                    };

                    AIModelManager.Instance.AddModel(newModel);
                    InitializeModelSelection(); // Refresh the dropdown

                    ShowSaveStatus($"Model '{modelName}' added successfully");
                    window.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error adding model: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            buttonPanel.Children.Add(addButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 100,
                Height = 40,
                Background = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
                Foreground = Brushes.White,
                FontSize = 14
            };
            cancelButton.Click += (s, args) => window.Close();
            buttonPanel.Children.Add(cancelButton);

            stackPanel.Children.Add(buttonPanel);
            scrollViewer.Content = stackPanel;
            mainGrid.Children.Add(scrollViewer);
            window.Content = mainGrid;

            window.ShowDialog();
        }

        private void AddFormField(StackPanel parent, string label, string tag, string placeholder)
        {
            var fieldLabel = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 14,
                Margin = new Thickness(0, 10, 0, 5)
            };
            parent.Children.Add(fieldLabel);

            var textBox = new TextBox
            {
                Tag = tag,
                Height = 35,
                FontSize = 14,
                Padding = new Thickness(10, 8, 10, 8),
                Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x5A))
            };

            if (!string.IsNullOrEmpty(placeholder))
            {
                textBox.Text = placeholder;
                textBox.Foreground = Brushes.Gray;
                textBox.GotFocus += (s, e) =>
                {
                    if (textBox.Text == placeholder)
                    {
                        textBox.Text = "";
                        textBox.Foreground = Brushes.White;
                    }
                };
                textBox.LostFocus += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(textBox.Text))
                    {
                        textBox.Text = placeholder;
                        textBox.Foreground = Brushes.Gray;
                    }
                };
            }
            parent.Children.Add(textBox);
        }

        private ComboBox AddComboField(StackPanel parent, string label, string[] items)
        {
            var fieldLabel = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 14,
                Margin = new Thickness(0, 10, 0, 5)
            };
            parent.Children.Add(fieldLabel);

            var combo = new ComboBox
            {
                Height = 35,
                FontSize = 14,
                Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x5A))
            };

            foreach (var item in items)
            {
                combo.Items.Add(item);
            }
            combo.SelectedIndex = 0;
            parent.Children.Add(combo);
            return combo;
        }

        private CheckBox AddCheckBox(StackPanel parent, string label, string tooltip, bool isChecked = false)
        {
            var checkBox = new CheckBox
            {
                Content = label,
                Foreground = Brushes.White,
                FontSize = 14,
                Margin = new Thickness(0, 5, 0, 5),
                IsChecked = isChecked
            };

            if (!string.IsNullOrEmpty(tooltip))
            {
                checkBox.ToolTip = tooltip;
            }

            parent.Children.Add(checkBox);
            return checkBox;
        }

        private string GetTextBoxValue(StackPanel parent, string tag)
        {
            foreach (var child in parent.Children)
            {
                if (child is TextBox textBox && textBox.Tag?.ToString() == tag)
                {
                    var text = textBox.Text;
                    // Check if it's still the placeholder
                    if (textBox.Foreground == Brushes.Gray)
                        return "";
                    return text;
                }
            }
            return "";
        }

        private void TemplateModel_Changed(object sender, SelectionChangedEventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo?.SelectedValue != null && combo.Tag is int templateId)
            {
                var modelId = combo.SelectedValue.ToString();
                var modelManager = AIModelManager.Instance;
                modelManager.SetTemplateModelPreference(templateId, modelId);
                ShowSaveStatus($"Model updated for template");
                Debug.WriteLine($"[AITransformationOverlay] Template {templateId} assigned model: {modelId}");
            }
        }

        private void EditTemplatePrompt_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.Tag is Database.AITransformationTemplate template)
            {
                // Get the model combo for this template
                var container = FindParentContentPresenter(button);
                var modelCombo = FindVisualChild<ComboBox>(container, "TemplateModelCombo");

                string modelId = modelCombo?.SelectedValue?.ToString() ?? AIModelManager.Instance.SelectedModelId;
                var model = AIModelManager.Instance.GetModel(modelId);

                if (model == null)
                {
                    MessageBox.Show("Please select a model for this template first.",
                        "No Model Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                ShowSingleTemplatePromptEditor(template, model);
            }
        }

        private void ShowSingleTemplatePromptEditor(Database.AITransformationTemplate template, AIModelDefinition model)
        {
            var window = new Window
            {
                Title = $"Edit Prompt: {template.Name} - {model.Name}",
                Width = 700,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
                Owner = Window.GetWindow(this)
            };

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var headerPanel = new StackPanel();
            var headerText = new TextBlock
            {
                Text = template.Name,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            };
            headerPanel.Children.Add(headerText);

            var modelText = new TextBlock
            {
                Text = $"Model: {model.Name} - {(model.PreservesIdentity ? "Identity Preserving" : "Style Transfer")}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                Margin = new Thickness(0, 0, 0, 15)
            };
            headerPanel.Children.Add(modelText);
            Grid.SetRow(headerPanel, 0);
            grid.Children.Add(headerPanel);

            // Content
            var contentStack = new StackPanel();
            var modelManager = AIModelManager.Instance;
            var existingPrompt = modelManager.GetPromptForTemplate(model.Id, template.Id.ToString());

            // Prompt
            var promptLabel = new TextBlock
            {
                Text = "Prompt:",
                FontSize = 14,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            };
            contentStack.Children.Add(promptLabel);

            var promptBox = new TextBox
            {
                Text = existingPrompt?.Prompt ?? template.Prompt,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                MinHeight = 120,
                Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)),
                Foreground = Brushes.White,
                Padding = new Thickness(10),
                FontSize = 12
            };
            contentStack.Children.Add(promptBox);

            // Always show negative prompt field, but indicate if model doesn't support it
            var negLabel = new TextBlock
            {
                Text = model.Capabilities?.SupportsNegativePrompt == true
                    ? "Negative Prompt (what to avoid):"
                    : "Negative Prompt (not supported by this model):",
                FontSize = 14,
                Foreground = model.Capabilities?.SupportsNegativePrompt == true
                    ? Brushes.White
                    : new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                Margin = new Thickness(0, 15, 0, 5)
            };
            contentStack.Children.Add(negLabel);

            var negativePromptHint = new TextBlock
            {
                Text = "Examples: blurry, low quality, deformed, ugly, bad anatomy, watermark",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                Margin = new Thickness(0, 0, 0, 5),
                FontStyle = FontStyles.Italic
            };
            contentStack.Children.Add(negativePromptHint);

            var negPromptBox = new TextBox
            {
                Text = existingPrompt?.NegativePrompt ?? template.NegativePrompt ?? "",
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                MinHeight = 100,
                MaxHeight = 150,
                Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)),
                Foreground = model.Capabilities?.SupportsNegativePrompt == true
                    ? Brushes.White
                    : new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                Padding = new Thickness(10),
                FontSize = 12,
                IsEnabled = model.Capabilities?.SupportsNegativePrompt == true,
                BorderBrush = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                BorderThickness = new Thickness(1)
            };

            // Add placeholder behavior
            if (string.IsNullOrEmpty(negPromptBox.Text))
            {
                negPromptBox.Text = "Enter things to avoid in the generation...";
                negPromptBox.Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100));
                negPromptBox.GotFocus += (s, e) =>
                {
                    if (negPromptBox.Text == "Enter things to avoid in the generation...")
                    {
                        negPromptBox.Text = "";
                        negPromptBox.Foreground = model.Capabilities?.SupportsNegativePrompt == true
                            ? Brushes.White
                            : new SolidColorBrush(Color.FromRgb(160, 160, 160));
                    }
                };
                negPromptBox.LostFocus += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(negPromptBox.Text))
                    {
                        negPromptBox.Text = "Enter things to avoid in the generation...";
                        negPromptBox.Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100));
                    }
                };
            }
            contentStack.Children.Add(negPromptBox);

            var scrollViewer = new ScrollViewer
            {
                Content = contentStack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(scrollViewer, 1);
            grid.Children.Add(scrollViewer);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };

            var saveButton = new Button
            {
                Content = "Save",
                Width = 100,
                Height = 35,
                Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                Foreground = Brushes.White,
                FontSize = 14,
                Margin = new Thickness(0, 0, 10, 0)
            };
            saveButton.Click += (s, args) =>
            {
                // Get the negative prompt text, handling placeholder
                string negativePromptText = negPromptBox?.Text;
                if (negativePromptText == "Enter things to avoid in the generation...")
                {
                    negativePromptText = "";
                }

                // Save to model manager for custom prompts
                modelManager.SetPromptForTemplate(
                    model.Id,
                    template.Id.ToString(),
                    promptBox.Text,
                    negativePromptText ?? ""
                );

                // Also update the template in the database
                template.Prompt = promptBox.Text;
                template.NegativePrompt = string.IsNullOrWhiteSpace(negativePromptText) ? null : negativePromptText;
                _templateService.UpdateTemplate(template);

                ShowSaveStatus($"Prompts saved for {template.Name}");

                // Refresh the template list to show updated negative prompt
                LoadAllTemplates();

                window.Close();
            };
            buttonPanel.Children.Add(saveButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 100,
                Height = 35,
                Background = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
                Foreground = Brushes.White,
                FontSize = 14
            };
            cancelButton.Click += (s, args) => window.Close();
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            window.Content = grid;
            window.ShowDialog();
        }

        private ContentPresenter FindParentContentPresenter(DependencyObject child)
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is ContentPresenter))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as ContentPresenter;
        }

        private void ShowSaveStatus(string message)
        {
            // Show a temporary status message
            if (HeaderSubtitle != null)
            {
                var originalText = HeaderSubtitle.Text;
                HeaderSubtitle.Text = $"✓ {message}";
                HeaderSubtitle.Foreground = FindResource("AccentBrush") as System.Windows.Media.Brush;

                // Reset after 2 seconds
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                timer.Tick += (s, e) =>
                {
                    HeaderSubtitle.Text = _isManagementMode
                        ? "Configure AI transformation templates for this event"
                        : "Select a transformation style to apply to your photo";
                    HeaderSubtitle.Foreground = FindResource("TextSecondaryBrush") as System.Windows.Media.Brush;
                    timer.Stop();
                };
                timer.Start();
            }
        }

        private void TestTemplate_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.Tag is Database.AITransformationTemplate template)
            {
                // Open file browser to select test image
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select a test photo",
                    Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*",
                    CheckFileExists = true
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    ShowTestResultWindow(template, openFileDialog.FileName, button);
                }
            }
        }

        private void ShowTestResultWindow(Database.AITransformationTemplate template, string imagePath, Button sourceButton)
        {
            var window = new Window
            {
                Title = $"Test Template: {template.Name}",
                Width = 1000,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
                Owner = Window.GetWindow(this)
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var headerPanel = new StackPanel { Margin = new Thickness(20, 20, 20, 10) };
            var titleText = new TextBlock
            {
                Text = $"Testing: {template.Name}",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            headerPanel.Children.Add(titleText);

            var statusText = new TextBlock
            {
                Text = "Initializing...",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                Margin = new Thickness(0, 5, 0, 0)
            };
            headerPanel.Children.Add(statusText);
            Grid.SetRow(headerPanel, 0);
            mainGrid.Children.Add(headerPanel);

            // Content area
            var contentGrid = new Grid { Margin = new Thickness(20, 10, 20, 20) };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Original image
            var originalPanel = new StackPanel();
            var originalLabel = new TextBlock
            {
                Text = "Original",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            originalPanel.Children.Add(originalLabel);

            var originalImage = new Image
            {
                Source = new BitmapImage(new Uri(imagePath)),
                Stretch = Stretch.Uniform,
                MaxWidth = 450,
                MaxHeight = 450
            };
            var originalBorder = new Border
            {
                Child = originalImage,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40))
            };
            originalPanel.Children.Add(originalBorder);
            Grid.SetColumn(originalPanel, 0);
            contentGrid.Children.Add(originalPanel);

            // Arrow
            var arrowText = new TextBlock
            {
                Text = "➡️",
                FontSize = 30,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(20, 0, 20, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
            };
            Grid.SetColumn(arrowText, 1);
            contentGrid.Children.Add(arrowText);

            // Result image area
            var resultPanel = new StackPanel();
            var resultLabel = new TextBlock
            {
                Text = "AI Transformation Result",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            resultPanel.Children.Add(resultLabel);

            var resultContainer = new Border
            {
                MinWidth = 450,
                MinHeight = 450,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40))
            };

            // Progress indicator
            var progressPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var progressRing = new ProgressBar
            {
                IsIndeterminate = true,
                Width = 200,
                Height = 4,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
            };
            progressPanel.Children.Add(progressRing);
            var progressText = new TextBlock
            {
                Text = "Processing transformation...",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            };
            progressPanel.Children.Add(progressText);
            resultContainer.Child = progressPanel;

            resultPanel.Children.Add(resultContainer);
            Grid.SetColumn(resultPanel, 2);
            contentGrid.Children.Add(resultPanel);

            Grid.SetRow(contentGrid, 1);
            mainGrid.Children.Add(contentGrid);

            // Error display area
            var errorText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 0, 20, 10),
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(errorText, 2);
            mainGrid.Children.Add(errorText);

            // Button panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(20, 10, 20, 20)
            };

            var retryButton = new Button
            {
                Content = "Retry",
                Width = 100,
                Height = 35,
                Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                Foreground = Brushes.White,
                FontSize = 14,
                Margin = new Thickness(0, 0, 10, 0),
                IsEnabled = false
            };

            var saveButton = new Button
            {
                Content = "Save Result",
                Width = 120,
                Height = 35,
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                Foreground = Brushes.White,
                FontSize = 14,
                Margin = new Thickness(0, 0, 10, 0),
                IsEnabled = false
            };

            var closeButton = new Button
            {
                Content = "Close",
                Width = 100,
                Height = 35,
                Background = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
                Foreground = Brushes.White,
                FontSize = 14
            };
            closeButton.Click += (s, args) => window.Close();

            buttonPanel.Children.Add(retryButton);
            buttonPanel.Children.Add(saveButton);
            buttonPanel.Children.Add(closeButton);

            Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            window.Content = mainGrid;

            // Start the transformation
            Task.Run(async () =>
            {
                try
                {
                    await window.Dispatcher.InvokeAsync(() =>
                    {
                        statusText.Text = "Preparing image...";
                        progressText.Text = "Converting image to base64...";
                    });

                    await window.Dispatcher.InvokeAsync(() =>
                    {
                        statusText.Text = "Sending to AI service...";
                        progressText.Text = "Processing with AI model...";
                    });

                    // Get selected model
                    var modelManager = AIModelManager.Instance;
                    var container = await window.Dispatcher.InvokeAsync(() => FindParentContentPresenter(sourceButton as DependencyObject));
                    var modelCombo = await window.Dispatcher.InvokeAsync(() => FindVisualChild<ComboBox>(container, "TemplateModelCombo"));
                    string modelId = await window.Dispatcher.InvokeAsync(() => modelCombo?.SelectedValue?.ToString() ?? modelManager.SelectedModelId);

                    if (string.IsNullOrEmpty(modelId))
                    {
                        modelId = "google-nano-bison"; // Default model
                    }

                    // Apply transformation using the singleton instance
                    var transformationService = AITransformationService.Instance;
                    var apiToken = Properties.Settings.Default.ReplicateAPIToken;

                    string resultPath = await transformationService.ApplyTransformationAsync(imagePath, template, null);

                    if (!string.IsNullOrEmpty(resultPath))
                    {
                        await window.Dispatcher.InvokeAsync(() =>
                        {
                            // Display result
                            var resultImage = new Image
                            {
                                Source = new BitmapImage(new Uri(resultPath)),
                                Stretch = Stretch.Uniform,
                                MaxWidth = 450,
                                MaxHeight = 450
                            };
                            resultContainer.Child = resultImage;

                            statusText.Text = "Transformation completed successfully!";
                            statusText.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));

                            // Enable buttons
                            retryButton.IsEnabled = true;
                            saveButton.IsEnabled = true;

                            // Save button action
                            saveButton.Click += (s, args) =>
                            {
                                var saveDialog = new Microsoft.Win32.SaveFileDialog
                                {
                                    Title = "Save Transformed Image",
                                    FileName = $"{Path.GetFileNameWithoutExtension(imagePath)}_{template.Name.Replace(" ", "_")}.png",
                                    Filter = "PNG Image|*.png|JPEG Image|*.jpg|All Files|*.*"
                                };

                                if (saveDialog.ShowDialog() == true)
                                {
                                    File.Copy(resultPath, saveDialog.FileName, true);
                                    ShowSaveStatus("Image saved successfully");
                                }
                            };

                            // Retry button action
                            retryButton.Click += (s, args) =>
                            {
                                window.Close();
                                ShowTestResultWindow(template, imagePath, sourceButton);
                            };
                        });
                    }
                    else
                    {
                        await window.Dispatcher.InvokeAsync(() =>
                        {
                            // Show error
                            errorText.Text = "❌ Error: Transformation failed - please check your API token and model settings";
                            errorText.Visibility = Visibility.Visible;

                            statusText.Text = "Transformation failed";
                            statusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));

                            progressPanel.Visibility = Visibility.Collapsed;
                            retryButton.IsEnabled = true;

                            // Retry button action
                            retryButton.Click += (s, args) =>
                            {
                                window.Close();
                                ShowTestResultWindow(template, imagePath, sourceButton);
                            };
                        });
                    }
                }
                catch (Exception ex)
                {
                    await window.Dispatcher.InvokeAsync(() =>
                    {
                        errorText.Text = $"❌ Error: {ex.Message}";
                        errorText.Visibility = Visibility.Visible;

                        statusText.Text = "An error occurred";
                        statusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));

                        progressPanel.Visibility = Visibility.Collapsed;
                        retryButton.IsEnabled = true;

                        retryButton.Click += (s, args) =>
                        {
                            window.Close();
                            ShowTestResultWindow(template, imagePath, sourceButton);
                        };

                        Debug.WriteLine($"[AITransformationOverlay] Test transformation error: {ex}");
                    });
                }
            });

            window.ShowDialog();
        }

        #endregion
    }

    public class AITransformationCompletedEventArgs : EventArgs
    {
        public string OriginalPath { get; set; }
        public string TransformedPath { get; set; }
        public Database.AITransformationTemplate Template { get; set; }
    }

    public class TemplateSelectionItem : Database.AITransformationTemplate
    {
        private bool _isSelected;
        private bool _isDefault;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public bool IsDefault
        {
            get => _isDefault;
            set
            {
                _isDefault = value;
                OnPropertyChanged();
            }
        }

        public TemplateSelectionItem(Database.AITransformationTemplate template)
        {
            Id = template.Id;
            Name = template.Name;
            Description = template.Description;
            Category = template.Category;
            Prompt = template.Prompt;
            NegativePrompt = template.NegativePrompt;
            ModelVersion = template.ModelVersion;
            Steps = template.Steps;
            GuidanceScale = template.GuidanceScale;
            PromptStrength = template.PromptStrength;
            ThumbnailPath = string.IsNullOrEmpty(template.ThumbnailPath) ? null : template.ThumbnailPath;
            Seed = template.Seed;
            IsActive = template.IsActive;
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    // Value converter to handle null/empty ThumbnailPath
    public class ThumbnailPathConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || string.IsNullOrEmpty(value.ToString()))
                return null;

            try
            {
                var path = value.ToString();
                if (File.Exists(path))
                {
                    return new BitmapImage(new Uri(path, UriKind.Absolute));
                }
            }
            catch (Exception)
            {
                // If there's any error loading the image, return null
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}