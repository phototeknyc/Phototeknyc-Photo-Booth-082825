using System;
using System.Linq;
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
            
            // Ensure this window is set as MainWindow if it's the startup window
            if (Application.Current != null && Application.Current.MainWindow == null)
            {
                Application.Current.MainWindow = this;
                System.Diagnostics.Debug.WriteLine("ModernPhotoboothWindow set as MainWindow");
            }
            
            // Navigate to the refactored modern photobooth page
            MainFrame.Navigate(new Pages.PhotoboothTouchModernRefactored());
            
            // Set up event handlers
            this.Loaded += OnWindowLoaded;
            this.KeyDown += OnKeyDown;
            this.Closing += OnWindowClosing;
            this.Closed += OnWindowClosed;
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
        
        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("ModernPhotoboothWindow closing - cleaning up...");
                
                // Get the current page and clean it up
                var currentPage = MainFrame?.Content as Pages.PhotoboothTouchModernRefactored;
                if (currentPage != null)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine("Calling page cleanup...");
                        currentPage.Cleanup();
                    }
                    catch (Exception pageEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error cleaning up page: {pageEx.Message}");
                    }
                }
                
                // Clear the frame content to release references
                if (MainFrame != null)
                {
                    MainFrame.Content = null;
                }
                
                // Don't try to stop cameras here - let App.OnExit handle it
                // This avoids RCW cleanup race conditions
                
                System.Diagnostics.Debug.WriteLine("Window cleanup completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during window closing: {ex.Message}");
            }
        }
        
        private void OnWindowClosed(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("ModernPhotoboothWindow closed");
                
                // Since ShutdownMode is OnMainWindowClose, we don't need to manually shutdown
                // The app will shutdown automatically when the main window closes
                // But we ensure it happens if this is the main window
                if (Application.Current != null && Application.Current.MainWindow == this)
                {
                    System.Diagnostics.Debug.WriteLine("ModernPhotoboothWindow is main window - application will shutdown");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during window closed: {ex.Message}");
            }
        }
    }
}