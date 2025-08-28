using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CameraControl.Devices;
using CameraControl.Devices.Classes;

namespace Photobooth.Controls.ModularComponents
{
    public class VideoModule : PhotoboothModuleBase
    {
        private DispatcherTimer recordingTimer;
        private DispatcherTimer countdownTimer;
        private int maxRecordingSeconds = 15;
        private int currentRecordingSeconds = 0;
        private int countdownSeconds = 3;
        private int currentCountdown;
        private bool isRecording = false;
        private string currentVideoPath;
        private CancellationTokenSource recordingCancellation;
        
        public override string ModuleName => "Video";
        public override string IconPath => "/Images/Icons/video.png";
        
        public int MaxRecordingDuration 
        { 
            get => maxRecordingSeconds; 
            set => maxRecordingSeconds = Math.Max(5, Math.Min(60, value)); 
        }
        
        public int CountdownDuration 
        { 
            get => countdownSeconds; 
            set => countdownSeconds = value; 
        }
        
        public event EventHandler<int> CountdownTick;
        public event EventHandler<int> RecordingTick;
        public event EventHandler RecordingStarted;
        public event EventHandler RecordingStopped;
        
        public VideoModule()
        {
            recordingTimer = new DispatcherTimer();
            recordingTimer.Interval = TimeSpan.FromSeconds(1);
            recordingTimer.Tick += RecordingTimer_Tick;
            
            countdownTimer = new DispatcherTimer();
            countdownTimer.Interval = TimeSpan.FromSeconds(1);
            countdownTimer.Tick += CountdownTimer_Tick;
        }
        
        public override async Task StartCapture()
        {
            if (_isActive || _camera == null) return;
            
            _isActive = true;
            currentRecordingSeconds = 0;
            
            UpdateStatus("Starting video", 0, "Preparing camera...");
            
            StartCountdown();
            
            await Task.CompletedTask;
        }
        
        public override async Task StopCapture()
        {
            if (!_isActive) return;
            
            if (isRecording)
            {
                await StopRecording();
            }
            else
            {
                countdownTimer.Stop();
                _isActive = false;
                UpdateStatus("Stopped", 0, "Video capture cancelled");
            }
            
            await Task.CompletedTask;
        }
        
        private void StartCountdown()
        {
            currentCountdown = countdownSeconds;
            UpdateStatus("Countdown", 0, $"Get ready to record! {currentCountdown}");
            countdownTimer.Start();
        }
        
        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            currentCountdown--;
            CountdownTick?.Invoke(this, currentCountdown);
            
            if (currentCountdown > 0)
            {
                UpdateStatus("Countdown", (countdownSeconds - currentCountdown) * 100 / countdownSeconds, 
                    $"Get ready to record! {currentCountdown}");
            }
            else
            {
                countdownTimer.Stop();
                _ = StartRecording();
            }
        }
        
        private async Task StartRecording()
        {
            try
            {
                isRecording = true;
                recordingCancellation = new CancellationTokenSource();
                
                UpdateStatus("Recording", 0, "Starting recording...");
                RecordingStarted?.Invoke(this, EventArgs.Empty);
                
                string fileName = $"Video_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
                currentVideoPath = Path.Combine(_outputFolder, fileName);
                
                // Check if camera supports video recording
                if (_camera.GetCapability(CapabilityEnum.RecordMovie) != null)
                {
                    // Start native camera recording
                    await Task.Run(() =>
                    {
                        _camera.StartRecordMovie();
                    }, recordingCancellation.Token);
                    
                    recordingTimer.Start();
                }
                else
                {
                    // Fallback to frame capture for cameras without video support
                    await CaptureVideoFrames();
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("Error", 0, $"Failed to start recording: {ex.Message}");
                isRecording = false;
                _isActive = false;
                
                OnCaptureCompleted(new ModuleCaptureEventArgs
                {
                    OutputPath = null,
                    ModuleName = ModuleName,
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }
        
        private async Task CaptureVideoFrames()
        {
            // Alternative video capture using sequential photos
            var frames = new System.Collections.Generic.List<string>();
            int frameRate = 10; // fps
            int frameDelay = 1000 / frameRate;
            
            try
            {
                while (currentRecordingSeconds < maxRecordingSeconds && !recordingCancellation.Token.IsCancellationRequested)
                {
                    string framePath = Path.Combine(_outputFolder, $"frame_{frames.Count:D5}.jpg");
                    
                    await Task.Run(() => _camera.CapturePhoto(), recordingCancellation.Token);
                    await Task.Delay(100);
                    
                    // Get last captured image from device manager
                    string lastCapturedPath = null;
                    if (_deviceManager?.LastCapturedImage?.TryGetValue(_camera, out lastCapturedPath) == true && 
                        !string.IsNullOrEmpty(lastCapturedPath) && File.Exists(lastCapturedPath))
                    {
                        File.Copy(lastCapturedPath, framePath, true);
                        frames.Add(framePath);
                    }
                    
                    await Task.Delay(frameDelay);
                    
                    currentRecordingSeconds = frames.Count / frameRate;
                    UpdateStatus("Recording", (currentRecordingSeconds * 100) / maxRecordingSeconds,
                        $"Recording... {currentRecordingSeconds}s / {maxRecordingSeconds}s");
                    
                    RecordingTick?.Invoke(this, currentRecordingSeconds);
                }
                
                // Convert frames to video using ffmpeg if available
                await ConvertFramesToVideo(frames, currentVideoPath);
                
                // Cleanup frames
                foreach (var frame in frames)
                {
                    try { File.Delete(frame); } catch { }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Frame capture failed: {ex.Message}", ex);
            }
        }
        
        private async Task ConvertFramesToVideo(System.Collections.Generic.List<string> frames, string outputPath)
        {
            if (frames.Count == 0) return;
            
            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(ffmpegPath))
            {
                string firstFrame = frames[0];
                string pattern = Path.Combine(Path.GetDirectoryName(firstFrame), "frame_%05d.jpg");
                
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-framerate 10 -i \"{pattern}\" -c:v libx264 -pix_fmt yuv420p \"{outputPath}\"",
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
        }
        
        private void RecordingTimer_Tick(object sender, EventArgs e)
        {
            currentRecordingSeconds++;
            RecordingTick?.Invoke(this, currentRecordingSeconds);
            
            UpdateStatus("Recording", (currentRecordingSeconds * 100) / maxRecordingSeconds,
                $"Recording... {currentRecordingSeconds}s / {maxRecordingSeconds}s");
            
            if (currentRecordingSeconds >= maxRecordingSeconds)
            {
                _ = StopRecording();
            }
        }
        
        private async Task StopRecording()
        {
            if (!isRecording) return;
            
            recordingTimer.Stop();
            recordingCancellation?.Cancel();
            isRecording = false;
            
            UpdateStatus("Processing", 90, "Stopping recording...");
            RecordingStopped?.Invoke(this, EventArgs.Empty);
            
            try
            {
                // Stop native camera recording
                if (_camera.GetCapability(CapabilityEnum.RecordMovie) != null)
                {
                    await Task.Run(() =>
                    {
                        _camera.StopRecordMovie();
                    });
                    
                    // Wait for file to be written
                    await Task.Delay(1000);
                }
                
                // Verify video file exists
                if (File.Exists(currentVideoPath))
                {
                    var fileInfo = new FileInfo(currentVideoPath);
                    
                    UpdateStatus("Complete", 100, "Video saved successfully!");
                    
                    OnCaptureCompleted(new ModuleCaptureEventArgs
                    {
                        OutputPath = currentVideoPath,
                        ModuleName = ModuleName,
                        Success = true,
                        Data = new { 
                            Duration = currentRecordingSeconds,
                            FileSize = fileInfo.Length
                        }
                    });
                }
                else
                {
                    throw new Exception("Video file was not created");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("Error", 0, $"Failed to save video: {ex.Message}");
                
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
                currentRecordingSeconds = 0;
                recordingCancellation?.Dispose();
                recordingCancellation = null;
            }
        }
        
        public override void Cleanup()
        {
            recordingTimer?.Stop();
            countdownTimer?.Stop();
            recordingCancellation?.Cancel();
            recordingCancellation?.Dispose();
            
            recordingTimer = null;
            countdownTimer = null;
            recordingCancellation = null;
            
            base.Cleanup();
        }
    }
}