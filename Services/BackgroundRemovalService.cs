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
using NetVips;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using VipsImage = NetVips.Image;
using Image = System.Drawing.Image;
using DeviceLog = CameraControl.Devices.Log;

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
        private InferenceSession _liveViewSession; // Lightweight model for live view (fallback)
        private InferenceSession _rvmLiveViewSession; // RVM MobileNet for live view streaming
        private bool _isInitialized;
        private readonly object _sessionLock = new object();
        private volatile byte[] _lastLiveViewFrame;
        private volatile int _lastLiveViewFrameWidth;
        private volatile int _lastLiveViewFrameHeight;
        private bool _useRvmLiveView;
        // Live tuning for RVM input scale when matte is weak
        private double _rvmScaleBoost = 1.0; // 1.0 = normal; >1.0 = larger input
        private int _rvmScaleBoostCooldown = 0; // frames to keep boost
        private DenseTensor<float> _r1State;
        private DenseTensor<float> _r2State;
        private DenseTensor<float> _r3State;
        private DenseTensor<float> _r4State;
        private int _rvmFrameCounter;
        private bool _loggedRvmDisabled;
        private volatile bool _isRvmProcessing;
        private volatile bool _isModnetProcessing;
        private int _modnetFrameCounter;
        private readonly object _backgroundCacheLock = new object();
        private SixLabors.ImageSharp.Image<Rgba32> _cachedBackgroundImage;
        private string _cachedBackgroundPath;
        private int _cachedBackgroundWidth;
        private int _cachedBackgroundHeight;
        private long _lastRvmProcessTicks;
        private long _lastModnetProcessTicks;
        private long _lastFpsLogTicks;
        private int _fpsFrameCount;
        private string _fpsMode;
        private readonly object _fpsLock = new object();
        private bool? _lastDesiredRvm;

        private const string LiveViewModeResponsive = "Responsive";
        private const string LiveViewModeSmooth = "Smooth";
        private const string LiveViewModeAuto = "Auto";
        private const int RvmMinFrameIntervalMs = 30;
        private const int ModnetMinFrameIntervalMs = 45;
        private const int LiveViewMaxOutputWidth = 640;
        // Alpha post-processing for live view (tune as needed)
        private const float LiveViewAlphaOffset = 0.00f;  // base offset
        private const float LiveViewAlphaGain = 2.5f;     // reduced gain for weak alphas
        private const float LiveViewAlphaGamma = 0.5f;    // <1 boosts mids, >1 compresses
        private const float LiveViewAlphaKneeLow = 0.02f; // lowered threshold for weak alphas
        private const float LiveViewAlphaKneeHigh = 0.20f;// lowered upper threshold

        // Model paths
        private readonly string _modelsFolder;
        private readonly string _captureModelPath;
        private readonly string _liveViewModelPath;
        private readonly string _rvmModelPath;

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
                        var availableNames = availableModels.Select(m => m.ToString()).ToList();
                        if (File.Exists(_rvmModelPath) && !availableNames.Contains("RVM"))
                        {
                            availableNames.Add("RVM");
                        }
                        Debug.WriteLine($"[BackgroundRemoval] Available models: {string.Join(", ", availableNames)}");
            }

            // Ensure models folder exists
            if (!Directory.Exists(_modelsFolder))
            {
                Directory.CreateDirectory(_modelsFolder);
            }

            // Set MODNet model path
            _captureModelPath = Path.Combine(_modelsFolder, "modnet.onnx");
            _liveViewModelPath = _captureModelPath; // Use same model for live view
            _rvmModelPath = Path.Combine(_modelsFolder, "rvm_mobilenetv3_fp32.onnx");
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

                        Debug.WriteLine($"[BackgroundRemoval] InitializeAsync - Checking for MODNet model...");

                        // Check if MODNet is available
                        var modnetPath = _modelManager.GetModelPath(BackgroundRemovalModelManager.ModelType.MODNet);
                        Debug.WriteLine($"[BackgroundRemoval] MODNet path: {modnetPath}");
                        Debug.WriteLine($"[BackgroundRemoval] MODNet exists: {File.Exists(modnetPath)}");

                        // Load MODNet model (only model we use)
                        bool captureModelLoaded = false;
                        try
                        {
                            var modelInfo = _modelManager.GetModelInfo(BackgroundRemovalModelManager.ModelType.MODNet);
                            Debug.WriteLine($"[BackgroundRemoval] Loading MODNet model ({modelInfo.Description})");

                            bool useGPU = Properties.Settings.Default.BackgroundRemovalUseGPU;
                            _captureSession = _modelManager.LoadModel(BackgroundRemovalModelManager.ModelType.MODNet, useGPU);
                            _currentCaptureModel = BackgroundRemovalModelManager.ModelType.MODNet;
                            captureModelLoaded = true;

                            Debug.WriteLine($"[BackgroundRemoval] ✓ MODNet loaded successfully (Speed: {modelInfo.SpeedMultiplier}x)");
                            DeviceLog.Info($"[BackgroundRemoval] Using MODNet model for background removal");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[BackgroundRemoval] Failed to load MODNet: {ex.Message}");
                            DeviceLog.Error($"Failed to load MODNet model: {ex.Message}");
                        }

                        if (!captureModelLoaded)
                        {
                            Debug.WriteLine("[BackgroundRemoval] Failed to load MODNet model - background removal unavailable");
                            DeviceLog.Error("Failed to load background removal model");
                            return false;
                        }

                        // Use same MODNet model for live view
                        _liveViewSession = _captureSession;
                        _currentLiveViewModel = BackgroundRemovalModelManager.ModelType.MODNet;

                        ApplyPreferredLiveViewMode();

                        if (ShouldUseRvmLiveView())
                        {
                            InitializeRvmLiveViewSession();
                        }
                        else
                        {
                            _useRvmLiveView = false;
                            DeviceLog.Debug("[BackgroundRemoval] Live view mode set to Responsive - using MODNet.");
                        }

                        _isInitialized = captureModelLoaded;
                        Debug.WriteLine($"[BackgroundRemoval] Initialization complete - Capture session: {(_captureSession != null ? "Loaded" : "Not loaded")}");
                        Debug.WriteLine($"[BackgroundRemoval] Initialization complete - Live view session: {(_liveViewSession != null ? "Loaded" : "Not loaded")}");
                        DeviceLog.Debug("BackgroundRemovalService initialized");
                        return true;
                    }
                });
            }
            catch (Exception ex)
            {
                DeviceLog.Error($"Failed to initialize BackgroundRemovalService: {ex.Message}");
                Debug.WriteLine($"[BackgroundRemoval] Initialization failed: {ex.Message}");
                return false;
            }
        }

        private SessionOptions GetSessionOptions(bool? useGPUOverride = null, bool isLiveView = false)
        {
            var options = new SessionOptions();
            options.GraphOptimizationLevel = isLiveView
                ? GraphOptimizationLevel.ORT_ENABLE_ALL
                : GraphOptimizationLevel.ORT_ENABLE_EXTENDED;

            // Check if GPU acceleration is enabled in settings
            bool tryGPU = useGPUOverride ?? Properties.Settings.Default.BackgroundRemovalUseGPU;

            if (tryGPU)
            {
                // Try to use DirectML on Windows for GPU acceleration
                try
                {
                    options.AppendExecutionProvider_DML();
                    Debug.WriteLine("[BackgroundRemoval] GPU acceleration enabled (DirectML)");
                    DeviceLog.Info("[BackgroundRemoval] GPU acceleration enabled for faster processing");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BackgroundRemoval] GPU not available: {ex.Message}");
                    DeviceLog.Debug($"[BackgroundRemoval] DirectML unavailable: {ex.Message}");
                    DeviceLog.Debug("GPU acceleration not available, using CPU");
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
            DeviceLog.Info($"[BackgroundRemoval] Processing image: {Path.GetFileName(imagePath)} with quality: {quality}");

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
            DeviceLog.Info($"[BackgroundRemoval] Using edge refinement level: {edgeRefinement}");

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
                            DeviceLog.Info($"[BackgroundRemoval] Mask applied successfully in {maskSw.ElapsedMilliseconds}ms");

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
                DeviceLog.Error($"Background removal failed: {ex.Message}");
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

            // MODNet uses 512x512 input
            modelWidth = 512;
            modelHeight = 512;

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

            // MODNet normalization parameters
            float[] mean = { 0.5f, 0.5f, 0.5f };
            float[] std = { 0.5f, 0.5f, 0.5f };

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
                DeviceLog.Error($"[BackgroundRemoval] Dimension mismatch in ApplyMask - Image: {image.Width}x{image.Height}, Mask: {mask.Width}x{mask.Height}");

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
            DeviceLog.Info($"[BackgroundRemoval] ApplyMask - EdgeRefinement: {edgeRefinement}, Dimensions: {image.Width}x{image.Height}");

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
            DeviceLog.Info($"[BackgroundRemoval] Processed {edgePixelCount} edge pixels with color decontamination");
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

        public Task<byte[]> ProcessLiveViewFrameAsync(byte[] frameData, int width, int height)
        {
            if (frameData == null || frameData.Length == 0)
            {
                return Task.FromResult(frameData);
            }

            if (!Properties.Settings.Default.EnableBackgroundRemoval ||
                !Properties.Settings.Default.EnableLiveViewBackgroundRemoval)
            {
                return Task.FromResult(frameData);
            }

            if (!_isInitialized)
            {
                var initialized = EnsureInitializedAsync().GetAwaiter().GetResult();
                if (!initialized)
                {
                    return Task.FromResult(frameData);
                }
            }

            var desiredRvm = ShouldUseRvmLiveView();
            if (_lastDesiredRvm != desiredRvm)
            {
                DeviceLog.Debug($"[BackgroundRemoval] Requested live view mode: {(desiredRvm ? "RVM" : "MODNet")}");
                _lastDesiredRvm = desiredRvm;
            }
            if (desiredRvm && !_useRvmLiveView)
            {
                InitializeRvmLiveViewSession();
            }
            else if (!desiredRvm && _useRvmLiveView)
            {
                _useRvmLiveView = false;
                _rvmLiveViewSession?.Dispose();
                _rvmLiveViewSession = null;
                _isRvmProcessing = false;
                _lastLiveViewFrame = null;
                _loggedRvmDisabled = false;
                DeviceLog.Debug("[BackgroundRemoval] Live view mode switched to Responsive - using MODNet.");
            }

            if (_useRvmLiveView && _rvmLiveViewSession != null)
            {
                if (!_isRvmProcessing)
                {
                    if (!ShouldProcessFrame(ref _lastRvmProcessTicks, RvmMinFrameIntervalMs))
                    {
                        var cached = _lastLiveViewFrame;
                        return Task.FromResult(cached ?? frameData);
                    }

                    _isRvmProcessing = true;
                    var frameCopy = (byte[])frameData.Clone();
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            var rvmBytes = ProcessLiveViewFrameWithRvm(frameCopy, width, height);
                            if (rvmBytes != null && rvmBytes.Length > 0)
                            {
                                _lastLiveViewFrame = rvmBytes;
                                // Update dimensions for async path - they should be set in ProcessLiveViewFrameWithRvm
                                // but we'll use the passed-in width/height for the log
                                TrackLiveViewFps("RVM");
                                if (_rvmFrameCounter % 10 == 0)
                                {
                                    DeviceLog.Debug($"[BackgroundRemoval] Cached RVM frame {width}x{height} ({rvmBytes.Length} bytes)");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            DeviceLog.Debug($"RVM live view processing failed, falling back to MODNet: {ex.Message}");
                        }
                        finally
                        {
                            _isRvmProcessing = false;
                        }
                    });
                }

                var latest = _lastLiveViewFrame;
                if (latest == null)
                {
                    _lastLiveViewFrameWidth = 0;
                    _lastLiveViewFrameHeight = 0;
                    return Task.FromResult(frameData);
                }

                return Task.FromResult(latest);
            }

            if (!_isInitialized)
            {
                // Ensure core services are ready
                var initialized = EnsureInitializedAsync().GetAwaiter().GetResult();
                if (!initialized)
                {
                    return Task.FromResult(frameData);
                }
            }

            if (_liveViewSession != null)
            {
                if (!_isModnetProcessing)
                {
                    if (!ShouldProcessFrame(ref _lastModnetProcessTicks, ModnetMinFrameIntervalMs))
                    {
                        var cached = _lastLiveViewFrame;
                        return Task.FromResult(cached ?? frameData);
                    }

                    _isModnetProcessing = true;
                    var frameCopy = (byte[])frameData.Clone();
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            var modnetBytes = ProcessLiveViewFrameWithModnet(frameCopy, width, height);
                            if (modnetBytes != null && modnetBytes.Length > 0)
                            {
                                _lastLiveViewFrame = modnetBytes;
                                TrackLiveViewFps("MODNet");
                                if (_modnetFrameCounter % 10 == 0)
                                {
                                    DeviceLog.Debug($"[BackgroundRemoval] Cached MODNet frame {_lastLiveViewFrameWidth}x{_lastLiveViewFrameHeight} ({modnetBytes.Length} bytes)");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            DeviceLog.Debug($"MODNet live view processing failed: {ex.Message}");
                        }
                        finally
                        {
                            _isModnetProcessing = false;
                        }
                    });
                }

                var latest = _lastLiveViewFrame;
                if (latest == null)
                {
                    _lastLiveViewFrameWidth = 0;
                    _lastLiveViewFrameHeight = 0;
                    return Task.FromResult(frameData);
                }

                return Task.FromResult(latest);
            }

            return Task.FromResult(frameData);
        }

        private byte[] ProcessLiveViewFrameWithRvm(byte[] frameData, int width, int height)
        {
            if (_rvmLiveViewSession == null)
            {
                return null;
            }

            var stopwatch = Stopwatch.StartNew();
            int maxDim = (int)Math.Min(512, Math.Round(384 * _rvmScaleBoost));
            float downsampleRatio = CalculateDownsampleRatio(width, height, 512.0 * 512.0, maxDim, 0.25);
            EnsureRvmStates();
            try
            {
                // Use NetVips for faster image processing
                using (var vipsImage = VipsImage.NewFromBuffer(frameData))
                {
                    int targetWidth = Math.Max(64, AlignToMultiple((int)(width * downsampleRatio), 16));
                    int targetHeight = Math.Max(64, AlignToMultiple((int)(height * downsampleRatio), 16));
                    targetWidth = Math.Min(width, targetWidth);
                    targetHeight = Math.Min(height, targetHeight);

                    var image = vipsImage;
                    if (targetWidth != width || targetHeight != height)
                    {
                        image = vipsImage.Resize((double)targetWidth / vipsImage.Width,
                                                vscale: (double)targetHeight / vipsImage.Height,
                                                kernel: Enums.Kernel.Lanczos3);
                    }

                    if (image.Width > LiveViewMaxOutputWidth)
                    {
                        double scale = LiveViewMaxOutputWidth / (double)image.Width;
                        image = image.Resize(scale, vscale: scale, kernel: Enums.Kernel.Lanczos3);
                    }

                    if (_rvmFrameCounter % 10 == 0)
                    {
                        DeviceLog.Debug($"[BackgroundRemoval] RVM inference size: {image.Width}x{image.Height} (ratio {downsampleRatio:F2})");
                    }

                    var srcTensor = VipsImageToTensorForRvm(image);

                    var inputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("src", srcTensor),
                        NamedOnnxValue.CreateFromTensor("r1i", _r1State),
                        NamedOnnxValue.CreateFromTensor("r2i", _r2State),
                        NamedOnnxValue.CreateFromTensor("r3i", _r3State),
                        NamedOnnxValue.CreateFromTensor("r4i", _r4State),
                        NamedOnnxValue.CreateFromTensor("downsample_ratio", new DenseTensor<float>(new[] { downsampleRatio }, new[] { 1 }))
                    };

                    using (var results = _rvmLiveViewSession.Run(inputs))
                    {
                        var fgrTensor = results.FirstOrDefault(r => r.Name.Equals("fgr", StringComparison.OrdinalIgnoreCase))?.AsTensor<float>();
                        var phaTensor = results.FirstOrDefault(r => r.Name.Equals("pha", StringComparison.OrdinalIgnoreCase))?.AsTensor<float>();
                        var r1Out = results.FirstOrDefault(r => r.Name.Equals("r1o", StringComparison.OrdinalIgnoreCase))?.AsTensor<float>();
                        var r2Out = results.FirstOrDefault(r => r.Name.Equals("r2o", StringComparison.OrdinalIgnoreCase))?.AsTensor<float>();
                        var r3Out = results.FirstOrDefault(r => r.Name.Equals("r3o", StringComparison.OrdinalIgnoreCase))?.AsTensor<float>();
                        var r4Out = results.FirstOrDefault(r => r.Name.Equals("r4o", StringComparison.OrdinalIgnoreCase))?.AsTensor<float>();

                        if (fgrTensor == null || phaTensor == null)
                        {
                            DeviceLog.Debug("RVM live view inference returned null foreground or alpha tensor");
                            return null;
                        }

                        // Quick alpha statistics to detect inversions or degenerate output
                        double alphaSum = 0; double alphaMin = 1, alphaMax = 0;
                        int aH = (int)phaTensor.Dimensions[2];
                        int aW = (int)phaTensor.Dimensions[3];
                        int sampleStrideY = Math.Max(1, aH / 32);
                        int sampleStrideX = Math.Max(1, aW / 32);
                        int sampleCount = 0;
                        for (int yy = 0; yy < aH; yy += sampleStrideY)
                        {
                            for (int xx = 0; xx < aW; xx += sampleStrideX)
                            {
                                float a = phaTensor[0, 0, yy, xx];
                                alphaSum += a;
                                if (a < alphaMin) alphaMin = a;
                                if (a > alphaMax) alphaMax = a;
                                sampleCount++;
                            }
                        }
                        double alphaAvg = sampleCount > 0 ? alphaSum / sampleCount : 0;

                        // Compute center vs border averages to decide inversion robustly
                        double centerSum = 0; int centerCount = 0;
                        double borderSum = 0; int borderCount = 0;
                        int cx0 = (int)(aW * 0.25), cx1 = (int)(aW * 0.75);
                        int cy0 = (int)(aH * 0.25), cy1 = (int)(aH * 0.75);
                        for (int yy = 0; yy < aH; yy += sampleStrideY)
                        {
                            for (int xx = 0; xx < aW; xx += sampleStrideX)
                            {
                                float aval = phaTensor[0, 0, yy, xx];
                                bool inCenter = (xx >= cx0 && xx <= cx1 && yy >= cy0 && yy <= cy1);
                                if (inCenter) { centerSum += aval; centerCount++; }
                                else { borderSum += aval; borderCount++; }
                            }
                        }
                        double centerAvg = centerCount > 0 ? centerSum / centerCount : alphaAvg;
                        double borderAvg = borderCount > 0 ? borderSum / borderCount : alphaAvg;

                        // Check if alpha might be inverted (subject is dark, background is bright)
                        bool possiblyInverted = centerAvg < 0.15 && borderAvg > centerAvg * 1.5;

                        // Try auto-detection: if overall alpha is very low, likely needs inversion
                        bool autoInvert = alphaAvg < 0.15 && alphaMax > 0.5;
                        bool invert = possiblyInverted || autoInvert;

                        // RVM fallback to MODNet when matte is completely degenerate
                        // More lenient threshold to avoid failures
                        bool degenerate = alphaMax < 0.02 || (alphaAvg < 0.05 && centerAvg < 0.05);

                        if (_rvmFrameCounter % 10 == 0)
                        {
                            DeviceLog.Debug($"[BackgroundRemoval] Alpha stats avg={alphaAvg:F2} center={centerAvg:F2} border={borderAvg:F2} min={alphaMin:F2} max={alphaMax:F2}");
                            DeviceLog.Debug($"[BackgroundRemoval] Alpha decision: invert={invert} (possiblyInv={possiblyInverted} autoInv={autoInvert}) degenerate={degenerate}");
                        }

                        UpdateRvmStates(r1Out, r2Out, r3Out, r4Out);

                        if (degenerate)
                        {
                            _rvmScaleBoost = Math.Min(2.0, _rvmScaleBoost * 1.25);
                            _rvmScaleBoostCooldown = 30; // keep boost for ~30 frames
                            DeviceLog.Debug("[BackgroundRemoval] RVM matte degenerate; falling back to MODNet for this frame");
                            return ProcessLiveViewFrameWithModnet(frameData, width, height);
                        }
                        else
                        {
                            if (_rvmScaleBoostCooldown > 0) _rvmScaleBoostCooldown--;
                            else _rvmScaleBoost = Math.Max(1.0, _rvmScaleBoost * 0.95);
                        }

                        using (var composed = BuildForegroundFromVips(image, phaTensor, invert))
                        {
                            int finalWidth = Math.Min(width, LiveViewMaxOutputWidth);
                            if (finalWidth < 1) finalWidth = 1;
                            int finalHeight = Math.Max(1, (int)Math.Round(finalWidth / (double)width * height));

                            var finalImage = composed;
                            if (composed.Width != finalWidth || composed.Height != finalHeight)
                            {
                                double scale = (double)finalWidth / composed.Width;
                                finalImage = composed.Resize(scale, vscale: (double)finalHeight / composed.Height, kernel: Enums.Kernel.Lanczos3);
                            }

                            _rvmFrameCounter++;
                            var compositeBytes = CompositeLiveViewFrameVips(finalImage);
                            stopwatch.Stop();
                            DeviceLog.Debug($"[BackgroundRemoval] RVM frame processed in {stopwatch.ElapsedMilliseconds}ms");

                            // Update dimensions before validation
                            width = finalImage.Width;
                            height = finalImage.Height;
                            _lastLiveViewFrameWidth = width;
                            _lastLiveViewFrameHeight = height;

                            if (finalImage != composed)
                                finalImage.Dispose();

                            if (compositeBytes == null)
                            {
                                _lastLiveViewFrameWidth = 0;
                                _lastLiveViewFrameHeight = 0;
                                return frameData;
                            }

                            // Don't validate JPEG byte count as it's compressed
                            // Just return the JPEG bytes
                            _lastLiveViewFrame = compositeBytes;
                            return compositeBytes;
                    }
                }
            }
            }
            finally
            {
                if (stopwatch.IsRunning)
                {
                    stopwatch.Stop();
                    DeviceLog.Debug($"[BackgroundRemoval] RVM frame processed in {stopwatch.ElapsedMilliseconds}ms (early exit)");
                }
            }
        }

        private void InitializeRvmLiveViewSession()
        {
            lock (_sessionLock)
            {
                if (_rvmLiveViewSession != null)
                {
                    return;
                }

                if (!ShouldUseRvmLiveView())
                {
                    _useRvmLiveView = false;
                    return;
                }

                try
                {
                    DeviceLog.Debug($"[BackgroundRemoval] Checking for RVM model at {_rvmModelPath}");
                    if (File.Exists(_rvmModelPath))
                    {
                        var rvmOptions = GetSessionOptions(useGPUOverride: true, isLiveView: true);
                        _rvmLiveViewSession = new InferenceSession(_rvmModelPath, rvmOptions);
                        _useRvmLiveView = true;
                        _rvmFrameCounter = 0;
                        _loggedRvmDisabled = false;
                        DeviceLog.Debug("[BackgroundRemoval] Live view mode: RVM (Smooth)");
                        DeviceLog.Debug("[BackgroundRemoval] RVM MobileNet model loaded for live-view background removal");
                        DeviceLog.Debug("[BackgroundRemoval] RVM session created with CPU execution provider");
                        foreach (var meta in _rvmLiveViewSession.InputMetadata)
                        {
                            var dims = string.Join(",", meta.Value.Dimensions.Select(d => d.ToString()));
                            DeviceLog.Debug($"[BackgroundRemoval] RVM input: {meta.Key} -> [{dims}] type={meta.Value.ElementType}");
                        }
                        foreach (var meta in _rvmLiveViewSession.OutputMetadata)
                        {
                            var dims = string.Join(",", meta.Value.Dimensions.Select(d => d.ToString()));
                            DeviceLog.Debug($"[BackgroundRemoval] RVM output: {meta.Key} -> [{dims}] type={meta.Value.ElementType}");
                        }
                    }
                    else
                    {
                        _useRvmLiveView = false;
                        DeviceLog.Debug("[BackgroundRemoval] Live view mode: MODNet (Responsive)");
                        DeviceLog.Debug("[BackgroundRemoval] RVM MobileNet model not found, falling back to MODNet for live view.");
                    }
                }
                catch (Exception ex)
                {
                    _useRvmLiveView = false;
                    _loggedRvmDisabled = false;
                    _rvmLiveViewSession?.Dispose();
                    _rvmLiveViewSession = null;
                    DeviceLog.Error($"Failed to load RVM MobileNet model: {ex.Message}");
                }
                finally
                {
                    DeviceLog.Debug($"[BackgroundRemoval] RVM live view enabled: {_useRvmLiveView}");
                }
            }
        }

        private byte[] ProcessLiveViewFrameWithModnet(byte[] frameData, int width, int height)
        {
            if (_liveViewSession == null)
            {
                return null;
            }

            float downsampleRatio = CalculateDownsampleRatio(width, height, 320.0 * 320.0, 512, 0.1);

            try
            {
                using (var image = SixLabors.ImageSharp.Image.Load<Rgba32>(frameData))
                {
                    int targetWidth = Math.Max(64, AlignToMultiple((int)(width * downsampleRatio), 16));
                    int targetHeight = Math.Max(64, AlignToMultiple((int)(height * downsampleRatio), 16));
                    targetWidth = Math.Min(width, targetWidth);
                    targetHeight = Math.Min(height, targetHeight);

                    if (targetWidth != width || targetHeight != height)
                    {
                        image.Mutate(ctx => ctx.Resize(targetWidth, targetHeight));
                    }

                    if (image.Width > LiveViewMaxOutputWidth)
                    {
                        int resizedHeight = Math.Max(1, (int)Math.Round(image.Height * (LiveViewMaxOutputWidth / (double)image.Width)));
                        image.Mutate(ctx => ctx.Resize(LiveViewMaxOutputWidth, resizedHeight));
                    }

                    using (var mask = RunInference(image, _liveViewSession))
                    {
                        int edgeRefinement = Math.Max(3, Properties.Settings.Default.BackgroundRemovalEdgeRefinement / 2);
                        ApplyMask(image, mask, edgeRefinement);
                    }

                    int finalWidth = Math.Min(width, LiveViewMaxOutputWidth);
                    if (finalWidth < 1) finalWidth = 1;
                    int finalHeight = Math.Max(1, (int)Math.Round(finalWidth / (double)width * height));

                    if (image.Width != finalWidth || image.Height != finalHeight)
                    {
                        image.Mutate(ctx => ctx.Resize(finalWidth, finalHeight, KnownResamplers.Lanczos3));
                    }

                    _modnetFrameCounter++;
                    var composed = CompositeLiveViewFrame(image);
                    if (composed == null)
                    {
                        _lastLiveViewFrameWidth = 0;
                        _lastLiveViewFrameHeight = 0;
                        return frameData;
                    }

                    width = image.Width;
                    height = image.Height;
                    return composed;
                }
            }
            catch (Exception ex)
            {
                DeviceLog.Debug($"MODNet live view processing failed: {ex.Message}");
                return null;
            }
        }

        private byte[] CompositeLiveViewFrameVips(VipsImage foreground)
        {
            if (foreground == null)
                return null;

            try
            {
                // Get background path from VirtualBackgroundService
                string backgroundPath = VirtualBackgroundService.Instance?.GetDefaultBackgroundPath();

                // Composite with background if enabled and available
                if (!string.IsNullOrEmpty(backgroundPath) && File.Exists(backgroundPath))
                {
                    VipsImage background = null;

                    try
                    {
                        // Always load the background image
                        background = VipsImage.NewFromFile(backgroundPath);
                        DeviceLog.Debug($"[BackgroundRemoval] Loaded background from: {backgroundPath} ({background.Width}x{background.Height})");

                        // Resize to match foreground if needed
                        if (background.Width != foreground.Width || background.Height != foreground.Height)
                        {
                            double xscale = (double)foreground.Width / background.Width;
                            double yscale = (double)foreground.Height / background.Height;
                            double scale = Math.Max(xscale, yscale); // Cover mode

                            var resized = background.Resize(scale, vscale: scale, kernel: Enums.Kernel.Lanczos3);
                            background.Dispose();
                            background = resized;

                            // Center crop if needed
                            if (background.Width > foreground.Width || background.Height > foreground.Height)
                            {
                                int x = (background.Width - foreground.Width) / 2;
                                int y = (background.Height - foreground.Height) / 2;
                                var cropped = background.Crop(x, y, foreground.Width, foreground.Height);
                                background.Dispose();
                                background = cropped;
                            }
                        }

                        // Ensure RGBA
                        if (background.Bands == 3)
                        {
                            var withAlpha = background.Bandjoin(255);
                            background.Dispose();
                            background = withAlpha;
                        }
                    }
                    catch (Exception ex)
                    {
                        DeviceLog.Debug($"[BackgroundRemoval] Failed to load background: {ex.Message}");
                    }

                    if (background != null)
                    {
                        DeviceLog.Debug($"[BackgroundRemoval] Compositing foreground ({foreground.Width}x{foreground.Height}, bands={foreground.Bands}) over background ({background.Width}x{background.Height}, bands={background.Bands})");

                        // In NetVips, composite takes [background, overlay1, overlay2, ...] with mode
                        // We want to composite foreground OVER background
                        var images = new[] { foreground };
                        var modes = new[] { Enums.BlendMode.Over };
                        var result = background.Composite(images, modes);

                        var jpegBytes = result.JpegsaveBuffer(q: 90);
                        background.Dispose();
                        result.Dispose();
                        DeviceLog.Debug($"[BackgroundRemoval] Composite successful, JPEG size: {jpegBytes.Length} bytes");
                        return jpegBytes;
                    }
                }

                // No background, just export foreground as JPEG
                if (foreground.Bands == 4)
                {
                    // Convert RGBA to RGB with white background for JPEG
                    var white = VipsImage.Black(foreground.Width, foreground.Height, bands: 3) + 255;
                    var composited = white.Composite(foreground, Enums.BlendMode.Over);
                    var jpegBytes = composited.JpegsaveBuffer(q: 90);
                    white.Dispose();
                    composited.Dispose();
                    return jpegBytes;
                }
                else
                {
                    return foreground.JpegsaveBuffer(q: 90);
                }
            }
            catch (Exception ex)
            {
                DeviceLog.Debug($"[BackgroundRemoval] CompositeLiveViewFrameVips failed: {ex.Message}");
                return null;
            }
        }

        private byte[] CompositeLiveViewFrame(SixLabors.ImageSharp.Image<Rgba32> foreground)
        {
            if (foreground == null)
            {
                return null;
            }

            int width = foreground.Width;
            int height = foreground.Height;
            if (width == 0 || height == 0)
            {
                return null;
            }

            try
            {
                using (var background = GetCachedBackground(width, height))
                {
                    background.Mutate(ctx => ctx.DrawImage(foreground, 1f));

                    var rgba = new byte[width * height * 4];
                    background.CopyPixelDataTo(rgba);

                    for (int i = 0; i < rgba.Length; i += 4)
                    {
                        byte r = rgba[i];
                        rgba[i] = rgba[i + 2];
                        rgba[i + 2] = r;
                    }

                    _lastLiveViewFrameWidth = width;
                    _lastLiveViewFrameHeight = height;
                    return rgba;
                }
            }
            catch (Exception ex)
            {
                DeviceLog.Debug($"Live view background composite error: {ex.Message}");
                return null;
            }
        }

        private SixLabors.ImageSharp.Image<Rgba32> GetCachedBackground(int width, int height)
        {
            if (width <= 0) width = 1;
            if (height <= 0) height = 1;

            string backgroundPath = VirtualBackgroundService.Instance.GetDefaultBackgroundPath();

            lock (_backgroundCacheLock)
            {
                if (string.IsNullOrWhiteSpace(backgroundPath) || !File.Exists(backgroundPath))
                {
                    return new SixLabors.ImageSharp.Image<Rgba32>(width, height, new Rgba32(30, 30, 30));
                }

                bool needsReload = _cachedBackgroundImage == null ||
                                   !string.Equals(_cachedBackgroundPath, backgroundPath, StringComparison.OrdinalIgnoreCase) ||
                                   _cachedBackgroundWidth != width ||
                                   _cachedBackgroundHeight != height;

                if (needsReload)
                {
                    _cachedBackgroundImage?.Dispose();
                    _cachedBackgroundImage = LoadBackgroundWithNetVips(backgroundPath, width, height);
                    _cachedBackgroundPath = backgroundPath;
                    _cachedBackgroundWidth = width;
                    _cachedBackgroundHeight = height;
                }

                return _cachedBackgroundImage?.Clone() ?? new SixLabors.ImageSharp.Image<Rgba32>(width, height, new Rgba32(30, 30, 30));
            }
        }

        private SixLabors.ImageSharp.Image<Rgba32> LoadBackgroundWithNetVips(string path, int width, int height)
        {
            int safeWidth = Math.Max(1, width);
            int safeHeight = Math.Max(1, height);
            try
            {
                using (var source = VipsImage.NewFromFile(path, access: NetVips.Enums.Access.Sequential))
                using (var resized = ResizeBackground(source, safeWidth, safeHeight))
                {
                    if (resized == null)
                    {
                        return new SixLabors.ImageSharp.Image<Rgba32>(safeWidth, safeHeight, new Rgba32(30, 30, 30));
                    }

                    var buffer = resized.WriteToBuffer(".png");
                    using (var ms = new MemoryStream(buffer))
                    {
                        return SixLabors.ImageSharp.Image.Load<Rgba32>(ms);
                    }
                }
            }
            catch (Exception ex)
            {
                DeviceLog.Debug($"[BackgroundRemoval] NetVips resize failed for '{path}': {ex.Message}");
                return new SixLabors.ImageSharp.Image<Rgba32>(safeWidth, safeHeight, new Rgba32(30, 30, 30));
            }
        }

        private static VipsImage ResizeBackground(VipsImage source, int width, int height)
        {
            if (source == null)
            {
                return null;
            }

            int safeWidth = Math.Max(1, width);
            int safeHeight = Math.Max(1, height);

            double scale = Math.Max((double)safeWidth / Math.Max(1, source.Width), (double)safeHeight / Math.Max(1, source.Height));
            if (scale <= 0)
            {
                scale = 1;
            }

            var scaled = source.Resize(scale, kernel: NetVips.Enums.Kernel.Lanczos3);

            if (scaled.Width != safeWidth || scaled.Height != safeHeight)
            {
                int cropWidth = Math.Min(safeWidth, scaled.Width);
                int cropHeight = Math.Min(safeHeight, scaled.Height);
                int left = Math.Max(0, (scaled.Width - cropWidth) / 2);
                int top = Math.Max(0, (scaled.Height - cropHeight) / 2);

                var cropped = scaled.ExtractArea(left, top, cropWidth, cropHeight);
                scaled.Dispose();
                scaled = cropped;

                if (scaled.Width != safeWidth || scaled.Height != safeHeight)
                {
                    var embedded = scaled.Embed(0, 0, safeWidth, safeHeight, extend: NetVips.Enums.Extend.Copy);
                    scaled.Dispose();
                    scaled = embedded;
                }
            }

            return scaled;
        }

        private float CalculateDownsampleRatio(int width, int height, double referenceArea, int maxDimension, double minRatio = 0.1)
        {
            if (width <= 0 || height <= 0)
            {
                return 1f;
            }

            double area = Math.Max(1, width * height);
            double downsample = Math.Sqrt(referenceArea / area);
            double limit = Math.Min(1.0, maxDimension / (double)Math.Max(width, height));
            downsample = Math.Min(downsample, limit);
            downsample = Math.Max(minRatio, downsample);
            return (float)Math.Round(downsample, 3, MidpointRounding.AwayFromZero);
        }

        private bool ShouldUseRvmLiveView()
        {
            var mode = Properties.Settings.Default.LiveViewBackgroundRemovalMode;
            if (string.IsNullOrWhiteSpace(mode) || string.Equals(mode, LiveViewModeAuto, StringComparison.OrdinalIgnoreCase))
            {
                return File.Exists(_rvmModelPath);
            }

            if (string.Equals(mode, LiveViewModeSmooth, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!File.Exists(_rvmModelPath))
            {
                return false;
            }

            // If RVM exists but mode is still responsive, honour manual override but log once
            if (!_loggedRvmDisabled)
            {
                DeviceLog.Debug("[BackgroundRemoval] RVM model detected but live view mode is set to Responsive. Switching to Smooth for better quality.");
            }
            Properties.Settings.Default.LiveViewBackgroundRemovalMode = LiveViewModeSmooth;
            Properties.Settings.Default.Save();
            return true;
        }

        private void ApplyPreferredLiveViewMode()
        {
            bool rvmAvailable = File.Exists(_rvmModelPath);
            var current = Properties.Settings.Default.LiveViewBackgroundRemovalMode;

            if (rvmAvailable)
            {
                if (!string.Equals(current, LiveViewModeSmooth, StringComparison.OrdinalIgnoreCase))
                {
                    Properties.Settings.Default.LiveViewBackgroundRemovalMode = LiveViewModeSmooth;
                    Properties.Settings.Default.Save();
                    DeviceLog.Debug("[BackgroundRemoval] RVM model detected - defaulting live view mode to Smooth.");
                }
            }
            else
            {
                if (string.Equals(current, LiveViewModeSmooth, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(current, LiveViewModeAuto, StringComparison.OrdinalIgnoreCase))
                {
                    Properties.Settings.Default.LiveViewBackgroundRemovalMode = LiveViewModeResponsive;
                    Properties.Settings.Default.Save();
                    DeviceLog.Debug("[BackgroundRemoval] RVM model missing - reverting live view mode to Responsive.");
                }
            }
        }

        public bool TryGetLatestFrameInfo(out int width, out int height)
        {
            width = _lastLiveViewFrameWidth;
            height = _lastLiveViewFrameHeight;
            return _lastLiveViewFrame != null && width > 0 && height > 0;
        }

        private int AlignToMultiple(int value, int multiple)
        {
            if (multiple <= 1)
                return Math.Max(1, value);
            return Math.Max(multiple, ((value + multiple - 1) / multiple) * multiple);
        }

        private void EnsureRvmStates()
        {
            if (_r1State == null)
            {
                _r1State = CreateZeroStateTensor(1, 16, 1, 1);
            }
            if (_r2State == null)
            {
                _r2State = CreateZeroStateTensor(1, 20, 1, 1);
            }
            if (_r3State == null)
            {
                _r3State = CreateZeroStateTensor(1, 40, 1, 1);
            }
            if (_r4State == null)
            {
                _r4State = CreateZeroStateTensor(1, 64, 1, 1);
            }
        }

        private DenseTensor<float> VipsImageToTensorForRvm(VipsImage image)
        {
            int width = image.Width;
            int height = image.Height;
            var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });

            // Ensure RGB format
            if (image.Bands == 4)
            {
                image = image.ExtractBand(0, n: 3);
            }
            else if (image.Bands == 1)
            {
                image = image.Bandjoin(new[] { image, image });
            }

            // Use simpler normalization for better alpha detection
            const float meanR = 0.5f, meanG = 0.5f, meanB = 0.5f;
            const float stdR = 0.5f, stdG = 0.5f, stdB = 0.5f;

            // Get pixel data efficiently
            var pixels = image.WriteToMemory();
            int stride = width * 3; // RGB

            unsafe
            {
                fixed (byte* pPixels = pixels)
                {
                    for (int y = 0; y < height; y++)
                    {
                        byte* row = pPixels + y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            int idx = x * 3;
                            tensor[0, 0, y, x] = (row[idx] / 255f - meanR) / stdR;     // R
                            tensor[0, 1, y, x] = (row[idx + 1] / 255f - meanG) / stdG; // G
                            tensor[0, 2, y, x] = (row[idx + 2] / 255f - meanB) / stdB; // B
                        }
                    }
                }
            }
            return tensor;
        }

        private DenseTensor<float> ImageToTensorForRvm(SixLabors.ImageSharp.Image<Rgba32> image)
        {
            int width = image.Width;
            int height = image.Height;
            var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });
            // Use simpler normalization for better alpha detection
            const float meanR = 0.5f, meanG = 0.5f, meanB = 0.5f;
            const float stdR = 0.5f, stdG = 0.5f, stdB = 0.5f;
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        var pixel = row[x];
                        float r = pixel.R / 255f;
                        float g = pixel.G / 255f;
                        float b = pixel.B / 255f;
                        tensor[0, 0, y, x] = (r - meanR) / stdR;
                        tensor[0, 1, y, x] = (g - meanG) / stdG;
                        tensor[0, 2, y, x] = (b - meanB) / stdB;
                    }
                }
            });

            return tensor;
        }

        private DenseTensor<float> CreateZeroStateTensor(int d0, int d1, int d2, int d3)
        {
            return new DenseTensor<float>(new[] { d0, d1, d2, d3 });
        }

        private void UpdateRvmStates(Tensor<float> r1Out, Tensor<float> r2Out, Tensor<float> r3Out, Tensor<float> r4Out)
        {
            if (r1Out != null) _r1State = CloneTensor(r1Out);
            if (r2Out != null) _r2State = CloneTensor(r2Out);
            if (r3Out != null) _r3State = CloneTensor(r3Out);
            if (r4Out != null) _r4State = CloneTensor(r4Out);
        }

        private DenseTensor<float> CloneTensor(Tensor<float> tensor)
        {
            var dimsSpan = tensor.Dimensions;
            var dims = new int[dimsSpan.Length];
            for (int i = 0; i < dimsSpan.Length; i++)
            {
                dims[i] = dimsSpan[i];
            }
            var data = tensor.ToArray();
            return new DenseTensor<float>(data, dims);
        }

        private SixLabors.ImageSharp.Image<Rgba32> CreateImageFromRvmOutput(Tensor<float> fgr, Tensor<float> pha, bool invertAlpha = false)
        {
            int height = (int)fgr.Dimensions[2];
            int width = (int)fgr.Dimensions[3];
            var image = new SixLabors.ImageSharp.Image<Rgba32>(width, height);

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        float r = fgr[0, 0, y, x];
                        float g = fgr[0, 1, y, x];
                        float b = fgr[0, 2, y, x];
                        float alpha = pha[0, 0, y, x];
                        if (invertAlpha)
                        {
                            alpha = 1f - alpha;
                        }

                        alpha = PostProcessAlpha(alpha);

                        float alphaClamped = Math.Max(0f, Math.Min(1f, alpha));
                        float rClamped = Math.Max(0f, Math.Min(1f, r));
                        float gClamped = Math.Max(0f, Math.Min(1f, g));
                        float bClamped = Math.Max(0f, Math.Min(1f, b));

                        byte aByte = (byte)(alphaClamped * 255f);
                        byte rByte = (byte)(rClamped * 255f);
                        byte gByte = (byte)(gClamped * 255f);
                        byte bByte = (byte)(bClamped * 255f);

                        row[x] = new Rgba32(rByte, gByte, bByte, aByte);
                    }
                }
            });

            return image;
        }

        private VipsImage BuildForegroundFromVips(VipsImage original, Tensor<float> pha, bool invertAlpha)
        {
            int height = (int)pha.Dimensions[2];
            int width = (int)pha.Dimensions[3];

            // Ensure original matches mask size
            var baseImage = original;
            if (original.Width != width || original.Height != height)
            {
                double xscale = (double)width / original.Width;
                double yscale = (double)height / original.Height;
                baseImage = original.Resize(xscale, vscale: yscale, kernel: Enums.Kernel.Lanczos3);
            }

            // Ensure RGBA format
            if (baseImage.Bands == 3)
            {
                baseImage = baseImage.Bandjoin(255); // Add alpha channel
            }

            // Create alpha mask array with smoothing
            var alphaMap = new float[height, width];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float a = pha[0, 0, y, x];
                    if (invertAlpha) a = 1f - a;
                    alphaMap[y, x] = a;
                }
            }

            // Apply edge smoothing
            ApplyAlphaSmoothing(alphaMap, width, height, true);

            // Create alpha mask image from processed values
            var alphaData = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float a = alphaMap[y, x];
                    a = PostProcessAlpha(a);
                    a = ApplyEdgeFeathering(a, alphaMap, x, y, width, height, true);
                    alphaData[y * width + x] = (byte)(Math.Max(0f, Math.Min(1f, a)) * 255f);
                }
            }

            // Create alpha mask as single-band image
            var alphaMask = VipsImage.NewFromMemory(alphaData, width, height, 1, Enums.BandFormat.Uchar);

            // Replace alpha channel with our mask
            var rgb = baseImage.ExtractBand(0, n: 3);
            var result = rgb.Bandjoin(alphaMask);

            if (baseImage != original)
                baseImage.Dispose();

            return result;
        }

        private SixLabors.ImageSharp.Image<Rgba32> BuildForegroundFromOriginal(SixLabors.ImageSharp.Image<Rgba32> original, Tensor<float> pha, bool invertAlpha)
        {
            int height = (int)pha.Dimensions[2];
            int width = (int)pha.Dimensions[3];

            // Ensure original matches mask size
            var baseImage = original;
            bool resized = false;
            if (original.Width != width || original.Height != height)
            {
                baseImage = original.Clone(ctx => ctx.Resize(width, height, KnownResamplers.Lanczos3));
                resized = true;
            }

            // First pass: extract raw alpha values and apply smoothing
            var alphaMap = new float[height, width];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float a = pha[0, 0, y, x];
                    if (invertAlpha) a = 1f - a;
                    alphaMap[y, x] = a;
                }
            }

            // Apply light edge smoothing for live view (heavy smoothing disabled for performance)
            ApplyAlphaSmoothing(alphaMap, width, height, true);

            var image = new SixLabors.ImageSharp.Image<Rgba32>(width, height);
            baseImage.ProcessPixelRows(image, (srcAccessor, dstAccessor) =>
            {
                for (int y = 0; y < height; y++)
                {
                    var srcRow = srcAccessor.GetRowSpan(y);
                    var dstRow = dstAccessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        var srcPix = srcRow[x];
                        float a = alphaMap[y, x];
                        a = PostProcessAlpha(a);

                        // Apply light edge feathering for live view
                        a = ApplyEdgeFeathering(a, alphaMap, x, y, width, height, true);

                        byte A = (byte)(Math.Max(0f, Math.Min(1f, a)) * 255f);
                        dstRow[x] = new Rgba32(srcPix.R, srcPix.G, srcPix.B, A);
                    }
                }
            });

            if (resized)
            {
                baseImage.Dispose();
            }
            return image;
        }

        private void ApplyAlphaSmoothing(float[,] alphaMap, int width, int height, bool isLiveView = false)
        {
            // Skip heavy smoothing for live view to improve performance
            if (isLiveView)
            {
                // Just do a very light 3-pixel box blur for live view
                float[,] temp = new float[height, width];
                Array.Copy(alphaMap, temp, height * width);

                for (int y = 1; y < height - 1; y += 2)  // Skip every other row for speed
                {
                    for (int x = 1; x < width - 1; x += 2)  // Skip every other column
                    {
                        float original = temp[y, x];
                        if (original > 0.1f && original < 0.9f)
                        {
                            // Simple average of immediate neighbors
                            alphaMap[y, x] = (temp[y, x] * 2f +
                                            temp[y-1, x] + temp[y+1, x] +
                                            temp[y, x-1] + temp[y, x+1]) / 6f;
                        }
                    }
                }
                return;
            }

            // Full smoothing for captured photos only
            float[,] smoothed = new float[height, width];
            float[] kernel = { 0.0625f, 0.125f, 0.0625f, 0.125f, 0.25f, 0.125f, 0.0625f, 0.125f, 0.0625f };

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    float sum = 0;
                    int ki = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            sum += alphaMap[y + dy, x + dx] * kernel[ki++];
                        }
                    }
                    smoothed[y, x] = sum;
                }
            }

            // Copy smoothed values back, preserving edges
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    // Only smooth if we're near an edge (alpha between 0.1 and 0.9)
                    float original = alphaMap[y, x];
                    if (original > 0.05f && original < 0.95f)
                    {
                        alphaMap[y, x] = smoothed[y, x];
                    }
                }
            }
        }

        private float ApplyEdgeFeathering(float alpha, float[,] alphaMap, int x, int y, int width, int height, bool isLiveView = false)
        {
            // Skip feathering for live view to improve performance
            if (isLiveView)
            {
                // Just apply simple threshold
                if (alpha < 0.05f) return 0f;
                if (alpha > 0.95f) return 1f;
                return alpha;
            }

            // Full feathering for captured photos only
            if (alpha < 0.1f || alpha > 0.9f)
                return alpha;

            float edgeStrength = 0;
            int samples = 0;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                    {
                        float diff = Math.Abs(alpha - alphaMap[ny, nx]);
                        edgeStrength += diff;
                        samples++;
                    }
                }
            }

            if (samples > 0)
            {
                edgeStrength /= samples;

                if (edgeStrength > 0.2f)
                {
                    float t = (alpha - 0.3f) / 0.4f;
                    t = Math.Max(0, Math.Min(1, t));
                    alpha = t * t * (3f - 2f * t);
                }
            }

            return alpha;
        }

        private float PostProcessAlpha(float a)
        {
            // Fast path for live view processing
            return PostProcessAlphaFast(a);
        }

        private float PostProcessAlphaFast(float a)
        {
            // Simplified alpha processing for live view performance
            // Quick thresholds
            if (a < 0.01f) return 0f;
            if (a > 0.9f) return 1f;

            // Boost weak alphas with simple scaling
            if (a < 0.15f)
            {
                a = a * 6f;  // Simple multiplier instead of pow
                if (a > 1f) a = 1f;
            }
            else if (a < LiveViewAlphaKneeLow)
            {
                return 0f;
            }
            else if (a > LiveViewAlphaKneeHigh)
            {
                return 1f;
            }
            else
            {
                // Simple linear interpolation for mid-range
                float t = (a - LiveViewAlphaKneeLow) / (LiveViewAlphaKneeHigh - LiveViewAlphaKneeLow);
                a = t * LiveViewAlphaGain;
            }

            return Math.Max(0f, Math.Min(1f, a));
        }

        private float PostProcessAlphaHighQuality(float a)
        {
            // Full quality processing for captured photos
            if (a < 0.005f) return 0f;

            if (a < 0.15f)
            {
                a = (float)Math.Pow(a * 8f, 0.45f);
                a = Math.Min(1f, a * 1.8f);
            }
            else if (a <= LiveViewAlphaKneeLow)
            {
                return 0f;
            }
            else if (a >= LiveViewAlphaKneeHigh)
            {
                return 0.95f + (a - LiveViewAlphaKneeHigh) * 0.05f;
            }
            else
            {
                float t = (a - LiveViewAlphaKneeLow) / (LiveViewAlphaKneeHigh - LiveViewAlphaKneeLow);
                t = Math.Max(0f, Math.Min(1f, t));
                t = t * t * (3f - 2f * t);
                t = (float)Math.Pow(t, LiveViewAlphaGamma);
                a = t * LiveViewAlphaGain + LiveViewAlphaOffset;
            }

            if (a > 0.05f && a < 0.95f)
            {
                float s = (a - 0.5f) * 2f;
                s = s / (1f + Math.Abs(s) * 0.2f);
                a = (s + 1f) * 0.5f;
            }

            return Math.Max(0f, Math.Min(1f, a));
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
            _rvmLiveViewSession?.Dispose();
            _isInitialized = false;
            _useRvmLiveView = false;
            _r1State = null;
            _r2State = null;
            _r3State = null;
            _r4State = null;
            _isRvmProcessing = false;
            _isModnetProcessing = false;
            _lastLiveViewFrame = null;
            _lastLiveViewFrameWidth = 0;
            _lastLiveViewFrameHeight = 0;
            _modnetFrameCounter = 0;
            _rvmFrameCounter = 0;
            _lastRvmProcessTicks = 0;
            _lastModnetProcessTicks = 0;
            _lastFpsLogTicks = 0;
            _fpsFrameCount = 0;
            _fpsMode = null;
            _lastDesiredRvm = null;
            lock (_backgroundCacheLock)
            {
                _cachedBackgroundImage?.Dispose();
                _cachedBackgroundImage = null;
                _cachedBackgroundPath = null;
                _cachedBackgroundWidth = 0;
                _cachedBackgroundHeight = 0;
            }
            DeviceLog.Debug("BackgroundRemovalService disposed");
        }

        private bool ShouldProcessFrame(ref long lastTicks, int minIntervalMs)
        {
            long now = Stopwatch.GetTimestamp();
            if (lastTicks == 0)
            {
                lastTicks = now;
                return true;
            }

            double elapsedMs = (now - lastTicks) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs >= minIntervalMs)
            {
                lastTicks = now;
                return true;
            }

            return false;
        }

        private void TrackLiveViewFps(string mode)
        {
            lock (_fpsLock)
            {
                if (!string.Equals(_fpsMode, mode, StringComparison.OrdinalIgnoreCase))
                {
                    _fpsMode = mode;
                    _fpsFrameCount = 0;
                    _lastFpsLogTicks = 0;
                }

                _fpsFrameCount++;
                long now = Stopwatch.GetTimestamp();
                if (_lastFpsLogTicks == 0)
                {
                    _lastFpsLogTicks = now;
                    return;
                }

                double elapsedMs = (now - _lastFpsLogTicks) * 1000.0 / Stopwatch.Frequency;
                if (elapsedMs >= 1000)
                {
                    double fps = _fpsFrameCount * 1000.0 / elapsedMs;
                    DeviceLog.Debug($"[BackgroundRemoval] Live view ({mode}) FPS: {fps:F1} over {elapsedMs:F0}ms");
                    _fpsFrameCount = 0;
                    _lastFpsLogTicks = now;
                }
            }
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
