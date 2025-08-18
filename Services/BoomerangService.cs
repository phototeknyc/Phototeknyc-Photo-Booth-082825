using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Photobooth.Services
{
    public class BoomerangService
    {
        private readonly string _tempDirectory;
        private readonly PhotoboothModulesConfig _config;
        private List<string> _capturedFrames;
        private bool _isCapturing;
        
        public event EventHandler<string> BoomerangCreated;
        public event EventHandler<int> FrameCaptured;
        
        public bool IsCapturing => _isCapturing;
        public int FrameCount => _config.BoomerangFrames;
        public int FrameDelay => _config.BoomerangSpeed;

        public BoomerangService()
        {
            _config = PhotoboothModulesConfig.Instance;
            _tempDirectory = Path.Combine(Path.GetTempPath(), "PhotoboothBoomerang");
            if (!Directory.Exists(_tempDirectory))
            {
                Directory.CreateDirectory(_tempDirectory);
            }
            _capturedFrames = new List<string>();
        }

        public void StartCapture()
        {
            if (_isCapturing) return;
            
            _isCapturing = true;
            _capturedFrames.Clear();
            
            // Clean up old temp files
            CleanupTempFiles();
        }

        public void CaptureFrame(BitmapSource frame)
        {
            if (!_isCapturing) return;
            
            try
            {
                string framePath = Path.Combine(_tempDirectory, $"frame_{_capturedFrames.Count:D3}.jpg");
                
                // Save the frame
                SaveFrame(frame, framePath);
                _capturedFrames.Add(framePath);
                
                // Notify progress
                FrameCaptured?.Invoke(this, _capturedFrames.Count);
                
                // Check if we have enough frames
                if (_capturedFrames.Count >= FrameCount)
                {
                    StopCapture();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error capturing boomerang frame: {ex.Message}");
            }
        }

        public void StopCapture()
        {
            _isCapturing = false;
        }

        public async Task<string> CreateBoomerangAsync(BoomerangType type = BoomerangType.Forward_Reverse)
        {
            if (_capturedFrames.Count < 2)
            {
                throw new InvalidOperationException("Not enough frames captured for boomerang");
            }

            return await Task.Run(() =>
            {
                try
                {
                    List<string> frameSequence = new List<string>(_capturedFrames);
                    
                    // Create the boomerang sequence based on type
                    switch (type)
                    {
                        case BoomerangType.Forward_Reverse:
                            // Add frames in reverse (excluding first and last to avoid duplication)
                            for (int i = frameSequence.Count - 2; i > 0; i--)
                            {
                                frameSequence.Add(_capturedFrames[i]);
                            }
                            break;
                            
                        case BoomerangType.Reverse:
                            frameSequence.Reverse();
                            break;
                            
                        case BoomerangType.Forward:
                            // Keep as is
                            break;
                    }

                    // Generate output paths
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string outputGif = Path.Combine(_tempDirectory, $"boomerang_{timestamp}.gif");
                    string outputMp4 = Path.Combine(_tempDirectory, $"boomerang_{timestamp}.mp4");

                    // Try to create MP4 first (better quality and smaller size)
                    string finalOutput = CreateBoomerangVideo(frameSequence, outputMp4);
                    
                    if (string.IsNullOrEmpty(finalOutput))
                    {
                        // Fallback to GIF if video creation fails
                        finalOutput = CreateBoomerangGif(frameSequence, outputGif);
                    }

                    if (!string.IsNullOrEmpty(finalOutput))
                    {
                        BoomerangCreated?.Invoke(this, finalOutput);
                    }

                    return finalOutput;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error creating boomerang: {ex.Message}");
                    return null;
                }
            });
        }

        private string CreateBoomerangVideo(List<string> frames, string outputPath)
        {
            try
            {
                // Check if FFmpeg is available
                string ffmpegPath = FindFFmpeg();
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    return null;
                }

                // Create a temporary file list for FFmpeg
                string fileListPath = Path.Combine(_tempDirectory, "filelist.txt");
                using (StreamWriter writer = new StreamWriter(fileListPath))
                {
                    foreach (var frame in frames)
                    {
                        writer.WriteLine($"file '{frame}'");
                        writer.WriteLine($"duration 0.1"); // 100ms per frame
                    }
                    // Add the last frame without duration
                    writer.WriteLine($"file '{frames.Last()}'");
                }

                // Build FFmpeg command for a smooth boomerang video
                string arguments = $"-f concat -safe 0 -i \"{fileListPath}\" " +
                                  $"-c:v libx264 -pix_fmt yuv420p " +
                                  $"-vf \"scale=720:1280:force_original_aspect_ratio=decrease,pad=720:1280:(ow-iw)/2:(oh-ih)/2\" " +
                                  $"-r 30 -preset fast -crf 23 " +
                                  $"-movflags +faststart -y \"{outputPath}\"";

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(psi))
                {
                    process.WaitForExit(5000); // 5 second timeout
                    
                    if (process.ExitCode == 0 && File.Exists(outputPath))
                    {
                        return outputPath;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating boomerang video: {ex.Message}");
            }

            return null;
        }

        private string CreateBoomerangGif(List<string> frames, string outputPath)
        {
            try
            {
                // This is a placeholder - you would need to implement GIF creation
                // using a library like ImageMagick.NET or Magick.NET
                // For now, return null to indicate GIF creation is not implemented
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating boomerang GIF: {ex.Message}");
                return null;
            }
        }

        private void SaveFrame(BitmapSource source, string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Create))
            {
                JpegBitmapEncoder encoder = new JpegBitmapEncoder();
                encoder.QualityLevel = 85;
                encoder.Frames.Add(BitmapFrame.Create(source));
                encoder.Save(stream);
            }
        }

        private string FindFFmpeg()
        {
            // Check common locations for FFmpeg
            string[] possiblePaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe"),
                "ffmpeg.exe" // System PATH
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            // Try to find in PATH
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(psi))
                {
                    process.WaitForExit(1000);
                    if (process.ExitCode == 0)
                    {
                        return "ffmpeg";
                    }
                }
            }
            catch { }

            return null;
        }

        private void CleanupTempFiles()
        {
            try
            {
                var files = Directory.GetFiles(_tempDirectory, "frame_*.jpg");
                foreach (var file in files)
                {
                    try { File.Delete(file); } catch { }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            CleanupTempFiles();
        }
    }

    public enum BoomerangType
    {
        Forward,
        Reverse,
        Forward_Reverse
    }
}