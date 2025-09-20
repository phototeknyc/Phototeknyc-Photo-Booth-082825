using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Photobooth.Database;
using Photobooth.Models;

namespace Photobooth.Services
{
    /// <summary>
    /// Service to manage template overlay functionality for live view
    /// Shows photo placeholder positions during capture
    /// </summary>
    public class TemplateOverlayService
    {
        private Template _overlayTemplate;
        private List<CanvasItemData> _placeholderItems;
        private readonly TemplateDatabase _templateDb;

        public TemplateOverlayService()
        {
            _templateDb = new TemplateDatabase();
        }

        /// <summary>
        /// Loads template overlay data from a TemplateData object
        /// </summary>
        public bool LoadTemplateForOverlay(TemplateData templateData)
        {
            if (templateData == null)
            {
                DebugService.LogDebug("LoadTemplateForOverlay: templateData is null");
                return false;
            }

            DebugService.LogDebug($"LoadTemplateForOverlay: Loading template '{templateData.Name}' ID: {templateData.Id}");

            // First try to load XML template if it exists
            var xmlLoaded = TryLoadXmlTemplate(templateData);

            if (!xmlLoaded)
            {
                // Fall back to database placeholders
                LoadPlaceholdersFromDatabase(templateData.Id);
            }

            return _overlayTemplate != null || (_placeholderItems != null && _placeholderItems.Count > 0);
        }

        /// <summary>
        /// Tries to load an XML template file
        /// </summary>
        private bool TryLoadXmlTemplate(TemplateData templateData)
        {
            try
            {
                string templatePath = null;

                // Try to find the template XML file
                if (!string.IsNullOrEmpty(templateData.BackgroundImagePath))
                {
                    // Try to find template in same folder as background image
                    var dir = Path.GetDirectoryName(templateData.BackgroundImagePath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        try
                        {
                            // Look for XML files in the same directory
                            var xmlFiles = Directory.GetFiles(dir, "*.xml");
                            if (xmlFiles.Length > 0)
                            {
                                templatePath = xmlFiles[0];
                                DebugService.LogDebug($"Found template XML at: {templatePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugService.LogDebug($"Could not search for XML files in {dir}: {ex.Message}");
                        }
                    }
                }

                if (string.IsNullOrEmpty(templatePath))
                {
                    // Try default templates directory
                    var templatesDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                        "PhotoBooth", "Templates");

                    // Try using the template ID
                    var possiblePath = Path.Combine(templatesDir, templateData.Id + ".xml");
                    if (File.Exists(possiblePath))
                    {
                        templatePath = possiblePath;
                        DebugService.LogDebug($"Found template XML by ID at: {templatePath}");
                    }
                    else
                    {
                        // Try using the template name
                        possiblePath = Path.Combine(templatesDir, templateData.Name + ".xml");
                        if (File.Exists(possiblePath))
                        {
                            templatePath = possiblePath;
                            DebugService.LogDebug($"Found template XML by name at: {templatePath}");
                        }
                    }
                }

                if (!string.IsNullOrEmpty(templatePath) && File.Exists(templatePath))
                {
                    _overlayTemplate = Template.LoadTemplateFromFile(templatePath);
                    DebugService.LogDebug($"Loaded XML template for overlay: {_overlayTemplate?.Name ?? "Unknown"}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"Error loading XML template: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Loads placeholder items from the database
        /// </summary>
        private void LoadPlaceholdersFromDatabase(int templateId)
        {
            try
            {
                _placeholderItems = _templateDb.GetCanvasItems(templateId)
                    .Where(item => item.ItemType == "Placeholder")
                    .OrderBy(item => item.Name) // Assumes names like "Placeholder 1", "Placeholder 2"
                    .ToList();

                DebugService.LogDebug($"Loaded {_placeholderItems.Count} placeholder items from database for template ID {templateId}");
            }
            catch (Exception ex)
            {
                DebugService.LogError($"Error loading placeholders from database: {ex.Message}");
                _placeholderItems = new List<CanvasItemData>();
            }
        }

        /// <summary>
        /// Gets placeholder data for the current photo index
        /// </summary>
        public PlaceholderOverlayData GetPlaceholderForPhoto(int photoIndex, TemplateData currentTemplate)
        {
            // Try XML template first
            if (_overlayTemplate != null)
            {
                var photoElements = _overlayTemplate.Elements?
                    .OfType<PhotoElement>()
                    .OrderBy(p => p.PhotoNumber)
                    .ToList();

                if (photoElements != null && photoElements.Count > 0)
                {
                    DebugService.LogDebug($"Found {photoElements.Count} photo elements in XML template, current photo index: {photoIndex}");

                    if (photoIndex < photoElements.Count)
                    {
                        var element = photoElements[photoIndex];
                        double.TryParse(_overlayTemplate.Width, out double templateWidth);
                        double.TryParse(_overlayTemplate.Height, out double templateHeight);

                        return new PlaceholderOverlayData
                        {
                            X = element.Left,
                            Y = element.Top,
                            Width = element.Width,
                            Height = element.Height,
                            TemplateWidth = templateWidth,
                            TemplateHeight = templateHeight,
                            PhotoNumber = photoIndex + 1
                        };
                    }
                }
            }

            // Fall back to database placeholders
            if (_placeholderItems != null && _placeholderItems.Count > 0 && currentTemplate != null)
            {
                DebugService.LogDebug($"Using database placeholders: {_placeholderItems.Count} items, photo index: {photoIndex}");

                if (photoIndex < _placeholderItems.Count)
                {
                    var placeholder = _placeholderItems[photoIndex];
                    DebugService.LogDebug($"Returning placeholder {photoIndex}: Position({placeholder.X}, {placeholder.Y}) Size({placeholder.Width}x{placeholder.Height})");
                    return new PlaceholderOverlayData
                    {
                        X = placeholder.X,
                        Y = placeholder.Y,
                        Width = placeholder.Width,
                        Height = placeholder.Height,
                        TemplateWidth = currentTemplate.CanvasWidth,
                        TemplateHeight = currentTemplate.CanvasHeight,
                        PhotoNumber = photoIndex + 1
                    };
                }
            }

            DebugService.LogDebug($"No placeholder found for photo index {photoIndex}");
            return null;
        }

        /// <summary>
        /// Clears the loaded template data
        /// </summary>
        public void Clear()
        {
            _overlayTemplate = null;
            _placeholderItems = null;
        }
    }

    /// <summary>
    /// Data for displaying a placeholder overlay
    /// </summary>
    public class PlaceholderOverlayData
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double TemplateWidth { get; set; }
        public double TemplateHeight { get; set; }
        public int PhotoNumber { get; set; }
    }
}