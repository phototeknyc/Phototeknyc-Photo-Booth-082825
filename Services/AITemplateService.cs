using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Photobooth.Database;

namespace Photobooth.Services
{
    public class AITemplateService
    {
        #region Singleton

        private static AITemplateService _instance;
        private static readonly object _lock = new object();

        public static AITemplateService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new AITemplateService();
                        }
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Private Fields

        private readonly AITemplateDatabase _database;
        private List<AITemplateCategory> _cachedCategories;
        private Dictionary<int, List<AITransformationTemplate>> _cachedTemplatesByCategory;
        private DateTime _lastCacheUpdate;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);
        private readonly object _cacheLock = new object();

        #endregion

        #region Events

        public event EventHandler<TemplateEventArgs> TemplateAdded;
        public event EventHandler<TemplateEventArgs> TemplateUpdated;
        public event EventHandler<TemplateEventArgs> TemplateDeleted;
        public event EventHandler CategoriesUpdated;

        #endregion

        #region Constructor

        private AITemplateService()
        {
            _database = AITemplateDatabase.Instance;
            _cachedTemplatesByCategory = new Dictionary<int, List<AITransformationTemplate>>();
            _lastCacheUpdate = DateTime.MinValue;

            // Initialize with sample templates if needed
            InitializeSampleTemplates();
        }

        #endregion

        #region Public Methods - Categories

        public List<AITemplateCategory> GetCategories(bool forceRefresh = false)
        {
            if (forceRefresh || _cachedCategories == null || IsCacheExpired())
            {
                RefreshCache();
            }

            return _cachedCategories;
        }

        public AITemplateCategory GetCategory(int categoryId)
        {
            var categories = GetCategories();
            return categories.FirstOrDefault(c => c.Id == categoryId);
        }

        public int AddCategory(string name, string description = null, int sortOrder = 999)
        {
            var category = new AITemplateCategory
            {
                Name = name,
                Description = description,
                SortOrder = sortOrder,
                IsActive = true
            };

            int categoryId = _database.SaveCategory(category);
            RefreshCache();
            CategoriesUpdated?.Invoke(this, EventArgs.Empty);

            Debug.WriteLine($"[AITemplateService] Added category: {name} (ID: {categoryId})");
            return categoryId;
        }

        #endregion

        #region Public Methods - Templates

        public List<AITransformationTemplate> GetTemplates(int? categoryId = null, bool forceRefresh = false)
        {
            if (forceRefresh || IsCacheExpired())
            {
                RefreshCache();
            }

            if (categoryId.HasValue)
            {
                if (_cachedTemplatesByCategory.ContainsKey(categoryId.Value))
                {
                    return _cachedTemplatesByCategory[categoryId.Value];
                }
                return new List<AITransformationTemplate>();
            }

            // Return all templates
            return _cachedTemplatesByCategory.SelectMany(kvp => kvp.Value).ToList();
        }

        public AITransformationTemplate GetTemplate(int templateId)
        {
            return _database.GetTemplate(templateId);
        }

        public AITransformationTemplate GetTemplateById(int templateId)
        {
            return _database.GetTemplate(templateId);
        }

        public List<AITransformationTemplate> GetAllTemplates()
        {
            return GetTemplates();
        }

        public List<AITransformationTemplate> GetPopularTemplates(int count = 10)
        {
            var allTemplates = GetTemplates();
            return allTemplates.OrderByDescending(t => t.Id).Take(count).ToList();
        }

        public int AddTemplate(AITransformationTemplate template, int categoryId)
        {
            try
            {
                int templateId = _database.SaveTemplate(template, categoryId);
                RefreshCache();

                TemplateAdded?.Invoke(this, new TemplateEventArgs { Template = template, TemplateId = templateId });

                Debug.WriteLine($"[AITemplateService] Added template: {template.Name} (ID: {templateId})");
                return templateId;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITemplateService] Error adding template: {ex.Message}");
                MessageBox.Show($"Failed to add template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return -1;
            }
        }

        public void UpdateTemplate(AITransformationTemplate template)
        {
            try
            {
                _database.UpdateTemplate(template);
                RefreshCache();

                TemplateUpdated?.Invoke(this, new TemplateEventArgs { Template = template, TemplateId = template.Id });

                Debug.WriteLine($"[AITemplateService] Updated template: {template.Name}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITemplateService] Error updating template: {ex.Message}");
                MessageBox.Show($"Failed to update template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void DeleteTemplate(int templateId)
        {
            try
            {
                var template = GetTemplate(templateId);
                if (template != null)
                {
                    _database.DeleteTemplate(templateId);
                    RefreshCache();

                    TemplateDeleted?.Invoke(this, new TemplateEventArgs { Template = template, TemplateId = templateId });

                    Debug.WriteLine($"[AITemplateService] Deleted template ID: {templateId}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITemplateService] Error deleting template: {ex.Message}");
                MessageBox.Show($"Failed to delete template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void RecordTemplateUsage(int templateId)
        {
            try
            {
                _database.IncrementUsageCount(templateId);
                Debug.WriteLine($"[AITemplateService] Recorded usage for template ID: {templateId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITemplateService] Error recording template usage: {ex.Message}");
            }
        }

        #endregion

        #region Public Methods - Transformation

        public async Task<string> ApplyTransformationAsync(
            string inputImagePath,
            AITransformationTemplate template,
            IProgress<int> progress = null)
        {
            try
            {
                // Ensure AI service is initialized
                var aiService = AITransformationService.Instance;
                if (!await aiService.InitializeAsync())
                {
                    throw new Exception("Failed to initialize AI transformation service. Please check your API token.");
                }

                // Record template usage
                RecordTemplateUsage(template.Id);

                // Apply transformation
                var result = await aiService.TransformImageAsync(inputImagePath, template);

                Debug.WriteLine($"[AITemplateService] Transformation completed: {result}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITemplateService] Transformation failed: {ex.Message}");
                throw;
            }
        }

        public async Task<List<string>> BatchApplyTransformationAsync(
            List<string> inputImagePaths,
            AITransformationTemplate template,
            IProgress<int> progress = null)
        {
            try
            {
                // Ensure AI service is initialized
                var aiService = AITransformationService.Instance;
                if (!await aiService.InitializeAsync())
                {
                    throw new Exception("Failed to initialize AI transformation service. Please check your API token.");
                }

                // Record template usage
                RecordTemplateUsage(template.Id);

                // Apply transformations
                var results = await aiService.BatchTransformAsync(inputImagePaths, template, progress);

                Debug.WriteLine($"[AITemplateService] Batch transformation completed: {results.Count} images");
                return results;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITemplateService] Batch transformation failed: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Private Methods

        private void InitializeSampleTemplates()
        {
            try
            {
                _database.InsertSampleTemplates();
                Debug.WriteLine("[AITemplateService] Sample templates initialized");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITemplateService] Error initializing sample templates: {ex.Message}");
            }
        }

        private void RefreshCache()
        {
            try
            {
                lock (_cacheLock)
                {
                    // Ensure dictionary is initialized
                    if (_cachedTemplatesByCategory == null)
                    {
                        _cachedTemplatesByCategory = new Dictionary<int, List<AITransformationTemplate>>();
                        Debug.WriteLine("[AITemplateService] Dictionary was null, recreated");
                    }

                    _cachedCategories = _database.GetCategories();

                    // Only clear if we got valid categories
                    if (_cachedCategories != null)
                    {
                        _cachedTemplatesByCategory.Clear();

                    foreach (var category in _cachedCategories)
                    {
                        if (category == null)
                        {
                            Debug.WriteLine("[AITemplateService] Skipping null category");
                            continue;
                        }

                        var templates = _database.GetTemplates(category.Id);

                        // Ensure we have a valid list even if GetTemplates returns null
                        if (templates == null)
                        {
                            Debug.WriteLine($"[AITemplateService] GetTemplates returned null for category {category.Id}, using empty list");
                            templates = new List<AITransformationTemplate>();
                        }

                        _cachedTemplatesByCategory[category.Id] = templates;
                    }

                        _lastCacheUpdate = DateTime.Now;

                        Debug.WriteLine($"[AITemplateService] Cache refreshed: {_cachedCategories.Count} categories, " +
                                       $"{_cachedTemplatesByCategory.Sum(kvp => kvp.Value?.Count ?? 0)} templates");
                    }
                    else
                    {
                        Debug.WriteLine("[AITemplateService] GetCategories returned null");
                        _cachedCategories = new List<AITemplateCategory>();
                    }
                } // End lock
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITemplateService] Error refreshing cache: {ex.Message}");
                Debug.WriteLine($"[AITemplateService] Stack trace: {ex.StackTrace}");

                // Ensure we have valid state even after error
                if (_cachedCategories == null)
                    _cachedCategories = new List<AITemplateCategory>();
                if (_cachedTemplatesByCategory == null)
                    _cachedTemplatesByCategory = new Dictionary<int, List<AITransformationTemplate>>();
            }
        }

        private bool IsCacheExpired()
        {
            return DateTime.Now - _lastCacheUpdate > _cacheExpiration;
        }

        #endregion

        #region Template Creation Helpers

        public AITransformationTemplate CreateTemplateFromPrompt(
            string name,
            string prompt,
            string category = "Custom",
            string negativePrompt = null)
        {
            return new AITransformationTemplate
            {
                Name = name,
                Category = new AITemplateCategory { Id = 0, Name = category },
                Prompt = prompt,
                NegativePrompt = negativePrompt ?? "low quality, blurry, deformed, distorted",
                Steps = 30,
                GuidanceScale = 7.5,
                PromptStrength = 0.8,
                IsActive = true
            };
        }

        public List<string> GetPromptSuggestions(string category)
        {
            var suggestions = new Dictionary<string, List<string>>
            {
                ["Characters"] = new List<string>
                {
                    "superhero with cape and mask",
                    "medieval knight in shining armor",
                    "pirate captain on ship deck",
                    "astronaut in space suit",
                    "wizard with magical powers"
                },
                ["Scenery"] = new List<string>
                {
                    "tropical beach at sunset",
                    "snowy mountain peak",
                    "enchanted forest with fireflies",
                    "futuristic city skyline",
                    "ancient temple ruins"
                },
                ["Artistic Styles"] = new List<string>
                {
                    "oil painting portrait",
                    "watercolor illustration",
                    "comic book style",
                    "vintage photograph",
                    "3D rendered artwork"
                }
            };

            return suggestions.ContainsKey(category) ? suggestions[category] : new List<string>();
        }

        #endregion
    }

    #region Event Args

    public class TemplateEventArgs : EventArgs
    {
        public AITransformationTemplate Template { get; set; }
        public int TemplateId { get; set; }
    }

    #endregion
}