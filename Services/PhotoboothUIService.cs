using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CameraControl.Devices;

namespace Photobooth.Services
{
    /// <summary>
    /// Clean service that handles all photobooth UI updates and notifications
    /// Decouples UI operations from business logic
    /// </summary>
    public class PhotoboothUIService
    {
        #region Events
        public event EventHandler<UIUpdateEventArgs> UIUpdateRequested;
        public event EventHandler<ThumbnailEventArgs> ThumbnailRequested;
        public event EventHandler<ThumbnailEventArgs> GalleryThumbnailRequested;
        public event EventHandler<StatusEventArgs> StatusUpdateRequested;
        public event EventHandler<ImageDisplayEventArgs> ImageDisplayRequested;
        public event EventHandler<GifDisplayEventArgs> GifDisplayRequested;
        #endregion

        #region UI State
        private readonly Dictionary<string, UIElementState> _elementStates;
        #endregion

        public PhotoboothUIService()
        {
            _elementStates = new Dictionary<string, UIElementState>();
        }

        /// <summary>
        /// Update status text
        /// </summary>
        public void UpdateStatus(string status)
        {
            try
            {
                Log.Debug($"PhotoboothUIService: Updating status - {status}");
                StatusUpdateRequested?.Invoke(this, new StatusEventArgs { Status = status });
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to update status: {ex.Message}");
            }
        }

        /// <summary>
        /// Update photo counter display
        /// </summary>
        public void UpdatePhotoCounter(int current, int total)
        {
            try
            {
                var counterText = $"{current} / {total}";
                Log.Debug($"PhotoboothUIService: Updating photo counter - {counterText}");
                
                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "photoCountText",
                    Property = "Text",
                    Value = counterText,
                    Visibility = Visibility.Visible
                });
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to update photo counter: {ex.Message}");
            }
        }

        /// <summary>
        /// Show countdown overlay
        /// </summary>
        public void ShowCountdown(int countdownValue, int totalSeconds)
        {
            try
            {
                Log.Debug($"PhotoboothUIService: Showing countdown - {countdownValue}");
                
                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "countdownText",
                    Property = "Text",
                    Value = countdownValue.ToString()
                });

                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "countdownSecondsDisplay",
                    Property = "Text",
                    Value = $"{countdownValue}s"
                });

                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "countdownOverlay",
                    Property = "Visibility",
                    Value = Visibility.Visible
                });
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to show countdown: {ex.Message}");
            }
        }

        /// <summary>
        /// Hide countdown overlay
        /// </summary>
        public void HideCountdown()
        {
            try
            {
                Log.Debug("PhotoboothUIService: Hiding countdown");
                
                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "countdownOverlay",
                    Property = "Visibility",
                    Value = Visibility.Collapsed
                });
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to hide countdown: {ex.Message}");
            }
        }

        /// <summary>
        /// Show start button
        /// </summary>
        public void ShowStartButton()
        {
            try
            {
                Log.Debug("PhotoboothUIService: Showing start button");
                
                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "startButtonOverlay",
                    Property = "Visibility",
                    Value = Visibility.Visible
                });
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to show start button: {ex.Message}");
            }
        }

        /// <summary>
        /// Hide start button
        /// </summary>
        public void HideStartButton()
        {
            try
            {
                Log.Debug("PhotoboothUIService: Hiding start button");
                
                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "startButtonOverlay",
                    Property = "Visibility",
                    Value = Visibility.Collapsed
                });
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to hide start button: {ex.Message}");
            }
        }

        /// <summary>
        /// Show session controls (stop, print, etc.)
        /// </summary>
        public void ShowSessionControls()
        {
            try
            {
                Log.Debug("PhotoboothUIService: Showing session controls");
                
                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "stopSessionButton",
                    Property = "Visibility",
                    Value = Visibility.Visible
                });
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to show session controls: {ex.Message}");
            }
        }

        /// <summary>
        /// Show video recording controls
        /// </summary>
        public void ShowVideoRecordingControls()
        {
            try
            {
                Log.Debug("PhotoboothUIService: Showing video recording controls");
                
                // Show recording indicator
                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "recordingIndicator",
                    Property = "Visibility",
                    Value = Visibility.Visible
                });
                
                // Update status for video mode
                UpdateStatus("Recording video...");
                
                // Hide photo-specific controls
                HideCountdown();
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to show video recording controls: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Show completion controls (share, print, home)
        /// </summary>
        public void ShowCompletionControls()
        {
            try
            {
                Log.Debug("PhotoboothUIService: Showing completion controls");
                
                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "shareButton",
                    Property = "Visibility",
                    Value = Visibility.Visible
                });

                // Only show print button if enabled in settings
                bool showPrintButton = Properties.Settings.Default.ShowPrintButton && Properties.Settings.Default.EnablePrinting;
                Log.Debug($"PhotoboothUIService: ShowPrintButton setting: {Properties.Settings.Default.ShowPrintButton}, EnablePrinting: {Properties.Settings.Default.EnablePrinting}, Will show: {showPrintButton}");
                
                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "printButton",
                    Property = "Visibility",
                    Value = showPrintButton ? Visibility.Visible : Visibility.Collapsed
                });

                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "homeButton",
                    Property = "Visibility",
                    Value = Visibility.Visible
                });
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to show completion controls: {ex.Message}");
            }
        }

        /// <summary>
        /// Add photo thumbnail to the UI
        /// </summary>
        public void AddPhotoThumbnail(string photoPath, int photoIndex)
        {
            AddPhotoThumbnail(photoPath, photoIndex, false);
        }
        
        /// <summary>
        /// Add photo thumbnail to the UI with option to suppress events
        /// </summary>
        public void AddPhotoThumbnail(string photoPath, int photoIndex, bool suppressEvents)
        {
            try
            {
                if (string.IsNullOrEmpty(photoPath) || !File.Exists(photoPath))
                {
                    Log.Error($"PhotoboothUIService: Invalid photo path for thumbnail: {photoPath}");
                    return;
                }

                Log.Debug($"PhotoboothUIService: Adding photo thumbnail - {photoPath} (suppressEvents: {suppressEvents})");
                
                if (!suppressEvents)
                {
                    ThumbnailRequested?.Invoke(this, new ThumbnailEventArgs
                    {
                        PhotoPath = photoPath,
                        PhotoIndex = photoIndex
                    });
                }
                else
                {
                    // For gallery loading - directly call UI manipulation
                    GalleryThumbnailRequested?.Invoke(this, new ThumbnailEventArgs
                    {
                        PhotoPath = photoPath,
                        PhotoIndex = photoIndex
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to add photo thumbnail: {ex.Message}");
            }
        }

        /// <summary>
        /// Update camera connection status
        /// </summary>
        public void UpdateCameraStatus(bool isConnected, string deviceName = null)
        {
            try
            {
                string status = isConnected
                    ? $"Connected: {deviceName ?? "Camera"}"
                    : "Camera disconnected";

                Log.Debug($"PhotoboothUIService: Updating camera status - {status}");

                // Update status indicator color
                var statusColor = isConnected ? Brushes.Green : Brushes.Red;
                
                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "cameraStatusIndicator",
                    Property = "Background",
                    Value = statusColor
                });

                UpdateStatus(status);
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to update camera status: {ex.Message}");
            }
        }

        /// <summary>
        /// Update live view image
        /// </summary>
        public void UpdateLiveViewImage(BitmapSource imageSource)
        {
            try
            {
                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "liveViewImage",
                    Property = "Source",
                    Value = imageSource
                });
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to update live view image: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset UI to initial state
        /// </summary>
        public void ResetToInitialState()
        {
            try
            {
                Log.Debug("PhotoboothUIService: Resetting UI to initial state");

                HideCountdown();
                ShowStartButton();

                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "photoCountText",
                    Property = "Visibility",
                    Value = Visibility.Collapsed
                });

                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "stopSessionButton",
                    Property = "Visibility",
                    Value = Visibility.Collapsed
                });

                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "shareButton",
                    Property = "Visibility",
                    Value = Visibility.Collapsed
                });

                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "printButton",
                    Property = "Visibility",
                    Value = Visibility.Collapsed
                });

                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "homeButton",
                    Property = "Visibility",
                    Value = Visibility.Collapsed
                });

                UpdateStatus("Touch START to begin");
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to reset UI state: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear photo thumbnails container
        /// </summary>
        public void ClearPhotoThumbnails()
        {
            try
            {
                Log.Debug("PhotoboothUIService: Clearing photo thumbnails");
                
                UIUpdateRequested?.Invoke(this, new UIUpdateEventArgs
                {
                    ElementName = "photosContainer",
                    Property = "Clear",
                    Value = null
                });
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to clear photo thumbnails: {ex.Message}");
            }
        }

        /// <summary>
        /// Display an animated GIF in the live view area
        /// </summary>
        public void DisplayGifInLiveView(string gifPath)
        {
            try
            {
                if (string.IsNullOrEmpty(gifPath) || !File.Exists(gifPath))
                {
                    Log.Error($"PhotoboothUIService: Invalid GIF path: {gifPath}");
                    return;
                }

                Log.Debug($"PhotoboothUIService: Requesting GIF display in live view: {gifPath}");
                
                GifDisplayRequested?.Invoke(this, new GifDisplayEventArgs
                {
                    GifPath = gifPath,
                    DisplayLocation = "LiveView"
                });
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to display GIF in live view: {ex.Message}");
            }
        }

        /// <summary>
        /// Add a GIF thumbnail to the photo strip
        /// </summary>
        public void AddGifThumbnail(string gifPath)
        {
            AddGifThumbnail(gifPath, false);
        }
        
        /// <summary>
        /// Add a GIF thumbnail to the photo strip with option to suppress events
        /// </summary>
        public void AddGifThumbnail(string gifPath, bool suppressEvents)
        {
            try
            {
                if (string.IsNullOrEmpty(gifPath) || !File.Exists(gifPath))
                {
                    Log.Error($"PhotoboothUIService: Invalid GIF path for thumbnail: {gifPath}");
                    return;
                }

                Log.Debug($"PhotoboothUIService: Adding GIF thumbnail to photo strip: {gifPath} (suppressEvents: {suppressEvents})");
                
                if (!suppressEvents)
                {
                    ThumbnailRequested?.Invoke(this, new ThumbnailEventArgs
                    {
                        ImagePath = gifPath,
                        ThumbnailType = "GIF",
                        PhotoIndex = -1 // Special index for GIFs
                    });
                }
                else
                {
                    // For gallery loading - directly call UI manipulation
                    GalleryThumbnailRequested?.Invoke(this, new ThumbnailEventArgs
                    {
                        ImagePath = gifPath,
                        ThumbnailType = "GIF",
                        PhotoIndex = -1
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to add GIF thumbnail: {ex.Message}");
            }
        }

        /// <summary>
        /// Add composed image thumbnail to photo strip
        /// </summary>
        public void AddComposedThumbnail(string composedPath)
        {
            AddComposedThumbnail(composedPath, false);
        }
        
        /// <summary>
        /// Add composed image thumbnail to photo strip with option to suppress events
        /// </summary>
        public void AddComposedThumbnail(string composedPath, bool suppressEvents)
        {
            try
            {
                if (string.IsNullOrEmpty(composedPath) || !File.Exists(composedPath))
                {
                    Log.Error($"PhotoboothUIService: Invalid composed image path for thumbnail: {composedPath}");
                    return;
                }

                Log.Debug($"PhotoboothUIService: Adding composed image thumbnail to photo strip: {composedPath} (suppressEvents: {suppressEvents})");
                
                if (!suppressEvents)
                {
                    ThumbnailRequested?.Invoke(this, new ThumbnailEventArgs
                    {
                        ImagePath = composedPath,
                        ThumbnailType = "COMPOSED",
                        PhotoIndex = -2 // Special index for composed images
                    });
                }
                else
                {
                    // For gallery loading - directly call UI manipulation
                    GalleryThumbnailRequested?.Invoke(this, new ThumbnailEventArgs
                    {
                        ImagePath = composedPath,
                        ThumbnailType = "COMPOSED",
                        PhotoIndex = -2
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to add composed thumbnail: {ex.Message}");
            }
        }

        /// <summary>
        /// Display an image in the live view area
        /// </summary>
        public void DisplayImage(string imagePath)
        {
            try
            {
                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                {
                    Log.Error($"PhotoboothUIService: Invalid image path: {imagePath}");
                    return;
                }

                Log.Debug($"PhotoboothUIService: Requesting image display in live view: {imagePath}");
                
                ImageDisplayRequested?.Invoke(this, new ImageDisplayEventArgs
                {
                    ImagePath = imagePath,
                    DisplayLocation = "LiveView"
                });
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothUIService: Failed to display image: {ex.Message}");
            }
        }
    }

    #region Event Args Classes
    public class UIUpdateEventArgs : EventArgs
    {
        public string ElementName { get; set; }
        public string Property { get; set; }
        public object Value { get; set; }
        public Visibility? Visibility { get; set; }
    }

    public class ThumbnailEventArgs : EventArgs
    {
        public string PhotoPath { get; set; }
        public string ImagePath { get; set; } // For compatibility
        public int PhotoIndex { get; set; }
        public string ThumbnailType { get; set; } = "Photo"; // "Photo" or "GIF"
    }

    public class ImageDisplayEventArgs : EventArgs
    {
        public string ImagePath { get; set; }
        public string DisplayLocation { get; set; } // "LiveView", etc.
    }

    public class GifDisplayEventArgs : EventArgs
    {
        public string GifPath { get; set; }
        public string DisplayLocation { get; set; } // "LiveView", etc.
    }

    public class UIElementState
    {
        public Visibility Visibility { get; set; }
        public object Value { get; set; }
        public DateTime LastUpdated { get; set; }
    }
    #endregion

}