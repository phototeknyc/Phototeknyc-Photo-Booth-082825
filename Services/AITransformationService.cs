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
            return await TransformImageAsync(inputImagePath, template, cancellationToken);
        }

        public async Task<string> TransformImageAsync(
            string inputImagePath,
            AITransformationTemplate template,
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

                // Convert image to base64
                string base64Image = ConvertImageToBase64(inputImagePath);

                // Create prediction
                var predictionId = await CreatePredictionAsync(base64Image, template, cancellationToken);

                // Poll for completion
                var result = await PollForCompletionAsync(predictionId, cancellationToken);

                // Download and save result
                string outputPath = await DownloadResultAsync(result, inputImagePath);

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
                    var result = await TransformImageAsync(imagePath, template, cancellationToken);
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
            CancellationToken cancellationToken)
        {
            // Get the selected model from AIModelManager
            var modelManager = AIModelManager.Instance;
            var model = modelManager.SelectedModel;

            if (model == null)
            {
                Debug.WriteLine("[AITransformation] No model selected, using default Nano Banana");
                model = modelManager.GetModel("nano-banana") ?? PredefinedModels.GetDefaultModels().First();
            }

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
            if (model.SupportsSynchronousMode && status == "succeeded" && prediction["output"] != null)
            {
                Debug.WriteLine($"[AITransformation] {model.Name} returned immediate result");
                return responseJson; // Return full JSON to handle in polling method
            }

            return prediction["id"]?.ToString();
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

        private async Task<string> DownloadResultAsync(JObject prediction, string originalPath)
        {
            var output = prediction["output"];
            if (output == null || !output.Any())
            {
                throw new Exception("No output from prediction");
            }

            string outputUrl = output.First().ToString();

            // Generate output filename
            string outputDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Photobooth",
                "AI_Transformations",
                DateTime.Now.ToString("yyyyMMdd"));

            Directory.CreateDirectory(outputDir);

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

        private string ConvertImageToBase64(string imagePath)
        {
            using (Image image = Image.FromFile(imagePath))
            {
                Debug.WriteLine($"[AITransformation] Original image size: {image.Width}x{image.Height}");

                // Resize to smaller size for faster AI processing while maintaining aspect ratio
                // Different max dimensions for portrait vs landscape for optimal quality/speed
                bool isPortrait = image.Height > image.Width;
                int maxDimension = isPortrait ? 768 : 1024; // Portrait images can be smaller, landscape need more width

                // Calculate if resizing is needed
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
                        return ImageToBase64(resized);
                    }
                }

                Debug.WriteLine($"[AITransformation] Image size OK, no resize needed ({image.Width}x{image.Height})");
                return ImageToBase64(image);
            }
        }

        private string ImageToBase64(Image image)
        {
            using (var memoryStream = new MemoryStream())
            {
                image.Save(memoryStream, ImageFormat.Png);
                byte[] imageBytes = memoryStream.ToArray();
                return Convert.ToBase64String(imageBytes);
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