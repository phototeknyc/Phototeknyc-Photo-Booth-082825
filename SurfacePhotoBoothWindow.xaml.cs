using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Photobooth.Pages;
using Photobooth.Windows;
using Microsoft.Win32;

namespace Photobooth
{
    public partial class SurfacePhotoBoothWindow : Window
    {
        private DispatcherTimer _statusTimer;
        private bool _isPortrait = false;
        private Frame _hiddenFrame;
        private Stack<string> _navigationStack = new Stack<string>();
        private bool _isNavigating = false;
        private bool _isLocked = false;
        private string _pinCode = "1234"; // Default PIN
        
        // Surface Pro specific dimensions
        private const double SURFACE_PRO_WIDTH = 2736;
        private const double SURFACE_PRO_HEIGHT = 1824;
        private const double SURFACE_PRO_ASPECT = 3.0 / 2.0;
        
        // DPI awareness
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
        
        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);
        
        // Fullscreen APIs
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        
        private const int GWL_STYLE = -16;
        private const int WS_SYSMENU = 0x80000;
        private const int WS_MAXIMIZEBOX = 0x10000;
        private const int WS_MINIMIZEBOX = 0x20000;
        
        public SurfacePhotoBoothWindow()
        {
            // Set DPI awareness before anything else
            SetupDpiAwareness();
            
            InitializeComponent();
            
            // Setup Surface Pro optimizations
            OptimizeForSurfacePro();
            
            // Enable touch support
            SetupTouchSupport();
            
            // Setup responsive design
            SetupResponsiveDesign();
            
            // Setup status updates
            SetupStatusTimer();
            
            // Handle orientation changes
            this.SizeChanged += Window_SizeChanged;
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            
            // Apply initial animations
            this.Loaded += Window_Loaded;
            
            // Add keyboard shortcuts
            this.PreviewKeyDown += Window_PreviewKeyDown;
            
            // Handle window state changes to fix black screen on restore
            this.StateChanged += Window_StateChanged;
        }
        
        private void SetupDpiAwareness()
        {
            try
            {
                // Try Windows 10 method first
                SetProcessDpiAwareness(2); // PerMonitorV2
            }
            catch
            {
                // Fallback to older method
                if (Environment.OSVersion.Version.Major >= 6)
                {
                    SetProcessDPIAware();
                }
            }
            
            // Force high-quality rendering
            RenderOptions.ProcessRenderMode = RenderMode.Default;
        }
        
        private void OptimizeForSurfacePro()
        {
            // Check if running on Surface Pro or high-DPI device
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            var aspectRatio = Math.Max(screenWidth, screenHeight) / Math.Min(screenWidth, screenHeight);
            
            // Optimize for Surface Pen and touch
            Stylus.SetIsPressAndHoldEnabled(this, false);
            Stylus.SetIsFlicksEnabled(this, false);
            Stylus.SetIsTapFeedbackEnabled(this, false);
            Stylus.SetIsTouchFeedbackEnabled(this, false);
            
            // Apply DPI scaling
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var dpiX = source.CompositionTarget.TransformToDevice.M11;
                var dpiY = source.CompositionTarget.TransformToDevice.M22;
                
                // Adjust UI scale for high DPI
                if (dpiX > 1.5 || dpiY > 1.5)
                {
                    Resources["TouchTargetSize"] = 72.0;
                    Resources["ButtonMinHeight"] = 64.0;
                    Resources["FontSizeNormal"] = 18.0;
                    Resources["FontSizeLarge"] = 24.0;
                }
            }
        }
        
        private void SetupTouchSupport()
        {
            // Enable manipulation events for touch gestures
            this.ManipulationStarting += OnManipulationStarting;
            this.ManipulationDelta += OnManipulationDelta;
            this.ManipulationCompleted += OnManipulationCompleted;
            this.IsManipulationEnabled = true;
            
            // Touch feedback
            this.TouchDown += OnTouchDown;
            this.TouchUp += OnTouchUp;
        }
        
        private void SetupResponsiveDesign()
        {
            // Get current screen dimensions
            CheckOrientation();
            
            // Set minimum sizes for touch
            this.MinWidth = 1024;
            this.MinHeight = 768;
        }
        
        private void SetupStatusTimer()
        {
            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _statusTimer.Tick += UpdateStatus;
            _statusTimer.Start();
        }
        
        private void UpdateStatus(object sender, EventArgs e)
        {
            // Update time or other status information
            // This could be connected to your camera service
            UpdateCameraStatus();
            UpdatePhotoCount();
        }
        
        public void UpdateCameraStatus(string cameraName = null, int batteryLevel = -1, bool isConnected = true)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (CameraStatusIndicator != null)
                {
                    CameraStatusIndicator.Fill = new SolidColorBrush(isConnected ? Colors.LimeGreen : Colors.Red);
                }
                
                if (CameraStatusText != null && !string.IsNullOrEmpty(cameraName))
                {
                    CameraStatusText.Text = $"📷 {cameraName}";
                }
                
                if (BatteryStatusText != null && batteryLevel >= 0)
                {
                    BatteryStatusText.Text = $"🔋 {batteryLevel}%";
                }
            }));
        }
        
        private void UpdatePhotoCount()
        {
            // This would connect to your photo service to get actual count
            if (PhotoCountText != null)
            {
                // Example: Get today's photo count from service
                var photoCount = GetTodaysPhotoCount();
                PhotoCountText.Text = $"💾 {photoCount:N0} Photos Today";
            }
        }
        
        private int GetTodaysPhotoCount()
        {
            // TODO: Implement actual photo counting from your service
            return 1234; // Placeholder
        }
        
        private void CheckOrientation()
        {
            var width = this.ActualWidth > 0 ? this.ActualWidth : SystemParameters.PrimaryScreenWidth;
            var height = this.ActualHeight > 0 ? this.ActualHeight : SystemParameters.PrimaryScreenHeight;
            
            bool newIsPortrait = height > width;
            
            if (newIsPortrait != _isPortrait)
            {
                _isPortrait = newIsPortrait;
                AdjustLayoutForOrientation();
            }
        }
        
        private void AdjustLayoutForOrientation()
        {
            if (_isPortrait)
            {
                // Portrait mode - stack navigation cards vertically
                if (NavigationGrid != null)
                {
                    var wrapPanel = NavigationGrid.ItemsPanel.LoadContent() as WrapPanel;
                    if (wrapPanel != null)
                    {
                        wrapPanel.Orientation = Orientation.Vertical;
                    }
                }
            }
            else
            {
                // Landscape mode - arrange navigation cards horizontally
                if (NavigationGrid != null)
                {
                    var wrapPanel = NavigationGrid.ItemsPanel.LoadContent() as WrapPanel;
                    if (wrapPanel != null)
                    {
                        wrapPanel.Orientation = Orientation.Horizontal;
                    }
                }
            }
            
            // Animate the transition
            var animation = new DoubleAnimation
            {
                From = 0.9,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new PowerEase { EasingMode = EasingMode.EaseOut }
            };
            
            this.BeginAnimation(OpacityProperty, animation);
        }
        
        #region Touch Gesture Handlers
        
        private void OnManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.ManipulationContainer = this;
            e.Mode = ManipulationModes.All;
        }
        
        private void OnManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            // Handle pinch, zoom, swipe gestures here
        }
        
        private void OnManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
            // Complete gesture handling
        }
        
        private void OnTouchDown(object sender, TouchEventArgs e)
        {
            // Visual feedback for touch
            var point = e.GetTouchPoint(this);
            CreateTouchRipple(point.Position);
        }
        
        private void OnTouchUp(object sender, TouchEventArgs e)
        {
            // Handle touch up
        }
        
        private void CreateTouchRipple(Point position)
        {
            // Create a visual ripple effect at touch point
            // This provides feedback for touch interactions
        }
        
        #endregion
        
        #region Navigation Event Handlers
        
        private void NavigateToTemplates_Click(object sender, MouseButtonEventArgs e)
        {
            NavigateToPage(MainPage.Instance, "Templates");
        }
        
        private void NavigateToCamera_Click(object sender, MouseButtonEventArgs e)
        {
            NavigateToPage(new Camera(), "Camera");
        }
        
        private void NavigateToPhotoBooth_Click(object sender, MouseButtonEventArgs e)
        {
            // Navigate to Event Selection first
            NavigateToPage(new Pages.EventSelectionPage(), "Select Event");
        }
        
        private void NavigateToEvents_Click(object sender, MouseButtonEventArgs e)
        {
            // For now, still open as dialog but could be converted to page
            var eventsBrowser = new EventBrowserWindow();
            eventsBrowser.Owner = this;
            eventsBrowser.ShowDialog();
        }
        
        private void NavigateToGallery_Click(object sender, MouseButtonEventArgs e)
        {
            // For now, still open as dialog but could be converted to page
            var galleryWindow = new GalleryWindow();
            galleryWindow.Owner = this;
            galleryWindow.ShowDialog();
        }
        
        private void NavigateToCameraSettings_Click(object sender, MouseButtonEventArgs e)
        {
            NavigateToPage(new CameraSettings(), "Camera Settings");
        }
        
        public void NavigateToPage(Page page, string title)
        {
            if (_isNavigating) return;
            _isNavigating = true;
            
            // Push current state to navigation stack
            _navigationStack.Push(title);
            
            // Update breadcrumb
            if (BreadcrumbText != null)
            {
                BreadcrumbText.Text = $"Home > {title}";
            }
            
            // Animate navigation grid out
            if (NavigationGrid != null && NavigationGrid.Visibility == Visibility.Visible)
            {
                var fadeOut = new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(200)
                };
                
                fadeOut.Completed += (s, e) =>
                {
                    NavigationGrid.Visibility = Visibility.Collapsed;
                    ShowContentPage(page);
                };
                
                NavigationGrid.BeginAnimation(OpacityProperty, fadeOut);
            }
            else
            {
                ShowContentPage(page);
            }
        }
        
        private void ShowContentPage(Page page)
        {
            // Show content container
            if (ContentContainer != null)
            {
                ContentContainer.Visibility = Visibility.Visible;
                
                // Show Home button in bottom bar
                if (HomeButton != null)
                {
                    HomeButton.Visibility = Visibility.Visible;
                }
                
                // Animate content in
                var slideIn = new DoubleAnimation
                {
                    From = 100,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new PowerEase { EasingMode = EasingMode.EaseOut }
                };
                
                var fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(300)
                };
                
                fadeIn.Completed += (s, e) => { _isNavigating = false; };
                
                if (ContentFrame != null)
                {
                    var transform = new TranslateTransform();
                    ContentFrame.RenderTransform = transform;
                    
                    transform.BeginAnimation(TranslateTransform.XProperty, slideIn);
                    ContentContainer.BeginAnimation(OpacityProperty, fadeIn);
                    
                    ContentFrame.Navigate(page);
                }
            }
        }
        
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateBack();
        }
        
        public void NavigateBack()
        {
            if (_isNavigating) return;
            _isNavigating = true;
            
            // Pop from navigation stack
            if (_navigationStack.Count > 0)
            {
                _navigationStack.Pop();
            }
            
            // Animate content out
            if (ContentContainer != null)
            {
                var fadeOut = new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(200)
                };
                
                var slideOut = new DoubleAnimation
                {
                    From = 0,
                    To = -100,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new PowerEase { EasingMode = EasingMode.EaseIn }
                };
                
                fadeOut.Completed += (s, e) =>
                {
                    ContentContainer.Visibility = Visibility.Collapsed;
                    ShowNavigationGrid();
                };
                
                if (ContentFrame != null)
                {
                    var transform = ContentFrame.RenderTransform as TranslateTransform;
                    if (transform != null)
                    {
                        transform.BeginAnimation(TranslateTransform.XProperty, slideOut);
                    }
                }
                
                ContentContainer.BeginAnimation(OpacityProperty, fadeOut);
            }
        }
        
        private void ShowNavigationGrid()
        {
            // Hide Home button when showing navigation grid
            if (HomeButton != null)
            {
                HomeButton.Visibility = Visibility.Collapsed;
            }
            
            // Show navigation grid
            if (NavigationGrid != null)
            {
                NavigationGrid.Visibility = Visibility.Visible;
                
                // Animate navigation grid in
                var fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(300)
                };
                
                fadeIn.Completed += (s, e) => { _isNavigating = false; };
                
                NavigationGrid.BeginAnimation(OpacityProperty, fadeIn);
            }
        }
        
        private void QuickCapture_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to Event Selection first
            NavigateToPage(new Pages.EventSelectionPage(), "Select Event");
        }
        
        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate back to home grid
            NavigateBack();
        }
        
        #endregion
        
        #region Window Control Events
        
        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new ModernSettingsWindow();
            settingsWindow.Show();
        }
        
        private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        {
            // Directly minimize without animation to avoid rendering issues
            this.WindowState = WindowState.Minimized;
        }
        
        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            // Animate close
            var animation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(300)
            };
            animation.Completed += (s, args) => this.Close();
            this.BeginAnimation(OpacityProperty, animation);
        }
        
        #endregion
        
        #region Window Events
        
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Make window truly fullscreen
            MakeTrueFullscreen();
            
            // Apply entrance animations
            var storyboard = this.FindResource("FadeIn") as Storyboard;
            storyboard?.Begin(this);
            
            // Force high-quality rendering after load
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                // Apply high-quality rendering settings
                RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
                RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
                RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);
                
                // Force a visual refresh
                this.InvalidateVisual();
            }
        }
        
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            CheckOrientation();
        }
        
        private void Window_StateChanged(object sender, EventArgs e)
        {
            // Handle window state changes
            if (this.WindowState == WindowState.Normal || this.WindowState == WindowState.Maximized)
            {
                // Window is being restored
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Force refresh the visual tree
                    this.InvalidateVisual();
                    
                    // Re-apply rendering settings
                    RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
                    RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
                    RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);
                    
                    // Force layout update
                    this.UpdateLayout();
                    
                    // Refresh all child controls
                    if (NavigationGrid != null)
                    {
                        NavigationGrid.InvalidateVisual();
                        NavigationGrid.UpdateLayout();
                    }
                    
                    if (ContentFrame != null)
                    {
                        ContentFrame.InvalidateVisual();
                        ContentFrame.UpdateLayout();
                    }
                    
                    // Reset opacity in case it was affected
                    this.Opacity = 1.0;
                }), DispatcherPriority.Render);
            }
        }
        
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Emergency exit: Ctrl+Shift+X
            if (e.Key == Key.X && 
                (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                // Emergency exit - close the application
                this.Close();
                Application.Current.Shutdown();
                e.Handled = true;
                return;
            }
            
            // Keyboard shortcuts
            if (e.Key == Key.Escape || (e.Key == Key.Back && ContentContainer?.Visibility == Visibility.Visible))
            {
                NavigateBack();
                e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                // Refresh current page
                ContentFrame?.Refresh();
                e.Handled = true;
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.D1: // Ctrl+1 for Templates
                        if (ContentContainer?.Visibility != Visibility.Visible)
                            NavigateToTemplates_Click(null, null);
                        e.Handled = true;
                        break;
                    case Key.D2: // Ctrl+2 for Camera
                        if (ContentContainer?.Visibility != Visibility.Visible)
                            NavigateToCamera_Click(null, null);
                        e.Handled = true;
                        break;
                    case Key.D3: // Ctrl+3 for Photo Booth
                        if (ContentContainer?.Visibility != Visibility.Visible)
                            NavigateToPhotoBooth_Click(null, null);
                        e.Handled = true;
                        break;
                    case Key.S: // Ctrl+S for Settings
                        OpenSettings_Click(null, null);
                        e.Handled = true;
                        break;
                }
            }
        }
        
        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            // Handle display rotation or DPI changes
            Dispatcher.Invoke(() =>
            {
                CheckOrientation();
                OptimizeForSurfacePro();
            });
        }
        
        protected override void OnClosed(EventArgs e)
        {
            _statusTimer?.Stop();
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            base.OnClosed(e);
        }
        
        #endregion
        
        #region Fullscreen and Lock Features
        
        private void MakeTrueFullscreen()
        {
            // Simply maximize the window without aggressive API calls
            this.WindowState = WindowState.Maximized;
            
            // Optional: Hide taskbar by setting window bounds (less aggressive)
            if (this.WindowState == WindowState.Maximized)
            {
                this.MaxHeight = SystemParameters.PrimaryScreenHeight;
                this.MaxWidth = SystemParameters.PrimaryScreenWidth;
            }
        }
        
        public void SetLocked(bool locked)
        {
            _isLocked = locked;
            UpdateLockUI();
        }
        
        public bool IsLocked => _isLocked;
        
        public void SetPin(string pin)
        {
            if (!string.IsNullOrEmpty(pin) && pin.Length >= 4)
            {
                _pinCode = pin;
                // TODO: Save to settings when Settings.Designer.cs is regenerated
                // Properties.Settings.Default.LockPin = pin;
                // Properties.Settings.Default.Save();
            }
        }
        
        public string GetPin()
        {
            // TODO: Load from settings when Settings.Designer.cs is regenerated
            // if (Properties.Settings.Default.LockPin != null)
            // {
            //     _pinCode = Properties.Settings.Default.LockPin;
            // }
            return _pinCode;
        }
        
        private void UpdateLockUI()
        {
            // This will be called to update UI based on lock state
            // Implementation depends on where you want to show lock status
        }
        
        public bool ValidatePin(string enteredPin)
        {
            return enteredPin == GetPin();
        }
        
        #endregion
    }
}