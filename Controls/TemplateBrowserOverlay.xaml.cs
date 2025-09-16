using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Photobooth.Database;
using Photobooth.Models;
using Photobooth.Services;
using CameraControl.Devices;

namespace Photobooth.Controls
{
    /// <summary>
    /// Interaction logic for TemplateBrowserOverlay.xaml
    /// </summary>
    public partial class TemplateBrowserOverlay : UserControl
    {
        private readonly TemplateService _templateService;
        private readonly TemplateDatabase _templateDatabase;
        private List<TemplateItemViewModel> _allTemplates;
        private ObservableCollection<TemplateItemViewModel> _filteredTemplates;
        private TemplateItemViewModel _selectedTemplate;
        private bool _isSelectMode = false;

        public event EventHandler<TemplateData> TemplateSelected;
        public event EventHandler SelectionCancelled;

        public TemplateBrowserOverlay()
        {
            InitializeComponent();

            _templateService = new TemplateService();
            _templateDatabase = new TemplateDatabase();
            _filteredTemplates = new ObservableCollection<TemplateItemViewModel>();
            _allTemplates = new List<TemplateItemViewModel>();

            TemplatesList.ItemsSource = _filteredTemplates;
        }

        /// <summary>
        /// Show the overlay with animation
        /// </summary>
        public void ShowOverlay()
        {
            ShowOverlay(-1);
        }

        /// <summary>
        /// Show the overlay with a preselected template
        /// </summary>
        public void ShowOverlay(int preselectedTemplateId)
        {
            try
            {
                Log.Debug("TemplateBrowserOverlay: Showing overlay");

                // Make sure the control itself is visible
                this.Visibility = Visibility.Visible;

                // Load templates
                LoadTemplates(preselectedTemplateId);

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
                Log.Error($"TemplateBrowserOverlay: Failed to show overlay: {ex.Message}");
            }
        }

        /// <summary>
        /// Hide the overlay with animation
        /// </summary>
        public void HideOverlay()
        {
            try
            {
                Log.Debug("TemplateBrowserOverlay: Hiding overlay");

                // Hide management buttons
                HideTemplateManagementButtons();

                // Animate out
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (s, e) =>
                {
                    MainOverlay.Visibility = Visibility.Collapsed;
                    this.Visibility = Visibility.Collapsed;

                    // Clear search
                    SearchTextBox.Clear();
                    _selectedTemplate = null;
                };
                MainOverlay.BeginAnimation(OpacityProperty, fadeOut);
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateBrowserOverlay: Failed to hide overlay: {ex.Message}");
            }
        }

        private void LoadTemplates(int preselectedTemplateId = -1)
        {
            try
            {
                var templates = _templateDatabase.GetAllTemplates();
                _allTemplates.Clear();

                foreach (var template in templates)
                {
                    var viewModel = new TemplateItemViewModel(template);

                    // Generate thumbnail if needed
                    if (string.IsNullOrEmpty(template.ThumbnailImagePath) || !File.Exists(template.ThumbnailImagePath))
                    {
                        viewModel.ThumbnailImageSource = CreateTemplateThumbnail(template);
                        viewModel.HasThumbnail = viewModel.ThumbnailImageSource != null;
                    }
                    else
                    {
                        viewModel.ThumbnailImageSource = LoadImageFromPath(template.ThumbnailImagePath);
                        viewModel.HasThumbnail = viewModel.ThumbnailImageSource != null;
                    }

                    // Mark preselected template
                    if (preselectedTemplateId > 0 && template.Id == preselectedTemplateId)
                    {
                        viewModel.IsLastUsed = true;
                    }

                    _allTemplates.Add(viewModel);
                }

                FilterTemplates();
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateBrowserOverlay: Failed to load templates: {ex.Message}");
                ShowNoTemplatesMessage();
            }
        }

        private void FilterTemplates()
        {
            var searchText = SearchTextBox.Text?.ToLower() ?? "";

            var filtered = _allTemplates.Where(t =>
                string.IsNullOrEmpty(searchText) ||
                (t.Template.Name?.ToLower().Contains(searchText) == true) ||
                (t.Template.Description?.ToLower().Contains(searchText) == true)
            ).OrderByDescending(t => t.Template.ModifiedDate);

            _filteredTemplates.Clear();
            foreach (var template in filtered)
            {
                _filteredTemplates.Add(template);
            }

            // Update UI
            if (_filteredTemplates.Count == 0)
            {
                ShowNoTemplatesMessage();
            }
            else
            {
                HideNoTemplatesMessage();
            }
        }

        private void ShowNoTemplatesMessage()
        {
            NoTemplatesMessage.Visibility = Visibility.Visible;
        }

        private void HideNoTemplatesMessage()
        {
            NoTemplatesMessage.Visibility = Visibility.Collapsed;
        }

        private void ShowTemplateManagementButtons(TemplateItemViewModel template)
        {
            _selectedTemplate = template;
            TemplateManagementButtons.Visibility = Visibility.Visible;
            LoadSelectedButton.IsEnabled = true;

            // Store the selected template for button handlers
            DuplicateTemplateButton.Tag = template;
            RenameTemplateButton.Tag = template;
            ExportTemplateButton.Tag = template;
            DeleteTemplateButton.Tag = template;
        }

        private void HideTemplateManagementButtons()
        {
            TemplateManagementButtons.Visibility = Visibility.Collapsed;
            LoadSelectedButton.IsEnabled = false;
        }

        private void UpdateTemplateSelection(Border selectedCard)
        {
            // Update visual selection for all template cards
            foreach (var item in _filteredTemplates)
            {
                var container = TemplatesList.ItemContainerGenerator.ContainerFromItem(item) as ContentPresenter;
                if (container != null)
                {
                    var border = FindVisualChild<Border>(container, "TemplateCard");
                    if (border != null)
                    {
                        if (border == selectedCard)
                        {
                            // Highlight selected
                            border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                            border.BorderThickness = new Thickness(3);
                        }
                        else
                        {
                            // Reset others
                            border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A3A3A"));
                            border.BorderThickness = new Thickness(2);
                        }
                    }
                }
            }
        }

        #region Event Handlers

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

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterTemplates();
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
        }

        private void TemplateCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is TemplateItemViewModel template)
            {
                // Check if we're in select mode
                if (_isSelectMode)
                {
                    // Toggle selection
                    template.IsSelected = !template.IsSelected;
                    UpdateSelectionStatus();
                }
                else
                {
                    if (e.ClickCount == 2)
                    {
                        // Double click loads the template
                        LoadSelectedTemplate(template);
                    }
                    else
                    {
                        // Single click selects and shows management buttons
                        ShowTemplateManagementButtons(template);
                        UpdateTemplateSelection(border);
                    }
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
            DeleteSelectedTemplates();
        }

        private void LoadTemplate_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var template = button?.Tag as TemplateItemViewModel;
            if (template != null)
            {
                LoadSelectedTemplate(template);
            }
        }

        private void LoadSelectedTemplate(TemplateItemViewModel template)
        {
            if (template != null)
            {
                HideOverlay();
                TemplateSelected?.Invoke(this, template.Template);
            }
        }

        private void NewTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create a new blank template
                var newTemplate = new TemplateData
                {
                    Name = "New Template " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    Description = "Created from Template Browser",
                    CanvasWidth = 1920,
                    CanvasHeight = 1080,
                    BackgroundColor = "#FFFFFF",
                    CreatedDate = DateTime.Now,
                    ModifiedDate = DateTime.Now
                };

                // Save to database
                int templateId = _templateDatabase.SaveTemplate(newTemplate);
                if (templateId > 0)
                {
                    newTemplate.Id = templateId;
                    HideOverlay();
                    TemplateSelected?.Invoke(this, newTemplate);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateBrowserOverlay: Failed to create new template: {ex.Message}");
                MessageBox.Show($"Failed to create new template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Import Template",
                    Filter = "Template Package (*.zip)|*.zip|Template JSON (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = ".zip"
                };

                if (dialog.ShowDialog() == true)
                {
                    // TODO: Implement template import logic
                    // For now, just reload templates
                    LoadTemplates();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateBrowserOverlay: Failed to import template: {ex.Message}");
                MessageBox.Show($"Failed to import template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DuplicateTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sourceTemplate = (sender as Button)?.Tag as TemplateItemViewModel ?? _selectedTemplate;
                if (sourceTemplate != null)
                {
                    var newName = ShowInputDialog("Duplicate Template",
                                                  "Enter name for the duplicated template:",
                                                  sourceTemplate.Template.Name + " (Copy)");

                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        // Create a copy of the template
                        var duplicate = new TemplateData
                        {
                            Name = newName,
                            Description = sourceTemplate.Template.Description,
                            CanvasWidth = sourceTemplate.Template.CanvasWidth,
                            CanvasHeight = sourceTemplate.Template.CanvasHeight,
                            BackgroundColor = sourceTemplate.Template.BackgroundColor,
                            BackgroundImagePath = sourceTemplate.Template.BackgroundImagePath,
                            ThumbnailImagePath = sourceTemplate.Template.ThumbnailImagePath,
                            // Note: Canvas items will need to be copied separately
                            CanvasItems = sourceTemplate.Template.CanvasItems,
                            CreatedDate = DateTime.Now,
                            ModifiedDate = DateTime.Now
                        };

                        int newId = _templateDatabase.SaveTemplate(duplicate);
                        if (newId > 0)
                        {
                            LoadTemplates();
                            HideTemplateManagementButtons();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateBrowserOverlay: Failed to duplicate template: {ex.Message}");
                MessageBox.Show($"Failed to duplicate template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RenameTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var templateToRename = (sender as Button)?.Tag as TemplateItemViewModel ?? _selectedTemplate;
                if (templateToRename != null)
                {
                    var newName = ShowInputDialog("Rename Template",
                                                  "Enter new name for the template:",
                                                  templateToRename.Template.Name);

                    if (!string.IsNullOrWhiteSpace(newName) && newName != templateToRename.Template.Name)
                    {
                        templateToRename.Template.Name = newName;
                        templateToRename.Template.ModifiedDate = DateTime.Now;
                        _templateDatabase.UpdateTemplate(templateToRename.Template.Id, templateToRename.Template);
                        LoadTemplates();
                        HideTemplateManagementButtons();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateBrowserOverlay: Failed to rename template: {ex.Message}");
                MessageBox.Show($"Failed to rename template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var templateToExport = (sender as Button)?.Tag as TemplateItemViewModel ?? _selectedTemplate;
                if (templateToExport != null)
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Export Template",
                        FileName = templateToExport.Template.Name.Replace(" ", "_") + ".zip",
                        Filter = "Template Package (*.zip)|*.zip|All files (*.*)|*.*",
                        DefaultExt = ".zip"
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        // TODO: Implement template export logic
                        MessageBox.Show("Template export functionality will be implemented soon.", "Export Template",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateBrowserOverlay: Failed to export template: {ex.Message}");
                MessageBox.Show($"Failed to export template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var templateToDelete = (sender as Button)?.Tag as TemplateItemViewModel ?? _selectedTemplate;
                if (templateToDelete != null)
                {
                    var result = MessageBox.Show(
                        $"Are you sure you want to delete the template '{templateToDelete.Template.Name}'?\n\nThis action cannot be undone.",
                        "Delete Template",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        _templateDatabase.DeleteTemplate(templateToDelete.Template.Id);
                        LoadTemplates();
                        HideTemplateManagementButtons();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateBrowserOverlay: Failed to delete template: {ex.Message}");
                MessageBox.Show($"Failed to delete template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Multi-Select Methods

        private void EnterSelectMode()
        {
            _isSelectMode = true;

            // Show select mode UI
            SelectModePanel.Visibility = Visibility.Visible;
            TemplateManagementButtons.Visibility = Visibility.Collapsed;

            // Update button text
            SelectModeButton.Content = "✓ Exit Select Mode";

            // Clear any previous selections and make checkboxes visible
            foreach (var template in _allTemplates)
            {
                template.IsSelected = false;
                template.ShowCheckbox = true;
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
            foreach (var template in _allTemplates)
            {
                template.IsSelected = false;
                template.ShowCheckbox = false;
            }
        }

        private void SelectAll()
        {
            foreach (var template in _filteredTemplates)
            {
                template.IsSelected = true;
            }
            UpdateSelectionStatus();
        }

        private void DeselectAll()
        {
            foreach (var template in _filteredTemplates)
            {
                template.IsSelected = false;
            }
            UpdateSelectionStatus();
        }

        private void UpdateSelectionStatus()
        {
            var selectedCount = _filteredTemplates.Count(t => t.IsSelected);
            SelectionStatusText.Text = $"{selectedCount} selected";
            DeleteSelectedButton.IsEnabled = selectedCount > 0;
        }

        private async void DeleteSelectedTemplates()
        {
            var selectedTemplates = _filteredTemplates.Where(t => t.IsSelected).ToList();

            if (selectedTemplates.Count == 0) return;

            var message = selectedTemplates.Count == 1
                ? $"Are you sure you want to delete '{selectedTemplates[0].Template.Name}'?"
                : $"Are you sure you want to delete {selectedTemplates.Count} templates?";

            var result = MessageBox.Show(
                message + "\n\nThis action cannot be undone.",
                "Delete Templates",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    foreach (var template in selectedTemplates)
                    {
                        _templateDatabase.DeleteTemplate(template.Template.Id);
                    }

                    // Reload templates
                    LoadTemplates();

                    // Exit select mode
                    ExitSelectMode();

                    // Show success message
                    var successMessage = selectedTemplates.Count == 1
                        ? "Template deleted successfully"
                        : $"{selectedTemplates.Count} templates deleted successfully";

                    Log.Debug($"TemplateBrowserOverlay: {successMessage}");
                }
                catch (Exception ex)
                {
                    Log.Error($"TemplateBrowserOverlay: Failed to delete templates: {ex.Message}");
                    MessageBox.Show($"Failed to delete templates: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Helper Methods

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

        private BitmapImage CreateTemplateThumbnail(TemplateData template)
        {
            try
            {
                var drawingVisual = new DrawingVisual();
                using (var drawingContext = drawingVisual.RenderOpen())
                {
                    // Background
                    var backgroundColor = Colors.White;
                    if (!string.IsNullOrEmpty(template.BackgroundColor))
                    {
                        try
                        {
                            backgroundColor = (Color)ColorConverter.ConvertFromString(template.BackgroundColor);
                        }
                        catch { }
                    }

                    var backgroundBrush = new SolidColorBrush(backgroundColor);
                    drawingContext.DrawRectangle(backgroundBrush, null, new Rect(0, 0, 200, 150));

                    // Template indicator
                    drawingContext.DrawRectangle(null, new Pen(Brushes.Gray, 1), new Rect(10, 40, 180, 100));
                }

                var renderTargetBitmap = new RenderTargetBitmap(200, 150, 96, 96, PixelFormats.Pbgra32);
                renderTargetBitmap.Render(drawingVisual);

                var bitmapImage = new BitmapImage();
                using (var stream = new MemoryStream())
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(renderTargetBitmap));
                    encoder.Save(stream);
                    stream.Position = 0;

                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = stream;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();
                }

                return bitmapImage;
            }
            catch
            {
                return null;
            }
        }

        private BitmapImage LoadImageFromPath(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return null;

            try
            {
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                return bitmapImage;
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }

    // ViewModel for template items
    public class TemplateItemViewModel : INotifyPropertyChanged
    {
        public TemplateData Template { get; }

        private BitmapImage _thumbnailImageSource;
        public BitmapImage ThumbnailImageSource
        {
            get => _thumbnailImageSource;
            set
            {
                _thumbnailImageSource = value;
                OnPropertyChanged();
            }
        }

        private bool _hasThumbnail;
        public bool HasThumbnail
        {
            get => _hasThumbnail;
            set
            {
                _hasThumbnail = value;
                OnPropertyChanged();
            }
        }

        public string Name => Template.Name ?? "Untitled Template";
        public string Description => Template.Description ?? "No description";
        public DateTime CreatedDate => Template.CreatedDate;
        public string SizeText => $"{Template.CanvasWidth:0} × {Template.CanvasHeight:0}";

        private bool _isLastUsed;
        public bool IsLastUsed
        {
            get => _isLastUsed;
            set
            {
                _isLastUsed = value;
                OnPropertyChanged();
            }
        }

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

        public TemplateItemViewModel(TemplateData template)
        {
            Template = template;
            HasThumbnail = !string.IsNullOrEmpty(template.ThumbnailImagePath) && File.Exists(template.ThumbnailImagePath);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}