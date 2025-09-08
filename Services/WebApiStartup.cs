using System;
using System.Threading.Tasks;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    /// <summary>
    /// Handles Web API service initialization and configuration
    /// </summary>
    public static class WebApiStartup
    {
        private static WebApiService _webApiService;
        private static bool _isInitialized = false;

        /// <summary>
        /// Initialize and start the Web API service
        /// </summary>
        public static void Initialize(int port = 8080, bool autoStart = true)
        {
            if (_isInitialized)
            {
                CameraControl.Devices.Log.Debug("WebApiStartup: Already initialized");
                return;
            }

            try
            {
                CameraControl.Devices.Log.Debug($"WebApiStartup: Initializing Web API on port {port}");
                
                _webApiService = WebApiService.Instance;
                
                if (autoStart)
                {
                    Start(port);
                }
                
                _isInitialized = true;
                CameraControl.Devices.Log.Debug("WebApiStartup: Initialization complete");
            }
            catch (Exception ex)
            {
                CameraControl.Devices.Log.Error($"WebApiStartup: Failed to initialize - {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Start the Web API service
        /// </summary>
        public static void Start(int port = 8080)
        {
            try
            {
                if (_webApiService == null)
                {
                    Initialize(port, false);
                }

                if (_webApiService.IsRunning)
                {
                    CameraControl.Devices.Log.Debug("WebApiStartup: Service already running");
                    return;
                }

                _webApiService.Start(port);
                
                CameraControl.Devices.Log.Debug($"WebApiStartup: Web API started on port {port}");
                CameraControl.Devices.Log.Debug($"WebApiStartup: Access the API at http://localhost:{port}/api/");
                CameraControl.Devices.Log.Debug($"WebApiStartup: Health check available at http://localhost:{port}/health");
                
                // Log some example endpoints for reference
                LogApiExamples(port);
            }
            catch (Exception ex)
            {
                CameraControl.Devices.Log.Error($"WebApiStartup: Failed to start - {ex.Message}");
                
                // Common error: port already in use
                if (ex.Message.Contains("denied") || ex.Message.Contains("in use"))
                {
                    CameraControl.Devices.Log.Error($"WebApiStartup: Port {port} is already in use. Try a different port.");
                }
                // Common error: admin privileges required
                else if (ex.Message.Contains("Access") || ex.Message.Contains("admin"))
                {
                    CameraControl.Devices.Log.Error("WebApiStartup: Administrator privileges may be required.");
                    CameraControl.Devices.Log.Error("WebApiStartup: Run this command as admin: netsh http add urlacl url=http://+:8080/ user=Everyone");
                }
                
                throw;
            }
        }

        /// <summary>
        /// Stop the Web API service
        /// </summary>
        public static void Stop()
        {
            try
            {
                if (_webApiService != null && _webApiService.IsRunning)
                {
                    _webApiService.Stop();
                    CameraControl.Devices.Log.Debug("WebApiStartup: Web API stopped");
                }
            }
            catch (Exception ex)
            {
                CameraControl.Devices.Log.Error($"WebApiStartup: Error stopping service - {ex.Message}");
            }
        }

        /// <summary>
        /// Check if the Web API service is running
        /// </summary>
        public static bool IsRunning => _webApiService?.IsRunning ?? false;

        /// <summary>
        /// Get the current port number
        /// </summary>
        public static int Port => _webApiService?.Port ?? 0;

        private static void LogApiExamples(int port)
        {
            CameraControl.Devices.Log.Debug("WebApiStartup: Example API calls:");
            CameraControl.Devices.Log.Debug($"  GET  http://localhost:{port}/api/camera/status     - Check camera connection");
            CameraControl.Devices.Log.Debug($"  POST http://localhost:{port}/api/camera/capture    - Take a photo");
            CameraControl.Devices.Log.Debug($"  GET  http://localhost:{port}/api/session/status    - Check session status");
            CameraControl.Devices.Log.Debug($"  POST http://localhost:{port}/api/session/start     - Start a new session");
            CameraControl.Devices.Log.Debug($"  GET  http://localhost:{port}/api/settings/all      - Get all settings");
            CameraControl.Devices.Log.Debug($"  GET  http://localhost:{port}/api/events/list       - List all events");
            CameraControl.Devices.Log.Debug("");
            CameraControl.Devices.Log.Debug("WebApiStartup: Test with curl:");
            CameraControl.Devices.Log.Debug($"  curl http://localhost:{port}/health");
            CameraControl.Devices.Log.Debug($"  curl http://localhost:{port}/api/camera/status");
            CameraControl.Devices.Log.Debug($"  curl -X POST http://localhost:{port}/api/camera/capture -H \"Content-Type: application/json\" -d '{{\"mode\":\"photo\"}}'");
        }
    }
}