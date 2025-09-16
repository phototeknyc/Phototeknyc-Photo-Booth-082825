using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DesignerCanvas;
using Microsoft.Win32;
using Photobooth.Services;
using Photobooth.MVVM.ViewModels.Designer;
using Photobooth.Database;
using CameraControl.Devices;

namespace Photobooth.Controls
{
    /// <summary>
    /// Touch-optimized template designer control
    /// This is a simplified version that will be enhanced with full functionality
    /// </summary>
    public partial class TouchTemplateDesigner : UserControl
    {
        private TouchTemplateDesignerViewModel _viewModel;
        private double _currentZoom = 1.0;
        private int _placeholderCount = 0;
        private int _currentTemplateId = -1;

        public TouchTemplateDesigner()
        {
            InitializeComponent();
            _viewModel = new TouchTemplateDesignerViewModel();
            DataContext = _viewModel;
            InitializeCanvas();
            
            // Handle responsive layout
            SizeChanged += OnSizeChanged;
            Loaded += OnLoaded;
        }

        private void InitializeCanvas()
        {
            // Set up the designer canvas with touch support
            DesignerCanvas.AllowDrop = true;
            DesignerCanvas.Background = Brushes.White;
            
            // Enable touch manipulation
            DesignerCanvas.IsManipulationEnabled = true;
            DesignerCanvas.ManipulationDelta += Canvas_ManipulationDelta;
            DesignerCanvas.ManipulationStarting += Canvas_ManipulationStarting;
        }

        private void Canvas_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.ManipulationContainer = CanvasScrollViewer;
            e.Mode = ManipulationModes.All;
        }

        private void Canvas_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            // Handle pinch to zoom
            if (e.DeltaManipulation.Scale.X != 1.0 || e.DeltaManipulation.Scale.Y != 1.0)
            {
                double zoomDelta = (e.DeltaManipulation.Scale.X + e.DeltaManipulation.Scale.Y) / 2;
                _currentZoom *= zoomDelta;
                _currentZoom = Math.Max(0.1, Math.Min(5.0, _currentZoom));
                UpdateZoom();
            }
        }

        #region File Operations

        private void NewTemplate_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Create a new template? Any unsaved changes will be lost.", 
                "New Template", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                // Clear the canvas
                _placeholderCount = 0;
                TemplateNameText.Text = "Untitled";
                // TODO: Implement canvas clearing
            }
        }

        private async void SaveTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var templateName = Microsoft.VisualBasic.Interaction.InputBox(
                    "Enter template name:", "Save Template", TemplateNameText.Text);
                
                if (string.IsNullOrWhiteSpace(templateName))
                    return;

                // TODO: Implement proper save functionality
                TemplateNameText.Text = templateName;
                MessageBox.Show("Template save functionality will be implemented soon.", "Info", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving template: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log.Debug("TouchTemplateDesigner: Opening Template Browser Overlay");

                // Show the template browser overlay
                TemplateBrowserOverlay.Visibility = Visibility.Visible;
                TemplateBrowserOverlay.ShowOverlay(_currentTemplateId);

                // Handle template selection
                TemplateBrowserOverlay.TemplateSelected -= OnTemplateSelected;
                TemplateBrowserOverlay.TemplateSelected += OnTemplateSelected;

                // Handle cancellation
                TemplateBrowserOverlay.SelectionCancelled -= OnTemplateSelectionCancelled;
                TemplateBrowserOverlay.SelectionCancelled += OnTemplateSelectionCancelled;
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to open template browser: {ex.Message}");
                MessageBox.Show($"Failed to open template browser: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnTemplateSelected(object sender, TemplateData template)
        {
            try
            {
                Log.Debug($"TouchTemplateDesigner: Loading template {template.Name}");

                // Update template name display
                TemplateNameText.Text = template.Name ?? "Untitled";

                // Load template data into the canvas
                // TODO: Load the template's canvas items into DesignerCanvas
                // For now, just update the canvas size
                if (template.CanvasWidth > 0 && template.CanvasHeight > 0)
                {
                    ((FrameworkElement)DesignerCanvas).Width = template.CanvasWidth;
                    ((FrameworkElement)DesignerCanvas).Height = template.CanvasHeight;
                    UpdateCanvasSizeDisplay();
                }

                // Store current template ID for future reference
                _currentTemplateId = template.Id;

                Log.Debug($"TouchTemplateDesigner: Template {template.Name} loaded successfully");
            }
            catch (Exception ex)
            {
                Log.Error($"TouchTemplateDesigner: Failed to load template: {ex.Message}");
                MessageBox.Show($"Failed to load template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnTemplateSelectionCancelled(object sender, EventArgs e)
        {
            Log.Debug("TouchTemplateDesigner: Template selection cancelled");
        }

        private void UpdateCanvasSizeDisplay()
        {
            if (CanvasSizeText != null && DesignerCanvas != null)
            {
                var width = ((FrameworkElement)DesignerCanvas).Width;
                var height = ((FrameworkElement)DesignerCanvas).Height;
                CanvasSizeText.Text = $"{width} x {height}";
            }
        }

        private void ImportTemplate_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Template files (*.template)|*.template|All files (*.*)|*.*",
                Title = "Import Template"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _viewModel.ImportTemplate(openFileDialog.FileName);
            }
        }

        private void ExportTemplate_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Template files (*.template)|*.template|All files (*.*)|*.*",
                Title = "Export Template",
                FileName = $"{TemplateNameText.Text}.template"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                // TODO: Implement proper export
                MessageBox.Show("Template export will be implemented soon.", "Info", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        #endregion

        #region Canvas Operations

        private void AddPlaceholder_Click(object sender, RoutedEventArgs e)
        {
            _placeholderCount++;
            // TODO: Implement adding placeholder to canvas
            MessageBox.Show($"Placeholder {_placeholderCount} will be added.", "Info", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AddText_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement adding text
            MessageBox.Show("Text addition will be implemented soon.", "Info", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AddImage_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Image files (*.jpg, *.jpeg, *.png, *.bmp)|*.jpg;*.jpeg;*.png;*.bmp",
                Title = "Select Image"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                // TODO: Implement adding image
                MessageBox.Show("Image addition will be implemented soon.", "Info", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void AddShape_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement adding shape
            MessageBox.Show("Shape addition will be implemented soon.", "Info", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region Arrange Operations

        private void BringToFront_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement bring to front
        }

        private void SendToBack_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement send to back
        }

        private void AlignItems_Click(object sender, RoutedEventArgs e)
        {
            // Show alignment menu
            var contextMenu = new ContextMenu();
            
            var alignLeftItem = new MenuItem { Header = "Align Left" };
            contextMenu.Items.Add(alignLeftItem);
            
            var alignCenterItem = new MenuItem { Header = "Align Center" };
            contextMenu.Items.Add(alignCenterItem);
            
            var alignRightItem = new MenuItem { Header = "Align Right" };
            contextMenu.Items.Add(alignRightItem);
            
            contextMenu.IsOpen = true;
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement delete selected
        }

        #endregion

        #region Zoom Operations

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            _currentZoom = Math.Min(_currentZoom * 1.2, 5.0);
            UpdateZoom();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _currentZoom = Math.Max(_currentZoom / 1.2, 0.1);
            UpdateZoom();
        }

        private void ZoomFit_Click(object sender, RoutedEventArgs e)
        {
            // Calculate zoom to fit canvas in viewport
            double viewportWidth = CanvasScrollViewer.ActualWidth - 120;
            double viewportHeight = CanvasScrollViewer.ActualHeight - 40;
            
            double scaleX = viewportWidth / DesignerCanvas.ActualWidth;
            double scaleY = viewportHeight / DesignerCanvas.ActualHeight;
            
            _currentZoom = Math.Min(scaleX, scaleY);
            _currentZoom = Math.Max(0.1, Math.Min(1.0, _currentZoom));
            
            UpdateZoom();
        }

        private void UpdateZoom()
        {
            CanvasScaleTransform.ScaleX = _currentZoom;
            CanvasScaleTransform.ScaleY = _currentZoom;
            ZoomLevelText.Text = $"{(_currentZoom * 100):F0}%";
        }

        #endregion

        #region Edit Operations

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement undo
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement redo
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement copy
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement paste
        }

        #endregion

        #region Properties Panel

        private void PropertiesToggle_Click(object sender, RoutedEventArgs e)
        {
            if (PropertiesToggle.IsChecked == true)
            {
                ShowPropertiesPanel();
            }
            else
            {
                HidePropertiesPanel();
            }
        }

        private void CloseProperties_Click(object sender, RoutedEventArgs e)
        {
            PropertiesToggle.IsChecked = false;
            HidePropertiesPanel();
        }

        private void ShowPropertiesPanel()
        {
            PropertiesPanel.Visibility = Visibility.Visible;
            
            var animation = new DoubleAnimation
            {
                From = 400,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            PropertiesPanelTransform.BeginAnimation(TranslateTransform.XProperty, animation);
            UpdatePropertiesPanel();
        }

        private void HidePropertiesPanel()
        {
            var animation = new DoubleAnimation
            {
                From = 0,
                To = 400,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            
            animation.Completed += (s, e) => PropertiesPanel.Visibility = Visibility.Collapsed;
            PropertiesPanelTransform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        private void UpdatePropertiesPanel()
        {
            PropertiesContent.Children.Clear();
            
            PropertiesContent.Children.Add(new TextBlock
            {
                Text = "Properties panel will be implemented soon",
                Foreground = Brushes.Gray,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 0)
            });
        }

        #endregion

        #region Responsive Layout

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            AdaptToScreenSize();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            AdaptToScreenSize();
        }

        private void AdaptToScreenSize()
        {
            double width = ActualWidth;
            double height = ActualHeight;
            
            // Determine orientation
            bool isPortrait = height > width;
            bool isSmallScreen = width < 1024 || height < 768;
            bool isMediumScreen = width < 1440 || height < 900;
            
            // Adjust button sizes based on screen size
            double buttonSize = isSmallScreen ? 44 : (isMediumScreen ? 50 : 60);
            UpdateButtonSizes(buttonSize);
            
            // Adjust layout for portrait mode
            if (isPortrait)
            {
                ConfigurePortraitLayout();
            }
            else
            {
                ConfigureLandscapeLayout();
            }
            
            // Adjust canvas container margins
            double margin = isSmallScreen ? 5 : (isMediumScreen ? 10 : 20);
            CanvasContainerBorder.Margin = new Thickness(margin);
            
            // Adjust properties panel width
            double propertiesWidth = Math.Min(width * 0.3, 500);
            propertiesWidth = Math.Max(propertiesWidth, 280);
            PropertiesPanel.Width = propertiesWidth;
            
            // Adjust toolbar heights
            TopToolbarRow.Height = new GridLength(isSmallScreen ? 60 : 80);
            BottomToolbarRow.Height = new GridLength(isSmallScreen ? 80 : 100);
            
            // Adjust side tools panel
            if (SideToolsPanel != null)
            {
                SideToolsPanel.Width = isSmallScreen ? 60 : (isMediumScreen ? 70 : 80);
            }
            
            // Update zoom controls visibility
            if (isSmallScreen && isPortrait)
            {
                ZoomControlsPanel.Margin = new Thickness(5);
            }
            else
            {
                ZoomControlsPanel.Margin = new Thickness(10);
            }
        }
        
        private void UpdateButtonSizes(double size)
        {
            var buttonStyle = Resources["TouchToolButton"] as Style;
            if (buttonStyle != null)
            {
                foreach (var button in FindVisualChildren<Button>(this))
                {
                    if (button.Style == buttonStyle)
                    {
                        button.MinWidth = size;
                        button.MinHeight = size;
                    }
                }
            }
        }
        
        private void ConfigurePortraitLayout()
        {
            if (SideToolsPanel != null)
            {
                Grid.SetRow(SideToolsPanel, 2);
                SideToolsPanel.HorizontalAlignment = HorizontalAlignment.Center;
                SideToolsPanel.VerticalAlignment = VerticalAlignment.Center;
                SideToolsPanel.Width = double.NaN;
                SideToolsPanel.MaxWidth = ActualWidth * 0.8;
            }
            
            CanvasScrollViewer.Margin = new Thickness(5);
        }
        
        private void ConfigureLandscapeLayout()
        {
            if (SideToolsPanel != null)
            {
                Grid.SetRow(SideToolsPanel, 1);
                SideToolsPanel.HorizontalAlignment = HorizontalAlignment.Left;
                SideToolsPanel.VerticalAlignment = VerticalAlignment.Center;
                SideToolsPanel.Width = 80;
                SideToolsPanel.MaxWidth = 100;
            }
            
            CanvasScrollViewer.Margin = new Thickness(90, 10, 10, 10);
        }
        
        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T)
                    {
                        yield return (T)child;
                    }

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }

        #endregion

        #region Other Operations

        private void RenameTemplate_Click(object sender, RoutedEventArgs e)
        {
            var newName = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter new template name:", "Rename Template", TemplateNameText.Text);
            
            if (!string.IsNullOrWhiteSpace(newName))
            {
                TemplateNameText.Text = newName;
            }
        }

        private void ChangeCanvasSize_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement canvas size change dialog
            MessageBox.Show("Canvas size change will be implemented soon.", "Info", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            var parent = Parent as Panel;
            parent?.Children.Remove(this);
        }

        #endregion
    }
}