using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    /// <summary>
    /// Service for compressing videos using FFmpeg before cloud upload
    /// </summary>
    public class VideoCompressionService
    {
        #region Singleton
        private static VideoCompressionService _instance;
        public static VideoCompressionService Instance => 
            _instance ?? (_instance = new VideoCompressionService());
        #endregion

        #region Properties
        private readonly string _ffmpegPath;
        private bool _isCompressing;
        
        public bool IsCompressing => _isCompressing;
        
        // FFmpeg quality presets
        private const int CRF_LOW_QUALITY = 35;      // Smallest file size
        private const int CRF_MEDIUM_QUALITY = 28;   // Balanced
        private const int CRF_HIGH_QUALITY = 23;     // Better quality
        private const int CRF_VERYHIGH_QUALITY = 18; // Best quality
        #endregion

        #region Events
        public event EventHandler<VideoCompressionEventArgs> CompressionStarted;
        public event EventHandler<VideoCompressionEventArgs> CompressionCompleted;
        public event EventHandler<VideoCompressionProgressEventArgs> CompressionProgress;
        public event EventHandler<string> CompressionError;
        #endregion

        #region Constructor
        private VideoCompressionService()
        {
            // Look for ffmpeg.exe in the application directory first
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            _ffmpegPath = Path.Combine(appDir, "ffmpeg", "ffmpeg.exe");
            
            if (!File.Exists(_ffmpegPath))
            {
                // Try common installation paths
                _ffmpegPath = Path.Combine(appDir, "ffmpeg.exe");
                
                if (!File.Exists(_ffmpegPath))
                {
                    // Try system PATH
                    _ffmpegPath = "ffmpeg.exe";
                }
            }
            
            Log.Debug($"VideoCompressionService: FFmpeg path set to {_ffmpegPath}");
        }
        #endregion

        #region Public Methods
        
        /// <summary>
        /// Compress a video file based on configured settings
        /// </summary>
        public async Task<string> CompressVideoAsync(string inputPath, string outputPath = null)
        {
            if (_isCompressing)
            {
                Log.Warning("VideoCompressionService: Already compressing a video");
                return null;
            }
            
            if (!File.Exists(inputPath))
            {
                Log.Error($"VideoCompressionService: Input file not found: {inputPath}");
                CompressionError?.Invoke(this, "Input video file not found");
                return null;
            }
            
            try
            {
                _isCompressing = true;
                
                // Get compression settings
                bool compressionEnabled = Properties.Settings.Default.EnableVideoCompression;
                if (!compressionEnabled)
                {
                    Log.Debug("VideoCompressionService: Compression disabled, returning original file");
                    return inputPath;
                }
                
                string quality = Properties.Settings.Default.VideoCompressionQuality ?? "medium";
                string resolution = Properties.Settings.Default.VideoUploadResolution ?? "1080p";
                
                // Generate output path if not provided
                if (string.IsNullOrEmpty(outputPath))
                {
                    string dir = Path.GetDirectoryName(inputPath);
                    string filename = Path.GetFileNameWithoutExtension(inputPath);
                    outputPath = Path.Combine(dir, $"{filename}_compressed.mp4");
                }
                
                Log.Debug($"VideoCompressionService: Starting compression - Quality: {quality}, Resolution: {resolution}");
                Log.Debug($"VideoCompressionService: Input: {inputPath}");
                Log.Debug($"VideoCompressionService: Output: {outputPath}");
                
                // Fire started event
                CompressionStarted?.Invoke(this, new VideoCompressionEventArgs 
                { 
                    InputPath = inputPath, 
                    OutputPath = outputPath 
                });
                
                // Build FFmpeg arguments
                string ffmpegArgs = BuildFFmpegArguments(inputPath, outputPath, quality, resolution);
                
                // Execute FFmpeg
                bool success = await ExecuteFFmpegAsync(ffmpegArgs, inputPath);
                
                if (success && File.Exists(outputPath))
                {
                    // Get file sizes for logging
                    long originalSize = new FileInfo(inputPath).Length;
                    long compressedSize = new FileInfo(outputPath).Length;
                    double reductionPercent = (1 - (double)compressedSize / originalSize) * 100;
                    
                    Log.Debug($"VideoCompressionService: Compression completed successfully");
                    Log.Debug($"VideoCompressionService: Original size: {originalSize / 1024.0 / 1024.0:F2} MB");
                    Log.Debug($"VideoCompressionService: Compressed size: {compressedSize / 1024.0 / 1024.0:F2} MB");
                    Log.Debug($"VideoCompressionService: Size reduction: {reductionPercent:F1}%");
                    
                    // Fire completed event
                    CompressionCompleted?.Invoke(this, new VideoCompressionEventArgs 
                    { 
                        InputPath = inputPath, 
                        OutputPath = outputPath,
                        OriginalSize = originalSize,
                        CompressedSize = compressedSize
                    });
                    
                    return outputPath;
                }
                else
                {
                    Log.Error("VideoCompressionService: Compression failed or output file not created");
                    CompressionError?.Invoke(this, "Video compression failed");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"VideoCompressionService: Error during compression - {ex.Message}");
                CompressionError?.Invoke(this, ex.Message);
                return null;
            }
            finally
            {
                _isCompressing = false;
            }
        }
        
        /// <summary>
        /// Check if FFmpeg is available
        /// </summary>
        public bool IsFFmpegAvailable()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _ffmpegPath,
                        Arguments = "-version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                process.WaitForExit(5000);
                
                bool available = process.ExitCode == 0;
                Log.Debug($"VideoCompressionService: FFmpeg available: {available}");
                
                return available;
            }
            catch (Exception ex)
            {
                Log.Error($"VideoCompressionService: Error checking FFmpeg availability - {ex.Message}");
                return false;
            }
        }
        
        #endregion

        #region Private Methods
        
        private string BuildFFmpegArguments(string inputPath, string outputPath, string quality, string resolution)
        {
            // Get CRF value based on quality setting
            int crf = GetCRFValue(quality);
            
            // Build base arguments with H.264 encoding
            string args = $"-i \"{inputPath}\" -c:v libx264 -preset fast -crf {crf}";
            
            // Add resolution scaling if not original
            if (resolution != "original")
            {
                string scaleFilter = GetScaleFilter(resolution);
                if (!string.IsNullOrEmpty(scaleFilter))
                {
                    args += $" -vf {scaleFilter}";
                }
            }
            
            // Add audio compression (AAC, 128k bitrate)
            args += " -c:a aac -b:a 128k";
            
            // Add MP4 optimization for streaming
            args += " -movflags +faststart";
            
            // Overwrite output file if exists
            args += $" -y \"{outputPath}\"";
            
            return args;
        }
        
        private int GetCRFValue(string quality)
        {
            switch (quality?.ToLower())
            {
                case "low":
                    return CRF_LOW_QUALITY;
                case "high":
                    return CRF_HIGH_QUALITY;
                case "veryhigh":
                    return CRF_VERYHIGH_QUALITY;
                case "medium":
                default:
                    return CRF_MEDIUM_QUALITY;
            }
        }
        
        private string GetScaleFilter(string resolution)
        {
            switch (resolution?.ToLower())
            {
                case "1080p":
                    return "scale='min(1920,iw)':min'(1080,ih)':force_original_aspect_ratio=decrease";
                case "720p":
                    return "scale='min(1280,iw)':min'(720,ih)':force_original_aspect_ratio=decrease";
                case "480p":
                    return "scale='min(854,iw)':min'(480,ih)':force_original_aspect_ratio=decrease";
                default:
                    return "";
            }
        }
        
        private async Task<bool> ExecuteFFmpegAsync(string arguments, string inputPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = _ffmpegPath,
                            Arguments = arguments,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };
                    
                    // Variables for progress calculation
                    double totalDuration = 0;
                    
                    // Handle stderr for progress updates (FFmpeg outputs progress to stderr)
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            // Parse duration from input file info
                            if (e.Data.Contains("Duration:") && totalDuration == 0)
                            {
                                string durationStr = ExtractDuration(e.Data);
                                totalDuration = ParseDuration(durationStr);
                            }
                            
                            // Parse current time for progress
                            if (e.Data.Contains("time=") && totalDuration > 0)
                            {
                                string timeStr = ExtractTime(e.Data);
                                double currentTime = ParseDuration(timeStr);
                                
                                if (currentTime > 0)
                                {
                                    double progress = (currentTime / totalDuration) * 100;
                                    progress = Math.Min(100, Math.Max(0, progress));
                                    
                                    // Fire progress event
                                    CompressionProgress?.Invoke(this, new VideoCompressionProgressEventArgs
                                    {
                                        ProgressPercentage = progress,
                                        CurrentTime = currentTime,
                                        TotalTime = totalDuration
                                    });
                                }
                            }
                        }
                    };
                    
                    process.Start();
                    process.BeginErrorReadLine();
                    process.BeginOutputReadLine();
                    
                    // Wait for process to complete (with timeout)
                    bool completed = process.WaitForExit(600000); // 10 minute timeout
                    
                    if (!completed)
                    {
                        process.Kill();
                        Log.Error("VideoCompressionService: FFmpeg process timed out");
                        return false;
                    }
                    
                    bool success = process.ExitCode == 0;
                    if (!success)
                    {
                        Log.Error($"VideoCompressionService: FFmpeg exited with code {process.ExitCode}");
                    }
                    
                    return success;
                }
                catch (Exception ex)
                {
                    Log.Error($"VideoCompressionService: Error executing FFmpeg - {ex.Message}");
                    return false;
                }
            });
        }
        
        private string ExtractDuration(string line)
        {
            // Extract duration from FFmpeg output like "Duration: 00:01:30.50"
            int startIndex = line.IndexOf("Duration:") + 9;
            int endIndex = line.IndexOf(",", startIndex);
            if (startIndex > 8 && endIndex > startIndex)
            {
                return line.Substring(startIndex, endIndex - startIndex).Trim();
            }
            return "00:00:00";
        }
        
        private string ExtractTime(string line)
        {
            // Extract time from FFmpeg output like "time=00:00:45.30"
            int startIndex = line.IndexOf("time=") + 5;
            int endIndex = line.IndexOf(" ", startIndex);
            if (endIndex == -1) endIndex = line.Length;
            if (startIndex > 4 && endIndex > startIndex)
            {
                return line.Substring(startIndex, endIndex - startIndex).Trim();
            }
            return "00:00:00";
        }
        
        private double ParseDuration(string duration)
        {
            // Parse duration string "HH:MM:SS.ms" to total seconds
            try
            {
                string[] parts = duration.Split(':');
                if (parts.Length == 3)
                {
                    double hours = double.Parse(parts[0]);
                    double minutes = double.Parse(parts[1]);
                    double seconds = double.Parse(parts[2]);
                    return hours * 3600 + minutes * 60 + seconds;
                }
            }
            catch
            {
                // Ignore parsing errors
            }
            return 0;
        }
        
        #endregion
    }
    
    #region Event Args
    
    public class VideoCompressionEventArgs : EventArgs
    {
        public string InputPath { get; set; }
        public string OutputPath { get; set; }
        public long OriginalSize { get; set; }
        public long CompressedSize { get; set; }
    }
    
    public class VideoCompressionProgressEventArgs : EventArgs
    {
        public double ProgressPercentage { get; set; }
        public double CurrentTime { get; set; }
        public double TotalTime { get; set; }
    }
    
    #endregion
}