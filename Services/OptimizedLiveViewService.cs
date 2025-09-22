using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CameraControl.Devices;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    /// <summary>
    /// Optimized live view service with parallel processing and frame buffering
    /// </summary>
    public class OptimizedLiveViewService : IDisposable
    {
        #region Singleton
        private static OptimizedLiveViewService _instance;
        public static OptimizedLiveViewService Instance => _instance ?? (_instance = new OptimizedLiveViewService());
        #endregion

        #region P/Invoke
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
        #endregion

        #region Fields
        private readonly ConcurrentQueue<byte[]> _frameQueue = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<BitmapSource> _processedFrames = new ConcurrentQueue<BitmapSource>();
        private readonly object _processingLock = new object();

        private Thread _captureThread;
        private Thread _processingThread;
        private CancellationTokenSource _cancellationToken;

        private volatile bool _isRunning = false;
        private volatile bool _skipFrames = false;
        private int _frameSkipCount = 0;
        private const int MAX_QUEUE_SIZE = 3; // Keep only latest 3 frames
        private const int PROCESSING_THREADS = 2; // Number of parallel processing threads

        // Performance metrics
        private DateTime _lastFpsUpdate = DateTime.Now;
        private int _captureFrameCount = 0;
        private int _processedFrameCount = 0;
        private double _captureFps = 0;
        private double _processFps = 0;

        // Cached bitmap for reuse
        private Bitmap _cachedBitmap;
        private int _cachedWidth = 0;
        private int _cachedHeight = 0;
        #endregion

        #region Properties
        public bool IsRunning => _isRunning;
        public double CaptureFPS => _captureFps;
        public double ProcessFPS => _processFps;
        public int QueuedFrames => _frameQueue.Count;
        #endregion

        #region Events
        public event EventHandler<BitmapSource> FrameReady;
        public event EventHandler<string> StatusUpdate;
        #endregion

        #region Constructor
        private OptimizedLiveViewService()
        {
            DebugService.LogDebug("OptimizedLiveViewService initialized");
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Start optimized live view capture
        /// </summary>
        public void Start(ICameraDevice camera)
        {
            if (_isRunning || camera == null)
                return;

            _isRunning = true;
            _cancellationToken = new CancellationTokenSource();

            // Start capture thread
            _captureThread = new Thread(() => CaptureThread(camera))
            {
                Name = "LiveView-Capture",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _captureThread.Start();

            // Start multiple processing threads
            for (int i = 0; i < PROCESSING_THREADS; i++)
            {
                var thread = new Thread(() => ProcessingThread())
                {
                    Name = $"LiveView-Process-{i}",
                    IsBackground = true,
                    Priority = ThreadPriority.Normal
                };
                thread.Start();
            }

            DebugService.LogDebug("OptimizedLiveViewService started with parallel processing");
        }

        /// <summary>
        /// Stop live view capture
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            _cancellationToken?.Cancel();

            // Clear queues
            while (_frameQueue.TryDequeue(out _)) { }
            while (_processedFrames.TryDequeue(out _)) { }

            // Dispose cached bitmap
            _cachedBitmap?.Dispose();
            _cachedBitmap = null;

            DebugService.LogDebug("OptimizedLiveViewService stopped");
        }

        /// <summary>
        /// Get next processed frame if available
        /// </summary>
        public BitmapSource GetNextFrame()
        {
            _processedFrames.TryDequeue(out var frame);
            return frame;
        }

        /// <summary>
        /// Enable/disable frame skipping for better performance
        /// </summary>
        public void SetFrameSkipping(bool enable)
        {
            _skipFrames = enable;
            DebugService.LogDebug($"Frame skipping {(enable ? "enabled" : "disabled")}");
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Capture thread - gets frames from camera as fast as possible
        /// </summary>
        private void CaptureThread(ICameraDevice camera)
        {
            var token = _cancellationToken.Token;
            var lastCapture = DateTime.MinValue;
            const int MIN_CAPTURE_INTERVAL_MS = 20; // Max 50 FPS capture rate

            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    // Rate limiting to prevent overwhelming the camera
                    var now = DateTime.Now;
                    var elapsed = (now - lastCapture).TotalMilliseconds;
                    if (elapsed < MIN_CAPTURE_INTERVAL_MS)
                    {
                        Thread.Sleep(MIN_CAPTURE_INTERVAL_MS - (int)elapsed);
                        continue;
                    }

                    // Get live view data from camera
                    var liveViewData = camera.GetLiveViewImage();
                    if (liveViewData?.ImageData != null && liveViewData.ImageData.Length > 0)
                    {
                        // Implement frame skipping if enabled
                        if (_skipFrames && _frameSkipCount++ % 2 == 1)
                        {
                            continue;
                        }

                        // Keep queue size limited - drop old frames if necessary
                        while (_frameQueue.Count >= MAX_QUEUE_SIZE)
                        {
                            _frameQueue.TryDequeue(out _);
                        }

                        // Add to queue for processing
                        _frameQueue.Enqueue(liveViewData.ImageData);
                        lastCapture = now;

                        // Update capture FPS
                        UpdateCaptureFps();
                    }
                }
                catch (Exception ex)
                {
                    // Ignore errors and continue
                    if (DateTime.Now.Second % 10 == 0)
                    {
                        DebugService.LogDebug($"Capture error (will retry): {ex.Message}");
                    }
                    Thread.Sleep(100);
                }
            }
        }

        /// <summary>
        /// Processing thread - converts raw image data to BitmapSource
        /// </summary>
        private void ProcessingThread()
        {
            var token = _cancellationToken.Token;

            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    if (_frameQueue.TryDequeue(out var imageData))
                    {
                        var processedFrame = ProcessFrameFast(imageData);
                        if (processedFrame != null)
                        {
                            // Keep processed queue limited
                            while (_processedFrames.Count >= 2)
                            {
                                _processedFrames.TryDequeue(out _);
                            }

                            _processedFrames.Enqueue(processedFrame);

                            // Notify on UI thread
                            Application.Current?.Dispatcher?.BeginInvoke(
                                DispatcherPriority.Render,
                                new Action(() => FrameReady?.Invoke(this, processedFrame)));

                            UpdateProcessFps();
                        }
                    }
                    else
                    {
                        Thread.Sleep(5); // No frames to process
                    }
                }
                catch (Exception ex)
                {
                    DebugService.LogDebug($"Processing error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Fast frame processing using cached bitmap
        /// </summary>
        private BitmapSource ProcessFrameFast(byte[] imageData)
        {
            IntPtr hBitmap = IntPtr.Zero;
            try
            {
                using (var ms = new MemoryStream(imageData))
                {
                    // Use faster decoder settings
                    using (var bitmap = new Bitmap(ms))
                    {
                        // Optionally reduce quality for speed
                        var targetWidth = bitmap.Width;
                        var targetHeight = bitmap.Height;

                        // If frame is too large, scale down for performance
                        if (bitmap.Width > 1920)
                        {
                            targetWidth = 1920;
                            targetHeight = (int)(bitmap.Height * (1920.0 / bitmap.Width));
                        }

                        // Reuse bitmap if possible
                        if (_cachedBitmap == null ||
                            _cachedWidth != targetWidth ||
                            _cachedHeight != targetHeight)
                        {
                            _cachedBitmap?.Dispose();
                            _cachedBitmap = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
                            _cachedWidth = targetWidth;
                            _cachedHeight = targetHeight;
                        }

                        // Fast scaling if needed
                        if (targetWidth != bitmap.Width || targetHeight != bitmap.Height)
                        {
                            using (var g = Graphics.FromImage(_cachedBitmap))
                            {
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low; // Fastest
                                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                                g.DrawImage(bitmap, 0, 0, targetWidth, targetHeight);
                            }
                            hBitmap = _cachedBitmap.GetHbitmap();
                        }
                        else
                        {
                            hBitmap = bitmap.GetHbitmap();
                        }

                        // Convert to WPF BitmapSource
                        var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());

                        bitmapSource.Freeze();
                        return bitmapSource;
                    }
                }
            }
            finally
            {
                if (hBitmap != IntPtr.Zero)
                {
                    DeleteObject(hBitmap);
                }
            }
        }

        /// <summary>
        /// Update capture FPS metrics
        /// </summary>
        private void UpdateCaptureFps()
        {
            _captureFrameCount++;
            var now = DateTime.Now;
            var elapsed = (now - _lastFpsUpdate).TotalSeconds;

            if (elapsed >= 1.0)
            {
                _captureFps = _captureFrameCount / elapsed;
                _processFps = _processedFrameCount / elapsed;
                _captureFrameCount = 0;
                _processedFrameCount = 0;
                _lastFpsUpdate = now;

                var status = $"Capture: {_captureFps:F1} FPS, Process: {_processFps:F1} FPS, Queue: {_frameQueue.Count}";
                DebugService.LogDebug($"[OPTIMIZED LIVE VIEW] {status}");
                StatusUpdate?.Invoke(this, status);
            }
        }

        /// <summary>
        /// Update process FPS metrics
        /// </summary>
        private void UpdateProcessFps()
        {
            _processedFrameCount++;
        }
        #endregion

        #region IDisposable
        public void Dispose()
        {
            Stop();
            _cachedBitmap?.Dispose();
            _cancellationToken?.Dispose();
        }
        #endregion
    }
}