using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Photobooth.Services
{
    /// <summary>
    /// Service specifically for compressing animation MP4 files before cloud upload
    /// Separate from VideoCompressionService to avoid conflicts with video recordings
    /// </summary>
    public class AnimationCompressionService
    {
        #region Singleton
        private static AnimationCompressionService _instance;
        public static AnimationCompressionService Instance =>
            _instance ?? (_instance = new AnimationCompressionService());
        #endregion

        #region Properties
        private readonly string _ffmpegPath;
        private bool _isCompressing;

        public bool IsCompressing => _isCompressing;

        // FFmpeg quality presets for animations (optimized for smaller files)
        private const int CRF_LOW_QUALITY = 32;      // Smallest file size for animations
        private const int CRF_MEDIUM_QUALITY = 26;   // Balanced for animations
        private const int CRF_HIGH_QUALITY = 22;     // Better quality for animations
        private const int CRF_VERYHIGH_QUALITY = 18; // Best quality
        #endregion

        #region Events
        public event EventHandler<AnimationCompressionEventArgs> CompressionStarted;
        public event EventHandler<AnimationCompressionEventArgs> CompressionCompleted;
        public event EventHandler<AnimationCompressionProgressEventArgs> CompressionProgress;
        public event EventHandler<string> CompressionError;
        #endregion

        #region Constructor
        private AnimationCompressionService()
        {
            // Look for ffmpeg.exe in the application directory first
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            _ffmpegPath = Path.Combine(appDir, "ffmpeg.exe");

            if (!File.Exists(_ffmpegPath))
            {
                // Try ffmpeg subfolder
                _ffmpegPath = Path.Combine(appDir, "ffmpeg", "ffmpeg.exe");

                if (!File.Exists(_ffmpegPath))
                {
                    // Fall back to system PATH
                    _ffmpegPath = "ffmpeg.exe";
                }
            }

            Debug.WriteLine($"AnimationCompressionService: FFmpeg path set to {_ffmpegPath}");
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Compress an animation file with simplified settings optimized for animations
        /// </summary>
        public async Task<string> CompressAnimationAsync(string inputPath, string outputPath = null)
        {
            if (_isCompressing)
            {
                Debug.WriteLine("AnimationCompressionService: WARNING - Already compressing an animation");
                return null;
            }

            if (!File.Exists(inputPath))
            {
                Debug.WriteLine($"AnimationCompressionService: ERROR - Input file not found: {inputPath}");
                CompressionError?.Invoke(this, "Input animation file not found");
                return null;
            }

            try
            {
                _isCompressing = true;

                // For animations, skip compression to avoid issues
                // Animations are already highly compressed and often have unusual formats
                Debug.WriteLine("AnimationCompressionService: Skipping compression for animation files");
                return inputPath;

                // Get settings (use simpler defaults for animations)
                string quality = Properties.Settings.Default.VideoCompressionQuality ?? "medium";
                string resolution = Properties.Settings.Default.VideoUploadResolution ?? "original";

                // Generate output path if not provided
                if (string.IsNullOrEmpty(outputPath))
                {
                    string tempDir = Path.GetTempPath();
                    string filename = $"compressed_animation_{Guid.NewGuid():N}.mp4";
                    outputPath = Path.Combine(tempDir, filename);
                }

                // Ensure output directory exists
                string outputDir = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(outputDir))
                {
                    Debug.WriteLine($"AnimationCompressionService: Creating output directory: {outputDir}");
                    Directory.CreateDirectory(outputDir);
                }

                Debug.WriteLine($"AnimationCompressionService: Starting compression - Quality: {quality}, Resolution: {resolution}");
                Debug.WriteLine($"AnimationCompressionService: Input: {inputPath}");
                Debug.WriteLine($"AnimationCompressionService: Output: {outputPath}");

                // Fire started event
                CompressionStarted?.Invoke(this, new AnimationCompressionEventArgs
                {
                    InputPath = inputPath,
                    OutputPath = outputPath
                });

                // Build simplified FFmpeg arguments for animations
                string ffmpegArgs = BuildAnimationArguments(inputPath, outputPath, quality, resolution);

                // Execute FFmpeg
                bool success = await ExecuteFFmpegAsync(ffmpegArgs, inputPath);

                if (success && File.Exists(outputPath))
                {
                    // Get file sizes for logging
                    long originalSize = new FileInfo(inputPath).Length;
                    long compressedSize = new FileInfo(outputPath).Length;
                    double reductionPercent = (1 - (double)compressedSize / originalSize) * 100;

                    Debug.WriteLine("AnimationCompressionService: Compression completed successfully");
                    Debug.WriteLine($"AnimationCompressionService: Original size: {originalSize / 1024.0 / 1024.0:F2} MB");
                    Debug.WriteLine($"AnimationCompressionService: Compressed size: {compressedSize / 1024.0 / 1024.0:F2} MB");
                    Debug.WriteLine($"AnimationCompressionService: Size reduction: {reductionPercent:F1}%");

                    // Fire completed event
                    CompressionCompleted?.Invoke(this, new AnimationCompressionEventArgs
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
                    Debug.WriteLine("AnimationCompressionService: ERROR - Compression failed or output file not created");
                    CompressionError?.Invoke(this, "Animation compression failed");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AnimationCompressionService: ERROR during compression - {ex.Message}");
                Debug.WriteLine($"AnimationCompressionService: Stack trace: {ex.StackTrace}");
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
                Debug.WriteLine($"AnimationCompressionService: FFmpeg available: {available}");

                return available;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AnimationCompressionService: ERROR checking FFmpeg availability - {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Build simplified FFmpeg arguments optimized for animations
        /// </summary>
        private string BuildAnimationArguments(string inputPath, string outputPath, string quality, string resolution)
        {
            // Get CRF value based on quality setting
            int crf = GetCRFValue(quality);

            Debug.WriteLine($"AnimationCompressionService: Building arguments - quality={quality}, resolution={resolution}, crf={crf}");

            // For animation files with issues, just remux without re-encoding
            // This preserves quality while fixing container issues
            // We'll skip compression to avoid errors with problematic animation files
            string args = $"-i \"{inputPath}\" -c:v copy -c:a copy";

            // Add MP4 optimization for streaming
            args += " -movflags +faststart";

            // Overwrite output file if exists
            args += $" -y \"{outputPath}\"";

            Debug.WriteLine($"AnimationCompressionService: Final arguments: {args}");

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

        /// <summary>
        /// Get simple scale values without complex filters
        /// </summary>
        private string GetSimpleScale(string resolution)
        {
            switch (resolution?.ToLower())
            {
                case "1080p":
                    return "1920:1080";
                case "720p":
                    return "1280:720";
                case "480p":
                    return "854:480";
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
                    Debug.WriteLine($"AnimationCompressionService: Executing FFmpeg");
                    Debug.WriteLine($"AnimationCompressionService: Command: {_ffmpegPath} {arguments}");

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
                    string fullErrorOutput = "";

                    // Handle stderr for progress updates (FFmpeg outputs progress to stderr)
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            fullErrorOutput += e.Data + Environment.NewLine;

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
                                    CompressionProgress?.Invoke(this, new AnimationCompressionProgressEventArgs
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
                    bool completed = process.WaitForExit(300000); // 5 minute timeout for animations

                    if (!completed)
                    {
                        process.Kill();
                        Debug.WriteLine("AnimationCompressionService: ERROR - FFmpeg process timed out");
                        return false;
                    }

                    bool success = process.ExitCode == 0;
                    if (!success)
                    {
                        Debug.WriteLine($"AnimationCompressionService: ERROR - FFmpeg exited with code {process.ExitCode}");
                        Debug.WriteLine($"AnimationCompressionService: FFmpeg error output: {fullErrorOutput}");
                    }

                    return success;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AnimationCompressionService: ERROR executing FFmpeg - {ex.Message}");
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

    public class AnimationCompressionEventArgs : EventArgs
    {
        public string InputPath { get; set; }
        public string OutputPath { get; set; }
        public long OriginalSize { get; set; }
        public long CompressedSize { get; set; }
    }

    public class AnimationCompressionProgressEventArgs : EventArgs
    {
        public double ProgressPercentage { get; set; }
        public double CurrentTime { get; set; }
        public double TotalTime { get; set; }
    }

    #endregion
}