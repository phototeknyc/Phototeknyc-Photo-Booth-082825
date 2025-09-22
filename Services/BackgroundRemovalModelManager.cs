using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Photobooth.Services
{
    /// <summary>
    /// Manages different background removal models for various speed/quality trade-offs
    /// </summary>
    public class BackgroundRemovalModelManager
    {
        private static BackgroundRemovalModelManager _instance;
        private static readonly object _lock = new object();

        public static BackgroundRemovalModelManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new BackgroundRemovalModelManager();
                        }
                    }
                }
                return _instance;
            }
        }

        public enum ModelType
        {
            MODNet          // ~25MB - fast, optimized for humans
        }

        public class ModelInfo
        {
            public string Name { get; set; }
            public string FileName { get; set; }
            public string Description { get; set; }
            public int InputSize { get; set; }
            public float SpeedMultiplier { get; set; } // Relative to U2Net
            public bool RequiresNormalization { get; set; }
            public float[] NormMean { get; set; }
            public float[] NormStd { get; set; }
            public string InputName { get; set; }
            public string OutputName { get; set; }
        }

        private readonly Dictionary<ModelType, ModelInfo> _modelConfigs = new Dictionary<ModelType, ModelInfo>
        {
            [ModelType.MODNet] = new ModelInfo
            {
                Name = "MODNet",
                FileName = "modnet.onnx",
                Description = "Fast, optimized for humans",
                InputSize = 512,
                SpeedMultiplier = 4.0f,
                RequiresNormalization = true,
                NormMean = new[] { 0.5f, 0.5f, 0.5f },
                NormStd = new[] { 0.5f, 0.5f, 0.5f },
                InputName = "input",
                OutputName = "output"
            }
        };

        /// <summary>
        /// Get the best available model based on quality settings
        /// </summary>
        public ModelType GetRecommendedModel(BackgroundRemovalQuality quality)
        {
            // Always use MODNet regardless of quality setting
            // It's fast and optimized for human subjects
            return ModelType.MODNet;
        }

        /// <summary>
        /// Check if a model file exists
        /// </summary>
        public bool IsModelAvailable(ModelType modelType)
        {
            if (!_modelConfigs.ContainsKey(modelType))
                return false;

            var modelPath = GetModelPath(modelType);
            return File.Exists(modelPath);
        }

        /// <summary>
        /// Get the full path to a model file
        /// </summary>
        public string GetModelPath(ModelType modelType)
        {
            if (!_modelConfigs.ContainsKey(modelType))
                return null;

            var modelsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "BackgroundRemoval");
            return Path.Combine(modelsFolder, _modelConfigs[modelType].FileName);
        }

        /// <summary>
        /// Get model configuration
        /// </summary>
        public ModelInfo GetModelInfo(ModelType modelType)
        {
            return _modelConfigs.ContainsKey(modelType) ? _modelConfigs[modelType] : null;
        }

        /// <summary>
        /// Get all available models
        /// </summary>
        public List<ModelType> GetAvailableModels()
        {
            var available = new List<ModelType>();
            foreach (var modelType in Enum.GetValues(typeof(ModelType)).Cast<ModelType>())
            {
                if (IsModelAvailable(modelType))
                {
                    available.Add(modelType);
                }
            }
            return available;
        }

        /// <summary>
        /// Load a model with appropriate session options
        /// </summary>
        public InferenceSession LoadModel(ModelType modelType, bool useGPU = false)
        {
            var modelPath = GetModelPath(modelType);
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"Model file not found: {modelPath}");
            }

            var sessionOptions = new SessionOptions();
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

            if (useGPU)
            {
                try
                {
                    sessionOptions.AppendExecutionProvider_DML();
                }
                catch
                {
                    // GPU not available, fallback to CPU
                }
            }

            // Set memory pattern for optimization
            sessionOptions.EnableMemoryPattern = true;
            sessionOptions.EnableCpuMemArena = true;

            // MODNet optimization settings
            if (modelType == ModelType.MODNet)
            {
                // MODNet benefits from multi-threading
                sessionOptions.InterOpNumThreads = Math.Min(2, Environment.ProcessorCount);
                sessionOptions.IntraOpNumThreads = Environment.ProcessorCount;
            }
            else
            {
                sessionOptions.InterOpNumThreads = 1;
                sessionOptions.IntraOpNumThreads = Environment.ProcessorCount;
            }

            return new InferenceSession(modelPath, sessionOptions);
        }

        /// <summary>
        /// Download a model if not present (placeholder for future implementation)
        /// </summary>
        public bool DownloadModel(ModelType modelType)
        {
            // TODO: Implement model downloading from CDN
            // For now, return false if model doesn't exist
            return IsModelAvailable(modelType);
        }
    }
}