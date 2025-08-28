using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CameraControl.Devices;
using CameraControl.Devices.Classes;
using Photobooth.Services;
using Photobooth.Database;
using System.Collections.Generic;
using System.Linq;

namespace Photobooth.Controls.ModularComponents
{
    /// <summary>
    /// Photo capture and print module - core photobooth functionality
    /// </summary>
    public partial class PhotoPrintModule : UserControl
    {
        #region Private Fields
        
        private ICameraDevice _camera;
        private string _outputFolder;
        private bool _isActive;
        private bool _isEnabled = true;
        
        // Services
        private PhotoCaptureService _captureService;
        private PrintingService _printingService;
        private EventTemplateService _templateService;
        private PhotoboothService _photoboothService;
        
        // Timers
        private DispatcherTimer _countdownTimer;
        private DispatcherTimer _sessionTimer;
        
        // Capture state
        private int _countdownSeconds = 3;
        private int _currentCountdown;
        private List<string> _capturedPhotoPaths = new List<string>();
        private int _totalPhotosNeeded = 1;
        private int _currentPhotoIndex = 0;
        
        // Template/Event data
        private TemplateData _currentTemplate;
        private EventData _currentEvent;
        
        #endregion
        
        #region Events
        
        public event EventHandler<PhotoCaptureCompletedEventArgs> CaptureCompleted;
        public event EventHandler<PhotoStatusEventArgs> StatusChanged;
        
        #endregion
        
        #region Properties
        
        public string ModuleName => "Photo Print";
        
        public string IconPath => "/images/print-icon.png";
        
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                ModuleButton.IsEnabled = value;
            }
        }
        
        public bool IsActive => _isActive;
        
        public int CountdownSeconds
        {
            get => _countdownSeconds;
            set => _countdownSeconds = Math.Max(0, Math.Min(10, value));
        }
        
        public bool AutoPrint { get; set; } = true;
        
        public bool ShowPrintConfirmation { get; set; } = true;
        
        #endregion
        
        #region Constructor
        
        public PhotoPrintModule()
        {
            InitializeComponent();
            InitializeServices();
            InitializeTimers();
            LoadSettings();
        }
        
        #endregion
        
        #region Initialization
        
        private void InitializeServices()
        {
            var databaseOps = new DatabaseOperations();
            _captureService = new PhotoCaptureService(databaseOps);
            _printingService = new PrintingService();
            _templateService = new EventTemplateService();
            _photoboothService = new PhotoboothService();
        }
        
        private void InitializeTimers()
        {
            _countdownTimer = new DispatcherTimer();
            _countdownTimer.Interval = TimeSpan.FromSeconds(1);
            _countdownTimer.Tick += CountdownTimer_Tick;
            
            _sessionTimer = new DispatcherTimer();
            _sessionTimer.Interval = TimeSpan.FromSeconds(30); // Auto-clear after 30 seconds
            _sessionTimer.Tick += SessionTimer_Tick;
        }
        
        private void LoadSettings()
        {
            try
            {
                CountdownSeconds = Properties.Settings.Default.CountdownSeconds;
                AutoPrint = Properties.Settings.Default.AutoPrintPhotos;
                ShowPrintConfirmation = Properties.Settings.Default.ShowPrintConfirmation;
            }
            catch
            {
                // Use defaults if settings not available
            }
        }
        
        public void Initialize(ICameraDevice camera, string outputFolder)
        {
            _camera = camera;
            _outputFolder = outputFolder;
            
            if (_camera != null)
            {
                _camera.PhotoCaptured += OnPhotoCaptur

;
                _camera.CameraDisconnected += OnCameraDisconnected;
            }
        }
        
        #endregion
        
        #region Public Methods
        
        public async Task StartCapture()
        {
            if (_isActive || _camera == null) return;
            
            _isActive = true;
            _capturedPhotoPaths.Clear();
            _currentPhotoIndex = 0;
            
            UpdateStatus("Initializing", 0);
            
            // Get current template if available
            _currentTemplate = _templateService.GetCurrentTemplate();
            _currentEvent = _templateService.GetCurrentEvent();
            
            if (_currentTemplate != null)
            {
                _totalPhotosNeeded = _currentTemplate.PlaceholderCount;
            }
            else
            {
                _totalPhotosNeeded = 1; // Single photo mode
            }
            
            // Start countdown
            StartCountdown();
        }
        
        public async Task StopCapture()
        {
            if (!_isActive) return;
            
            _isActive = false;
            _countdownTimer.Stop();
            _sessionTimer.Stop();
            
            CountdownOverlay.Visibility = Visibility.Collapsed;
            UpdateStatus("Cancelled", 0);
        }
        
        public void Cleanup()
        {
            StopCapture().Wait();
            
            if (_camera != null)
            {
                _camera.PhotoCaptured -= OnPhotoCaptured;
                _camera.CameraDisconnected -= OnCameraDisconnected;
            }
            
            _countdownTimer?.Stop();
            _sessionTimer?.Stop();
            _capturedPhotoPaths.Clear();
        }
        
        #endregion
        
        #region Private Methods - Capture Flow
        
        private void StartCountdown()
        {
            _currentCountdown = CountdownSeconds;
            
            if (_currentCountdown <= 0)
            {
                // No countdown, capture immediately
                CapturePhoto();
                return;
            }
            
            CountdownOverlay.Visibility = Visibility.Visible;
            CountdownText.Text = _currentCountdown.ToString();
            
            // Animate countdown number
            AnimateCountdownNumber();
            
            _countdownTimer.Start();
        }
        
        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            _currentCountdown--;
            
            if (_currentCountdown <= 0)
            {
                _countdownTimer.Stop();
                CountdownOverlay.Visibility = Visibility.Collapsed;
                CapturePhoto();
            }
            else
            {
                CountdownText.Text = _currentCountdown.ToString();
                AnimateCountdownNumber();
            }
        }
        
        private void AnimateCountdownNumber()
        {
            var scaleAnimation = new DoubleAnimation
            {
                From = 1.5,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(800),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut }
            };
            
            var opacityAnimation = new DoubleAnimation
            {
                From = 0.5,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(400)
            };
            
            var transform = new ScaleTransform();
            CountdownText.RenderTransform = transform;
            CountdownText.RenderTransformOrigin = new Point(0.5, 0.5);
            
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            CountdownText.BeginAnimation(OpacityProperty, opacityAnimation);
        }
        
        private async void CapturePhoto()
        {
            if (_camera == null || !_isActive) return;
            
            try
            {
                UpdateStatus("Capturing", 50);
                
                // Trigger camera capture
                await Task.Run(() => _camera.CapturePhoto());
                
                // Photo captured event will handle the rest
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}", 0);
                await StopCapture();
            }
        }
        
        private void OnPhotoCaptured(object sender, PhotoCapturedEventArgs e)
        {
            if (!_isActive) return;
            
            Dispatcher.Invoke(() =>
            {
                ProcessCapturedPhoto(e);
            });
        }
        
        private async void ProcessCapturedPhoto(PhotoCapturedEventArgs e)
        {
            try
            {
                // Save photo to output folder
                string fileName = $"Photo_{DateTime.Now:yyyyMMdd_HHmmss}_{_currentPhotoIndex + 1}.jpg";
                string outputPath = Path.Combine(_outputFolder, fileName);
                
                // Copy or move the captured file
                if (File.Exists(e.FileName))
                {
                    File.Copy(e.FileName, outputPath, true);
                    _capturedPhotoPaths.Add(outputPath);
                }
                
                _currentPhotoIndex++;
                UpdateStatus($"Photo {_currentPhotoIndex}/{_totalPhotosNeeded}", 
                            (_currentPhotoIndex * 100) / _totalPhotosNeeded);
                
                // Check if we need more photos
                if (_currentPhotoIndex < _totalPhotosNeeded)
                {
                    // Capture next photo after a brief delay
                    await Task.Delay(1500);
                    StartCountdown();
                }
                else
                {
                    // All photos captured
                    await ProcessCompleteSession();
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error processing photo: {ex.Message}", 0);
                await StopCapture();
            }
        }
        
        private async Task ProcessCompleteSession()
        {
            UpdateStatus("Processing", 80);
            
            string finalOutputPath = null;
            
            try
            {
                if (_currentTemplate != null && _capturedPhotoPaths.Count > 0)
                {
                    // Apply template
                    finalOutputPath = await ApplyTemplate();
                }
                else if (_capturedPhotoPaths.Count == 1)
                {
                    // Single photo, no template
                    finalOutputPath = _capturedPhotoPaths[0];
                }
                
                if (!string.IsNullOrEmpty(finalOutputPath))
                {
                    // Save to database
                    await SaveToDatabase(finalOutputPath);
                    
                    // Print if auto-print is enabled
                    if (AutoPrint)
                    {
                        await PrintPhoto(finalOutputPath);
                    }
                    
                    UpdateStatus("Complete", 100);
                    
                    // Raise completion event
                    OnCaptureCompleted(new PhotoCaptureCompletedEventArgs
                    {
                        Success = true,
                        OutputPath = finalOutputPath,
                        PhotoPaths = _capturedPhotoPaths.ToList(),
                        TemplateUsed = _currentTemplate != null
                    });
                    
                    // Start session timer for auto-clear
                    _sessionTimer.Start();
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}", 0);
                OnCaptureCompleted(new PhotoCaptureCompletedEventArgs
                {
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
            finally
            {
                _isActive = false;
            }
        }
        
        private async Task<string> ApplyTemplate()
        {
            if (_currentTemplate == null || _capturedPhotoPaths.Count == 0)
                return null;
            
            // Use PhotoboothService to apply template
            var templatePath = _currentTemplate.TemplatePath;
            var outputFileName = $"Template_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
            var outputPath = Path.Combine(_outputFolder, outputFileName);
            
            // Apply template (simplified - actual implementation would use canvas)
            await Task.Run(() =>
            {
                _photoboothService.ProcessTemplate(
                    templatePath,
                    _capturedPhotoPaths,
                    outputPath,
                    _currentTemplate.PlaceholderCount
                );
            });
            
            return outputPath;
        }
        
        private async Task SaveToDatabase(string photoPath)
        {
            try
            {
                await _captureService.SavePhotoSession(new PhotoSession
                {
                    EventId = _currentEvent?.Id,
                    TemplateId = _currentTemplate?.Id,
                    PhotoPath = photoPath,
                    CaptureTime = DateTime.Now,
                    Printed = AutoPrint,
                    PhotoPaths = string.Join(";", _capturedPhotoPaths)
                });
            }
            catch (Exception ex)
            {
                // Log error but don't fail the whole process
                System.Diagnostics.Debug.WriteLine($"Database save error: {ex.Message}");
            }
        }
        
        private async Task PrintPhoto(string photoPath)
        {
            if (!File.Exists(photoPath)) return;
            
            try
            {
                UpdateStatus("Printing", 90);
                
                if (ShowPrintConfirmation)
                {
                    // Show print confirmation dialog
                    var result = MessageBox.Show(
                        "Would you like to print this photo?",
                        "Print Photo",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    
                    if (result != MessageBoxResult.Yes) return;
                }
                
                await _printingService.PrintPhotoAsync(photoPath);
                UpdateStatus("Printed", 100);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Print error: {ex.Message}", 0);
            }
        }
        
        #endregion
        
        #region Event Handlers
        
        private async void ModuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isActive)
            {
                await StopCapture();
            }
            else
            {
                await StartCapture();
            }
        }
        
        private void SessionTimer_Tick(object sender, EventArgs e)
        {
            _sessionTimer.Stop();
            ResetSession();
        }
        
        private void OnCameraDisconnected(object sender, DisconnectCameraEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                StopCapture().Wait();
                UpdateStatus("Camera disconnected", 0);
            });
        }
        
        #endregion
        
        #region Helper Methods
        
        private void UpdateStatus(string status, int progress)
        {
            StatusText.Text = status;
            CaptureProgress.Value = progress;
            
            OnStatusChanged(new PhotoStatusEventArgs
            {
                Status = status,
                Progress = progress
            });
        }
        
        private void ResetSession()
        {
            _capturedPhotoPaths.Clear();
            _currentPhotoIndex = 0;
            _currentTemplate = null;
            _currentEvent = null;
            UpdateStatus("Ready", 0);
        }
        
        protected virtual void OnCaptureCompleted(PhotoCaptureCompletedEventArgs e)
        {
            CaptureCompleted?.Invoke(this, e);
        }
        
        protected virtual void OnStatusChanged(PhotoStatusEventArgs e)
        {
            StatusChanged?.Invoke(this, e);
        }
        
        #endregion
    }
    
    #region Event Args
    
    public class PhotoCaptureCompletedEventArgs : EventArgs
    {
        public bool Success { get; set; }
        public string OutputPath { get; set; }
        public List<string> PhotoPaths { get; set; }
        public bool TemplateUsed { get; set; }
        public string ErrorMessage { get; set; }
    }
    
    public class PhotoStatusEventArgs : EventArgs
    {
        public string Status { get; set; }
        public int Progress { get; set; }
    }
    
    public class PhotoSession
    {
        public int? EventId { get; set; }
        public int? TemplateId { get; set; }
        public string PhotoPath { get; set; }
        public DateTime CaptureTime { get; set; }
        public bool Printed { get; set; }
        public string PhotoPaths { get; set; }
    }
    
    #endregion
}