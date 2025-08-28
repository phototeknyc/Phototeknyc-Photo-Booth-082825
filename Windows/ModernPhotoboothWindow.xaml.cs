using System;
using System.Windows;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Photobooth.Windows
{
    public partial class ModernPhotoboothWindow : Window
    {
        // Import Windows API for true fullscreen
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        
        private const int GWL_STYLE = -16;
        private const int WS_SYSMENU = 0x80000;
        private const int WS_MAXIMIZEBOX = 0x10000;
        private const int WS_MINIMIZEBOX = 0x20000;
        
        public ModernPhotoboothWindow()
        {
            InitializeComponent();
            
            // Navigate to the refactored modern photobooth page
            MainFrame.Navigate(new Pages.PhotoboothTouchModernRefactored());
            
            // Set up event handlers
            this.Loaded += OnWindowLoaded;
            this.KeyDown += OnKeyDown;
        }
        
        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // Don't set fullscreen automatically - let user control it
            // User can press F11 for fullscreen if desired
        }
        
        private void SetFullscreen(bool fullscreen)
        {
            if (fullscreen)
            {
                // Store current state
                this.Tag = this.WindowState;
                
                // Set fullscreen
                this.WindowState = WindowState.Maximized;
                this.WindowStyle = WindowStyle.None;
                
                // Optional: Hide cursor for touch mode
                // Mouse.OverrideCursor = Cursors.None;
            }
            else
            {
                // Restore windowed mode
                this.WindowState = (WindowState)(this.Tag ?? WindowState.Normal);
                this.WindowStyle = WindowStyle.None; // Keep custom chrome
                
                // Show cursor
                Mouse.OverrideCursor = null;
            }
        }
        
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double-click to maximize/restore
                MaximizeButton_Click(null, null);
            }
            else
            {
                // Drag to move window
                this.DragMove();
            }
        }
        
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }
        
        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                MaximizeButton.Content = "□";
            }
            else
            {
                this.WindowState = WindowState.Maximized;
                MaximizeButton.Content = "❐";
            }
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                // ESC to exit fullscreen or close window
                if (this.WindowState == WindowState.Maximized && this.WindowStyle == WindowStyle.None)
                {
                    // Exit fullscreen
                    SetFullscreen(false);
                }
                else
                {
                    // Ask to close
                    var result = MessageBox.Show("Close Modern Photobooth?", 
                                                "Close", 
                                                MessageBoxButton.YesNo, 
                                                MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        this.Close();
                    }
                }
            }
            else if (e.Key == Key.F11)
            {
                // Toggle fullscreen with F11
                bool isFullscreen = (this.WindowState == WindowState.Maximized && this.WindowStyle == WindowStyle.None);
                SetFullscreen(!isFullscreen);
            }
        }
    }
}