using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using CameraControl.Devices;
using CameraControl.Devices.Classes;
using Photobooth.Services;

namespace Photobooth.Controls
{
    /// <summary>
    /// UI control for capture mode selection - follows clean architecture pattern
    /// All business logic is handled by CaptureModesService
    /// </summary>
    public partial class CaptureModesOverlay : UserControl
    {
        private readonly Services.CaptureModesService _captureModesService;
        
        public event EventHandler<Photobooth.Services.CaptureMode> ModeSelected;
        public event EventHandler OverlayClosed;

        public CaptureModesOverlay()
        {
            InitializeComponent();
            _captureModesService = Services.CaptureModesService.Instance;
            InitializeUI();
            SubscribeToServiceEvents();
        }

        #region Initialization - UI Only

        private void InitializeUI()
        {
            UpdateButtonVisibility();
            UpdateButtonDescriptions();
        }

        private void SubscribeToServiceEvents()
        {
            _captureModesService.PropertyChanged += OnServicePropertyChanged;
        }

        #endregion

        #region UI Event Handlers - Routing Only

        private void ModeButton_Click(object sender, RoutedEventArgs e)
        {
            // Route to service
            var button = sender as Button;
            if (button == null) return;

            var modeName = button.Content?.ToString();
            if (Enum.TryParse<Photobooth.Services.CaptureMode>(modeName.Replace(" ", ""), out var mode))
            {
                OnModeSelected(mode);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        #endregion

        #region Service Event Handlers

        private void OnServicePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.PropertyName == nameof(Services.CaptureModesService.EnabledModes))
                {
                    UpdateButtonVisibility();
                }
            });
        }

        #endregion

        #region UI Updates - Simple Property Updates Only

        private void UpdateButtonVisibility()
        {
            var enabledModes = _captureModesService.EnabledModes;

            PhotoModeButton.Visibility = GetModeVisibility(Services.CaptureMode.Photo, enabledModes);
            VideoModeButton.Visibility = GetModeVisibility(Services.CaptureMode.Video, enabledModes);
            BoomerangModeButton.Visibility = GetModeVisibility(Services.CaptureMode.Boomerang, enabledModes);
            GifModeButton.Visibility = GetModeVisibility(Services.CaptureMode.Gif, enabledModes);
            GreenScreenModeButton.Visibility = GetModeVisibility(Services.CaptureMode.GreenScreen, enabledModes);
            AIModeButton.Visibility = GetModeVisibility(Services.CaptureMode.AI, enabledModes);
            FlipbookModeButton.Visibility = GetModeVisibility(Services.CaptureMode.Flipbook, enabledModes);
        }

        private Visibility GetModeVisibility(Photobooth.Services.CaptureMode mode, System.Collections.Generic.List<Services.CaptureModeInfo> enabledModes)
        {
            return enabledModes.Any(m => m.Mode == mode) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateButtonDescriptions()
        {
            // Update descriptions from service configuration
            SetButtonDescription(PhotoModeButton, Services.CaptureMode.Photo);
            SetButtonDescription(VideoModeButton, Services.CaptureMode.Video);
            SetButtonDescription(BoomerangModeButton, Services.CaptureMode.Boomerang);
            SetButtonDescription(GifModeButton, Services.CaptureMode.Gif);
            SetButtonDescription(GreenScreenModeButton, Services.CaptureMode.GreenScreen);
            SetButtonDescription(AIModeButton, Services.CaptureMode.AI);
            SetButtonDescription(FlipbookModeButton, Services.CaptureMode.Flipbook);
        }

        private void SetButtonDescription(Button button, Photobooth.Services.CaptureMode mode)
        {
            // This would update the description text in the button template
            // For now, descriptions are handled by the service
        }

        #endregion

        #region Public Methods - UI Control Only

        public void Show()
        {
            if (MainGrid == null)
            {
                CameraControl.Devices.Log.Error("CaptureModesOverlay.Show: MainGrid is null!");
                return;
            }
            
            CameraControl.Devices.Log.Debug($"CaptureModesOverlay.Show: MainGrid current visibility = {MainGrid.Visibility}");
            
            UpdateButtonVisibility();
            
            CameraControl.Devices.Log.Debug($"CaptureModesOverlay.Show: Setting MainGrid visibility to Visible");
            MainGrid.Visibility = Visibility.Visible;
            
            // Set initial opacity for animation
            MainGrid.Opacity = 0;
            CameraControl.Devices.Log.Debug($"CaptureModesOverlay.Show: MainGrid opacity set to 0");
            
            // Fade in animation
            var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(300)));
            MainGrid.BeginAnimation(Grid.OpacityProperty, fadeIn);
            CameraControl.Devices.Log.Debug($"CaptureModesOverlay.Show: Animation started");
            
            var enabledCount = _captureModesService.EnabledModes.Count;
            CameraControl.Devices.Log.Debug($"CaptureModesOverlay: Shown with {enabledCount} enabled modes");
            
            // Log each enabled mode
            foreach (var mode in _captureModesService.EnabledModes)
            {
                CameraControl.Devices.Log.Debug($"  - {mode.Mode}: Enabled");
            }
            
            // Force UI refresh
            MainGrid.UpdateLayout();
        }

        public void Hide()
        {
            // Fade out animation
            var fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(300)));
            fadeOut.Completed += (s, e) => 
            {
                MainGrid.Visibility = Visibility.Collapsed;
                OverlayClosed?.Invoke(this, EventArgs.Empty);
            };
            MainGrid.BeginAnimation(Grid.OpacityProperty, fadeOut);
            
            CameraControl.Devices.Log.Debug("CaptureModesOverlay: Hidden");
        }

        #endregion

        #region Mode Selection - Delegates to Service

        private async void OnModeSelected(Photobooth.Services.CaptureMode mode)
        {
            CameraControl.Devices.Log.Debug($"CaptureModesOverlay: Mode selected - {mode}");
            
            // Let service handle the business logic
            var success = await _captureModesService.StartCaptureSession(mode);
            
            if (success)
            {
                ModeSelected?.Invoke(this, mode);
                Hide();
            }
            else
            {
                InfoTextBlock.Text = $"Cannot start {mode} mode. Please check settings.";
            }
        }

        #endregion

        #region Cleanup

        public void Dispose()
        {
            if (_captureModesService != null)
            {
                _captureModesService.PropertyChanged -= OnServicePropertyChanged;
            }
        }

        #endregion
    }
}