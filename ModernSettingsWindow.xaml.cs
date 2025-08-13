using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Photobooth.Pages;

namespace Photobooth
{
    public partial class ModernSettingsWindow : Window
    {
        private DispatcherTimer particleTimer;
        private Random random = new Random();
        private Point lastTouchPoint;
        private bool isSwipeGesture = false;
        
        // For orientation detection
        private double lastWidth;
        private double lastHeight;
        private bool isPortrait = false;
        
        // Surface Pro specific dimensions
        private const double SURFACE_PRO_WIDTH = 2736;
        private const double SURFACE_PRO_HEIGHT = 1824;
        private const double SURFACE_PRO_ASPECT = 3.0 / 2.0;

        public ModernSettingsWindow()
        {
            InitializeComponent();
            
            // Enable touch support
            this.ManipulationStarting += OnManipulationStarting;
            this.ManipulationDelta += OnManipulationDelta;
            this.ManipulationCompleted += OnManipulationCompleted;
            this.IsManipulationEnabled = true;
            
            // Setup responsive design
            SetupResponsiveDesign();
            
            // Setup animated background
            SetupAnimatedBackground();
            
            // Handle orientation changes
            this.SizeChanged += Window_SizeChanged;
            
            // Enable tablet mode detection
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            
            // Set initial orientation
            CheckOrientation();
            
            // Optimize for Surface Pro
            OptimizeForSurfacePro();
        }
        
        private void SetupResponsiveDesign()
        {
            // Get current screen dimensions
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            
            // Check if this is likely a Surface Pro or similar high-DPI device
            if (screenWidth >= 2736 || screenHeight >= 2736)
            {
                // High DPI device detected
                ApplyHighDPIScaling();
            }
            
            // Set minimum sizes for touch
            this.MinWidth = 1024;
            this.MinHeight = 768;
        }
        
        private void ApplyHighDPIScaling()
        {
            // Get DPI scaling
            var source = PresentationSource.FromVisual(this);
            if (source != null)
            {
                var dpiX = source.CompositionTarget.TransformToDevice.M11;
                var dpiY = source.CompositionTarget.TransformToDevice.M22;
                
                // Adjust UI elements for high DPI
                if (dpiX > 1.5 || dpiY > 1.5)
                {
                    // Scale up touch targets
                    var transform = new ScaleTransform(1.2, 1.2);
                    settingsControl.RenderTransform = transform;
                }
            }
        }
        
        private void OptimizeForSurfacePro()
        {
            // Check if running on Surface Pro
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            var aspectRatio = Math.Max(screenWidth, screenHeight) / Math.Min(screenWidth, screenHeight);
            
            if (Math.Abs(aspectRatio - SURFACE_PRO_ASPECT) < 0.1)
            {
                // Likely a Surface Pro
                // Optimize touch targets for Surface Pen and touch
                Stylus.SetIsPressAndHoldEnabled(this, false);
                Stylus.SetIsFlicksEnabled(this, false);
                Stylus.SetIsTapFeedbackEnabled(this, false);
                
                // Increase touch target sizes in settings
                if (settingsControl != null)
                {
                    settingsControl.Resources["TouchTargetSize"] = 48.0;
                    settingsControl.Resources["ButtonMinHeight"] = 52.0;
                    settingsControl.Resources["SliderHeight"] = 40.0;
                }
            }
        }
        
        private void CheckOrientation()
        {
            var width = this.ActualWidth > 0 ? this.ActualWidth : SystemParameters.PrimaryScreenWidth;
            var height = this.ActualHeight > 0 ? this.ActualHeight : SystemParameters.PrimaryScreenHeight;
            
            bool newIsPortrait = height > width;
            
            if (newIsPortrait != isPortrait)
            {
                isPortrait = newIsPortrait;
                AdjustLayoutForOrientation();
            }
            
            lastWidth = width;
            lastHeight = height;
        }
        
        private void AdjustLayoutForOrientation()
        {
            if (isPortrait)
            {
                // Portrait mode adjustments
                ApplyPortraitLayout();
            }
            else
            {
                // Landscape mode adjustments
                ApplyLandscapeLayout();
            }
            
            // Animate the transition
            var animation = new DoubleAnimation
            {
                From = 0.8,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            settingsControl.BeginAnimation(OpacityProperty, animation);
        }
        
        private void ApplyPortraitLayout()
        {
            // Adjust header for portrait
            if (this.FindName("LaunchPhotoboothBtn") is FrameworkElement launchBtn)
            {
                launchBtn.Width = 140;
                launchBtn.Height = 40;
            }
            
            // Adjust settings control margins for portrait
            settingsControl.Margin = new Thickness(10);
            
            // Make scrollviewer full width in portrait
            if (settingsControl.Parent is ScrollViewer scrollViewer)
            {
                scrollViewer.Margin = new Thickness(0);
            }
        }
        
        private void ApplyLandscapeLayout()
        {
            // Adjust header for landscape
            if (this.FindName("LaunchPhotoboothBtn") is FrameworkElement launchBtn)
            {
                launchBtn.Width = 160;
                launchBtn.Height = 45;
            }
            
            // Adjust settings control margins for landscape
            settingsControl.Margin = new Thickness(20);
            
            // Add side margins in landscape for better readability
            if (settingsControl.Parent is ScrollViewer scrollViewer)
            {
                var screenWidth = this.ActualWidth;
                if (screenWidth > 1920)
                {
                    var sideMargin = (screenWidth - 1920) / 2;
                    scrollViewer.Margin = new Thickness(Math.Max(sideMargin, 40), 0, Math.Max(sideMargin, 40), 0);
                }
            }
        }
        
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            CheckOrientation();
            
            // Adjust particle canvas size
            if (ParticleCanvas != null)
            {
                ParticleCanvas.Width = e.NewSize.Width;
                ParticleCanvas.Height = e.NewSize.Height;
            }
        }
        
        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            // Handle display rotation
            Dispatcher.Invoke(() =>
            {
                CheckOrientation();
                SetupResponsiveDesign();
            });
        }
        
        private void SetupAnimatedBackground()
        {
            particleTimer = new DispatcherTimer();
            particleTimer.Interval = TimeSpan.FromMilliseconds(50);
            particleTimer.Tick += (s, e) => UpdateParticles();
            particleTimer.Start();
            
            // Create initial particles
            for (int i = 0; i < 30; i++)
            {
                CreateParticle();
            }
        }
        
        private void CreateParticle()
        {
            var particle = new System.Windows.Shapes.Ellipse
            {
                Width = random.Next(2, 6),
                Height = random.Next(2, 6),
                Fill = new SolidColorBrush(Color.FromArgb(50, 102, 126, 234)),
                Opacity = random.NextDouble() * 0.5 + 0.2
            };
            
            var left = random.NextDouble() * (ActualWidth > 0 ? ActualWidth : 1920);
            var top = random.NextDouble() * (ActualHeight > 0 ? ActualHeight : 1080);
            
            particle.SetValue(System.Windows.Controls.Canvas.LeftProperty, left);
            particle.SetValue(System.Windows.Controls.Canvas.TopProperty, top);
            particle.Tag = new ParticleData 
            { 
                SpeedX = (random.NextDouble() - 0.5) * 2, 
                SpeedY = (random.NextDouble() - 0.5) * 2 
            };
            
            ParticleCanvas.Children.Add(particle);
        }
        
        private void UpdateParticles()
        {
            foreach (System.Windows.Shapes.Ellipse particle in ParticleCanvas.Children)
            {
                var data = particle.Tag as ParticleData;
                if (data != null)
                {
                    var left = (double)particle.GetValue(System.Windows.Controls.Canvas.LeftProperty);
                    var top = (double)particle.GetValue(System.Windows.Controls.Canvas.TopProperty);
                    
                    left += data.SpeedX;
                    top += data.SpeedY;
                    
                    // Wrap around screen
                    if (left < -10) left = ActualWidth + 10;
                    if (left > ActualWidth + 10) left = -10;
                    if (top < -10) top = ActualHeight + 10;
                    if (top > ActualHeight + 10) top = -10;
                    
                    particle.SetValue(System.Windows.Controls.Canvas.LeftProperty, left);
                    particle.SetValue(System.Windows.Controls.Canvas.TopProperty, top);
                }
            }
        }
        
        // Touch gesture handlers
        private void OnManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.ManipulationContainer = this;
            e.Mode = ManipulationModes.Translate;
            isSwipeGesture = false;
        }
        
        private void OnManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            // Detect swipe down gesture
            if (e.CumulativeManipulation.Translation.Y > 100 && 
                Math.Abs(e.CumulativeManipulation.Translation.X) < 50)
            {
                isSwipeGesture = true;
                TouchOverlay.Visibility = Visibility.Visible;
            }
        }
        
        private void OnManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
            if (isSwipeGesture && e.TotalManipulation.Translation.Y > 150)
            {
                // Minimize window on swipe down
                this.WindowState = WindowState.Minimized;
            }
            
            TouchOverlay.Visibility = Visibility.Collapsed;
            isSwipeGesture = false;
        }
        
        private void LaunchDesigner_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveSettings();
                
                // Open main Surface window with designer
                var mainWindow = new SurfacePhotoBoothWindow();
                mainWindow.Show();
                
                // Navigate to Templates/Designer after window loads
                mainWindow.Loaded += (s, args) =>
                {
                    mainWindow.NavigateToPage(MainPage.Instance, "Templates");
                };
                
                this.WindowState = WindowState.Minimized;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error launching designer: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void LaunchPhotobooth_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveSettings();
                
                // Open main Surface window with PhotoBooth
                var mainWindow = new SurfacePhotoBoothWindow();
                mainWindow.Show();
                
                // Navigate to Event Selection after window loads
                mainWindow.Loaded += (s, args) =>
                {
                    mainWindow.NavigateToPage(new Pages.EventSelectionPage(), "Select Event");
                };
                
                this.WindowState = WindowState.Minimized;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error launching photobooth: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void SaveSettings()
        {
            // Save settings first
            if (settingsControl != null)
            {
                // Trigger save through the control
                var saveMethod = settingsControl.GetType().GetMethod("SaveSettings_Click", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (saveMethod != null)
                {
                    saveMethod.Invoke(settingsControl, new object[] { null, null });
                }
            }
        }
        
        private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        {
            // Animate minimize
            var animation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(200)
            };
            animation.Completed += (s, args) => this.WindowState = WindowState.Minimized;
            this.BeginAnimation(OpacityProperty, animation);
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
        
        protected override void OnClosed(EventArgs e)
        {
            particleTimer?.Stop();
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            base.OnClosed(e);
        }
        
        private class ParticleData
        {
            public double SpeedX { get; set; }
            public double SpeedY { get; set; }
        }
    }
}