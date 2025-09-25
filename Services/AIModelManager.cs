using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Photobooth.Models;
using Photobooth.Database;

namespace Photobooth.Services
{
    public class AIModelManager
    {
        #region Singleton

        private static AIModelManager _instance;
        private static readonly object _lock = new object();

        public static AIModelManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new AIModelManager();
                        }
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Private Fields

        private List<AIModelDefinition> _availableModels;
        private Dictionary<string, Dictionary<string, AIModelTemplatePrompt>> _modelTemplatePrompts;
        private string _selectedModelId;
        private readonly string _configFilePath;

        #endregion

        #region Events

        public event EventHandler<ModelChangedEventArgs> ModelChanged;
        public event EventHandler<PromptsUpdatedEventArgs> PromptsUpdated;

        #endregion

        #region Constructor

        private AIModelManager()
        {
            _configFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth",
                "ai_models_config.json");

            LoadConfiguration();
        }

        #endregion

        #region Public Properties

        public List<AIModelDefinition> AvailableModels => _availableModels ?? new List<AIModelDefinition>();

        public AIModelDefinition SelectedModel
        {
            get
            {
                if (string.IsNullOrEmpty(_selectedModelId))
                    return AvailableModels.FirstOrDefault();

                return AvailableModels.FirstOrDefault(m => m.Id == _selectedModelId);
            }
        }

        public string SelectedModelId
        {
            get => _selectedModelId;
            set
            {
                if (_selectedModelId != value)
                {
                    var oldModelId = _selectedModelId;
                    _selectedModelId = value;
                    SaveConfiguration();

                    ModelChanged?.Invoke(this, new ModelChangedEventArgs
                    {
                        OldModelId = oldModelId,
                        NewModelId = value,
                        Model = SelectedModel
                    });

                    Debug.WriteLine($"[AIModelManager] Selected model changed to: {value}");
                }
            }
        }

        #endregion

        #region Public Methods

        public void Initialize()
        {
            if (_availableModels == null || !_availableModels.Any())
            {
                LoadDefaultModels();
            }
        }

        public void AddModel(AIModelDefinition model)
        {
            if (_availableModels == null)
                _availableModels = new List<AIModelDefinition>();

            if (!_availableModels.Any(m => m.Id == model.Id))
            {
                model.CreatedAt = DateTime.Now;
                model.UpdatedAt = DateTime.Now;
                _availableModels.Add(model);
                SaveConfiguration();
                Debug.WriteLine($"[AIModelManager] Added model: {model.Name}");
            }
        }

        public void RemoveModel(string modelId)
        {
            if (_availableModels?.Any(m => m.Id == modelId) == true)
            {
                _availableModels.RemoveAll(m => m.Id == modelId);

                // Remove associated prompts
                if (_modelTemplatePrompts?.ContainsKey(modelId) == true)
                {
                    _modelTemplatePrompts.Remove(modelId);
                }

                // If this was the selected model, select another
                if (_selectedModelId == modelId)
                {
                    _selectedModelId = _availableModels.FirstOrDefault()?.Id;
                }

                SaveConfiguration();
                Debug.WriteLine($"[AIModelManager] Removed model: {modelId}");
            }
        }

        public void UpdateModel(AIModelDefinition model)
        {
            var existingModel = _availableModels?.FirstOrDefault(m => m.Id == model.Id);
            if (existingModel != null)
            {
                var index = _availableModels.IndexOf(existingModel);
                model.UpdatedAt = DateTime.Now;
                _availableModels[index] = model;
                SaveConfiguration();
                Debug.WriteLine($"[AIModelManager] Updated model: {model.Name}");
            }
        }

        public AIModelTemplatePrompt GetPromptForTemplate(string modelId, string templateId)
        {
            if (_modelTemplatePrompts?.ContainsKey(modelId) == true)
            {
                if (_modelTemplatePrompts[modelId].ContainsKey(templateId))
                {
                    return _modelTemplatePrompts[modelId][templateId];
                }
            }

            // Return default from template if no custom prompt exists
            return null;
        }

        public void SetPromptForTemplate(string modelId, string templateId, string prompt, string negativePrompt, ModelParameters parameters = null)
        {
            if (_modelTemplatePrompts == null)
                _modelTemplatePrompts = new Dictionary<string, Dictionary<string, AIModelTemplatePrompt>>();

            if (!_modelTemplatePrompts.ContainsKey(modelId))
                _modelTemplatePrompts[modelId] = new Dictionary<string, AIModelTemplatePrompt>();

            _modelTemplatePrompts[modelId][templateId] = new AIModelTemplatePrompt
            {
                Id = Guid.NewGuid().ToString(),
                ModelId = modelId,
                TemplateId = templateId,
                Prompt = prompt,
                NegativePrompt = negativePrompt,
                Parameters = parameters ?? GetModel(modelId)?.DefaultParameters,
                UpdatedAt = DateTime.Now
            };

            SaveConfiguration();

            PromptsUpdated?.Invoke(this, new PromptsUpdatedEventArgs
            {
                ModelId = modelId,
                TemplateId = templateId,
                Prompt = prompt
            });

            Debug.WriteLine($"[AIModelManager] Updated prompt for model {modelId}, template {templateId}");
        }

        public Dictionary<string, AIModelTemplatePrompt> GetPromptsForModel(string modelId)
        {
            if (_modelTemplatePrompts?.ContainsKey(modelId) == true)
            {
                return _modelTemplatePrompts[modelId];
            }
            return new Dictionary<string, AIModelTemplatePrompt>();
        }

        public AIModelDefinition GetModel(string modelId)
        {
            return _availableModels?.FirstOrDefault(m => m.Id == modelId);
        }

        public void ResetToDefaults()
        {
            LoadDefaultModels();
            _modelTemplatePrompts = new Dictionary<string, Dictionary<string, AIModelTemplatePrompt>>();
            _selectedModelId = "nano-banana"; // Default to identity-preserving model
            SaveConfiguration();
            Debug.WriteLine("[AIModelManager] Reset to default configuration");
        }

        public Dictionary<string, object> BuildModelInput(AIModelDefinition model, string base64Image, AIModelTemplatePrompt customPrompt, AITransformationTemplate template)
        {
            var input = new Dictionary<string, object>();

            // Add prompt
            input["prompt"] = customPrompt?.Prompt ?? template.Prompt;

            // Handle special case for Seedream-4 text-to-image model
            if (model.Id == "seedream-4")
            {
                // Seedream-4 specific parameters
                input["aspect_ratio"] = "4:3"; // Can be made configurable later
                // No image input needed for text-to-image
            }
            // Handle image input based on model requirements
            else if (model.ImageInputFormat == "dataurl")
            {
                var dataUrl = $"data:image/png;base64,{base64Image}";
                if (model.RequiresImageArray)
                {
                    input["image_input"] = new[] { dataUrl };
                }
                else
                {
                    input["image"] = dataUrl;
                }
            }
            else if (model.ImageInputFormat == "base64")
            {
                if (model.RequiresImageArray)
                {
                    input["image_input"] = new[] { base64Image };
                }
                else
                {
                    input["image"] = base64Image;
                }
            }
            else if (model.ImageInputFormat == "none" || !model.SupportsImageInput)
            {
                // No image input for text-to-image models
                // The prompt is already added above
            }

            // Add parameters based on model capabilities
            var parameters = customPrompt?.Parameters ?? model.DefaultParameters;

            if (model.Capabilities.SupportsNegativePrompt && !string.IsNullOrEmpty(customPrompt?.NegativePrompt))
            {
                input["negative_prompt"] = customPrompt.NegativePrompt;
            }

            if (model.Capabilities.SupportsStrength)
            {
                input["strength"] = parameters.Strength;
            }

            if (model.Capabilities.SupportsGuidanceScale)
            {
                input["guidance_scale"] = parameters.GuidanceScale;
            }

            if (model.Capabilities.SupportsSteps)
            {
                input["num_inference_steps"] = parameters.Steps;
            }

            if (model.Capabilities.SupportsScheduler && !string.IsNullOrEmpty(parameters.Scheduler))
            {
                input["scheduler"] = parameters.Scheduler;
            }

            if (model.Capabilities.SupportsSeed && parameters.Seed.HasValue)
            {
                input["seed"] = parameters.Seed.Value;
            }

            input["output_format"] = parameters.OutputFormat;

            return input;
        }

        #endregion

        #region Private Methods

        private void LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    var config = JsonConvert.DeserializeObject<ModelConfiguration>(json);

                    _availableModels = config.Models ?? new List<AIModelDefinition>();
                    _modelTemplatePrompts = config.ModelTemplatePrompts ?? new Dictionary<string, Dictionary<string, AIModelTemplatePrompt>>();
                    _selectedModelId = config.SelectedModelId;

                    Debug.WriteLine($"[AIModelManager] Loaded {_availableModels.Count} models from configuration");
                }
                else
                {
                    LoadDefaultModels();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AIModelManager] Error loading configuration: {ex.Message}");
                LoadDefaultModels();
            }
        }

        private void LoadDefaultModels()
        {
            _availableModels = PredefinedModels.GetDefaultModels();
            _selectedModelId = "nano-banana"; // Default to identity-preserving model
            Debug.WriteLine($"[AIModelManager] Loaded {_availableModels.Count} default models");
        }

        private void SaveConfiguration()
        {
            try
            {
                var config = new ModelConfiguration
                {
                    Models = _availableModels,
                    ModelTemplatePrompts = _modelTemplatePrompts,
                    SelectedModelId = _selectedModelId
                };

                var json = JsonConvert.SerializeObject(config, Formatting.Indented);

                var directory = Path.GetDirectoryName(_configFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_configFilePath, json);
                Debug.WriteLine("[AIModelManager] Configuration saved");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AIModelManager] Error saving configuration: {ex.Message}");
            }
        }

        #endregion

        #region Helper Classes

        private class ModelConfiguration
        {
            public List<AIModelDefinition> Models { get; set; }
            public Dictionary<string, Dictionary<string, AIModelTemplatePrompt>> ModelTemplatePrompts { get; set; }
            public string SelectedModelId { get; set; }
        }

        #endregion
    }

    #region Event Args

    public class ModelChangedEventArgs : EventArgs
    {
        public string OldModelId { get; set; }
        public string NewModelId { get; set; }
        public AIModelDefinition Model { get; set; }
    }

    public class PromptsUpdatedEventArgs : EventArgs
    {
        public string ModelId { get; set; }
        public string TemplateId { get; set; }
        public string Prompt { get; set; }
    }

    #endregion
}