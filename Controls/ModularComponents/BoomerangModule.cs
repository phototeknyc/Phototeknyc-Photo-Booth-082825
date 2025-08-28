using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CameraControl.Devices;
using ImageMagick;

namespace Photobooth.Controls.ModularComponents
{
    public class BoomerangModule : PhotoboothModuleBase
    {
        private DispatcherTimer captureTimer;
        private DispatcherTimer countdownTimer;
        private List<string> capturedFrames;
        private int frameCount = 10;
        private int frameDelay = 100; // milliseconds for capture
        private int playbackDelay = 50; // milliseconds for playback
        private int currentFrame = 0;
        private int countdownSeconds = 3;
        private int currentCountdown;
        private bool isCapturing = false;
        
        public override string ModuleName => "Boomerang";
        public override string IconPath => "/Images/Icons/boomerang.png";
        
        public int FrameCount 
        { 
            get => frameCount; 
            set => frameCount = Math.Max(5, Math.Min(20, value)); 
        }
        
        public int CaptureDelayMs 
        { 
            get => frameDelay; 
            set => frameDelay = Math.Max(50, Math.Min(500, value)); 
        }
        
        public int PlaybackDelayMs 
        { 
            get => playbackDelay; 
            set => playbackDelay = Math.Max(30, Math.Min(200, value)); 
        }
        
        public int CountdownDuration 
        { 
            get => countdownSeconds; 
            set => countdownSeconds = value; 
        }
        
        public event EventHandler<int> CountdownTick;
        public event EventHandler<int> FrameCaptured;
        
        public BoomerangModule()
        {
            captureTimer = new DispatcherTimer();
            captureTimer.Interval = TimeSpan.FromMilliseconds(frameDelay);
            captureTimer.Tick += CaptureTimer_Tick;
            
            countdownTimer = new DispatcherTimer();
            countdownTimer.Interval = TimeSpan.FromSeconds(1);
            countdownTimer.Tick += CountdownTimer_Tick;
            
            capturedFrames = new List<string>();
        }
        
        public override async Task StartCapture()
        {
            if (_isActive || _camera == null) return;
            
            _isActive = true;
            isCapturing = true;
            capturedFrames.Clear();
            currentFrame = 0;
            
            UpdateStatus("Starting Boomerang", 0, "Preparing camera...");
            
            StartCountdown();
            
            await Task.CompletedTask;
        }
        
        public override async Task StopCapture()
        {
            if (!_isActive) return;
            
            captureTimer.Stop();
            countdownTimer.Stop();
            _isActive = false;
            isCapturing = false;
            
            CleanupFrames();
            
            UpdateStatus("Stopped", 0, "Boomerang capture cancelled");
            
            await Task.CompletedTask;
        }
        
        private void StartCountdown()
        {
            currentCountdown = countdownSeconds;
            UpdateStatus("Countdown", 0, $"Get ready for Boomerang! {currentCountdown}");
            countdownTimer.Start();
        }
        
        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            currentCountdown--;
            CountdownTick?.Invoke(this, currentCountdown);
            
            if (currentCountdown > 0)
            {
                UpdateStatus("Countdown", (countdownSeconds - currentCountdown) * 100 / countdownSeconds, 
                    $"Get ready for Boomerang! {currentCountdown}");
            }
            else
            {
                countdownTimer.Stop();
                StartFrameCapture();
            }
        }
        
        private void StartFrameCapture()
        {
            UpdateStatus("Capturing", 0, $"Frame 1 of {frameCount}");
            captureTimer.Interval = TimeSpan.FromMilliseconds(frameDelay);
            captureTimer.Start();
            CaptureFrame();
        }
        
        private async void CaptureTimer_Tick(object sender, EventArgs e)
        {
            if (currentFrame < frameCount)
            {
                CaptureFrame();
            }
            else
            {
                captureTimer.Stop();
                await CreateBoomerang();
            }
        }
        
        private async void CaptureFrame()
        {
            try
            {
                currentFrame++;
                UpdateStatus("Capturing", (currentFrame * 100) / frameCount, 
                    $"Frame {currentFrame} of {frameCount}");
                
                await Task.Run(() => _camera.CapturePhoto());
                
                await Task.Delay(100);
                
                // Get last captured image from device manager
                string lastCapturedPath = null;
                if (_deviceManager?.LastCapturedImage?.TryGetValue(_camera, out lastCapturedPath) == true && 
                    !string.IsNullOrEmpty(lastCapturedPath) && File.Exists(lastCapturedPath))
                {
                    string framePath = Path.Combine(_outputFolder, $"boomerang_frame_{currentFrame}_{DateTime.Now.Ticks}.jpg");
                    File.Copy(lastCapturedPath, framePath, true);
                    capturedFrames.Add(framePath);
                    
                    FrameCaptured?.Invoke(this, currentFrame);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("Error", 0, $"Frame capture failed: {ex.Message}");
            }
        }
        
        private async Task CreateBoomerang()
        {
            try
            {
                if (capturedFrames.Count < 5)
                {
                    throw new Exception("Not enough frames captured for Boomerang");
                }
                
                UpdateStatus("Creating Boomerang", 90, "Processing frames...");
                
                string boomerangPath = Path.Combine(_outputFolder, $"Boomerang_{DateTime.Now:yyyyMMdd_HHmmss}.gif");
                
                await Task.Run(() =>
                {
                    using (var collection = new MagickImageCollection())
                    {
                        // Add frames forward
                        foreach (var framePath in capturedFrames)
                        {
                            if (File.Exists(framePath))
                            {
                                var image = new MagickImage(framePath);
                                image.Resize(600, 0);
                                image.AnimationDelay = playbackDelay / 10;
                                collection.Add(image);
                            }
                        }
                        
                        // Add frames backward (excluding first and last to avoid pause)
                        for (int i = capturedFrames.Count - 2; i > 0; i--)
                        {
                            if (File.Exists(capturedFrames[i]))
                            {
                                var image = new MagickImage(capturedFrames[i]);
                                image.Resize(600, 0);
                                image.AnimationDelay = playbackDelay / 10;
                                collection.Add(image);
                            }
                        }
                        
                        // Optimize the GIF
                        collection.OptimizePlus();
                        
                        // Reduce colors for smaller file size
                        var settings = new QuantizeSettings
                        {
                            Colors = 128,
                            DitherMethod = DitherMethod.FloydSteinberg
                        };
                        collection.Quantize(settings);
                        
                        collection.Write(boomerangPath);
                    }
                });
                
                // Also create MP4 version for better quality
                string mp4Path = Path.ChangeExtension(boomerangPath, ".mp4");
                await CreateBoomerangVideo(capturedFrames, mp4Path);
                
                CleanupFrames();
                
                UpdateStatus("Complete", 100, "Boomerang created successfully!");
                
                OnCaptureCompleted(new ModuleCaptureEventArgs
                {
                    OutputPath = boomerangPath,
                    ModuleName = ModuleName,
                    Success = true,
                    Data = new { 
                        GifPath = boomerangPath,
                        Mp4Path = mp4Path,
                        FrameCount = currentFrame, 
                        Duration = playbackDelay * currentFrame * 2 
                    }
                });
            }
            catch (Exception ex)
            {
                UpdateStatus("Error", 0, ex.Message);
                
                OnCaptureCompleted(new ModuleCaptureEventArgs
                {
                    OutputPath = null,
                    ModuleName = ModuleName,
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
            finally
            {
                _isActive = false;
                isCapturing = false;
                currentFrame = 0;
            }
        }
        
        private async Task CreateBoomerangVideo(List<string> frames, string outputPath)
        {
            try
            {
                // Create a temporary file list for ffmpeg
                string listFile = Path.Combine(Path.GetTempPath(), $"boomerang_{Guid.NewGuid()}.txt");
                
                using (var writer = new StreamWriter(listFile))
                {
                    // Forward sequence
                    foreach (var frame in frames)
                    {
                        writer.WriteLine($"file '{frame}'");
                        writer.WriteLine($"duration {playbackDelay / 1000.0:F3}");
                    }
                    
                    // Backward sequence
                    for (int i = frames.Count - 2; i > 0; i--)
                    {
                        writer.WriteLine($"file '{frames[i]}'");
                        writer.WriteLine($"duration {playbackDelay / 1000.0:F3}");
                    }
                    
                    // Add last frame reference for ffmpeg
                    writer.WriteLine($"file '{frames[0]}'");
                }
                
                // Use ffmpeg to create MP4 if available
                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (File.Exists(ffmpegPath))
                {
                    var processInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = $"-f concat -safe 0 -i \"{listFile}\" -c:v libx264 -pix_fmt yuv420p -r 30 \"{outputPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    
                    using (var process = System.Diagnostics.Process.Start(processInfo))
                    {
                        await Task.Run(() => process.WaitForExit());
                    }
                }
                
                // Clean up temp file
                if (File.Exists(listFile))
                {
                    File.Delete(listFile);
                }
            }
            catch
            {
                // Video creation is optional, don't fail the whole operation
            }
        }
        
        private void CleanupFrames()
        {
            foreach (var frame in capturedFrames)
            {
                if (File.Exists(frame))
                {
                    try { File.Delete(frame); } catch { }
                }
            }
            capturedFrames.Clear();
        }
        
        public override void Cleanup()
        {
            captureTimer?.Stop();
            countdownTimer?.Stop();
            captureTimer = null;
            countdownTimer = null;
            
            CleanupFrames();
            
            base.Cleanup();
        }
    }
}