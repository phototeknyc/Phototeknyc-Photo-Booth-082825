using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using Photobooth.Database;
using Photobooth.Models;

namespace Photobooth.Services
{
    public class AITransformationService : IDisposable
    {
        #region Singleton

        private static AITransformationService _instance;
        private static readonly object _lock = new object();

        public static AITransformationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new AITransformationService();
                            Debug.WriteLine("[AITransformation] Service instance created");
                        }
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Private Fields

        private readonly HttpClient _httpClient;
        private bool _isInitialized;
        private string _apiToken;
        private readonly object _requestLock = new object();
        private readonly Queue<TransformationRequest> _requestQueue;
        private readonly Dictionary<string, TransformationResult> _resultCache;
        private readonly int _maxCacheSize = 50;
        private CancellationTokenSource _cancellationTokenSource;

        // API Configuration
        private const string REPLICATE_API_BASE = "https://api.replicate.com/v1";
        private const string DEFAULT_MODEL_VERSION = "39ed52f2a78e934b3ba6e2a89f5b1c712de7dfea535525255b1aa35c5565e08b"; // SDXL
        private const int MAX_RETRIES = 3;
        private const int TIMEOUT_SECONDS = 180; // Increased timeout for AI processing

        #endregion

        #region Events

        public event EventHandler<TransformationProgressEventArgs> TransformationProgress;
        public event EventHandler<TransformationCompletedEventArgs> TransformationCompleted;
        public event EventHandler<TransformationErrorEventArgs> TransformationError;

        #endregion

        #region Constructor

        private AITransformationService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(TIMEOUT_SECONDS);
            _requestQueue = new Queue<TransformationRequest>();
            _resultCache = new Dictionary<string, TransformationResult>();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        #endregion

        #region Public Methods

        public async Task<bool> InitializeAsync(string apiToken = null)
        {
            try
            {
                Debug.WriteLine($"[AITransformation] InitializeAsync called with token: {(!string.IsNullOrEmpty(apiToken) ? "PROVIDED (length: " + apiToken.Length + ")" : "NULL/EMPTY")}");

                if (string.IsNullOrEmpty(apiToken))
                {
                    // Try to load from settings if available
                    try
                    {
                        apiToken = Properties.Settings.Default.ReplicateAPIToken;
                        Debug.WriteLine($"[AITransformation] Loaded token from Settings: {(!string.IsNullOrEmpty(apiToken) ? "YES (length: " + apiToken.Length + ")" : "NO/EMPTY")}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AITransformation] Failed to load token from Settings: {ex.Message}");
                    }
                }

                if (string.IsNullOrEmpty(apiToken))
                {
                    Debug.WriteLine("[AITransformation] No API token provided or loaded from settings");
                    return false;
                }

                _apiToken = apiToken;

                // Save the token for future use
                try
                {
                    if (Properties.Settings.Default.ReplicateAPIToken != apiToken)
                    {
                        Properties.Settings.Default.ReplicateAPIToken = apiToken;
                        Properties.Settings.Default.Save();
                    }
                }
                catch { }

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Token {_apiToken}");

                _isInitialized = true;
                Debug.WriteLine("[AITransformation] Service initialized successfully");

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITransformation] Initialization failed: {ex.Message}");
                return false;
            }
        }

        public async Task<string> ApplyTransformationAsync(
            string inputImagePath,
            AITransformationTemplate template,
            string outputFolder,
            CancellationToken cancellationToken = default)
        {
            // Wrapper method for compatibility with existing code
            return await TransformImageAsync(inputImagePath, template, outputFolder, cancellationToken);
        }

        public async Task<string> TransformImageAsync(
            string inputImagePath,
            AITransformationTemplate template,
            string outputFolder = null,
            CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"[AITransformation] TransformImageAsync called");
            Debug.WriteLine($"[AITransformation] Input image: {inputImagePath}");
            Debug.WriteLine($"[AITransformation] Template: {template?.Name} (ID: {template?.Id})");
            Debug.WriteLine($"[AITransformation] Prompt: {template?.Prompt}");

            if (!_isInitialized)
            {
                Debug.WriteLine("[AITransformation] Service not initialized!");
                throw new InvalidOperationException("AI Transformation service is not initialized");
            }

            if (!File.Exists(inputImagePath))
            {
                Debug.WriteLine($"[AITransformation] Input image not found: {inputImagePath}");
                throw new FileNotFoundException("Input image not found", inputImagePath);
            }

            // Log detailed file information for debugging
            var fileInfo = new System.IO.FileInfo(inputImagePath);
            Debug.WriteLine($"[AITransformation] *** FILE INFO ***");
            Debug.WriteLine($"[AITransformation] *** Path: {inputImagePath}");
            Debug.WriteLine($"[AITransformation] *** Size: {fileInfo.Length} bytes ({fileInfo.Length / 1024.0:F2} KB)");
            Debug.WriteLine($"[AITransformation] *** Extension: {fileInfo.Extension}");
            Debug.WriteLine($"[AITransformation] *** Last Modified: {fileInfo.LastWriteTime}");

            // Try to read image properties
            try
            {
                using (var img = System.Drawing.Image.FromFile(inputImagePath))
                {
                    Debug.WriteLine($"[AITransformation] *** Image Format: {img.RawFormat}");
                    Debug.WriteLine($"[AITransformation] *** Pixel Format: {img.PixelFormat}");
                    Debug.WriteLine($"[AITransformation] *** Has Alpha: {System.Drawing.Image.IsAlphaPixelFormat(img.PixelFormat)}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITransformation] *** Could not read image properties: {ex.Message}");
            }

            try
            {
                // Check cache first
                string cacheKey = GenerateCacheKey(inputImagePath, template);
                if (_resultCache.ContainsKey(cacheKey))
                {
                    Debug.WriteLine($"[AITransformation] Returning cached result for key: {cacheKey}");
                    return _resultCache[cacheKey].OutputImagePath;
                }

                Debug.WriteLine("[AITransformation] No cached result, proceeding with transformation");

                // Get the selected model - check template preference first
                var modelManager = AIModelManager.Instance;
                string preferredModelId = modelManager.GetTemplateModelPreference(template.Id);
                var model = !string.IsNullOrEmpty(preferredModelId)
                    ? modelManager.GetModel(preferredModelId)
                    : modelManager.SelectedModel;

                if (model == null)
                {
                    Debug.WriteLine("[AITransformation] No model found, using default");
                    model = modelManager.AvailableModels.FirstOrDefault();
                }

                Debug.WriteLine($"[AITransformation] Using model: {model?.Name} (ID: {model?.Id})");
                Debug.WriteLine($"[AITransformation] Template preference: {preferredModelId ?? "none (using global)"}");

                // Convert image to base64 with appropriate sizing for the model
                int minDimension = (model?.Id == "seedream-4") ? 2048 : 0;
                int? targetMaxKB = null; // Allow large inputs; Seedream works with bigger files
                Debug.WriteLine($"[AITransformation] *** About to convert image: {inputImagePath}");
                Debug.WriteLine($"[AITransformation] *** File exists: {File.Exists(inputImagePath)}");
                bool preferHighQuality = model?.PreservesIdentity == true;
                string base64Image = ConvertImageToBase64(inputImagePath, minDimension, targetMaxKB, preferHighQuality);
                Debug.WriteLine($"[AITransformation] *** Base64 conversion complete, length: {base64Image?.Length ?? 0}");
                Debug.WriteLine($"[AITransformation] *** Base64 preview (first 100 chars): {(base64Image?.Length > 100 ? base64Image.Substring(0, 100) : base64Image)}");

                // Create prediction with the selected model
                var predictionId = await CreatePredictionAsync(base64Image, template, model, cancellationToken);

                // Poll for completion
                var result = await PollForCompletionAsync(predictionId, cancellationToken);

                // Download and save result
                string outputPath = await DownloadResultAsync(result, inputImagePath, outputFolder);

                // Cache result
                CacheResult(cacheKey, new TransformationResult
                {
                    OutputImagePath = outputPath,
                    Timestamp = DateTime.Now
                });

                // Raise completion event
                TransformationCompleted?.Invoke(this, new TransformationCompletedEventArgs
                {
                    InputPath = inputImagePath,
                    OutputPath = outputPath,
                    Template = template
                });

                return outputPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITransformation] Transform failed: {ex.Message}");

                TransformationError?.Invoke(this, new TransformationErrorEventArgs
                {
                    Error = ex.Message,
                    InputPath = inputImagePath
                });

                throw;
            }
        }

        public async Task<List<string>> BatchTransformAsync(
            List<string> inputImagePaths,
            AITransformationTemplate template,
            IProgress<int> progress = null,
            CancellationToken cancellationToken = default)
        {
            var results = new List<string>();
            int completed = 0;

            foreach (var imagePath in inputImagePaths)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var result = await TransformImageAsync(imagePath, template, null, cancellationToken);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AITransformation] Batch item failed: {ex.Message}");
                    results.Add(null);
                }

                completed++;
                progress?.Report((completed * 100) / inputImagePaths.Count);
            }

            return results;
        }

        #endregion

        #region Private Methods

        private async Task<string> CreatePredictionAsync(
            string base64Image,
            AITransformationTemplate template,
            AIModelDefinition model,
            CancellationToken cancellationToken)
        {
            var modelManager = AIModelManager.Instance;

            Debug.WriteLine($"[AITransformation] Using model: {model.Name} ({model.Id})");
            Debug.WriteLine($"[AITransformation] Model path: {model.ModelPath}");
            Debug.WriteLine($"[AITransformation] Preserves identity: {model.PreservesIdentity}");

            // Get custom prompt for this model/template combination
            var customPrompt = modelManager.GetPromptForTemplate(model.Id, template.Id.ToString());

            // Build input based on model requirements
            var input = modelManager.BuildModelInput(model, base64Image, customPrompt, template);

            // Build request body based on whether we have a version
            object requestBody;
            if (!string.IsNullOrEmpty(model.ModelVersion))
            {
                requestBody = new
                {
                    version = model.ModelVersion,
                    input = input
                };
            }
            else
            {
                requestBody = new
                {
                    input = input
                };
            }

            var json = JsonConvert.SerializeObject(requestBody);
            Debug.WriteLine($"[AITransformation] Image data length: {base64Image.Length} characters");
            Debug.WriteLine($"[AITransformation] Prompt: {customPrompt?.Prompt ?? template.Prompt}");
            Debug.WriteLine($"[AITransformation] Request JSON preview: {json.Substring(0, Math.Min(json.Length, 500))}...");

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Add Prefer: wait header for synchronous processing if supported
            _httpClient.DefaultRequestHeaders.Remove("Prefer");
            if (model.SupportsSynchronousMode)
            {
                _httpClient.DefaultRequestHeaders.Add("Prefer", "wait");
                Debug.WriteLine("[AITransformation] Using synchronous mode (Prefer: wait)");
            }

            // Build the API URL
            string apiUrl = $"{REPLICATE_API_BASE}/models/{model.ModelPath}/predictions";
            if (!string.IsNullOrEmpty(model.ModelVersion))
            {
                apiUrl = $"{REPLICATE_API_BASE}/predictions";
            }

            Debug.WriteLine($"[AITransformation] API URL: {apiUrl}");

            var response = await _httpClient.PostAsync(
                apiUrl,
                content,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[AITransformation] API Error Response: {errorContent}");
                throw new HttpRequestException($"Replicate API error ({response.StatusCode}): {errorContent}");
            }

            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var prediction = JObject.Parse(responseJson);

            // Handle synchronous response if model supports it
            var status = prediction["status"]?.ToString();
            Debug.WriteLine($"[AITransformation] Response status: {status}");
            Debug.WriteLine($"[AITransformation] Has output: {(prediction["output"] != null)}");

            if (model.SupportsSynchronousMode && status == "succeeded" && prediction["output"] != null)
            {
                Debug.WriteLine($"[AITransformation] {model.Name} returned immediate result");
                return responseJson; // Return full JSON to handle in polling method
            }

            var predictionId = prediction["id"]?.ToString();
            Debug.WriteLine($"[AITransformation] Prediction ID: {predictionId}");
            return predictionId;
        }

        private async Task<JObject> PollForCompletionAsync(
            string predictionIdOrJson,
            CancellationToken cancellationToken)
        {
            // Check if we already have a completed prediction (from Prefer: wait)
            if (predictionIdOrJson.StartsWith("{"))
            {
                var completedPrediction = JObject.Parse(predictionIdOrJson);
                if (completedPrediction["status"]?.ToString() == "succeeded")
                {
                    Debug.WriteLine("[AITransformation] Using immediate result from Nano Banana");
                    return completedPrediction;
                }
            }

            int attempts = 0;
            const int maxAttempts = 120; // 120 seconds max wait for AI processing

            while (attempts < maxAttempts)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException();

                var response = await _httpClient.GetAsync(
                    $"{REPLICATE_API_BASE}/predictions/{predictionIdOrJson}",
                    cancellationToken);

                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var prediction = JObject.Parse(responseJson);

                var status = prediction["status"]?.ToString();

                // Report progress
                TransformationProgress?.Invoke(this, new TransformationProgressEventArgs
                {
                    Status = status,
                    Progress = (attempts * 100) / maxAttempts
                });

                switch (status)
                {
                    case "succeeded":
                        return prediction;
                    case "failed":
                        throw new Exception($"Prediction failed: {prediction["error"]}");
                    case "canceled":
                        throw new OperationCanceledException();
                    default:
                        await Task.Delay(1000, cancellationToken);
                        attempts++;
                        break;
                }
            }

            throw new TimeoutException("Prediction timed out");
        }

        private async Task<string> DownloadResultAsync(JObject prediction, string originalPath, string preferredOutputFolder = null)
        {
            var output = prediction["output"];
            if (output == null)
            {
                Debug.WriteLine($"[AITransformation] Prediction response: {prediction}");
                throw new Exception("No output from prediction");
            }

            Debug.WriteLine($"[AITransformation] Output type: {output.Type}");
            Debug.WriteLine($"[AITransformation] Output value: {output}");

            string outputUrl;

            // Handle different output formats
            if (output.Type == JTokenType.Array && output.Any())
            {
                outputUrl = output.First().ToString();
            }
            else if (output.Type == JTokenType.String)
            {
                outputUrl = output.ToString();
            }
            else
            {
                Debug.WriteLine($"[AITransformation] Unexpected output format. Full prediction: {prediction}");
                throw new Exception($"Unexpected output format: {output.Type}");
            }

            // Decide output folder
            string outputDir;
            if (!string.IsNullOrWhiteSpace(preferredOutputFolder))
            {
                outputDir = preferredOutputFolder;
            }
            else
            {
                outputDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "Photobooth",
                    "AI_Transformations",
                    DateTime.Now.ToString("yyyyMMdd"));
            }

            Directory.CreateDirectory(outputDir);

            // Generate output filename
            string outputFilename = $"AI_{Path.GetFileNameWithoutExtension(originalPath)}_{DateTime.Now:HHmmss}.png";
            string outputPath = Path.Combine(outputDir, outputFilename);

            // Download image
            using (var response = await _httpClient.GetAsync(outputUrl))
            {
                response.EnsureSuccessStatusCode();

                using (var fileStream = File.Create(outputPath))
                {
                    await response.Content.CopyToAsync(fileStream);
                }
            }

            return outputPath;
        }

        private string ConvertImageToBase64(string imagePath, int minDimension = 0, int? targetMaxKB = null, bool preferHighQuality = false)
        {
            using (Image image = Image.FromFile(imagePath))
            {
                Debug.WriteLine($"[AITransformation] Original image size: {image.Width}x{image.Height}, minDimension: {minDimension}");

                // For models with dimension requirements (like Seedream-4), enforce both min and max
                if (minDimension > 0)
                {
                    // For Seedream-4, align with 2K size (~2048)
                    const int maxDimensionForModel = 2048;

                    // Check if image needs resizing (too small OR too large)
                    bool needsResizing = image.Width < minDimension || image.Height < minDimension ||
                                      image.Width > maxDimensionForModel || image.Height > maxDimensionForModel;

                    if (needsResizing)
                    {
                        // Calculate target dimensions to fit within min-max range
                        float scale;

                        if (image.Width < minDimension || image.Height < minDimension)
                        {
                            // Scale UP to meet minimum
                            scale = Math.Max(
                                (float)minDimension / image.Width,
                                (float)minDimension / image.Height);
                        }
                        else
                        {
                            // Scale DOWN to meet maximum
                            scale = Math.Min(
                                (float)maxDimensionForModel / image.Width,
                                (float)maxDimensionForModel / image.Height);
                        }

                        int newWidth = (int)(image.Width * scale);
                        int newHeight = (int)(image.Height * scale);

                        // Ensure we're within bounds
                        newWidth = Math.Max(minDimension, Math.Min(maxDimensionForModel, newWidth));
                        newHeight = Math.Max(minDimension, Math.Min(maxDimensionForModel, newHeight));

                        Debug.WriteLine($"[AITransformation] Resizing image: {image.Width}x{image.Height} → {newWidth}x{newHeight} (scale: {scale:F2})");

                        using (var resized = new Bitmap(newWidth, newHeight))
                        {
                            using (var graphics = Graphics.FromImage(resized))
                            {
                                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                                graphics.DrawImage(image, 0, 0, newWidth, newHeight);
                            }

                            return ImageToBase64(resized, targetMaxKB, preferHighQuality);
                        }
                    }

                    // Image is within acceptable range
                    Debug.WriteLine($"[AITransformation] Image dimensions acceptable ({image.Width}x{image.Height}), using as is");
                    return ImageToBase64(image, targetMaxKB, preferHighQuality);
                }

                // Standard resizing logic for models without minimum requirements
                bool isPortrait = image.Height > image.Width;
                int maxDimension = isPortrait ? 768 : 1024; // Default max dimensions
                bool needsResize = image.Width > maxDimension || image.Height > maxDimension;

                if (needsResize)
                {
                    // Calculate scale factor to fit within maxDimension while preserving aspect ratio
                    float scale = Math.Min(
                        (float)maxDimension / image.Width,
                        (float)maxDimension / image.Height);

                    int newWidth = (int)(image.Width * scale);
                    int newHeight = (int)(image.Height * scale);

                    Debug.WriteLine($"[AITransformation] Resizing image from {image.Width}x{image.Height} to {newWidth}x{newHeight} (scale: {scale:F2})");

                    using (var resized = new Bitmap(newWidth, newHeight))
                    {
                        using (var graphics = Graphics.FromImage(resized))
                        {
                            // High quality resizing settings
                            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                            // Draw the resized image
                            graphics.DrawImage(image, 0, 0, newWidth, newHeight);
                        }

                        Debug.WriteLine($"[AITransformation] Image resized successfully to {newWidth}x{newHeight}");
                        return ImageToBase64(resized, targetMaxKB, preferHighQuality);
                    }
                }

                Debug.WriteLine($"[AITransformation] Image size OK, no resize needed ({image.Width}x{image.Height})");
                return ImageToBase64(image, targetMaxKB, preferHighQuality);
            }
        }

        private string ImageToBase64(Image image, int? targetMaxKB = null, bool preferHighQuality = false)
        {
            // Encode to JPEG with adaptive quality to meet optional size target
            var jpegEncoder = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

            // Quality ladder to try if a target is specified
            var qualities = preferHighQuality
                ? new List<long> { 95L, 90L, 85L, 80L, 75L, 70L, 65L, 60L }
                : new List<long> { 90L, 85L, 80L, 75L, 70L, 65L, 60L };

            foreach (var q in qualities)
            {
                using (var memoryStream = new MemoryStream())
                {
                    if (jpegEncoder != null)
                    {
                        var encoderParams = new System.Drawing.Imaging.EncoderParameters(1);
                        encoderParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                            System.Drawing.Imaging.Encoder.Quality, q);

                        image.Save(memoryStream, jpegEncoder, encoderParams);
                        Debug.WriteLine($"[AITransformation] *** Converted to JPEG (quality {q}%), size: {memoryStream.Length / 1024.0:F2} KB");
                    }
                    else
                    {
                        image.Save(memoryStream, ImageFormat.Png);
                        Debug.WriteLine($"[AITransformation] *** Converted to PNG, size: {memoryStream.Length / 1024.0:F2} KB");
                    }

                    // If a target size is provided, check and, if satisfied, return; otherwise, try next quality
                    if (!targetMaxKB.HasValue || (memoryStream.Length / 1024.0) <= targetMaxKB.Value || q == qualities.Last())
                    {
                        byte[] imageBytes = memoryStream.ToArray();
                        string base64 = Convert.ToBase64String(imageBytes);
                        Debug.WriteLine($"[AITransformation] *** Base64 output length: {base64.Length} characters");
                        return base64;
                    }
                }
            }

            // Fallback shouldn't be reached
            using (var ms = new MemoryStream())
            {
                image.Save(ms, ImageFormat.Jpeg);
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        private string GenerateCacheKey(string imagePath, AITransformationTemplate template)
        {
            var fileInfo = new FileInfo(imagePath);
            return $"{fileInfo.Name}_{fileInfo.LastWriteTime.Ticks}_{template.GetHashCode()}";
        }

        private void CacheResult(string key, TransformationResult result)
        {
            lock (_resultCache)
            {
                if (_resultCache.Count >= _maxCacheSize)
                {
                    // Remove oldest entry
                    var oldestKey = _resultCache
                        .OrderBy(kvp => kvp.Value.Timestamp)
                        .First().Key;
                    _resultCache.Remove(oldestKey);
                }

                _resultCache[key] = result;
            }
        }

        #endregion

        #region Cleanup

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _httpClient?.Dispose();
        }

        #endregion
    }

    #region Supporting Classes

    public class TransformationRequest
    {
        public string InputPath { get; set; }
        public AITransformationTemplate Template { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class TransformationResult
    {
        public string OutputImagePath { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class TransformationProgressEventArgs : EventArgs
    {
        public string Status { get; set; }
        public int Progress { get; set; }
    }

    public class TransformationCompletedEventArgs : EventArgs
    {
        public string InputPath { get; set; }
        public string OutputPath { get; set; }
        public AITransformationTemplate Template { get; set; }
    }

    public class TransformationErrorEventArgs : EventArgs
    {
        public string Error { get; set; }
        public string InputPath { get; set; }
    }

    #endregion
}
