using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CameraControl.Devices;
using Photobooth.Controls;

namespace Photobooth.Services
{
    /// <summary>
    /// Service that handles background selection workflow
    /// Following clean architecture pattern - all business logic here
    /// </summary>
    public class BackgroundSelectionService
    {
        #region Singleton

        private static BackgroundSelectionService _instance;
        private static readonly object _lock = new object();

        public static BackgroundSelectionService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new BackgroundSelectionService();
                        }
                    }
                }
                return _instance;
            }
        }

        private BackgroundSelectionService()
        {
            Initialize();
        }

        #endregion

        #region Private Fields

        private BackgroundSelectionOverlay _currentOverlay;
        private TaskCompletionSource<BackgroundSelectionResult> _selectionTcs;
        private Panel _overlayContainer;

        #endregion

        #region Events

        /// <summary>
        /// Raised when a background is selected
        /// </summary>
        public event EventHandler<BackgroundSelectedEventArgs> BackgroundSelected;

        /// <summary>
        /// Raised when selection is cancelled
        /// </summary>
        public event EventHandler SelectionCancelled;

        /// <summary>
        /// Raised when no background is selected (skip)
        /// </summary>
        public event EventHandler NoBackgroundSelected;

        #endregion

        #region Initialization

        private void Initialize()
        {
            Log.Debug("BackgroundSelectionService initialized");
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Set the container where overlays will be displayed
        /// </summary>
        public void SetOverlayContainer(Panel container)
        {
            _overlayContainer = container;
        }

        /// <summary>
        /// Show background selection overlay and wait for selection
        /// </summary>
        public async Task<BackgroundSelectionResult> ShowSelectionOverlayAsync()
        {
            if (_overlayContainer == null)
            {
                Log.Error("Overlay container not set");
                return new BackgroundSelectionResult { Cancelled = true };
            }

            // Create task completion source for async waiting
            _selectionTcs = new TaskCompletionSource<BackgroundSelectionResult>();

            // Execute on UI thread
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    // Clean up any existing overlay
                    CleanupOverlay();

                    // Create new overlay
                    _currentOverlay = new BackgroundSelectionOverlay();

                    // Wire up events
                    _currentOverlay.CancelRequested += OnCancelRequested;
                    _currentOverlay.BackgroundSelected += OnBackgroundSelected;
                    _currentOverlay.NoBackgroundSelected += OnNoBackgroundSelected;

                    // Add to container
                    AddOverlayToContainer();

                    Log.Debug("Background selection overlay displayed");
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to show background selection overlay: {ex.Message}");
                    _selectionTcs.TrySetResult(new BackgroundSelectionResult { Cancelled = true });
                }
            });

            // Wait for user selection
            return await _selectionTcs.Task;
        }

        /// <summary>
        /// Hide the current selection overlay
        /// </summary>
        public void HideSelectionOverlay()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                CleanupOverlay();
            });
        }

        /// <summary>
        /// Check if background selection should be shown
        /// </summary>
        public bool ShouldShowBackgroundSelection()
        {
            return Properties.Settings.Default.EnableBackgroundRemoval;
        }

        /// <summary>
        /// Get the currently selected background path
        /// </summary>
        public string GetSelectedBackgroundPath()
        {
            return VirtualBackgroundService.Instance.GetDefaultBackgroundPath();
        }

        #endregion

        #region Private Methods

        private void AddOverlayToContainer()
        {
            if (_overlayContainer is Grid grid)
            {
                // Set to span entire grid
                Grid.SetRowSpan(_currentOverlay, Math.Max(1, grid.RowDefinitions.Count));
                Grid.SetColumnSpan(_currentOverlay, Math.Max(1, grid.ColumnDefinitions.Count));
            }

            // Set high z-index
            Panel.SetZIndex(_currentOverlay, 999);

            // Add to container
            _overlayContainer.Children.Add(_currentOverlay);
        }

        private void CleanupOverlay()
        {
            if (_currentOverlay != null)
            {
                // Unwire events
                _currentOverlay.CancelRequested -= OnCancelRequested;
                _currentOverlay.BackgroundSelected -= OnBackgroundSelected;
                _currentOverlay.NoBackgroundSelected -= OnNoBackgroundSelected;

                // Remove from container
                if (_overlayContainer?.Children.Contains(_currentOverlay) == true)
                {
                    _overlayContainer.Children.Remove(_currentOverlay);
                }

                _currentOverlay = null;
            }
        }

        #endregion

        #region Event Handlers

        private void OnCancelRequested(object sender, EventArgs e)
        {
            Log.Debug("Background selection cancelled");

            // Complete the task
            _selectionTcs?.TrySetResult(new BackgroundSelectionResult { Cancelled = true });

            // Raise event
            SelectionCancelled?.Invoke(this, EventArgs.Empty);

            // Cleanup
            HideSelectionOverlay();
        }

        private void OnBackgroundSelected(object sender, BackgroundSelectedForSessionEventArgs e)
        {
            Log.Debug($"Background selected: {e.BackgroundName} from {e.Category}");

            // Save selection
            VirtualBackgroundService.Instance.SetSelectedBackground(e.BackgroundPath);

            // Complete the task
            _selectionTcs?.TrySetResult(new BackgroundSelectionResult
            {
                Selected = true,
                BackgroundPath = e.BackgroundPath,
                BackgroundName = e.BackgroundName,
                Category = e.Category
            });

            // Raise event
            BackgroundSelected?.Invoke(this, new BackgroundSelectedEventArgs
            {
                BackgroundPath = e.BackgroundPath,
                BackgroundName = e.BackgroundName,
                Category = e.Category
            });

            // Cleanup
            HideSelectionOverlay();
        }

        private void OnNoBackgroundSelected(object sender, EventArgs e)
        {
            Log.Debug("No background selected (skip)");

            // Clear any previous selection
            VirtualBackgroundService.Instance.SetSelectedBackground(null);

            // Complete the task
            _selectionTcs?.TrySetResult(new BackgroundSelectionResult
            {
                Skipped = true
            });

            // Raise event
            NoBackgroundSelected?.Invoke(this, EventArgs.Empty);

            // Cleanup
            HideSelectionOverlay();
        }

        #endregion
    }

    #region Result Classes

    /// <summary>
    /// Result of background selection
    /// </summary>
    public class BackgroundSelectionResult
    {
        public bool Selected { get; set; }
        public bool Cancelled { get; set; }
        public bool Skipped { get; set; }
        public string BackgroundPath { get; set; }
        public string BackgroundName { get; set; }
        public string Category { get; set; }
    }

    /// <summary>
    /// Event args for background selection
    /// </summary>
    public class BackgroundSelectedEventArgs : EventArgs
    {
        public string BackgroundPath { get; set; }
        public string BackgroundName { get; set; }
        public string Category { get; set; }
    }

    #endregion
}