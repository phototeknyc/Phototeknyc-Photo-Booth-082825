using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Photobooth.Services
{
    public static class VideoGenerationService
    {
        /// <summary>
        /// Generate an MP4 video from a collection of images using FFmpeg
        /// </summary>
        public static string GenerateMP4Video(List<string> imagePaths, string outputPath, int frameRate = 2, int width = 1280, int height = 720)
        {
            try
            {
                if (imagePaths == null || imagePaths.Count < 2)
                {
                    // Not enough images for video
                    return null;
                }

                // Check if FFmpeg is available
                string ffmpegPath = FindFFmpeg();
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    // FFmpeg not found. Please install FFmpeg.
                    return null;
                }

                // Creating MP4 with images

                // Create a temporary directory for processed frames
                string tempDir = Path.Combine(Path.GetTempPath(), $"photobooth_video_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    // Copy and rename images for FFmpeg (needs sequential naming)
                    for (int i = 0; i < imagePaths.Count; i++)
                    {
                        string sourcePath = imagePaths[i];
                        string destPath = Path.Combine(tempDir, $"frame_{i:D4}.jpg");
                        File.Copy(sourcePath, destPath, true);
                    }

                    // Build FFmpeg command for a smooth video with image interpolation
                    // Using H.264 codec with good compatibility
                    string arguments = $"-framerate 1/{frameRate} " +  // Each image shows for 'frameRate' seconds
                                      $"-i \"{Path.Combine(tempDir, "frame_%04d.jpg")}\" " +
                                      $"-c:v libx264 " +  // H.264 codec
                                      $"-r 30 " +  // Output frame rate (smooth playback)
                                      $"-pix_fmt yuv420p " +  // Compatibility
                                      $"-vf \"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,fps=30\" " +
                                      $"-preset fast " +  // Fast encoding
                                      $"-crf 23 " +  // Good quality (lower = better, 23 is good)
                                      $"-movflags +faststart " +  // Web optimization
                                      $"-y \"{outputPath}\"";  // Overwrite output

                    // Execute FFmpeg
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
                        // Read output for debugging
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();

                        process.WaitForExit(10000); // 10 second timeout

                        if (process.ExitCode == 0 && File.Exists(outputPath))
                        {
                            long fileSize = new FileInfo(outputPath).Length;
                            // Successfully created MP4
                            return outputPath;
                        }
                        else
                        {
                            // FFmpeg failed
                            if (!string.IsNullOrEmpty(error))
                            {
                                // FFmpeg error: {error}
                            }
                            return null;
                        }
                    }
                }
                finally
                {
                    // Clean up temporary files
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                // Failed to generate video
                return null;
            }
        }

        /// <summary>
        /// Generate a looping GIF-like MP4 (short, auto-loops in most players)
        /// </summary>
        public static string GenerateLoopingMP4(List<string> imagePaths, string outputPath, int frameDurationMs = 500)
        {
            string debugLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mp4_generation.txt");
            try
            {
                File.WriteAllText(debugLog, $"Starting MP4 generation at {DateTime.Now}\r\n");
                File.AppendAllText(debugLog, $"Image count: {imagePaths?.Count ?? 0}\r\n");
                
                if (imagePaths == null || imagePaths.Count < 2)
                {
                    File.AppendAllText(debugLog, "Not enough images\r\n");
                    return null;
                }

                string ffmpegPath = FindFFmpeg();
                File.AppendAllText(debugLog, $"FFmpeg path: {ffmpegPath ?? "NULL"}\r\n");
                
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    // FFmpeg not found
                    File.AppendAllText(debugLog, "FFmpeg not found\r\n");
                    return null;
                }

                // Create input file list for FFmpeg with seamless looping
                string tempListFile = Path.GetTempFileName();
                File.AppendAllText(debugLog, $"Temp list file: {tempListFile}\r\n");
                
                using (StreamWriter sw = new StreamWriter(tempListFile))
                {
                    // For seamless looping, we'll play forward then backward
                    // This creates a perfect loop without jarring transitions
                    
                    // Forward sequence
                    for (int i = 0; i < imagePaths.Count; i++)
                    {
                        string line = $"file '{imagePaths[i].Replace('\\', '/')}'";
                        sw.WriteLine(line);
                        sw.WriteLine($"duration {frameDurationMs / 1000.0:F3}");
                        File.AppendAllText(debugLog, $"Added image (forward): {line}\r\n");
                    }
                    
                    // Backward sequence (excluding first and last to avoid duplication)
                    for (int i = imagePaths.Count - 2; i > 0; i--)
                    {
                        string line = $"file '{imagePaths[i].Replace('\\', '/')}'";
                        sw.WriteLine(line);
                        sw.WriteLine($"duration {frameDurationMs / 1000.0:F3}");
                        File.AppendAllText(debugLog, $"Added image (backward): {line}\r\n");
                    }
                    
                    // Add first image again to complete the loop
                    sw.WriteLine($"file '{imagePaths[0].Replace('\\', '/')}'");
                }

                // FFmpeg command for a looping GIF-like video
                // Using higher frame rate for smoother animation
                string arguments = $"-f concat -safe 0 -i \"{tempListFile}\" " +
                                  $"-c:v libx264 -preset ultrafast " +  // Ultra fast encoding
                                  $"-crf 18 " +  // Better quality for GIF-like appearance
                                  $"-pix_fmt yuv420p " +
                                  $"-vf \"scale=640:480:force_original_aspect_ratio=decrease,pad=640:480:(ow-iw)/2:(oh-ih)/2,fps=10\" " +  // 10fps for smooth animation
                                  $"-movflags +faststart " +  // Web optimization
                                  $"-metadata comment=\"Looping GIF-style video\" " +
                                  $"-y \"{outputPath}\"";

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                File.AppendAllText(debugLog, $"Starting FFmpeg with args: {arguments}\r\n");
                
                using (Process process = Process.Start(psi))
                {
                    // Read output asynchronously to avoid deadlock
                    StringBuilder outputBuilder = new StringBuilder();
                    StringBuilder errorBuilder = new StringBuilder();
                    
                    process.OutputDataReceived += (sender, e) => 
                    {
                        if (e.Data != null) outputBuilder.AppendLine(e.Data);
                    };
                    process.ErrorDataReceived += (sender, e) => 
                    {
                        if (e.Data != null) errorBuilder.AppendLine(e.Data);
                    };
                    
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    
                    bool completed = process.WaitForExit(15000); // Increased to 15 second timeout
                    
                    string output = outputBuilder.ToString();
                    string error = errorBuilder.ToString();
                    
                    File.AppendAllText(debugLog, $"FFmpeg completed: {completed}\r\n");
                    File.AppendAllText(debugLog, $"FFmpeg exit code: {(completed ? process.ExitCode.ToString() : "TIMEOUT")}\r\n");
                    File.AppendAllText(debugLog, $"FFmpeg output: {output}\r\n");
                    File.AppendAllText(debugLog, $"FFmpeg error: {error}\r\n");
                    File.AppendAllText(debugLog, $"Output file exists: {File.Exists(outputPath)}\r\n");
                    
                    if (!completed)
                    {
                        try { process.Kill(); } catch { }
                        File.AppendAllText(debugLog, "Process killed due to timeout\r\n");
                    }

                    if (completed && process.ExitCode == 0 && File.Exists(outputPath))
                    {
                        File.Delete(tempListFile);
                        File.AppendAllText(debugLog, "SUCCESS - MP4 created\r\n");
                        // Created looping MP4
                        return outputPath;
                    }
                    else
                    {
                        File.AppendAllText(debugLog, "FAILED - MP4 not created\r\n");
                    }
                }

                File.Delete(tempListFile);
                return null;
            }
            catch (Exception ex)
            {
                // Failed to generate looping video
                try
                {
                    File.AppendAllText(debugLog, $"EXCEPTION: {ex.Message}\r\n{ex.StackTrace}\r\n");
                }
                catch { }
                return null;
            }
        }

        /// <summary>
        /// Generate a boomerang-style MP4 (plays forward then backward)
        /// </summary>
        public static string GenerateBoomerangMP4(List<string> imagePaths, string outputPath, int frameDurationMs = 200)
        {
            try
            {
                if (imagePaths == null || imagePaths.Count < 2)
                    return null;

                string ffmpegPath = FindFFmpeg();
                if (string.IsNullOrEmpty(ffmpegPath))
                    return null;

                // Create temporary video files for forward and reverse
                string tempForward = Path.GetTempFileName() + ".mp4";
                string tempReverse = Path.GetTempFileName() + ".mp4";
                string tempListFile = Path.GetTempFileName();

                try
                {
                    // Create input list for forward video
                    using (StreamWriter sw = new StreamWriter(tempListFile))
                    {
                        foreach (string imagePath in imagePaths)
                        {
                            sw.WriteLine($"file '{imagePath.Replace('\\', '/')}'");
                            sw.WriteLine($"duration {frameDurationMs / 1000.0:F3}");
                        }
                        sw.WriteLine($"file '{imagePaths.Last().Replace('\\', '/')}'");
                    }

                    // Create forward video
                    string forwardArgs = $"-f concat -safe 0 -i \"{tempListFile}\" " +
                                        $"-c:v libx264 -preset ultrafast -crf 18 " +
                                        $"-vf \"scale=640:480:force_original_aspect_ratio=decrease,pad=640:480:(ow-iw)/2:(oh-ih)/2,fps=15\" " +
                                        $"-y \"{tempForward}\"";

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = forwardArgs,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (Process process = Process.Start(psi))
                    {
                        process.WaitForExit(10000);
                        if (process.ExitCode != 0) return null;
                    }

                    // Create reverse video
                    string reverseArgs = $"-i \"{tempForward}\" -vf reverse -y \"{tempReverse}\"";
                    psi.Arguments = reverseArgs;

                    using (Process process = Process.Start(psi))
                    {
                        process.WaitForExit(10000);
                        if (process.ExitCode != 0) return null;
                    }

                    // Concatenate forward and reverse
                    File.WriteAllText(tempListFile, 
                        $"file '{tempForward.Replace('\\', '/')}'\r\n" +
                        $"file '{tempReverse.Replace('\\', '/')}'");

                    string concatArgs = $"-f concat -safe 0 -i \"{tempListFile}\" " +
                                       $"-c copy -movflags +faststart -y \"{outputPath}\"";
                    psi.Arguments = concatArgs;

                    using (Process process = Process.Start(psi))
                    {
                        process.WaitForExit(10000);
                        if (process.ExitCode == 0 && File.Exists(outputPath))
                        {
                            return outputPath;
                        }
                    }
                }
                finally
                {
                    // Cleanup temp files
                    try { File.Delete(tempForward); } catch { }
                    try { File.Delete(tempReverse); } catch { }
                    try { File.Delete(tempListFile); } catch { }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Find FFmpeg executable
        /// </summary>
        private static string FindFFmpeg()
        {
            // The application is running from bin\Debug or bin\Release
            // FFmpeg should be in the same directory as the exe
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // Check common locations
            string[] possiblePaths = new[]
            {
                Path.Combine(baseDir, "ffmpeg.exe"),  // Same directory as exe (most likely)
                "ffmpeg.exe",  // Current directory
                Path.Combine(baseDir, "bin", "ffmpeg.exe"),
                Path.Combine(baseDir, "..", "ffmpeg.exe"),  // One level up
                Path.Combine(baseDir, "..", "..", "bin", "ffmpeg.exe"),  // Project bin folder
                Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg.exe"),
                Path.Combine(Directory.GetCurrentDirectory(), "bin", "ffmpeg.exe"),
                Path.Combine(baseDir, "tools", "ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe"),
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe"
            };

            // Debug: Write current base directory to help locate ffmpeg
            string debugPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg_search.txt");
            try
            {
                File.WriteAllText(debugPath, $"Base Directory: {AppDomain.CurrentDomain.BaseDirectory}\r\nCurrent Directory: {Directory.GetCurrentDirectory()}\r\n\r\nSearching paths:\r\n");
            }
            catch { }

            foreach (string path in possiblePaths)
            {
                try
                {
                    File.AppendAllText(debugPath, $"Checking: {path} - Exists: {File.Exists(path)}\r\n");
                }
                catch { }

                if (File.Exists(path))
                {
                    // Found FFmpeg at path
                    try
                    {
                        File.AppendAllText(debugPath, $"\r\nFOUND FFMPEG AT: {path}\r\n");
                    }
                    catch { }
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
                        // Found FFmpeg in PATH
                        return "ffmpeg";
                    }
                }
            }
            catch { }

            return null;
        }
    }
}