using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesignerCanvas;
using Photobooth.Database;
using Photobooth.Services;

namespace Photobooth.MVVM.ViewModels.Designer
{
    public class TouchTemplateDesignerViewModel : INotifyPropertyChanged
    {
        private readonly TemplateService _templateService;
        private readonly TemplateDatabase _database;
        private ObservableCollection<TemplateData> _templates;
        private TemplateData _currentTemplate;
        private bool _hasUnsavedChanges;
        private string _statusMessage;

        public ObservableCollection<TemplateData> Templates
        {
            get => _templates;
            set => SetProperty(ref _templates, value);
        }

        public TemplateData CurrentTemplate
        {
            get => _currentTemplate;
            set => SetProperty(ref _currentTemplate, value);
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set => SetProperty(ref _hasUnsavedChanges, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public TouchTemplateDesignerViewModel()
        {
            _templateService = new TemplateService();
            _database = new TemplateDatabase();
            Templates = new ObservableCollection<TemplateData>();
            LoadTemplates();
        }

        public void LoadTemplates()
        {
            try
            {
                var templates = _templateService.GetAllTemplates();
                Templates.Clear();
                foreach (var template in templates)
                {
                    Templates.Add(template);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading templates: {ex.Message}";
            }
        }

        public void ExportTemplate(string filePath, List<ICanvasItem> items, dynamic canvas)
        {
            try
            {
                // Create a temporary directory for the template package
                string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                // Create template metadata
                var templateData = new TemplateData
                {
                    Name = Path.GetFileNameWithoutExtension(filePath),
                    CanvasWidth = canvas.ActualWidth,
                    CanvasHeight = canvas.ActualHeight,
                    BackgroundColor = BrushToColorString(canvas.Background),
                    CreatedDate = DateTime.Now,
                    ModifiedDate = DateTime.Now,
                    CanvasItems = new List<CanvasItemData>()
                };

                // Build list of items with actual z-index
                var itemsWithZIndex = new List<(ICanvasItem item, int zIndex)>();
                foreach (var item in items)
                {
                    var container = canvas.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                    int actualZIndex = 0;
                    if (container?.Parent is System.Windows.Controls.Canvas canvasParent)
                    {
                        actualZIndex = canvasParent.Children.IndexOf(container);
                    }
                    itemsWithZIndex.Add((item, actualZIndex));
                }

                // Sort by z-index
                itemsWithZIndex.Sort((a, b) => a.zIndex.CompareTo(b.zIndex));

                // Normalize z-indices
                for (int i = 0; i < itemsWithZIndex.Count; i++)
                {
                    var (item, _) = itemsWithZIndex[i];
                    var canvasItemData = ConvertToCanvasItemData(item, 0, i);
                    
                    // Handle image export
                    if (item is ImageCanvasItem imageItem && imageItem.Image is BitmapImage bitmapImage)
                    {
                        string imageName = $"image_{i}.png";
                        string imagePath = Path.Combine(tempDir, imageName);
                        SaveImageToFile(bitmapImage, imagePath);
                        canvasItemData.ImagePath = imageName; // Store relative path
                    }
                    
                    templateData.CanvasItems.Add(canvasItemData);
                }

                // Save metadata as JSON
                string metadataPath = Path.Combine(tempDir, "template.json");
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(templateData, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(metadataPath, json);

                // Create ZIP package
                if (File.Exists(filePath))
                    File.Delete(filePath);
                    
                ZipFile.CreateFromDirectory(tempDir, filePath);

                // Clean up temp directory
                Directory.Delete(tempDir, true);

                StatusMessage = "Template exported successfully!";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error exporting template: {ex.Message}";
                MessageBox.Show($"Failed to export template: {ex.Message}", "Export Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ImportTemplate(string filePath)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                ZipFile.ExtractToDirectory(filePath, tempDir);

                string metadataPath = Path.Combine(tempDir, "template.json");
                if (!File.Exists(metadataPath))
                {
                    throw new Exception("Invalid template file - missing metadata");
                }

                string json = File.ReadAllText(metadataPath);
                var templateData = Newtonsoft.Json.JsonConvert.DeserializeObject<TemplateData>(json);

                // Process imported template
                // This would typically trigger an event to load the template in the UI
                StatusMessage = "Template imported successfully!";

                // Clean up
                Directory.Delete(tempDir, true);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error importing template: {ex.Message}";
                MessageBox.Show($"Failed to import template: {ex.Message}", "Import Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private CanvasItemData ConvertToCanvasItemData(ICanvasItem item, int templateId, int zIndex)
        {
            var data = new CanvasItemData
            {
                TemplateId = templateId,
                ZIndex = zIndex,
                IsVisible = true
            };

            // Common properties for IBoxCanvasItem
            if (item is IBoxCanvasItem boxItem)
            {
                data.X = boxItem.Left;
                data.Y = boxItem.Top;
                data.Width = boxItem.Width;
                data.Height = boxItem.Height;
                data.LockedPosition = boxItem.LockedPosition;
            }

            // Handle rotation
            if (item is CanvasItem canvasItem)
            {
                data.Rotation = canvasItem.Angle;
                data.LockedAspectRatio = canvasItem.LockedAspectRatio;
            }

            // Type-specific properties
            switch (item)
            {
                case TextCanvasItem textItem:
                    data.ItemType = "Text";
                    data.Name = $"Text: {textItem.Text?.Substring(0, Math.Min(textItem.Text.Length, 20)) ?? ""}";
                    data.Text = textItem.Text;
                    data.FontFamily = textItem.FontFamily;
                    data.FontSize = textItem.FontSize;
                    data.TextColor = BrushToColorString(textItem.Foreground);
                    data.IsBold = textItem.IsBold;
                    data.IsItalic = textItem.IsItalic;
                    data.IsUnderlined = textItem.IsUnderlined;
                    break;

                case PlaceholderCanvasItem placeholderItem:
                    data.ItemType = "Placeholder";
                    data.Name = $"Placeholder {placeholderItem.PlaceholderNo}";
                    data.PlaceholderNumber = placeholderItem.PlaceholderNo;
                    data.PlaceholderColor = BrushToColorString(placeholderItem.Background);
                    break;

                case ImageCanvasItem imageItem:
                    data.ItemType = "Image";
                    data.Name = "Image Item";
                    break;

                case ShapeCanvasItem shapeItem:
                    data.ItemType = "Shape";
                    data.Name = $"Shape: {shapeItem.ShapeType}";
                    data.ShapeType = shapeItem.ShapeType.ToString();
                    data.FillColor = BrushToColorString(shapeItem.Fill);
                    data.StrokeColor = BrushToColorString(shapeItem.Stroke);
                    data.StrokeThickness = shapeItem.StrokeThickness;
                    break;
            }

            return data;
        }

        private string BrushToColorString(Brush brush)
        {
            if (brush is SolidColorBrush solidBrush)
            {
                return solidBrush.Color.ToString();
            }
            return null;
        }

        private void SaveImageToFile(BitmapImage source, string filePath)
        {
            BitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                encoder.Save(fileStream);
            }
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
                return false;

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}