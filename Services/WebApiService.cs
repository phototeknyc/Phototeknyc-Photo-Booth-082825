using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using CameraControl.Devices;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    /// <summary>
    /// Simplified Web API service for remote control of photobooth
    /// Provides basic RESTful endpoints for camera control and settings
    /// </summary>
    public class WebApiService : IDisposable
    {
        #region Singleton
        private static WebApiService _instance;
        private static readonly object _lock = new object();
        
        public static WebApiService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new WebApiService();
                        }
                    }
                }
                return _instance;
            }
        }
        #endregion

        #region Fields
        private HttpListener _listener;
        private Thread _listenerThread;
        private bool _isRunning;
        private int _port = 8080;
        private string _baseUrl;
        #endregion

        #region Properties
        public bool IsRunning => _isRunning;
        public int Port => _port;
        public string BaseUrl => _baseUrl;
        #endregion

        #region Constructor
        private WebApiService()
        {
            // Private constructor for singleton
        }
        #endregion

        #region Public Methods
        
        /// <summary>
        /// Start the Web API server
        /// </summary>
        public void Start(int port = 8080)
        {
            if (_isRunning)
            {
                CameraControl.Devices.Log.Debug("WebApiService: Already running");
                return;
            }

            _port = port;
            _baseUrl = $"http://+:{_port}/";
            
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(_baseUrl);
                _listener.Start();
                
                _isRunning = true;
                
                // Start listener thread
                _listenerThread = new Thread(ListenerLoop)
                {
                    IsBackground = true,
                    Name = "WebApiListener"
                };
                _listenerThread.Start();
                
                CameraControl.Devices.Log.Debug($"WebApiService: Started on port {_port}");
            }
            catch (Exception ex)
            {
                CameraControl.Devices.Log.Error($"WebApiService: Failed to start - {ex.Message}");
                _isRunning = false;
                throw;
            }
        }
        
        /// <summary>
        /// Stop the Web API server
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
                return;
            
            try
            {
                _isRunning = false;
                
                if (_listener != null)
                {
                    _listener.Stop();
                    _listener.Close();
                    _listener = null;
                }
                
                if (_listenerThread != null)
                {
                    _listenerThread.Join(1000);
                    _listenerThread = null;
                }
                
                CameraControl.Devices.Log.Debug("WebApiService: Stopped");
            }
            catch (Exception ex)
            {
                CameraControl.Devices.Log.Error($"WebApiService: Error stopping - {ex.Message}");
            }
        }
        
        /// <summary>
        /// IDisposable implementation
        /// </summary>
        public void Dispose()
        {
            Stop();
        }
        #endregion

        #region Request Processing
        
        private void ListenerLoop()
        {
            while (_isRunning)
            {
                try
                {
                    var context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => ProcessRequest(context));
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        CameraControl.Devices.Log.Error($"WebApiService: Listener error - {ex.Message}");
                    }
                }
            }
        }
        
        private async void ProcessRequest(HttpListenerContext context)
        {
            try
            {
                // Add CORS headers
                context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
                context.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Accept");
                
                // Handle OPTIONS requests for CORS
                if (context.Request.HttpMethod == "OPTIONS")
                {
                    context.Response.StatusCode = 200;
                    context.Response.Close();
                    return;
                }
                
                var path = context.Request.Url.AbsolutePath.ToLower();
                var method = context.Request.HttpMethod;
                
                CameraControl.Devices.Log.Debug($"WebApiService: {method} {path}");
                
                // Route request
                if (path == "/health")
                {
                    await HandleHealthCheck(context);
                }
                else if (path == "/" || path == "/index.html" || path == "/webclient" || path == "/client")
                {
                    // Serve the WebApiClient.html file
                    await ServeWebClient(context);
                }
                else if (path.StartsWith("/api/"))
                {
                    await RouteApiRequest(context, path.Substring(5), method);
                }
                else
                {
                    await SendError(context, 404, "Not Found");
                }
            }
            catch (Exception ex)
            {
                CameraControl.Devices.Log.Error($"WebApiService: Request error - {ex.Message}");
                await SendError(context, 500, ex.Message);
            }
        }
        
        private async Task RouteApiRequest(HttpListenerContext context, string path, string method)
        {
            // Camera endpoints
            if (path.StartsWith("camera/"))
            {
                await HandleCameraRequest(context, path.Substring(7), method);
            }
            // Session endpoints
            else if (path.StartsWith("session/"))
            {
                await HandleSessionRequest(context, path.Substring(8), method);
            }
            // Settings endpoints
            else if (path.StartsWith("settings/"))
            {
                await HandleSettingsRequest(context, path.Substring(9), method);
            }
            // Print endpoints
            else if (path.StartsWith("print/"))
            {
                await HandlePrintRequest(context, path.Substring(6), method);
            }
            // Event endpoints
            else if (path.StartsWith("events/"))
            {
                await HandleEventRequest(context, path.Substring(7), method);
            }
            else
            {
                await SendError(context, 404, "Unknown API endpoint");
            }
        }
        #endregion

        #region API Handlers - Camera
        
        private async Task HandleCameraRequest(HttpListenerContext context, string path, string method)
        {
            var cameraSessionManager = CameraSessionManager.Instance;
            var cameraDevice = cameraSessionManager?.DeviceManager?.SelectedCameraDevice;
            
            switch (path)
            {
                case "status":
                    if (method == "GET")
                    {
                        var status = new
                        {
                            connected = cameraDevice != null && cameraDevice.IsConnected,
                            deviceName = cameraDevice?.DeviceName ?? "None",
                            modelName = cameraDevice?.DisplayName ?? "Unknown",
                            serialNumber = cameraDevice?.SerialNumber ?? "",
                            batteryLevel = cameraDevice?.Battery ?? 0,
                            liveViewActive = cameraDevice?.HaveLiveView ?? false
                        };
                        await SendJson(context, status);
                    }
                    break;
                    
                case "capture":
                    if (method == "POST")
                    {
                        if (cameraDevice == null || !cameraDevice.IsConnected)
                        {
                            await SendError(context, 503, "Camera not connected");
                            return;
                        }
                        
                        try
                        {
                            // Take a photo
                            await Task.Run(() => cameraDevice.CapturePhoto());
                            await SendJson(context, new { success = true, message = "Photo captured" });
                        }
                        catch (Exception ex)
                        {
                            await SendError(context, 500, $"Capture failed: {ex.Message}");
                        }
                    }
                    break;
                    
                case "liveview/start":
                    if (method == "POST")
                    {
                        if (cameraDevice == null || !cameraDevice.IsConnected)
                        {
                            await SendError(context, 503, "Camera not connected");
                            return;
                        }
                        
                        try
                        {
                            cameraDevice.StartLiveView();
                            await SendJson(context, new { success = true, message = "Live view started" });
                        }
                        catch (Exception ex)
                        {
                            await SendError(context, 500, $"Failed to start live view: {ex.Message}");
                        }
                    }
                    break;
                    
                case "liveview/stop":
                    if (method == "POST")
                    {
                        if (cameraDevice == null || !cameraDevice.IsConnected)
                        {
                            await SendError(context, 503, "Camera not connected");
                            return;
                        }
                        
                        try
                        {
                            cameraDevice.StopLiveView();
                            await SendJson(context, new { success = true, message = "Live view stopped" });
                        }
                        catch (Exception ex)
                        {
                            await SendError(context, 500, $"Failed to stop live view: {ex.Message}");
                        }
                    }
                    break;
                    
                case "settings":
                    if (method == "GET")
                    {
                        if (cameraDevice == null)
                        {
                            await SendJson(context, new { error = "No camera connected" });
                            return;
                        }
                        
                        var settings = new
                        {
                            iso = cameraDevice.IsoNumber?.Value ?? "Auto",
                            shutterSpeed = cameraDevice.ShutterSpeed?.Value ?? "Auto",
                            aperture = cameraDevice.FNumber?.Value ?? "Auto",
                            exposureCompensation = cameraDevice.ExposureCompensation?.Value ?? "0",
                            whiteBalance = cameraDevice.WhiteBalance?.Value ?? "Auto",
                            focusMode = cameraDevice.FocusMode?.Value ?? "Auto"
                        };
                        await SendJson(context, settings);
                    }
                    break;
                    
                default:
                    await SendError(context, 404, "Unknown camera endpoint");
                    break;
            }
        }
        #endregion

        #region API Handlers - Settings
        
        private async Task HandleSettingsRequest(HttpListenerContext context, string path, string method)
        {
            var settings = Properties.Settings.Default;
            
            switch (path)
            {
                case "all":
                    if (method == "GET")
                    {
                        var allSettings = new Dictionary<string, object>
                        {
                            ["countdownSeconds"] = settings.CountdownSeconds,
                            ["showCountdown"] = settings.ShowCountdown,
                            ["photographerMode"] = settings.PhotographerMode,
                            ["photoDisplayDuration"] = settings.PhotoDisplayDuration,
                            ["delayBetweenPhotos"] = settings.DelayBetweenPhotos,
                            ["autoClearSession"] = settings.AutoClearSession,
                            ["autoClearTimeout"] = settings.AutoClearTimeout,
                            ["photoLocation"] = settings.PhotoLocation,
                            ["mirrorLiveView"] = settings.MirrorLiveView,
                            ["liveViewFrameRate"] = settings.LiveViewFrameRate,
                            ["enableIdleLiveView"] = settings.EnableIdleLiveView,
                            ["fullscreenMode"] = settings.FullscreenMode,
                            ["hideCursor"] = settings.HideCursor,
                            ["enableRetake"] = settings.EnableRetake,
                            ["retakeTimeout"] = settings.RetakeTimeout,
                            ["allowMultipleRetakes"] = settings.AllowMultipleRetakes,
                            ["enableFilters"] = settings.EnableFilters,
                            ["defaultFilter"] = settings.DefaultFilter,
                            ["filterIntensity"] = settings.FilterIntensity,
                            ["allowFilterChange"] = settings.AllowFilterChange,
                            ["autoApplyFilters"] = settings.AutoApplyFilters,
                            ["showFilterPreview"] = settings.ShowFilterPreview,
                            ["beautyModeEnabled"] = settings.BeautyModeEnabled,
                            ["beautyModeIntensity"] = settings.BeautyModeIntensity
                        };
                        await SendJson(context, allSettings);
                    }
                    break;
                    
                case "update":
                    if (method == "PUT")
                    {
                        try
                        {
                            var data = await GetRequestData(context);
                            
                            // Update settings (simplified - only a few key settings)
                            foreach (var kvp in data)
                            {
                                switch (kvp.Key.ToLower())
                                {
                                    case "countdownseconds":
                                        settings.CountdownSeconds = int.Parse(kvp.Value);
                                        break;
                                    case "showcountdown":
                                        settings.ShowCountdown = bool.Parse(kvp.Value);
                                        break;
                                    case "photographermode":
                                        settings.PhotographerMode = bool.Parse(kvp.Value);
                                        break;
                                    case "photodisplayduration":
                                        settings.PhotoDisplayDuration = int.Parse(kvp.Value);
                                        break;
                                    case "mirrorliveview":
                                        settings.MirrorLiveView = bool.Parse(kvp.Value);
                                        break;
                                    case "enablefilters":
                                        settings.EnableFilters = bool.Parse(kvp.Value);
                                        break;
                                    case "beautymodeenabled":
                                        settings.BeautyModeEnabled = bool.Parse(kvp.Value);
                                        break;
                                    case "autoclearsession":
                                        settings.AutoClearSession = bool.Parse(kvp.Value);
                                        break;
                                    case "autocleartimeout":
                                        settings.AutoClearTimeout = int.Parse(kvp.Value);
                                        break;
                                    case "enableretake":
                                        settings.EnableRetake = bool.Parse(kvp.Value);
                                        break;
                                    case "retaketimeout":
                                        settings.RetakeTimeout = int.Parse(kvp.Value);
                                        break;
                                }
                            }
                            
                            settings.Save();
                            await SendJson(context, new { success = true, message = "Settings updated" });
                        }
                        catch (Exception ex)
                        {
                            await SendError(context, 500, $"Failed to update settings: {ex.Message}");
                        }
                    }
                    break;
                    
                default:
                    await SendError(context, 404, "Unknown settings endpoint");
                    break;
            }
        }
        #endregion

        #region API Handlers - Session
        
        private async Task HandleSessionRequest(HttpListenerContext context, string path, string method)
        {
            switch (path)
            {
                case "status":
                    if (method == "GET")
                    {
                        // Return mock session status for now
                        var status = new
                        {
                            hasActiveSession = false,
                            sessionId = 0,
                            sessionGuid = "",
                            photoCount = 0,
                            isProcessing = false
                        };
                        await SendJson(context, status);
                    }
                    break;
                    
                case "start":
                    if (method == "POST")
                    {
                        // Mock session start
                        await SendJson(context, new 
                        { 
                            success = true, 
                            sessionId = new Random().Next(1000, 9999),
                            message = "Session started (mock)"
                        });
                    }
                    break;
                    
                case "complete":
                case "clear":
                    if (method == "POST")
                    {
                        await SendJson(context, new { success = true, message = "Session operation completed (mock)" });
                    }
                    break;
                    
                case "list":
                    if (method == "GET")
                    {
                        // Return empty list for now
                        await SendJson(context, new object[0]);
                    }
                    break;
                    
                default:
                    await SendError(context, 404, "Unknown session endpoint");
                    break;
            }
        }
        #endregion

        #region API Handlers - Print
        
        private async Task HandlePrintRequest(HttpListenerContext context, string path, string method)
        {
            switch (path)
            {
                case "status":
                    if (method == "GET")
                    {
                        var status = new
                        {
                            printerAvailable = false,
                            printerName = Properties.Settings.Default.PrinterName ?? "None",
                            queueSize = 0,
                            isPrinting = false
                        };
                        await SendJson(context, status);
                    }
                    break;
                    
                case "print":
                    if (method == "POST")
                    {
                        await SendJson(context, new { success = false, message = "Printing not implemented in simplified API" });
                    }
                    break;
                    
                default:
                    await SendError(context, 404, "Unknown print endpoint");
                    break;
            }
        }
        #endregion

        #region API Handlers - Events
        
        private async Task HandleEventRequest(HttpListenerContext context, string path, string method)
        {
            switch (path)
            {
                case "list":
                    if (method == "GET")
                    {
                        // Return empty event list for now
                        await SendJson(context, new object[0]);
                    }
                    break;
                    
                case "current":
                    if (method == "GET")
                    {
                        await SendJson(context, new { message = "No event selected" });
                    }
                    break;
                    
                case "select":
                    if (method == "POST")
                    {
                        await SendJson(context, new { success = true, message = "Event selection not implemented" });
                    }
                    break;
                    
                default:
                    await SendError(context, 404, "Unknown event endpoint");
                    break;
            }
        }
        #endregion

        #region Helper Methods
        
        private async Task ServeWebClient(HttpListenerContext context)
        {
            try
            {
                // Get the path to WebApiClient.html
                string htmlFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebApiClient.html");
                
                // Check if file exists in the application directory
                if (!File.Exists(htmlFilePath))
                {
                    // Try to find it in the parent directories (for development)
                    string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                    for (int i = 0; i < 5; i++)
                    {
                        currentDir = Path.GetDirectoryName(currentDir);
                        if (currentDir == null) break;
                        
                        string testPath = Path.Combine(currentDir, "WebApiClient.html");
                        if (File.Exists(testPath))
                        {
                            htmlFilePath = testPath;
                            break;
                        }
                    }
                }
                
                if (File.Exists(htmlFilePath))
                {
                    // Read the HTML file
                    string htmlContent = File.ReadAllText(htmlFilePath);
                    
                    // Send the HTML response
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.StatusCode = 200;
                    
                    byte[] buffer = Encoding.UTF8.GetBytes(htmlContent);
                    context.Response.ContentLength64 = buffer.Length;
                    await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    context.Response.Close();
                    
                    CameraControl.Devices.Log.Debug($"WebApiService: Served WebApiClient.html from {htmlFilePath}");
                }
                else
                {
                    // If WebApiClient.html is not found, serve a simple built-in page
                    await ServeBuiltInClient(context);
                }
            }
            catch (Exception ex)
            {
                CameraControl.Devices.Log.Error($"WebApiService: Error serving web client - {ex.Message}");
                await SendError(context, 500, "Failed to serve web client");
            }
        }
        
        private async Task ServeBuiltInClient(HttpListenerContext context)
        {
            // Serve a simple built-in HTML page if WebApiClient.html is not found
            string html = @"<!DOCTYPE html>
<html>
<head>
    <title>Photobooth Web API</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 40px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); min-height: 100vh; }
        .container { max-width: 800px; margin: 0 auto; background: white; padding: 30px; border-radius: 10px; box-shadow: 0 4px 6px rgba(0,0,0,0.1); }
        h1 { color: #333; }
        .status { padding: 10px; background: #f0f0f0; border-radius: 5px; margin: 20px 0; }
        .endpoint { margin: 10px 0; padding: 10px; background: #f8f8f8; border-left: 3px solid #667eea; }
        .endpoint code { background: #e0e0e0; padding: 2px 5px; border-radius: 3px; }
        button { background: #667eea; color: white; border: none; padding: 10px 20px; border-radius: 5px; cursor: pointer; margin: 5px; }
        button:hover { background: #5568d3; }
        #response { margin-top: 20px; padding: 10px; background: #f5f5f5; border-radius: 5px; font-family: monospace; white-space: pre-wrap; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>🎭 Photobooth Web API</h1>
        <div class='status'>
            <strong>Status:</strong> <span id='status'>Checking...</span><br>
            <strong>API Base:</strong> http://localhost:8080
        </div>
        
        <h2>Quick Tests</h2>
        <button onclick='testHealth()'>Test Health</button>
        <button onclick='testCamera()'>Test Camera Status</button>
        <button onclick='testSettings()'>Get Settings</button>
        <button onclick='capturePhoto()'>Capture Photo</button>
        
        <h2>Available Endpoints</h2>
        <div class='endpoint'><strong>GET</strong> <code>/health</code> - Check API health</div>
        <div class='endpoint'><strong>GET</strong> <code>/api/camera/status</code> - Get camera status</div>
        <div class='endpoint'><strong>POST</strong> <code>/api/camera/capture</code> - Capture a photo</div>
        <div class='endpoint'><strong>GET</strong> <code>/api/camera/settings</code> - Get camera settings</div>
        <div class='endpoint'><strong>POST</strong> <code>/api/camera/liveview/start</code> - Start live view</div>
        <div class='endpoint'><strong>POST</strong> <code>/api/camera/liveview/stop</code> - Stop live view</div>
        <div class='endpoint'><strong>GET</strong> <code>/api/settings/all</code> - Get all settings</div>
        <div class='endpoint'><strong>PUT</strong> <code>/api/settings/update</code> - Update settings</div>
        
        <h2>Response</h2>
        <div id='response'>Ready for testing...</div>
        
        <p style='margin-top: 30px; color: #666;'>
            <strong>Note:</strong> The full WebApiClient.html file was not found. 
            Place it in the application directory for the complete control panel interface.
        </p>
    </div>
    
    <script>
        const API_BASE = 'http://localhost:8080';
        
        async function apiCall(endpoint, method = 'GET', data = null) {
            try {
                const options = { method, headers: { 'Content-Type': 'application/json' } };
                if (data) options.body = JSON.stringify(data);
                const response = await fetch(API_BASE + endpoint, options);
                const result = await response.json();
                document.getElementById('response').textContent = JSON.stringify(result, null, 2);
                document.getElementById('status').textContent = 'Connected';
                document.getElementById('status').style.color = 'green';
                return result;
            } catch (error) {
                document.getElementById('response').textContent = 'Error: ' + error.message;
                document.getElementById('status').textContent = 'Disconnected';
                document.getElementById('status').style.color = 'red';
            }
        }
        
        async function testHealth() { await apiCall('/health'); }
        async function testCamera() { await apiCall('/api/camera/status'); }
        async function testSettings() { await apiCall('/api/settings/all'); }
        async function capturePhoto() { await apiCall('/api/camera/capture', 'POST'); }
        
        // Check status on load
        testHealth();
    </script>
</body>
</html>";
            
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.StatusCode = 200;
            
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.Close();
            
            CameraControl.Devices.Log.Debug("WebApiService: Served built-in client page (WebApiClient.html not found)");
        }
        
        private async Task HandleHealthCheck(HttpListenerContext context)
        {
            var health = new
            {
                status = "healthy",
                service = "PhotoboothWebAPI",
                version = "1.0.0",
                timestamp = DateTime.UtcNow
            };
            await SendJson(context, health);
        }
        
        private async Task SendJson(HttpListenerContext context, object data)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = 200;
            
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            var buffer = Encoding.UTF8.GetBytes(json);
            
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.Close();
        }
        
        private async Task SendError(HttpListenerContext context, int statusCode, string message)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            
            var error = new { error = message, statusCode = statusCode };
            var json = JsonConvert.SerializeObject(error);
            var buffer = Encoding.UTF8.GetBytes(json);
            
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.Close();
        }
        
        private async Task<Dictionary<string, string>> GetRequestData(HttpListenerContext context)
        {
            if (context.Request.ContentLength64 == 0)
                return new Dictionary<string, string>();
            
            using (var reader = new StreamReader(context.Request.InputStream))
            {
                var body = await reader.ReadToEndAsync();
                
                if (string.IsNullOrWhiteSpace(body))
                    return new Dictionary<string, string>();
                
                try
                {
                    return JsonConvert.DeserializeObject<Dictionary<string, string>>(body) 
                           ?? new Dictionary<string, string>();
                }
                catch
                {
                    return new Dictionary<string, string>();
                }
            }
        }
        #endregion
    }
}