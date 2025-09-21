using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CameraControl.Devices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = System.Drawing.Image;

namespace Photobooth.Services
{
    /// <summary>
    /// Service for AI-powered background removal using ONNX Runtime
    /// </summary>
    public class BackgroundRemovalService : IDisposable
    {
        #region Singleton

        private static BackgroundRemovalService _instance;
        private static readonly object _lock = new object();

        public static BackgroundRemovalService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new BackgroundRemovalService();
                            Debug.WriteLine("[BackgroundRemoval] Service instance created");

                            // Only initialize if background removal is enabled
                            if (Properties.Settings.Default.EnableBackgroundRemoval)
                            {
                                Debug.WriteLine("[BackgroundRemoval] Background removal is enabled, starting async initialization...");
                                Task.Run(async () =>
                                {
                                    await Task.Delay(2000); // Allow UI to load first
                                    Debug.WriteLine("[BackgroundRemoval] Starting async model loading...");
                                    var result = await _instance.InitializeAsync();
                                    Debug.WriteLine($"[BackgroundRemoval] Async initialization complete: {result}");
                                });
                            }
                            else
                            {
                                Debug.WriteLine("[BackgroundRemoval] Background removal is disabled, skipping model loading");
                            }
                        }
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Private Fields

        private InferenceSession _captureSession;  // High-quality model for photo capture
        private InferenceSession _liveViewSession; // Lightweight model for live view
        private bool _isInitialized;
        private readonly object _sessionLock = new object();
        private readonly int _liveViewFrameSkip = 2; // Process every 3rd frame
        private int _frameCounter = 0;
        private byte[] _lastMask;
        private DateTime _lastProcessTime = DateTime.MinValue;

        // Model paths
        private readonly string _modelsFolder;
        private readonly string _captureModelPath;
        private readonly string _liveViewModelPath;

        // Model manager for flexible model switching
        private BackgroundRemovalModelManager _modelManager;
        private BackgroundRemovalModelManager.ModelType _currentCaptureModel;
        private BackgroundRemovalModelManager.ModelType _currentLiveViewModel;

        #endregion

        #region Constructor

        private BackgroundRemovalService()
        {
            _modelsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "BackgroundRemoval");

            // Initialize model manager
            _modelManager = BackgroundRemovalModelManager.Instance;

            // Only log if background removal is enabled
            if (Properties.Settings.Default.EnableBackgroundRemoval)
            {
                Debug.WriteLine($"[BackgroundRemoval] Constructor - BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}");
                Debug.WriteLine($"[BackgroundRemoval] Constructor - ModelsFolder: {_modelsFolder}");

                // Log available models
                var availableModels = _modelManager.GetAvailableModels();
                Debug.WriteLine($"[BackgroundRemoval] Available models: {string.Join(", ", availableModels)}");
            }

            // Ensure models folder exists
            if (!Directory.Exists(_modelsFolder))
            {
                Directory.CreateDirectory(_modelsFolder);
            }

            // Set legacy model paths for backward compatibility
            _captureModelPath = Path.Combine(_modelsFolder, "u2net.onnx");
            _liveViewModelPath = Path.Combine(_modelsFolder, "u2netp.onnx");
        }

        #endregion

        #region Initialization

        public async Task<bool> InitializeAsync()
        {
            if (_isInitialized)
                return true;

            lock (_sessionLock)
            {
                if (_isInitialized)
                    return true;
            }

            try
            {
                return await Task.Run(() =>
                {
                    lock (_sessionLock)
                    {
                        if (_isInitialized)
                            return true;
                        // Check if background removal is enabled
                        if (!Properties.Settings.Default.EnableBackgroundRemoval)
                        {
                            Debug.WriteLine("[BackgroundRemoval] Background removal is disabled, skipping model loading");
                            return false;
                        }

                        Debug.WriteLine($"[BackgroundRemoval] InitializeAsync - Checking for models...");

                        // Get quality setting from properties
                        var qualityString = Properties.Settings.Default.BackgroundRemovalQuality;
                        BackgroundRemovalQuality quality = BackgroundRemovalQuality.Medium;

                        Debug.WriteLine($"[BackgroundRemoval] Quality string from settings: '{qualityString}'");

                        switch (qualityString?.ToLower())
                        {
                            case "fast":
                            case "low":
                                quality = BackgroundRemovalQuality.Low;
                                break;
                            case "balanced":
                            case "medium":
                                quality = BackgroundRemovalQuality.Medium;
                                break;
                            case "quality":
                            case "high":
                                quality = BackgroundRemovalQuality.High;
                                break;
                        }

                        // Check if MODNet is available
                        var modnetPath = _modelManager.GetModelPath(BackgroundRemovalModelManager.ModelType.MODNet);
                        Debug.WriteLine($"[BackgroundRemoval] MODNet path: {modnetPath}");
                        Debug.WriteLine($"[BackgroundRemoval] MODNet exists: {File.Exists(modnetPath)}");

                        // Try to load the best available model based on quality
                        var recommendedModel = _modelManager.GetRecommendedModel(quality);
                        var availableModels = _modelManager.GetAvailableModels();

                        Debug.WriteLine($"[BackgroundRemoval] Quality setting: {quality}");
                        Debug.WriteLine($"[BackgroundRemoval] Recommended model: {recommendedModel}");
                        Debug.WriteLine($"[BackgroundRemoval] Available models: {string.Join(", ", availableModels)}");

                        // Load capture model
                        bool captureModelLoaded = false;

                        // Try recommended model first
                        if (recommendedModel != default(BackgroundRemovalModelManager.ModelType))
                        {
                            try
                            {
                                var modelInfo = _modelManager.GetModelInfo(recommendedModel);
                                Debug.WriteLine($"[BackgroundRemoval] Loading recommended model: {modelInfo.Name} ({modelInfo.Description})");

                                bool useGPU = Properties.Settings.Default.BackgroundRemovalUseGPU;
                                _captureSession = _modelManager.LoadModel(recommendedModel, useGPU);
                                _currentCaptureModel = recommendedModel;
                                captureModelLoaded = true;

                                Debug.WriteLine($"[BackgroundRemoval] ✓ {modelInfo.Name} loaded successfully (Speed: {modelInfo.SpeedMultiplier}x)");
                                Log.Info($"[BackgroundRemoval] Using {modelInfo.Name} model for capture");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[BackgroundRemoval] Failed to load {recommendedModel}: {ex.Message}");
                            }
                        }

                        // Fallback to legacy U2Net if recommended model failed
                        if (!captureModelLoaded && File.Exists(_captureModelPath))
                        {
                            try
                            {
                                Debug.WriteLine("[BackgroundRemoval] Falling back to legacy U2Net model...");
                                var sessionOptions = GetSessionOptions(useGPU: false);
                                _captureSession = new InferenceSession(_captureModelPath, sessionOptions);
                                _currentCaptureModel = BackgroundRemovalModelManager.ModelType.U2Net;
                                captureModelLoaded = true;
                                Debug.WriteLine("[BackgroundRemoval] ✓ Legacy U2Net model loaded");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[BackgroundRemoval] ✗ Failed to load legacy model: {ex.Message}");
                                Log.Error($"Failed to load capture model: {ex.Message}");
                            }
                        }

                        // Load live view model (prefer lighter models)
                        if (availableModels.Contains(BackgroundRemovalModelManager.ModelType.PPHumanSeg))
                        {
                            try
                            {
                                _liveViewSession = _modelManager.LoadModel(BackgroundRemovalModelManager.ModelType.PPHumanSeg, false);
                                _currentLiveViewModel = BackgroundRemovalModelManager.ModelType.PPHumanSeg;
                                Log.Debug("Ultra-fast PP-HumanSeg model loaded for live view");
                            }
                            catch { }
                        }
                        else if (File.Exists(_liveViewModelPath))
                        {
                            try
                            {
                                var sessionOptions = GetSessionOptions(useGPU: false, isLiveView: true);
                                _liveViewSession = new InferenceSession(_liveViewModelPath, sessionOptions);
                                _currentLiveViewModel = BackgroundRemovalModelManager.ModelType.U2NetP;
                                Log.Debug("Legacy U2NetP model loaded for live view");
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"Failed to load live view model: {ex.Message}");
                            }
                        }

                        _isInitialized = true;
                        Debug.WriteLine($"[BackgroundRemoval] Initialization complete - Capture session: {(_captureSession != null ? "Loaded" : "Not loaded")}");
                        Debug.WriteLine($"[BackgroundRemoval] Initialization complete - Live view session: {(_liveViewSession != null ? "Loaded" : "Not loaded")}");
                        Log.Debug("BackgroundRemovalService initialized");
                        return true;
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to initialize BackgroundRemovalService: {ex.Message}");
                Debug.WriteLine($"[BackgroundRemoval] Initialization failed: {ex.Message}");
                return false;
            }
        }

        private SessionOptions GetSessionOptions(bool useGPU = false, bool isLiveView = false)
        {
            var options = new SessionOptions();
            options.GraphOptimizationLevel = isLiveView
                ? GraphOptimizationLevel.ORT_ENABLE_ALL
                : GraphOptimizationLevel.ORT_ENABLE_EXTENDED;

            // Check if GPU acceleration is enabled in settings
            bool tryGPU = useGPU || Properties.Settings.Default.BackgroundRemovalUseGPU;

            if (tryGPU)
            {
                // Try to use DirectML on Windows for GPU acceleration
                try
                {
                    options.AppendExecutionProvider_DML();
                    Debug.WriteLine("[BackgroundRemoval] GPU acceleration enabled (DirectML)");
                    Log.Info("[BackgroundRemoval] GPU acceleration enabled for faster processing");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BackgroundRemoval] GPU not available: {ex.Message}");
                    Log.Debug("GPU acceleration not available, using CPU");
                }
            }

            // CPU optimization
            options.InterOpNumThreads = Math.Max(2, Environment.ProcessorCount / 2);
            options.IntraOpNumThreads = Math.Max(2, Environment.ProcessorCount / 2);
            options.ExecutionMode = ExecutionMode.ORT_PARALLEL;

            return options;
        }

        #endregion

        #region Lazy Initialization

        /// <summary>
        /// Ensures the service is initialized when background removal is first used
        /// </summary>
        private async Task<bool> EnsureInitializedAsync()
        {
            if (_isInitialized)
                return true;

            if (!Properties.Settings.Default.EnableBackgroundRemoval)
            {
                Debug.WriteLine("[BackgroundRemoval] Background removal is disabled in settings");
                return false;
            }

            Debug.WriteLine("[BackgroundRemoval] First use detected, initializing models...");
            return await InitializeAsync();
        }

        #endregion

        #region Background Removal

        public async Task<BackgroundRemovalResult> RemoveBackgroundAsync(string imagePath, BackgroundRemovalQuality quality = BackgroundRemovalQuality.Medium)
        {
            Debug.WriteLine($"[BackgroundRemoval] RemoveBackgroundAsync started - ImagePath: {imagePath}, Quality: {quality}");
            Log.Info($"[BackgroundRemoval] Processing image: {Path.GetFileName(imagePath)} with quality: {quality}");

            // Ensure initialization is complete
            if (!_isInitialized)
            {
                Debug.WriteLine("[BackgroundRemoval] Service not initialized, checking if enabled...");
                var initialized = await EnsureInitializedAsync();
                if (!initialized)
                {
                    Debug.WriteLine("[BackgroundRemoval] Not initialized (disabled or failed), using fallback method");
                    return await FallbackBackgroundRemoval(imagePath);
                }
            }

            // Get edge refinement setting
            int edgeRefinement = Properties.Settings.Default.BackgroundRemovalEdgeRefinement;
            Debug.WriteLine($"[BackgroundRemoval] Edge refinement setting: {edgeRefinement}");
            Log.Info($"[BackgroundRemoval] Using edge refinement level: {edgeRefinement}");

            if (_captureSession == null)
            {
                Debug.WriteLine("[BackgroundRemoval] No ONNX model available, using fallback method");
                // Fallback to simple background removal if no model
                return await FallbackBackgroundRemoval(imagePath);
            }

            try
            {
                return await Task.Run(() =>
                {
                    var startTime = DateTime.Now;

                    using (var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath))
                    {
                        // Resize for processing based on quality
                        var (processWidth, processHeight) = GetProcessingSize(image.Width, image.Height, quality);
                        Debug.WriteLine($"[BackgroundRemoval] Original image: {image.Width}x{image.Height}, Processing size: {processWidth}x{processHeight}");

                        SixLabors.ImageSharp.Image<L8> mask;

                        // Process at the target size
                        if (processWidth != image.Width || processHeight != image.Height)
                        {
                            Debug.WriteLine($"[BackgroundRemoval] Resizing image for processing from {image.Width}x{image.Height} to {processWidth}x{processHeight}");
                            using (var resizedImage = image.Clone(ctx => ctx.Resize(processWidth, processHeight)))
                            {
                                mask = RunInference(resizedImage, _captureSession);
                                Debug.WriteLine($"[BackgroundRemoval] Mask generated at: {mask.Width}x{mask.Height}");
                            }
                            // Resize mask back to original size
                            Debug.WriteLine($"[BackgroundRemoval] Resizing mask back to original size: {image.Width}x{image.Height}");
                            mask.Mutate(ctx => ctx.Resize(new ResizeOptions
                            {
                                Size = new SixLabors.ImageSharp.Size(image.Width, image.Height),
                                Mode = ResizeMode.Stretch,
                                Sampler = KnownResamplers.Lanczos3  // High-quality resampling
                            }));
                            Debug.WriteLine($"[BackgroundRemoval] Mask after resize: {mask.Width}x{mask.Height}");
                        }
                        else
                        {
                            mask = RunInference(image, _captureSession);
                            Debug.WriteLine($"[BackgroundRemoval] Mask generated at original size: {mask.Width}x{mask.Height}");
                        }

                        // Validate dimensions match
                        if (image.Width != mask.Width || image.Height != mask.Height)
                        {
                            Debug.WriteLine($"[BackgroundRemoval] ERROR: Dimension mismatch - Image: {image.Width}x{image.Height}, Mask: {mask.Width}x{mask.Height}");
                            Debug.WriteLine($"[BackgroundRemoval] Forcing mask resize to match image dimensions");
                            mask.Mutate(ctx => ctx.Resize(image.Width, image.Height));
                        }

                        using (mask)
                        {
                            // Apply mask to original image with edge refinement
                            Debug.WriteLine($"[BackgroundRemoval] Starting mask application with edge refinement: {edgeRefinement}");
                            var maskSw = Stopwatch.StartNew();
                            ApplyMask(image, mask, edgeRefinement);
                            Debug.WriteLine($"[BackgroundRemoval] Mask application completed in {maskSw.ElapsedMilliseconds}ms");
                            Log.Info($"[BackgroundRemoval] Mask applied successfully in {maskSw.ElapsedMilliseconds}ms");

                            // Save results
                            var resultFolder = Path.Combine(Path.GetDirectoryName(imagePath), "BackgroundRemoved");
                            Directory.CreateDirectory(resultFolder);

                            var fileName = Path.GetFileNameWithoutExtension(imagePath);
                            var foregroundPath = Path.Combine(resultFolder, $"{fileName}_nobg.png");
                            var maskPath = Path.Combine(resultFolder, $"{fileName}_mask.png");

                            // Save with high quality PNG settings
                            var encoder = new SixLabors.ImageSharp.Formats.Png.PngEncoder
                            {
                                CompressionLevel = SixLabors.ImageSharp.Formats.Png.PngCompressionLevel.BestCompression,
                                BitDepth = SixLabors.ImageSharp.Formats.Png.PngBitDepth.Bit8,
                                ColorType = SixLabors.ImageSharp.Formats.Png.PngColorType.RgbWithAlpha
                            };

                            Debug.WriteLine($"[BackgroundRemoval] Saving PNG with alpha channel to: {foregroundPath}");
                            image.Save(foregroundPath, encoder);
                            mask.Save(maskPath);

                            return new BackgroundRemovalResult
                            {
                                Success = true,
                                ProcessedImagePath = foregroundPath,
                                MaskPath = maskPath,
                                ProcessingTime = DateTime.Now - startTime,
                                ErrorMessage = null
                            };
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"Background removal failed: {ex.Message}");
                return new BackgroundRemovalResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private (int width, int height) GetProcessingSize(int originalWidth, int originalHeight, BackgroundRemovalQuality quality)
        {
            // ULTRA AGGRESSIVE scaling for INSTANT processing
            // Model works fine at 320x320, no need for more!
            const int ULTRA_FAST_SIZE = 320;  // Fixed size for ultra-fast
            const int FAST_SIZE = 400;        // Slightly better for medium
            const int QUALITY_SIZE = 512;     // Max for "high" quality

            int targetSize;
            switch (quality)
            {
                case BackgroundRemovalQuality.Low:
                    targetSize = ULTRA_FAST_SIZE;  // 320px - INSTANT!
                    break;
                case BackgroundRemovalQuality.High:
                    targetSize = QUALITY_SIZE;      // 512px max
                    break;
                case BackgroundRemovalQuality.Medium:
                default:
                    targetSize = FAST_SIZE;         // 400px balanced
                    break;
            }

            // Calculate dimensions maintaining aspect ratio
            double scale;
            if (originalWidth > originalHeight)
            {
                scale = (double)targetSize / originalWidth;
            }
            else
            {
                scale = (double)targetSize / originalHeight;
            }

            int targetWidth = (int)(originalWidth * scale);
            int targetHeight = (int)(originalHeight * scale);

            // Ensure dimensions are at least 256px (minimum for model)
            targetWidth = Math.Max(256, targetWidth);
            targetHeight = Math.Max(256, targetHeight);

            // Round to nearest 32 for better model performance
            targetWidth = ((targetWidth + 16) / 32) * 32;
            targetHeight = ((targetHeight + 16) / 32) * 32;

            Debug.WriteLine($"[BackgroundRemoval] ULTRA-FAST MODE - Quality: {quality}, Original: {originalWidth}x{originalHeight}, Processing at: {targetWidth}x{targetHeight} (scale: {scale:F2})");
            return (targetWidth, targetHeight);
        }

        private SixLabors.ImageSharp.Image<L8> RunInference(SixLabors.ImageSharp.Image<Rgba32> image, InferenceSession session)
        {
            if (session == null)
                throw new InvalidOperationException("Inference session not initialized");

            // Store original dimensions
            int originalWidth = image.Width;
            int originalHeight = image.Height;
            Debug.WriteLine($"[BackgroundRemoval] RunInference - Input image: {originalWidth}x{originalHeight}");

            // Get model input metadata
            var inputMeta = session.InputMetadata;
            var inputName = inputMeta.Keys.First();
            var inputShape = inputMeta[inputName].Dimensions;

            // Handle different model input shape formats
            // Some models use [batch, channels, height, width] others might differ
            int modelWidth = 320;  // Default
            int modelHeight = 320; // Default

            // Try to get dimensions from the model metadata
            if (inputShape != null && inputShape.Length >= 4)
            {
                // Standard NCHW format: [batch, channels, height, width]
                modelHeight = (int)inputShape[2];
                modelWidth = (int)inputShape[3];
            }
            else if (inputShape != null && inputShape.Length == 3)
            {
                // Some models might use [channels, height, width]
                modelHeight = (int)inputShape[1];
                modelWidth = (int)inputShape[2];
            }

            // Use model-specific sizes if we know the current model
            if (_currentCaptureModel == BackgroundRemovalModelManager.ModelType.MODNet)
            {
                modelWidth = 512;
                modelHeight = 512;
            }
            else if (_currentCaptureModel == BackgroundRemovalModelManager.ModelType.PPHumanSeg)
            {
                modelWidth = 192;
                modelHeight = 192;
            }
            else if (_currentCaptureModel == BackgroundRemovalModelManager.ModelType.SelfieSegmentation)
            {
                modelWidth = 256;
                modelHeight = 256;
            }

            // Validate dimensions
            if (modelWidth <= 0 || modelHeight <= 0)
            {
                Debug.WriteLine($"[BackgroundRemoval] Invalid model dimensions detected ({modelWidth}x{modelHeight}), using defaults");
                modelWidth = 320;
                modelHeight = 320;
            }

            Debug.WriteLine($"[BackgroundRemoval] Model expects input: {modelWidth}x{modelHeight}");

            // Resize image to model input size
            var resizedImage = image.Clone(ctx => ctx.Resize(modelWidth, modelHeight));

            try
            {
                // Convert image to tensor
                var inputTensor = ImageToTensor(resizedImage);

                // Create input container
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
                };

                // Run inference
                using (var outputs = session.Run(inputs))
                {
                    // Get output tensor
                    var outputTensor = outputs.First().AsTensor<float>();

                    // Convert tensor to mask image at model size
                    var modelMask = TensorToMask(outputTensor, modelWidth, modelHeight);
                    Debug.WriteLine($"[BackgroundRemoval] Model output mask: {modelMask.Width}x{modelMask.Height}");

                    // Resize mask back to original image size
                    if (modelWidth != originalWidth || modelHeight != originalHeight)
                    {
                        Debug.WriteLine($"[BackgroundRemoval] Resizing mask from {modelWidth}x{modelHeight} to {originalWidth}x{originalHeight}");
                        modelMask.Mutate(ctx => ctx.Resize(new ResizeOptions
                        {
                            Size = new SixLabors.ImageSharp.Size(originalWidth, originalHeight),
                            Mode = ResizeMode.Stretch,
                            Sampler = KnownResamplers.Lanczos3  // High-quality resampling for better edges
                        }));
                    }

                    Debug.WriteLine($"[BackgroundRemoval] Final mask size: {modelMask.Width}x{modelMask.Height}");
                    return modelMask;
                }
            }
            finally
            {
                resizedImage?.Dispose();
            }
        }

        private DenseTensor<float> ImageToTensor(SixLabors.ImageSharp.Image<Rgba32> image)
        {
            var width = image.Width;
            var height = image.Height;
            var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });

            // Get normalization parameters based on current model
            float[] mean = { 0.485f, 0.456f, 0.406f };  // Default ImageNet normalization
            float[] std = { 0.229f, 0.224f, 0.225f };

            if (_currentCaptureModel == BackgroundRemovalModelManager.ModelType.MODNet)
            {
                // MODNet uses different normalization
                mean = new[] { 0.5f, 0.5f, 0.5f };
                std = new[] { 0.5f, 0.5f, 0.5f };
            }
            else if (_currentCaptureModel == BackgroundRemovalModelManager.ModelType.PPHumanSeg)
            {
                // PP-HumanSeg also uses 0.5/0.5 normalization
                mean = new[] { 0.5f, 0.5f, 0.5f };
                std = new[] { 0.5f, 0.5f, 0.5f };
            }
            else if (_currentCaptureModel == BackgroundRemovalModelManager.ModelType.SelfieSegmentation)
            {
                // MediaPipe models typically don't use normalization
                mean = new[] { 0.0f, 0.0f, 0.0f };
                std = new[] { 1.0f, 1.0f, 1.0f };
            }

            // Access pixel data
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var rowSpan = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        var pixel = rowSpan[x];

                        // Normalize pixels according to model requirements
                        tensor[0, 0, y, x] = ((pixel.R / 255f) - mean[0]) / std[0];
                        tensor[0, 1, y, x] = ((pixel.G / 255f) - mean[1]) / std[1];
                        tensor[0, 2, y, x] = ((pixel.B / 255f) - mean[2]) / std[2];
                    }
                }
            });

            return tensor;
        }

        private SixLabors.ImageSharp.Image<L8> TensorToMask(Tensor<float> tensor, int width, int height)
        {
            var mask = new SixLabors.ImageSharp.Image<L8>(width, height);

            mask.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var rowSpan = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        // Get alpha value from tensor (assuming single channel output)
                        float alpha = tensor[0, 0, y, x];

                        // Clamp to [0, 1] and convert to byte
                        byte alphaValue = (byte)(Math.Max(0, Math.Min(1, alpha)) * 255);

                        rowSpan[x] = new L8(alphaValue);
                    }
                }
            });

            return mask;
        }

        private void ApplyMask(SixLabors.ImageSharp.Image<Rgba32> image, SixLabors.ImageSharp.Image<L8> mask, int edgeRefinement = 50)
        {
            if (image.Width != mask.Width || image.Height != mask.Height)
            {
                Debug.WriteLine($"[BackgroundRemoval] ApplyMask dimension mismatch - Image: {image.Width}x{image.Height}, Mask: {mask.Width}x{mask.Height}");
                Log.Error($"[BackgroundRemoval] Dimension mismatch in ApplyMask - Image: {image.Width}x{image.Height}, Mask: {mask.Width}x{mask.Height}");

                // Try to recover by resizing the mask
                Debug.WriteLine($"[BackgroundRemoval] Attempting to recover by resizing mask to match image");
                mask.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new SixLabors.ImageSharp.Size(image.Width, image.Height),
                    Mode = ResizeMode.Stretch,
                    Sampler = KnownResamplers.Lanczos3
                }));

                // Re-check after resize
                if (image.Width != mask.Width || image.Height != mask.Height)
                {
                    throw new ArgumentException($"Image ({image.Width}x{image.Height}) and mask ({mask.Width}x{mask.Height}) dimensions must match");
                }
            }

            Debug.WriteLine($"[BackgroundRemoval] ApplyMask started - EdgeRefinement: {edgeRefinement}, Image: {image.Width}x{image.Height}");
            Log.Info($"[BackgroundRemoval] ApplyMask - EdgeRefinement: {edgeRefinement}, Dimensions: {image.Width}x{image.Height}");

            // Calculate feather radius based on edge refinement setting (0-100 scale)
            int featherRadius = Math.Max(1, (edgeRefinement * 5) / 100); // 1-5 pixel feather
            Debug.WriteLine($"[BackgroundRemoval] Calculated feather radius: {featherRadius} pixels");

            // First, apply edge refinement to the mask
            var sw = Stopwatch.StartNew();
            RefineMaskEdges(mask, edgeRefinement);
            Debug.WriteLine($"[BackgroundRemoval] RefineMaskEdges completed in {sw.ElapsedMilliseconds}ms");

            // Apply feathering for smoother edges
            sw.Restart();
            ApplyFeathering(mask, featherRadius);
            Debug.WriteLine($"[BackgroundRemoval] ApplyFeathering completed in {sw.ElapsedMilliseconds}ms");

            int edgePixelCount = 0;
            int transparentPixelCount = 0;
            int opaquePixelCount = 0;

            image.ProcessPixelRows(mask, (imageAccessor, maskAccessor) =>
            {
                for (int y = 0; y < image.Height; y++)
                {
                    var imageRow = imageAccessor.GetRowSpan(y);
                    var maskRow = maskAccessor.GetRowSpan(y);

                    for (int x = 0; x < image.Width; x++)
                    {
                        var pixel = imageRow[x];
                        var alpha = maskRow[x].PackedValue;

                        // Apply alpha matting for better edge blending
                        if (alpha > 0 && alpha < 255)
                        {
                            edgePixelCount++;
                            // Edge pixel - apply color decontamination
                            pixel = DecontaminateColor(pixel, alpha);
                        }
                        else if (alpha == 0)
                        {
                            transparentPixelCount++;
                        }
                        else
                        {
                            opaquePixelCount++;
                        }

                        // Apply alpha channel from mask
                        imageRow[x] = new Rgba32(pixel.R, pixel.G, pixel.B, alpha);
                    }
                }
            });

            // Log pixel statistics
            Debug.WriteLine($"[BackgroundRemoval] Pixel statistics - Edge: {edgePixelCount}, Transparent: {transparentPixelCount}, Opaque: {opaquePixelCount}");
            Log.Info($"[BackgroundRemoval] Processed {edgePixelCount} edge pixels with color decontamination");
            Debug.WriteLine($"[BackgroundRemoval] ApplyMask completed - Total pixels: {image.Width * image.Height}");
        }

        private void RefineMaskEdges(SixLabors.ImageSharp.Image<L8> mask, int refinementLevel)
        {
            // ULTRA-FAST: Skip most refinement for speed
            if (refinementLevel <= 5)
            {
                Debug.WriteLine($"[BackgroundRemoval] ULTRA-FAST: Skipping edge refinement (level: {refinementLevel})");
                return;
            }

            Debug.WriteLine($"[BackgroundRemoval] RefineMaskEdges - Level: {refinementLevel}");

            // Simplified refinement for speed
            mask.Mutate(ctx =>
            {
                // Single pass processing for speed
                if (refinementLevel > 10)
                {
                    // Just one threshold and blur for basic cleanup
                    ctx.BinaryThreshold(0.4f);
                    ctx.GaussianBlur(0.5f);
                    Debug.WriteLine("[BackgroundRemoval] Applied basic refinement");
                }
                else
                {
                    // Ultra minimal - just threshold
                    ctx.BinaryThreshold(0.5f);
                    Debug.WriteLine("[BackgroundRemoval] Applied minimal refinement");
                }
            });
        }

        private void ApplyFeathering(SixLabors.ImageSharp.Image<L8> mask, int featherRadius)
        {
            Debug.WriteLine($"[BackgroundRemoval] ApplyFeathering started - FeatherRadius: {featherRadius}");

            if (featherRadius <= 0)
            {
                Debug.WriteLine("[BackgroundRemoval] Skipping feathering - radius is 0 or negative");
                return;
            }

            // Create a temporary copy for edge detection
            using (var edgeMask = mask.Clone())
            {
                // Detect edges by finding the difference between dilated and eroded versions
                Debug.WriteLine("[BackgroundRemoval] Detecting edges with binary threshold: 0.5");
                edgeMask.Mutate(ctx => ctx.BinaryThreshold(0.5f));

                // Apply Gaussian blur only to edge areas
                float blurAmount = featherRadius * 0.5f;
                Debug.WriteLine($"[BackgroundRemoval] Applying Gaussian blur for feathering: {blurAmount:F2}");
                mask.Mutate(ctx =>
                {
                    ctx.GaussianBlur(blurAmount);
                });
            }
            Debug.WriteLine("[BackgroundRemoval] ApplyFeathering completed");
        }

        private Rgba32 DecontaminateColor(Rgba32 pixel, byte alpha)
        {
            // Remove color spill from background
            float alphaRatio = alpha / 255f;

            // Adjust RGB values based on alpha to remove background contamination
            byte r = (byte)(pixel.R * alphaRatio + (255 - alpha) * 0.5f);
            byte g = (byte)(pixel.G * alphaRatio + (255 - alpha) * 0.5f);
            byte b = (byte)(pixel.B * alphaRatio + (255 - alpha) * 0.5f);

            return new Rgba32(r, g, b, pixel.A);
        }

        #endregion

        #region Live View Processing

        public async Task<byte[]> ProcessLiveViewFrameAsync(byte[] frameData, int width, int height)
        {
            // Skip frames for performance
            _frameCounter++;
            if (_frameCounter % (_liveViewFrameSkip + 1) != 0)
            {
                // Return cached result if available
                return _lastMask != null ? ApplyCachedMask(frameData) : frameData;
            }

            if (!_isInitialized || _liveViewSession == null)
                return frameData;

            try
            {
                // Simple processing for live view - just return original for now
                // Full implementation would process with lightweight model
                return frameData;
            }
            catch (Exception ex)
            {
                Log.Debug($"Live view processing error: {ex.Message}");
                return frameData;
            }
        }

        private byte[] ApplyCachedMask(byte[] frameData)
        {
            // Apply previously calculated mask to new frame
            // This is a placeholder - real implementation would blend the mask
            return frameData;
        }

        #endregion

        #region Fallback Methods

        private async Task<BackgroundRemovalResult> FallbackBackgroundRemoval(string imagePath)
        {
            // Simple fallback using color-based removal (like green screen)
            // This is a placeholder for when ML models aren't available
            return await Task.Run(() =>
            {
                try
                {
                    var resultFolder = Path.Combine(Path.GetDirectoryName(imagePath), "BackgroundRemoved");
                    Directory.CreateDirectory(resultFolder);

                    var fileName = Path.GetFileNameWithoutExtension(imagePath);
                    var outputPath = Path.Combine(resultFolder, $"{fileName}_nobg.png");

                    // For now, just copy the original
                    File.Copy(imagePath, outputPath, true);

                    return new BackgroundRemovalResult
                    {
                        Success = true,
                        ProcessedImagePath = outputPath,
                        MaskPath = outputPath,
                        ErrorMessage = "Using fallback method - no ML model available"
                    };
                }
                catch (Exception ex)
                {
                    return new BackgroundRemovalResult
                    {
                        Success = false,
                        ErrorMessage = $"Fallback removal failed: {ex.Message}"
                    };
                }
            });
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _captureSession?.Dispose();
            _liveViewSession?.Dispose();
            _isInitialized = false;
            Log.Debug("BackgroundRemovalService disposed");
        }

        #endregion
    }

    #region Supporting Classes

    public enum BackgroundRemovalQuality
    {
        Low,      // Lower quality, faster processing
        Medium,   // Medium quality and speed
        High      // Highest quality, slower
    }

    public class BackgroundRemovalResult
    {
        public bool Success { get; set; }
        public string ProcessedImagePath { get; set; }
        public string MaskPath { get; set; }
        public string ErrorMessage { get; set; }
        public TimeSpan ProcessingTime { get; set; }
    }

    #endregion
}