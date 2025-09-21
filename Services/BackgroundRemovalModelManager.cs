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
            U2Net,           // Original 168MB model - highest quality
            U2NetP,          // Light 4.4MB model - good quality
            MODNet,          // ~25MB - fast, good for humans
            PPHumanSeg,      // <1MB - ultra-fast, basic quality
            RMBG14,          // ~15-40MB - modern, balanced
            SelfieSegmentation // ~5-10MB - optimized for selfies
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
            [ModelType.U2Net] = new ModelInfo
            {
                Name = "U²-Net",
                FileName = "u2net.onnx",
                Description = "Highest quality, slower processing",
                InputSize = 320,
                SpeedMultiplier = 1.0f,
                RequiresNormalization = true,
                NormMean = new[] { 0.485f, 0.456f, 0.406f },
                NormStd = new[] { 0.229f, 0.224f, 0.225f },
                InputName = "input",
                OutputName = "output"
            },
            [ModelType.U2NetP] = new ModelInfo
            {
                Name = "U²-Net Light",
                FileName = "u2netp.onnx",
                Description = "Good quality, faster processing",
                InputSize = 320,
                SpeedMultiplier = 2.5f,
                RequiresNormalization = true,
                NormMean = new[] { 0.485f, 0.456f, 0.406f },
                NormStd = new[] { 0.229f, 0.224f, 0.225f },
                InputName = "input",
                OutputName = "output"
            },
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
            },
            [ModelType.PPHumanSeg] = new ModelInfo
            {
                Name = "PP-HumanSeg-Lite",
                FileName = "pp_humanseg_lite.onnx",
                Description = "Ultra-fast, basic quality",
                InputSize = 192,
                SpeedMultiplier = 15.0f,
                RequiresNormalization = true,
                NormMean = new[] { 0.5f, 0.5f, 0.5f },
                NormStd = new[] { 0.5f, 0.5f, 0.5f },
                InputName = "x",
                OutputName = "save_infer_model/scale_0.tmp_1"
            },
            [ModelType.RMBG14] = new ModelInfo
            {
                Name = "RMBG-1.4",
                FileName = "rmbg-1.4.onnx",
                Description = "Modern balanced model",
                InputSize = 320,
                SpeedMultiplier = 3.0f,
                RequiresNormalization = true,
                NormMean = new[] { 0.485f, 0.456f, 0.406f },
                NormStd = new[] { 0.229f, 0.224f, 0.225f },
                InputName = "input",
                OutputName = "output"
            },
            [ModelType.SelfieSegmentation] = new ModelInfo
            {
                Name = "Selfie Segmentation",
                FileName = "selfie_segmentation.onnx",
                Description = "Optimized for portraits",
                InputSize = 256,
                SpeedMultiplier = 8.0f,
                RequiresNormalization = false,
                InputName = "input",
                OutputName = "output"
            }
        };

        /// <summary>
        /// Get the best available model based on quality settings
        /// </summary>
        public ModelType GetRecommendedModel(BackgroundRemovalQuality quality)
        {
            switch (quality)
            {
                case BackgroundRemovalQuality.Low:
                    // Try fast models - MODNet is fast and better than U2NetP
                    if (IsModelAvailable(ModelType.PPHumanSeg))
                        return ModelType.PPHumanSeg;
                    if (IsModelAvailable(ModelType.SelfieSegmentation))
                        return ModelType.SelfieSegmentation;
                    if (IsModelAvailable(ModelType.MODNet))  // MODNet is 4x faster than U2Net
                        return ModelType.MODNet;
                    if (IsModelAvailable(ModelType.U2NetP))
                        return ModelType.U2NetP;
                    break;

                case BackgroundRemovalQuality.Medium:
                    // Balanced models - prefer MODNet for speed/quality balance
                    if (IsModelAvailable(ModelType.MODNet))
                        return ModelType.MODNet;
                    if (IsModelAvailable(ModelType.RMBG14))
                        return ModelType.RMBG14;
                    if (IsModelAvailable(ModelType.U2NetP))
                        return ModelType.U2NetP;
                    break;

                case BackgroundRemovalQuality.High:
                    // Use highest quality available
                    if (IsModelAvailable(ModelType.U2Net))
                        return ModelType.U2Net;
                    if (IsModelAvailable(ModelType.RMBG14))
                        return ModelType.RMBG14;
                    if (IsModelAvailable(ModelType.MODNet))
                        return ModelType.MODNet;
                    break;
            }

            // Fallback to any available model
            return GetAvailableModels().FirstOrDefault();
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

            // Model-specific optimizations
            if (modelType == ModelType.PPHumanSeg || modelType == ModelType.SelfieSegmentation)
            {
                // These lightweight models benefit from different settings
                sessionOptions.InterOpNumThreads = 1;
                sessionOptions.IntraOpNumThreads = Math.Min(4, Environment.ProcessorCount);
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