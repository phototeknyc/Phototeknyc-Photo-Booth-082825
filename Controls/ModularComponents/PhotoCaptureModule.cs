using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CameraControl.Devices;
using CameraControl.Devices.Classes;
using Photobooth.Services;
using System.Windows.Threading;
using System.Threading;

namespace Photobooth.Controls.ModularComponents
{
    public class PhotoCaptureModule : PhotoboothModuleBase
    {
        private DispatcherTimer countdownTimer;
        private int countdownSeconds = 3;
        private int currentCountdown;
        private bool isCapturing = false;
        private string lastCapturedPath;
        
        public override string ModuleName => "Photo";
        public override string IconPath => "/Images/Icons/camera.png";
        
        public int CountdownDuration 
        { 
            get => countdownSeconds; 
            set => countdownSeconds = value; 
        }
        
        public event EventHandler<int> CountdownTick;
        public event EventHandler CountdownStarted;
        public event EventHandler CountdownCompleted;
        
        public PhotoCaptureModule()
        {
            countdownTimer = new DispatcherTimer();
            countdownTimer.Interval = TimeSpan.FromSeconds(1);
            countdownTimer.Tick += CountdownTimer_Tick;
        }
        
        public override async Task StartCapture()
        {
            if (_isActive || _camera == null) return;
            
            _isActive = true;
            isCapturing = true;
            
            UpdateStatus("Starting capture", 0, "Preparing camera...");
            
            // Check if photographer mode is enabled
            bool photographerMode = Properties.Settings.Default.PhotographerMode;
            Log.Debug($"PhotoCaptureModule.StartCapture: PhotographerMode = {photographerMode}");
            
            if (photographerMode)
            {
                // Photographer mode - wait for camera trigger instead of countdown
                UpdateStatus("Waiting for trigger", 0, "Press camera trigger when ready");
                Log.Debug("PhotoCaptureModule: Photographer mode enabled - waiting for manual trigger");
                
                // Stop live view to release camera for trigger
                try
                {
                    // Try to stop live view (no way to check if it's on via interface)
                    try
                    {
                        Log.Debug("PhotoCaptureModule: Stopping live view to release camera trigger");
                        _camera.StopLiveView();
                    }
                    catch
                    {
                        // Live view might not be running, that's ok
                    }
                    
                    // Reset IsBusy flag to allow trigger
                    _camera.IsBusy = false;
                    Log.Debug("PhotoCaptureModule: IsBusy set to false - trigger should be ready");
                }
                catch (Exception ex)
                {
                    Log.Debug($"PhotoCaptureModule: Error preparing for photographer mode: {ex.Message}");
                }
                
                // Subscribe to camera's photo captured event for trigger-based capture
                _camera.PhotoCaptured += OnPhotographerModeTrigger;
            }
            else
            {
                // Normal mode - use countdown
                StartCountdown();
            }
            
            await Task.CompletedTask;
        }
        
        public override async Task StopCapture()
        {
            if (!_isActive) return;
            
            countdownTimer.Stop();
            _isActive = false;
            isCapturing = false;
            
            // Unsubscribe from photographer mode event if active
            if (_camera != null)
            {
                _camera.PhotoCaptured -= OnPhotographerModeTrigger;
            }
            
            UpdateStatus("Stopped", 0, "Capture cancelled");
            
            await Task.CompletedTask;
        }
        
        private void StartCountdown()
        {
            currentCountdown = countdownSeconds;
            CountdownStarted?.Invoke(this, EventArgs.Empty);
            UpdateStatus("Countdown", 0, $"Get ready! {currentCountdown}");
            countdownTimer.Start();
        }
        
        private async void CountdownTimer_Tick(object sender, EventArgs e)
        {
            currentCountdown--;
            
            if (currentCountdown > 0)
            {
                CountdownTick?.Invoke(this, currentCountdown);
                UpdateStatus("Countdown", (countdownSeconds - currentCountdown) * 100 / countdownSeconds, 
                    $"Get ready! {currentCountdown}");
            }
            else
            {
                countdownTimer.Stop();
                CountdownCompleted?.Invoke(this, EventArgs.Empty);
                await CapturePhoto();
            }
        }
        
        private async Task CapturePhoto()
        {
            try
            {
                UpdateStatus("Capturing", 50, "Taking photo...");
                
                if (_camera == null || !_camera.IsConnected)
                {
                    throw new InvalidOperationException("Camera is not connected");
                }
                
                // Prepare camera
                _camera.IsBusy = false;
                
                // Set up photo captured event handler
                var tcs = new TaskCompletionSource<PhotoCapturedEventArgs>();
                PhotoCapturedEventHandler handler = null;
                handler = (sender, args) =>
                {
                    _camera.PhotoCaptured -= handler;
                    tcs.TrySetResult(args);
                };
                _camera.PhotoCaptured += handler;
                
                // Capture photo
                try
                {
                    _camera.CapturePhoto();
                }
                catch (DeviceException ex) when ((uint)ex.ErrorCode == 0x00008D01)
                {
                    // Canon AutoFocus failed error - photo may still be captured
                    Log.Debug("PhotoCaptureModule: Canon AF failed (8D01), waiting for photo event");
                }
                catch (Exception ex) when (ex.Message.Contains("8D01") || ex.Message.Contains("Canon error"))
                {
                    // Canon error wrapped in general exception
                    Log.Debug("PhotoCaptureModule: Canon error detected, waiting for photo event");
                }
                
                // Wait for photo captured event with timeout
                var photoEventTask = tcs.Task;
                var timeoutTask = Task.Delay(15000); // 15 second timeout
                var completedTask = await Task.WhenAny(photoEventTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    _camera.PhotoCaptured -= handler;
                    throw new TimeoutException("Photo capture timed out - no photo received from camera");
                }
                
                var eventArgs = await photoEventTask;
                
                UpdateStatus("Processing", 75, "Transferring photo...");
                
                // Generate output path
                string fileName = $"Photo_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
                string fullPath = Path.Combine(_outputFolder, fileName);
                
                // Transfer the photo
                try
                {
                    if (eventArgs.Handle == null)
                    {
                        throw new InvalidOperationException("No photo handle received from camera");
                    }
                    
                    _camera.TransferFile(eventArgs.Handle, fullPath);
                    
                    // Release camera resources
                    try
                    {
                        _camera.ReleaseResurce(eventArgs.Handle);
                    }
                    catch { }
                    
                    if (!File.Exists(fullPath))
                    {
                        throw new FileNotFoundException("Photo was not saved after transfer", fullPath);
                    }
                    
                    lastCapturedPath = fullPath;
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to transfer photo: {ex.Message}", ex);
                }
                
                UpdateStatus("Complete", 100, "Photo captured successfully!");
                
                OnCaptureCompleted(new ModuleCaptureEventArgs
                {
                    OutputPath = fullPath,
                    ModuleName = ModuleName,
                    Success = true,
                    Data = new { Width = 0, Height = 0 }
                });
            }
            catch (Exception ex)
            {
                UpdateStatus("Error", 0, ex.Message);
                Log.Error($"PhotoCaptureModule error: {ex.Message}", ex);
                
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
                _camera.IsBusy = false;
            }
        }
        
        public string GetLastCapturedPhoto()
        {
            return lastCapturedPath;
        }
        
        private async void OnPhotographerModeTrigger(object sender, PhotoCapturedEventArgs e)
        {
            // Handle photo captured by camera's physical button in photographer mode
            if (!_isActive || !isCapturing) return;
            
            try
            {
                Log.Debug("PhotoCaptureModule: Photo captured via camera trigger in photographer mode");
                
                // Unsubscribe to avoid duplicate captures
                _camera.PhotoCaptured -= OnPhotographerModeTrigger;
                
                UpdateStatus("Processing", 75, "Transferring photo...");
                
                // Generate output path
                string fileName = $"Photo_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
                string fullPath = Path.Combine(_outputFolder, fileName);
                
                // Handle the photo transfer
                try
                {
                    if (!string.IsNullOrEmpty(e.FileName) && File.Exists(e.FileName))
                    {
                        // Photo already saved to disk
                        File.Copy(e.FileName, fullPath, true);
                    }
                    else if (e.EventArgs != null)
                    {
                        // Check if EventArgs contains stream data
                        // Note: The EventArgs is of type object, specific handling depends on camera type
                        Log.Debug("PhotoCaptureModule: EventArgs present but stream handling not implemented for this camera type");
                    }
                    else if (e.Handle != null)
                    {
                        // Transfer using handle
                        _camera.TransferFile(e.Handle, fullPath);
                        
                        // Release camera resources
                        try
                        {
                            _camera.ReleaseResurce(e.Handle);
                        }
                        catch { }
                    }
                    
                    if (!File.Exists(fullPath))
                    {
                        throw new FileNotFoundException("Photo was not saved after transfer", fullPath);
                    }
                    
                    lastCapturedPath = fullPath;
                    
                    UpdateStatus("Complete", 100, "Photo captured successfully!");
                    
                    OnCaptureCompleted(new ModuleCaptureEventArgs
                    {
                        OutputPath = fullPath,
                        ModuleName = ModuleName,
                        Success = true,
                        Data = new { Width = 0, Height = 0 }
                    });
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to transfer photo: {ex.Message}", ex);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("Error", 0, ex.Message);
                Log.Error($"PhotoCaptureModule photographer mode error: {ex.Message}", ex);
                
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
                _camera.IsBusy = false;
            }
        }
        
        public override void Cleanup()
        {
            countdownTimer?.Stop();
            countdownTimer = null;
            
            // Ensure we unsubscribe from photographer mode event
            if (_camera != null)
            {
                _camera.PhotoCaptured -= OnPhotographerModeTrigger;
            }
            
            base.Cleanup();
        }
    }
}