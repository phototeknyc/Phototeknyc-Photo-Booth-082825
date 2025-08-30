using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    /// <summary>
    /// Service for managing photo retake selection UI and logic
    /// </summary>
    public class RetakeSelectionService
    {
        #region Events
        public event EventHandler<RetakeSelectedEventArgs> RetakeSelected;
        public event EventHandler RetakeSelectionCancelled;
        public event EventHandler<RetakeRequestedEventArgs> RetakeRequested;
        public event EventHandler ShowRetakeSelectionRequested;
        public event EventHandler HideRetakeSelectionRequested;
        public event EventHandler<int> RetakeTimerTick;
        public event EventHandler<RetakePhotoEventArgs> RetakePhotoRequired;
        public event EventHandler RetakeProcessCompleted;
        #endregion

        #region Private Fields
        private ObservableCollection<RetakePhotoItem> _retakePhotos;
        private DispatcherTimer _retakeTimer;
        private int _retakeTimeRemaining;
        private List<string> _currentPhotoPaths;
        private Queue<int> _pendingRetakeIndices;
        private Dictionary<int, string> _retakenPhotos;
        private int _currentRetakeIndex = -1;
        private bool _isRetaking = false;
        private int _totalRetakesCompleted = 0;
        #endregion

        #region Constructor
        public RetakeSelectionService()
        {
            _retakePhotos = new ObservableCollection<RetakePhotoItem>();
            InitializeTimer();
        }
        #endregion

        #region Properties
        public ObservableCollection<RetakePhotoItem> RetakePhotos => _retakePhotos;
        public bool IsRetakeEnabled => Properties.Settings.Default.EnableRetake;
        public int RetakeTimeout => Properties.Settings.Default.RetakeTimeout;
        public bool AllowMultipleRetakes => Properties.Settings.Default.AllowMultipleRetakes;
        #endregion

        #region Public Methods
        /// <summary>
        /// Initialize the retake selection with captured photos
        /// </summary>
        public void InitializeRetakeSelection(List<string> photoPaths)
        {
            _currentPhotoPaths = photoPaths;
            _retakePhotos.Clear();

            for (int i = 0; i < photoPaths.Count; i++)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 400; // Thumbnail size
                    bitmap.UriSource = new Uri(photoPaths[i], UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();

                    _retakePhotos.Add(new RetakePhotoItem
                    {
                        Image = bitmap,
                        Label = $"Photo {i + 1}",
                        PhotoIndex = i,
                        FilePath = photoPaths[i],
                        MarkedForRetake = false
                    });
                }
                catch (Exception ex)
                {
                    // Log.Error($"RetakeSelectionService: Error loading photo for retake: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"RetakeSelectionService: Error loading photo for retake: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Request to show the retake selection UI
        /// </summary>
        public void RequestRetakeSelection()
        {
            if (!IsRetakeEnabled)
            {
                // Skip retake if disabled
                SkipRetake();
                return;
            }

            // Start the timer
            StartRetakeTimer();

            // Show the UI
            ShowRetakeSelectionRequested?.Invoke(this, EventArgs.Empty);
        }
        
        /// <summary>
        /// Show retake selection UI again after retaking (doesn't reinitialize)
        /// </summary>
        public void ShowRetakeSelectionAgain()
        {
            // Restart the timer
            StartRetakeTimer();
            
            // Show the UI with updated photos
            ShowRetakeSelectionRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Toggle retake status for a photo
        /// </summary>
        public void TogglePhotoRetake(int photoIndex)
        {
            var photo = _retakePhotos.FirstOrDefault(p => p.PhotoIndex == photoIndex);
            if (photo != null)
            {
                photo.MarkedForRetake = !photo.MarkedForRetake;
                System.Diagnostics.Debug.WriteLine($"RetakeSelectionService: Toggled photo {photoIndex + 1} - MarkedForRetake = {photo.MarkedForRetake}");

                // If multiple retakes not allowed, unmark others
                if (!AllowMultipleRetakes && photo.MarkedForRetake)
                {
                    foreach (var otherPhoto in _retakePhotos.Where(p => p.PhotoIndex != photoIndex))
                    {
                        otherPhoto.MarkedForRetake = false;
                        System.Diagnostics.Debug.WriteLine($"RetakeSelectionService: Unmarked photo {otherPhoto.PhotoIndex + 1} (single selection mode)");
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"RetakeSelectionService: Photo with index {photoIndex} not found in collection");
            }
        }

        /// <summary>
        /// Process selected retakes
        /// </summary>
        public void ProcessRetakes()
        {
            StopTimer();

            var photosToRetake = _retakePhotos
                .Where(p => p.MarkedForRetake)
                .Select(p => p.PhotoIndex)
                .ToList();

            if (photosToRetake.Any())
            {
                // Initialize retake process
                _pendingRetakeIndices = new Queue<int>(photosToRetake);
                _retakenPhotos = new Dictionary<int, string>();
                _isRetaking = true;
                
                // Hide the selection UI
                HideRetakeSelectionRequested?.Invoke(this, EventArgs.Empty);
                
                // Start processing retakes one by one
                ProcessNextRetake();
            }
            else
            {
                // No photos selected, continue
                HideRetakeSelectionRequested?.Invoke(this, EventArgs.Empty);
                ContinueWithoutRetake();
            }
        }
        
        /// <summary>
        /// Process the next retake in queue
        /// </summary>
        private void ProcessNextRetake()
        {
            if (_pendingRetakeIndices == null || _pendingRetakeIndices.Count == 0)
            {
                // All retakes completed
                System.Diagnostics.Debug.WriteLine($"RetakeSelectionService: All retakes completed. Total retaken: {_retakenPhotos?.Count ?? 0}");
                CompleteRetakeProcess();
                return;
            }
            
            _currentRetakeIndex = _pendingRetakeIndices.Dequeue();
            System.Diagnostics.Debug.WriteLine($"RetakeSelectionService: Processing retake for photo {_currentRetakeIndex + 1}. Remaining in queue: {_pendingRetakeIndices.Count}");
            
            // Fire event to request photo capture for this specific index
            RetakePhotoRequired?.Invoke(this, new RetakePhotoEventArgs 
            { 
                PhotoIndex = _currentRetakeIndex,
                PhotoNumber = _currentRetakeIndex + 1
            });
        }
        
        /// <summary>
        /// Handle when a retake photo has been captured
        /// </summary>
        public void OnRetakePhotoCaptured(int photoIndex, string newPhotoPath)
        {
            System.Diagnostics.Debug.WriteLine($"RetakeSelectionService: OnRetakePhotoCaptured called for photo {photoIndex + 1}, path: {newPhotoPath}");
            
            if (!_isRetaking || photoIndex != _currentRetakeIndex)
            {
                System.Diagnostics.Debug.WriteLine($"RetakeSelectionService: Ignoring capture - IsRetaking={_isRetaking}, Expected={_currentRetakeIndex}, Got={photoIndex}");
                return;
            }
            
            // Store the retaken photo
            _retakenPhotos[photoIndex] = newPhotoPath;
            System.Diagnostics.Debug.WriteLine($"RetakeSelectionService: Stored retaken photo {photoIndex + 1}. Total retaken so far: {_retakenPhotos.Count}");
            
            // Update the current photo paths
            if (photoIndex < _currentPhotoPaths.Count)
            {
                _currentPhotoPaths[photoIndex] = newPhotoPath;
            }
            
            // Update the display photo
            var photoItem = _retakePhotos.FirstOrDefault(p => p.PhotoIndex == photoIndex);
            if (photoItem != null)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 400;
                    bitmap.UriSource = new Uri(newPhotoPath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    
                    photoItem.Image = bitmap;
                    photoItem.FilePath = newPhotoPath;
                    photoItem.MarkedForRetake = false;
                }
                catch { }
            }
            
            // Process next retake
            ProcessNextRetake();
        }
        
        /// <summary>
        /// Complete the retake process
        /// </summary>
        private void CompleteRetakeProcess()
        {
            System.Diagnostics.Debug.WriteLine("===== RETAKE PROCESS COMPLETING =====");
            System.Diagnostics.Debug.WriteLine($"RetakeSelectionService.CompleteRetakeProcess: Starting completion");
            System.Diagnostics.Debug.WriteLine($"  - Total retakes completed: {_retakenPhotos?.Count ?? 0}");
            System.Diagnostics.Debug.WriteLine($"  - Current photo paths count: {_currentPhotoPaths?.Count ?? 0}");
            System.Diagnostics.Debug.WriteLine($"  - AllowMultipleRetakes: {AllowMultipleRetakes}");
            System.Diagnostics.Debug.WriteLine($"  - Total retakes so far: {_totalRetakesCompleted}");
            
            _isRetaking = false;
            _currentRetakeIndex = -1;
            _totalRetakesCompleted += _retakenPhotos.Count;
            
            // Check if we should allow more retakes or auto-complete
            if (AllowMultipleRetakes)
            {
                // Multiple retakes allowed - show selection again
                System.Diagnostics.Debug.WriteLine("RetakeSelectionService: Multiple retakes allowed, showing selection again");
                System.Diagnostics.Debug.WriteLine("User can select more photos to retake or click Skip to continue");
                
                // Fire completion event to update UI state
                System.Diagnostics.Debug.WriteLine("RetakeSelectionService: Firing RetakeProcessCompleted event");
                RetakeProcessCompleted?.Invoke(this, EventArgs.Empty);
                
                // Show selection again for additional retakes
                ShowRetakeSelectionAgain();
            }
            else
            {
                // Single retake mode - auto-complete after retake
                System.Diagnostics.Debug.WriteLine("RetakeSelectionService: Single retake mode - auto-completing");
                System.Diagnostics.Debug.WriteLine("AllowMultipleRetakes is disabled, proceeding to composition");
                
                // Fire completion event
                System.Diagnostics.Debug.WriteLine("RetakeSelectionService: Firing RetakeProcessCompleted event");
                RetakeProcessCompleted?.Invoke(this, EventArgs.Empty);
                
                // Add a small delay to allow photo processing to complete
                // This ensures ProcessCapturedPhotoAsync has time to replace the photo
                System.Diagnostics.Debug.WriteLine("RetakeSelectionService: Adding delay for photo processing before auto-completion");
                
                var delayTimer = new DispatcherTimer();
                delayTimer.Interval = TimeSpan.FromMilliseconds(2000); // Need more time for photo processing + beauty mode
                delayTimer.Tick += (s, args) =>
                {
                    delayTimer.Stop();
                    
                    // Auto-complete - continue with session (same as clicking Skip)
                    System.Diagnostics.Debug.WriteLine("RetakeSelectionService: Firing RetakeSelected event with RetakeCompleted=true after delay");
                    RetakeSelected?.Invoke(this, new RetakeSelectedEventArgs 
                    { 
                        RetakeCompleted = true,
                        PhotoPaths = _currentPhotoPaths
                    });
                    
                    System.Diagnostics.Debug.WriteLine("RetakeSelectionService: Auto-completion done");
                };
                delayTimer.Start();
            }
            
            // Clear temporary state
            _pendingRetakeIndices = null;
            _retakenPhotos = null;
            System.Diagnostics.Debug.WriteLine("===== RETAKE PROCESS COMPLETED - WAITING FOR USER ACTION =====");
        }

        /// <summary>
        /// Skip retake and continue
        /// </summary>
        public void SkipRetake()
        {
            StopTimer();
            ContinueWithoutRetake();
            HideRetakeSelectionRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Continue without retaking any photos
        /// </summary>
        public void ContinueWithoutRetake()
        {
            StopTimer();
            RetakeSelected?.Invoke(this, new RetakeSelectedEventArgs 
            { 
                RetakeCompleted = true,
                PhotoPaths = _currentPhotoPaths
            });
        }

        /// <summary>
        /// Update photos after retake
        /// </summary>
        public void UpdatePhotosAfterRetake(Dictionary<int, string> updatedPhotos)
        {
            foreach (var update in updatedPhotos)
            {
                if (update.Key < _currentPhotoPaths.Count)
                {
                    _currentPhotoPaths[update.Key] = update.Value;

                    // Update the display
                    var photoItem = _retakePhotos.FirstOrDefault(p => p.PhotoIndex == update.Key);
                    if (photoItem != null)
                    {
                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.DecodePixelWidth = 400;
                            bitmap.UriSource = new Uri(update.Value, UriKind.Absolute);
                            bitmap.EndInit();
                            bitmap.Freeze();

                            photoItem.Image = bitmap;
                            photoItem.FilePath = update.Value;
                            photoItem.MarkedForRetake = false;
                        }
                        catch { }
                    }
                }
            }

        }

        /// <summary>
        /// Reset the service
        /// </summary>
        public void Reset()
        {
            StopTimer();
            _retakePhotos.Clear();
            _currentPhotoPaths = null;
            _pendingRetakeIndices = null;
            _retakenPhotos = null;
            _currentRetakeIndex = -1;
            _isRetaking = false;
            _totalRetakesCompleted = 0;
        }
        #endregion

        #region Private Methods
        private void InitializeTimer()
        {
            _retakeTimer = new DispatcherTimer();
            _retakeTimer.Interval = TimeSpan.FromSeconds(1);
            _retakeTimer.Tick += RetakeTimer_Tick;
        }

        private void StartRetakeTimer()
        {
            _retakeTimeRemaining = RetakeTimeout;
            _retakeTimer.Start();
            RetakeTimerTick?.Invoke(this, _retakeTimeRemaining);
        }

        public void StopTimer()
        {
            _retakeTimer.Stop();
        }

        private void RetakeTimer_Tick(object sender, EventArgs e)
        {
            _retakeTimeRemaining--;
            RetakeTimerTick?.Invoke(this, _retakeTimeRemaining);

            if (_retakeTimeRemaining <= 0)
            {
                // Timeout - continue without retake
                SkipRetake();
            }
        }
        #endregion
    }

    #region Supporting Classes
    /// <summary>
    /// Retake photo item for display
    /// </summary>
    public class RetakePhotoItem : INotifyPropertyChanged
    {
        private bool _markedForRetake;
        private BitmapImage _image;

        public BitmapImage Image 
        { 
            get => _image;
            set
            {
                _image = value;
                OnPropertyChanged();
            }
        }
        
        public string Label { get; set; }
        public int PhotoIndex { get; set; }
        public string FilePath { get; set; }

        public bool MarkedForRetake
        {
            get => _markedForRetake;
            set
            {
                _markedForRetake = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Event args for retake selection
    /// </summary>
    public class RetakeSelectedEventArgs : EventArgs
    {
        public bool RetakeCompleted { get; set; }
        public List<string> PhotoPaths { get; set; }
    }

    /// <summary>
    /// Event args for retake request
    /// </summary>
    public class RetakeRequestedEventArgs : EventArgs
    {
        public List<int> PhotoIndices { get; set; }
    }
    
    /// <summary>
    /// Event args for single photo retake
    /// </summary>
    public class RetakePhotoEventArgs : EventArgs
    {
        public int PhotoIndex { get; set; }
        public int PhotoNumber { get; set; }
    }
    #endregion
}