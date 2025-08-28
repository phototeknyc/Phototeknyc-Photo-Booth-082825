using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CameraControl.Devices;
using ImageMagick;

namespace Photobooth.Controls.ModularComponents
{
    public class GifModule : PhotoboothModuleBase
    {
        private DispatcherTimer captureTimer;
        private DispatcherTimer countdownTimer;
        private List<string> capturedFrames;
        private int frameCount = 4;
        private int frameDelay = 500; // milliseconds
        private int currentFrame = 0;
        private int countdownSeconds = 3;
        private int currentCountdown;
        private bool isCapturing = false;
        
        public override string ModuleName => "GIF";
        public override string IconPath => "/Images/Icons/gif.png";
        
        public int FrameCount 
        { 
            get => frameCount; 
            set => frameCount = Math.Max(2, Math.Min(10, value)); 
        }
        
        public int FrameDelayMs 
        { 
            get => frameDelay; 
            set => frameDelay = Math.Max(100, Math.Min(2000, value)); 
        }
        
        public int CountdownDuration 
        { 
            get => countdownSeconds; 
            set => countdownSeconds = value; 
        }
        
        public event EventHandler<int> CountdownTick;
        public event EventHandler<int> FrameCaptured;
        
        public GifModule()
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
            
            UpdateStatus("Starting GIF capture", 0, "Preparing camera...");
            
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
            
            foreach (var frame in capturedFrames)
            {
                if (File.Exists(frame))
                {
                    try { File.Delete(frame); } catch { }
                }
            }
            capturedFrames.Clear();
            
            UpdateStatus("Stopped", 0, "GIF capture cancelled");
            
            await Task.CompletedTask;
        }
        
        private void StartCountdown()
        {
            currentCountdown = countdownSeconds;
            UpdateStatus("Countdown", 0, $"Get ready for GIF! {currentCountdown}");
            countdownTimer.Start();
        }
        
        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            currentCountdown--;
            CountdownTick?.Invoke(this, currentCountdown);
            
            if (currentCountdown > 0)
            {
                UpdateStatus("Countdown", (countdownSeconds - currentCountdown) * 100 / countdownSeconds, 
                    $"Get ready for GIF! {currentCountdown}");
            }
            else
            {
                countdownTimer.Stop();
                StartFrameCapture();
            }
        }
        
        private void StartFrameCapture()
        {
            UpdateStatus("Capturing frames", 0, $"Frame 1 of {frameCount}");
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
                await CreateGif();
            }
        }
        
        private async void CaptureFrame()
        {
            try
            {
                currentFrame++;
                UpdateStatus("Capturing frames", (currentFrame * 100) / frameCount, 
                    $"Frame {currentFrame} of {frameCount}");
                
                await Task.Run(() => _camera.CapturePhoto());
                
                await Task.Delay(200);
                
                // Get last captured image from device manager
                string lastCapturedPath = null;
                if (_deviceManager?.LastCapturedImage?.TryGetValue(_camera, out lastCapturedPath) == true && 
                    !string.IsNullOrEmpty(lastCapturedPath) && File.Exists(lastCapturedPath))
                {
                    string framePath = Path.Combine(_outputFolder, $"gif_frame_{currentFrame}_{DateTime.Now.Ticks}.jpg");
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
        
        private async Task CreateGif()
        {
            try
            {
                if (capturedFrames.Count < 2)
                {
                    throw new Exception("Not enough frames captured for GIF");
                }
                
                UpdateStatus("Creating GIF", 90, "Processing frames...");
                
                string gifPath = Path.Combine(_outputFolder, $"GIF_{DateTime.Now:yyyyMMdd_HHmmss}.gif");
                
                await Task.Run(() =>
                {
                    using (var collection = new MagickImageCollection())
                    {
                        foreach (var framePath in capturedFrames)
                        {
                            if (File.Exists(framePath))
                            {
                                var image = new MagickImage(framePath);
                                image.Resize(800, 0);
                                image.AnimationDelay = frameDelay / 10;
                                collection.Add(image);
                            }
                        }
                        
                        collection.OptimizePlus();
                        
                        var settings = new QuantizeSettings
                        {
                            Colors = 256,
                            DitherMethod = DitherMethod.FloydSteinberg
                        };
                        collection.Quantize(settings);
                        
                        collection.Write(gifPath);
                    }
                });
                
                foreach (var frame in capturedFrames)
                {
                    if (File.Exists(frame))
                    {
                        try { File.Delete(frame); } catch { }
                    }
                }
                capturedFrames.Clear();
                
                UpdateStatus("Complete", 100, "GIF created successfully!");
                
                OnCaptureCompleted(new ModuleCaptureEventArgs
                {
                    OutputPath = gifPath,
                    ModuleName = ModuleName,
                    Success = true,
                    Data = new { FrameCount = currentFrame, Duration = frameDelay * currentFrame }
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
        
        public override void Cleanup()
        {
            captureTimer?.Stop();
            countdownTimer?.Stop();
            captureTimer = null;
            countdownTimer = null;
            
            foreach (var frame in capturedFrames)
            {
                if (File.Exists(frame))
                {
                    try { File.Delete(frame); } catch { }
                }
            }
            capturedFrames.Clear();
            
            base.Cleanup();
        }
    }
}