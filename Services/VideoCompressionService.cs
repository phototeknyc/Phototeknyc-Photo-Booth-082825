using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

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
            
            Debug.WriteLine($"VideoCompressionService: FFmpeg path set to {_ffmpegPath}");
        }
        #endregion

        #region Public Methods
        
        /// <summary>
        /// Generate a thumbnail from a video file
        /// </summary>
        public async Task<string> GenerateThumbnailAsync(string videoPath, string thumbnailPath = null, int secondsOffset = 2)
        {
            if (!File.Exists(videoPath))
            {
                Debug.WriteLine($"VideoCompressionService: Video file not found for thumbnail: {videoPath}");
                return null;
            }
            
            try
            {
                // Generate thumbnail path if not provided
                if (string.IsNullOrEmpty(thumbnailPath))
                {
                    string dir = Path.GetDirectoryName(videoPath);
                    string filename = Path.GetFileNameWithoutExtension(videoPath);
                    thumbnailPath = Path.Combine(dir, $"{filename}_thumb.jpg");
                }
                
                Debug.WriteLine($"VideoCompressionService: Generating thumbnail at {secondsOffset}s from {videoPath}");
                
                // Normalize paths for Windows
                string normalizedVideoPath = NormalizePath(videoPath);
                string normalizedThumbnailPath = NormalizePath(thumbnailPath);

                // Build FFmpeg arguments for thumbnail generation
                // -ss: seek to position (before -i for fast seek)
                // -i: input file
                // -vframes 1: extract one frame
                // -q:v 2: quality (2 is high quality)
                string ffmpegArgs = $"-ss {secondsOffset} -i \"{normalizedVideoPath}\" -vframes 1 -q:v 2 -y \"{normalizedThumbnailPath}\"";

                Debug.WriteLine($"VideoCompressionService: Thumbnail FFmpeg arguments: '{ffmpegArgs}'");
                
                // Execute FFmpeg
                bool success = await ExecuteFFmpegAsync(ffmpegArgs, videoPath);
                
                if (success && File.Exists(thumbnailPath))
                {
                    Debug.WriteLine($"VideoCompressionService: Thumbnail generated successfully: {thumbnailPath}");
                    return thumbnailPath;
                }
                else
                {
                    Debug.WriteLine("VideoCompressionService: Failed to generate thumbnail");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"VideoCompressionService: Error generating thumbnail - {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Compress a video file based on configured settings
        /// </summary>
        public async Task<string> CompressVideoAsync(string inputPath, string outputPath = null)
        {
            if (_isCompressing)
            {
                Debug.WriteLine("VideoCompressionService: WARNING - Already compressing a video");
                return null;
            }
            
            if (!File.Exists(inputPath))
            {
                Debug.WriteLine($"VideoCompressionService: ERROR - Input file not found: {inputPath}");
                CompressionError?.Invoke(this, "Input video file not found");
                return null;
            }
            
            try
            {
                _isCompressing = true;

                // Validate FFmpeg availability
                if (!IsFFmpegAvailable())
                {
                    Debug.WriteLine("VideoCompressionService: ERROR - FFmpeg is not available");
                    CompressionError?.Invoke(this, "FFmpeg is not available. Please ensure FFmpeg is installed and accessible.");
                    return null;
                }

                // Get compression settings
                bool compressionEnabled = Properties.Settings.Default.EnableVideoCompression;
                if (!compressionEnabled)
                {
                    Debug.WriteLine("VideoCompressionService: Compression disabled, returning original file");
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

                // Validate output directory exists
                string outputDir = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(outputDir))
                {
                    Debug.WriteLine($"VideoCompressionService: Creating output directory: {outputDir}");
                    Directory.CreateDirectory(outputDir);
                }

                // Check if input file is accessible
                try
                {
                    using (var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        // File is accessible
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"VideoCompressionService: ERROR - Cannot access input file: {ex.Message}");
                    CompressionError?.Invoke(this, $"Cannot access input file: {ex.Message}");
                    return null;
                }

                Debug.WriteLine($"VideoCompressionService: Starting compression - Quality: {quality}, Resolution: {resolution}");
                Debug.WriteLine($"VideoCompressionService: Input: {inputPath}");
                Debug.WriteLine($"VideoCompressionService: Output: {outputPath}");

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
                    
                    Debug.WriteLine("VideoCompressionService: Compression completed successfully");
                    Debug.WriteLine($"VideoCompressionService: Original size: {originalSize / 1024.0 / 1024.0:F2} MB");
                    Debug.WriteLine($"VideoCompressionService: Compressed size: {compressedSize / 1024.0 / 1024.0:F2} MB");
                    Debug.WriteLine($"VideoCompressionService: Size reduction: {reductionPercent:F1}%");
                    
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
                    Debug.WriteLine("VideoCompressionService: ERROR - Compression failed or output file not created");
                    CompressionError?.Invoke(this, "Video compression failed");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"VideoCompressionService: ERROR during compression - {ex.Message}");
                CompressionError?.Invoke(this, ex.Message);
                return null;
            }
            finally
            {
                _isCompressing = false;
            }
        }
        
        /// <summary>
        /// Check if FFmpeg is available and validate executable
        /// </summary>
        public bool IsFFmpegAvailable()
        {
            try
            {
                Debug.WriteLine($"VideoCompressionService: Checking FFmpeg availability at path: '{_ffmpegPath}'");

                // First check if file exists
                if (!File.Exists(_ffmpegPath) && _ffmpegPath != "ffmpeg.exe")
                {
                    Debug.WriteLine($"VideoCompressionService: FFmpeg executable not found at path: {_ffmpegPath}");
                    return false;
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _ffmpegPath,
                        Arguments = "-version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(_ffmpegPath)
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string errorOutput = process.StandardError.ReadToEnd();
                process.WaitForExit(5000);

                bool available = process.ExitCode == 0;
                Debug.WriteLine($"VideoCompressionService: FFmpeg available: {available}");
                Debug.WriteLine($"VideoCompressionService: FFmpeg version output: {output}");

                if (!available)
                {
                    Debug.WriteLine($"VideoCompressionService: FFmpeg error output: {errorOutput}");
                }

                return available;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"VideoCompressionService: ERROR checking FFmpeg availability - {ex.Message}");
                Debug.WriteLine($"VideoCompressionService: Exception details: {ex}");
                return false;
            }
        }
        
        /// <summary>
        /// Test method to validate FFmpeg command construction (debug only)
        /// </summary>
        public string TestFFmpegCommandConstruction(string inputPath, string outputPath, string quality = "medium", string resolution = "1080p")
        {
            Debug.WriteLine("VideoCompressionService: Testing FFmpeg command construction...");
            string args = BuildFFmpegArguments(inputPath, outputPath, quality, resolution);
            Debug.WriteLine($"VideoCompressionService: Test command: ffmpeg {args}");
            return args;
        }

        /// <summary>
        /// Test basic FFmpeg functionality with a simple command
        /// </summary>
        public async Task<bool> TestBasicFFmpegAsync()
        {
            try
            {
                Debug.WriteLine("VideoCompressionService: Testing basic FFmpeg functionality...");

                // Simple command that should always work: get input information
                string testArgs = "-f lavfi -i testsrc2=duration=1:size=320x240:rate=1 -t 1 -f null -";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _ffmpegPath,
                        Arguments = testArgs,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(_ffmpegPath)
                    }
                };

                Debug.WriteLine($"VideoCompressionService: Running test command: {_ffmpegPath} {testArgs}");

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                string errorOutput = await process.StandardError.ReadToEndAsync();
                process.WaitForExit();

                bool success = process.ExitCode == 0;
                Debug.WriteLine($"VideoCompressionService: Basic test result: {success}, exit code: {process.ExitCode}");

                if (!success)
                {
                    Debug.WriteLine($"VideoCompressionService: Test error output: {errorOutput}");
                }
                else
                {
                    Debug.WriteLine("VideoCompressionService: Basic FFmpeg test passed!");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"VideoCompressionService: Basic test failed with exception: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Private Methods
        
        private string BuildFFmpegArguments(string inputPath, string outputPath, string quality, string resolution)
        {
            // Get CRF value based on quality setting
            int crf = GetCRFValue(quality);

            Debug.WriteLine($"VideoCompressionService: Building FFmpeg arguments with quality={quality}, resolution={resolution}, crf={crf}");

            // Normalize paths for Windows - handle spaces and special characters
            string normalizedInputPath = NormalizePath(inputPath);
            string normalizedOutputPath = NormalizePath(outputPath);

            Debug.WriteLine($"VideoCompressionService: Normalized input path: '{normalizedInputPath}'");
            Debug.WriteLine($"VideoCompressionService: Normalized output path: '{normalizedOutputPath}'");

            // Build base arguments with H.264 encoding
            string args = $"-i \"{normalizedInputPath}\" -c:v libx264 -preset fast -crf {crf}";

            // Add resolution scaling if not original
            if (resolution != "original")
            {
                string scaleFilter = GetScaleFilter(resolution);
                if (!string.IsNullOrEmpty(scaleFilter))
                {
                    args += $" -vf \"{scaleFilter}\""; // Quote the filter for Windows compatibility
                    Debug.WriteLine($"VideoCompressionService: Added scale filter: '{scaleFilter}'");
                }
            }

            // Add audio compression (AAC, 128k bitrate)
            args += " -c:a aac -b:a 128k";

            // Add MP4 optimization for streaming
            args += " -movflags +faststart";

            // Overwrite output file if exists
            args += $" -y \"{normalizedOutputPath}\"";

            Debug.WriteLine($"VideoCompressionService: Final FFmpeg arguments: '{args}'");

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
            // Use simple scale filter syntax that's reliable across platforms
            switch (resolution?.ToLower())
            {
                case "1080p":
                    return "scale=1920:1080:force_original_aspect_ratio=decrease";
                case "720p":
                    return "scale=1280:720:force_original_aspect_ratio=decrease";
                case "480p":
                    return "scale=854:480:force_original_aspect_ratio=decrease";
                default:
                    return "";
            }
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Convert to absolute path and normalize separators
            string normalized = Path.GetFullPath(path);

            // Replace forward slashes with backslashes for Windows
            normalized = normalized.Replace('/', '\\');

            Debug.WriteLine($"VideoCompressionService: Path normalized from '{path}' to '{normalized}'");

            return normalized;
        }
        
        private async Task<bool> ExecuteFFmpegAsync(string arguments, string inputPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Add detailed debug logging
                    Debug.WriteLine($"VideoCompressionService: EXECUTING FFmpeg");
                    Debug.WriteLine($"VideoCompressionService: FFmpeg Path: '{_ffmpegPath}'");
                    Debug.WriteLine($"VideoCompressionService: FFmpeg Arguments: '{arguments}'");
                    Debug.WriteLine($"VideoCompressionService: Working Directory: '{Environment.CurrentDirectory}'");

                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = _ffmpegPath,
                            Arguments = arguments,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            WorkingDirectory = Path.GetDirectoryName(_ffmpegPath) // Set working directory to FFmpeg location
                        }
                    };

                    // Variables for progress calculation and error capture
                    double totalDuration = 0;
                    string errorOutput = "";
                    string standardOutput = "";

                    // Handle stderr for progress updates (FFmpeg outputs progress to stderr)
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            errorOutput += e.Data + "\n";
                            Debug.WriteLine($"VideoCompressionService: FFmpeg STDERR: {e.Data}");

                            // Parse duration from input file info
                            if (e.Data.Contains("Duration:") && totalDuration == 0)
                            {
                                string durationStr = ExtractDuration(e.Data);
                                totalDuration = ParseDuration(durationStr);
                                Debug.WriteLine($"VideoCompressionService: Detected duration: {totalDuration} seconds");
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

                    // Handle stdout output
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            standardOutput += e.Data + "\n";
                            Debug.WriteLine($"VideoCompressionService: FFmpeg STDOUT: {e.Data}");
                        }
                    };

                    Debug.WriteLine("VideoCompressionService: Starting FFmpeg process...");
                    process.Start();
                    process.BeginErrorReadLine();
                    process.BeginOutputReadLine();

                    // Wait for process to complete (with timeout)
                    bool completed = process.WaitForExit(600000); // 10 minute timeout

                    if (!completed)
                    {
                        process.Kill();
                        Debug.WriteLine("VideoCompressionService: ERROR - FFmpeg process timed out");
                        return false;
                    }

                    Debug.WriteLine($"VideoCompressionService: FFmpeg process completed with exit code: {process.ExitCode}");

                    bool success = process.ExitCode == 0;
                    if (!success)
                    {
                        Debug.WriteLine($"VideoCompressionService: ERROR - FFmpeg exited with code {process.ExitCode}");
                        Debug.WriteLine($"VideoCompressionService: FFmpeg STDERR OUTPUT:\n{errorOutput}");
                        Debug.WriteLine($"VideoCompressionService: FFmpeg STDOUT OUTPUT:\n{standardOutput}");
                    }
                    else
                    {
                        Debug.WriteLine("VideoCompressionService: FFmpeg execution successful!");
                    }

                    return success;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"VideoCompressionService: ERROR executing FFmpeg - {ex.Message}");
                    Debug.WriteLine($"VideoCompressionService: Exception stack trace: {ex.StackTrace}");
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