using DesignerCanvas;
﻿using Photobooth.Pages;
using Photobooth.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace Photobooth
{
    /// <summary>
    /// Interaction logic for PhotoBoothWindow.xaml
    /// </summary>
    public partial class PhotoBoothWindow : Window
    {
        private bool _isSidebarExpanded = true;
        private DispatcherTimer _timeUpdateTimer;
        
        // Windows API for removing window chrome
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_SYSMENU = 0x80000;
        private const int WS_CAPTION = 0xC00000;
        private const int WS_THICKFRAME = 0x40000;
        private const int WS_MINIMIZE = 0x20000000;
        private const int WS_MAXIMIZE = 0x1000000;
        private const int WS_EX_DLGMODALFRAME = 0x0001;
        private const int WS_EX_WINDOWEDGE = 0x0100;
        private const int WS_EX_CLIENTEDGE = 0x0200;
        private const int WS_EX_STATICEDGE = 0x20000;
        
        static PhotoBoothWindow()
        {
            // Force WPF to use the best rendering settings at the type level
            TextOptions.TextFormattingModeProperty.OverrideMetadata(
                typeof(PhotoBoothWindow),
                new FrameworkPropertyMetadata(TextFormattingMode.Display));
            
            RenderOptions.BitmapScalingModeProperty.OverrideMetadata(
                typeof(PhotoBoothWindow),
                new FrameworkPropertyMetadata(BitmapScalingMode.HighQuality));
        }
        
        public PhotoBoothWindow() : this(false)
        {
        }
        
        public PhotoBoothWindow(bool openTemplates)
        {
            // Force window style before anything else
            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.NoResize;
            this.WindowState = WindowState.Maximized;
            
            // Set DPI awareness and rendering options BEFORE InitializeComponent
            SetupHighQualityRendering();
            
            InitializeComponent();
            //dcvs.CanvasRatio = 4.0 / 6.0;

            //dcvs.SelectedItems.CollectionChanged += SelectedItems_CollectionChanged;
            
            // Initialize components
            InitializeModernInterface();
            
            // Remove window chrome as soon as handle is available
            this.SourceInitialized += OnSourceInitialized;
            
            // Apply DPI scaling after window is loaded
            this.Loaded += OnWindowLoaded;
            
            // Navigate to MainPage if opening for templates
            if (openTemplates)
            {
                this.Loaded += (s, e) =>
                {
                    if (frame != null)
                    {
                        frame.Navigate(MainPage.Instance);
                        UpdateBreadcrumb("📋 Templates");
                    }
                };
            }
        }
        
        private void OnSourceInitialized(object sender, EventArgs e)
        {
            // Remove window chrome immediately when handle is available
            RemoveWindowChrome();
            
            // Hide from Alt+Tab if needed
            var helper = new WindowInteropHelper(this);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, 
                GetWindowLong(helper.Handle, GWL_EXSTYLE) | 0x00000080); // WS_EX_TOOLWINDOW
        }
        
        private void SetupHighQualityRendering()
        {
            // These must be set before InitializeComponent
            this.UseLayoutRounding = true;
            this.SnapsToDevicePixels = true;
            
            // Force ClearType rendering
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
            
            // High quality image rendering
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
            RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);
        }
        
        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // Apply DPI scaling adjustments after window is loaded
            ApplyDpiScaling();
        }
        
        private void RemoveWindowChrome()
        {
            // Get the window handle
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                // Get current window style
                int currentStyle = GetWindowLong(hwnd, GWL_STYLE);
                
                // Remove all window chrome elements
                currentStyle &= ~WS_CAPTION;
                currentStyle &= ~WS_SYSMENU;
                currentStyle &= ~WS_THICKFRAME;
                currentStyle &= ~WS_MINIMIZE;
                currentStyle &= ~WS_MAXIMIZE;
                
                SetWindowLong(hwnd, GWL_STYLE, currentStyle);
                
                // Also modify extended window styles
                int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                extendedStyle &= ~WS_EX_DLGMODALFRAME;
                extendedStyle &= ~WS_EX_WINDOWEDGE;
                extendedStyle &= ~WS_EX_CLIENTEDGE;
                extendedStyle &= ~WS_EX_STATICEDGE;
                
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle);
                
                // Force window to redraw
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
                
                // Force maximize state
                this.WindowState = WindowState.Maximized;
            }
        }
        
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, 
            int X, int Y, int cx, int cy, uint uFlags);
        
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;
        
        private void ApplyDpiScaling()
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var dpiX = source.CompositionTarget.TransformToDevice.M11;
                var dpiY = source.CompositionTarget.TransformToDevice.M22;
                
                // If high DPI, ensure proper scaling
                if (dpiX > 1.0 || dpiY > 1.0)
                {
                    // Force redraw with proper DPI settings
                    this.InvalidateVisual();
                    
                    // Ensure all child elements use proper rendering
                    if (frame != null)
                    {
                        RenderOptions.SetBitmapScalingMode(frame, BitmapScalingMode.HighQuality);
                        TextOptions.SetTextFormattingMode(frame, TextFormattingMode.Display);
                    }
                }
            }
        }
        
        private void InitializeModernInterface()
        {
            try
            {
                // Set up time display timer
                _timeUpdateTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMinutes(1)
                };
                _timeUpdateTimer.Tick += UpdateTimeDisplay;
                _timeUpdateTimer.Start();
                
                // Update time immediately
                UpdateTimeDisplay(null, null);
                
                // Enable window dragging from title bar
                this.MouseLeftButtonDown += Window_MouseLeftButtonDown;
                
                // Set initial sidebar state
                UpdateSidebarState();
            }
            catch (Exception ex)
            {
                // Log error but don't crash the application
                System.Diagnostics.Debug.WriteLine($"Error initializing modern interface: {ex.Message}");
            }
        }

        private void SelectedItems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            //// clear selection of orientation checkboxes, and properties
            //comboBox.SelectedIndex = -1;
            //comboBox1.SelectedIndex = -1;
        }

        private void dcvs_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void dcvs_MouseMove(object sender, MouseEventArgs e)
        {
            //// todo remove it later
            //// this listener is not needed as it is already handled by the designer canvas
            //try
            //{
            //    MousePositionLabel.Content = dcvs_container.dcvs.PointToCanvas(e.GetPosition(dcvs_container.dcvs));
            //}
            //catch (Exception)
            //{ }
        }

        //private void ActionImportImage(object sender, RoutedEventArgs e)
        //{

        //    try
        //    {
        //        Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
        //        openFileDialog.Filter = "Image files (*.jpg, *.jpeg, *.jpe, *.jfif, *.png, *.bmp, *.dib, *.gif, *.tif, *.tiff, *.ico, *.svg, *.svgz, *.wmf, *.emf, *.webp)|*.jpg; *.jpeg; *.jpe; *.jfif; *.png; *.bmp; *.dib; *.gif; *.tif; *.tiff; *.ico; *.svg; *.svgz; *.wmf; *.emf; *.webp|All files (*.*)|*.*";
        //        openFileDialog.Title = "Select a background image";
        //        if (openFileDialog.ShowDialog() == true)
        //        {
        //            // always use for aspect ratio
        //            var img = new ImageCanvasItem(0, 0, dcvs.Width / 2, dcvs.Height, new BitmapImage(new Uri(openFileDialog.FileName)), 2, 6);


        //            dcvs.Items.Add(img);
        //            ShowPropertiesOfCanvasItem(img);
        //        }
        //    }
        //    catch (FileNotFoundException ex)
        //    {
        //        MessageBox.Show(ex.Message, "File not found", MessageBoxButton.OK, MessageBoxImage.Error);
        //    }
        //    catch (Exception)
        //    { }
        //}

        //private void ActionPortraitOrientation(object sender, RoutedEventArgs e)
        //{
        //    ActionOrientation(true);
        //}

        //private void ActionLandscapeOrientation(object sender, RoutedEventArgs e)
        //{
        //    ActionOrientation(false);
        //}

        //private void ActionOrientation(bool isPortrait)
        //{
        //    // for each selected item
        //    foreach(IBoxCanvasItem item in dcvs.SelectedItems)
        //    {
        //        // calculate dimensions, width will be higher in portrait
        //        // if convert to portrait, inverse ratio, if > 1
        //        if (isPortrait && item.AspectRatio > 1)
        //        {
        //            item.AspectRatio = 1 / item.AspectRatio;
        //        }
        //        else if (!isPortrait && item.AspectRatio < 1)
        //        {
        //            item.AspectRatio = 1 / item.AspectRatio;
        //        }
        //    }
        //}

        //private void ActionChangeCanvasSize(object sender, RoutedEventArgs e)
        //{
        //    // get value of content from sender
        //    String content = (sender as ComboBoxItem).Content.ToString();

        //    // split string to width and height
        //    String[] tokens = content.Split("x");


        //    dcvs.Width = Convert.ToInt32(tokens[0]);
        //    dcvs.Height = Convert.ToInt32(tokens[0]);

        //    // update aspect ratio
        //    dcvs.CanvasRatio = dcvs.Width / dcvs.Height;
        //}


        //private void btnExport_Click(object sender, RoutedEventArgs e)
        //{
        //    try
        //    {
        //        // save file
        //        Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
        //        saveFileDialog.Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp|TIFF Image (*.tif)|*.tif|GIF Image (*.gif)|*.gif|All files (*.*)|*.*";
        //        saveFileDialog.Title = "Save an image";
        //        if (saveFileDialog.ShowDialog() == true)
        //        {
        //            var encoder = CanvasImageExporter.EncoderFromFileName(saveFileDialog.FileName);
        //            using (var s = File.Open(saveFileDialog.FileName, FileMode.Create))
        //            {
        //                CanvasImageExporter.ExportImage(dcvs_container.dcvs, s, encoder, 300, 300);
        //            }
        //        }
        //    }
        //    catch (Exception)
        //    { }
        //}

        //private void btnPrint_Click(object sender, RoutedEventArgs e)
        //{
        //    try
        //    {
        //        BitmapSource image = CanvasImageExporter.CreateImage(dcvs_container.dcvs, 300, 300);
        //        // print as 4x6 image and also show the preview image
        //        var printDialog = new PrintDialog();
        //        if (printDialog.ShowDialog() == true)
        //        {
        //            var printCapabilities = printDialog.PrintQueue.GetPrintCapabilities(printDialog.PrintTicket);
        //            var scale = Math.Min(printCapabilities.PageImageableArea.ExtentWidth / image.Width, printCapabilities.PageImageableArea.ExtentHeight / image.Height);
        //            var printImage = new Image
        //            {
        //                Source = image,
        //                Stretch = Stretch.Uniform,
        //                StretchDirection = StretchDirection.DownOnly,
        //                Width = image.Width * scale,
        //                Height = image.Height * scale
        //            };
        //            var printCanvas = new Canvas
        //            {
        //                Width = printImage.Width,
        //                Height = printImage.Height
        //            };
        //            printCanvas.Children.Add(printImage);
        //            printDialog.PrintVisual(printCanvas, "Photobooth");
        //        }
        //    }
        //    catch (Exception)
        //    { }
        //}

        // todo create clear canvas button, and call it from there
        //private void ActionClearCanvas(object sender, RoutedEventArgs e)
        //{
        //    try
        //    {
        //        dcvs.Items.Clear();
        //    }
        //    catch (Exception)
        //    { }
        //}

        //private void ActionPlaceholderSelected(object sender, RoutedEventArgs e)
        //{
        //    ToolBoxList.SelectedIndex = -1;

        //    // Create a new PlaceholderCanvasItem with specified properties
        //    var placeholderItem = new PlaceholderCanvasItem(50, 50, 40, 40, 3, 2);

        //    // Add the PlaceholderCanvasItem to your canvas or UI element
        //    dcvs.Items.Add(placeholderItem);
        //    ShowPropertiesOfCanvasItem(placeholderItem);
        //}

        //private void ShowPropertiesOfCanvasItem(CanvasItem item)
        //{
        //    // show properties of item
        //    tbLocationX.Text = item.Location.X.ToString();
        //    tbLocationY.Text = item.Location.Y.ToString();

        //    tbSizeW.Text = item.Width.ToString();
        //    tbSizeY.Text = item.Height.ToString();
        //}

        //private void btnLockSize_Click(object sender, RoutedEventArgs e)
        //{
        //    // lock all selected items
        //    foreach (IBoxCanvasItem item in dcvs_container.dcvs.SelectedItems)
        //    {
        //        item.Resizeable = !item.Resizeable;

        //        //  remove and re-add to update the adorner
        //        dcvs_container.dcvs.Items.Remove(item);
        //        dcvs_container.dcvs.Items.Add(item);
        //    }
        //}

        //private void btnLockAspectRatio_Click(object sender, RoutedEventArgs e)
        //{
        //    // lock all selected items
        //    foreach (CanvasItem item in dcvs_container.dcvs.SelectedItems)
        //    {
        //        item.LockedAspectRatio = !item.LockedAspectRatio;
        //        //if (item.LockedAspectRatio)
        //        //{
        //        //	item.LockedAspectRatio = false;
        //        //} else
        //        //{
        //        //	item.LockedAspectRatio = true;
        //        //	item.AspectRatio = item.AspectRatio;
        //        //}
        //    }
        //}

        //private void btnLockPosition_Click(object sender, RoutedEventArgs e)
        //{
        //    // lock all selected items
        //    foreach (CanvasItem item in dcvs_container.dcvs.SelectedItems)
        //    {
        //        item.LockedPosition = !item.LockedPosition;
        //    }
        //}

        //private void btnHorizontalVerticalRatio_Click(object sender, RoutedEventArgs e)
        //{
        //    // reverse aspect ratios of all selected items
        //    foreach (CanvasItem item in dcvs_container.dcvs.SelectedItems)
        //    {
        //        item.reverseAspectRatio();
        //    }
        //}


        //private void Window_Initialized(object sender, EventArgs e)
        //{
        //    dcvs_container.MouseMove += dcvs_MouseMove;
        //    dcvs_container.MouseDown += dcvs_MouseDown;
        //}

        //private void btnHVCanvas_Click(object sender, RoutedEventArgs e)
        //{
        //    dcvs_container.CanvasRatio = 1 / dcvs_container.CanvasRatio;
        //}
        private void sidebar_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedButton = sidebar.SelectedItem as NavButton;
            Navigate(frame,selectedButton);
            
        }
        public static void Navigate(Frame frame,NavButton button)
        {
            if (button != null)
            {
                try
                {
                    var uri = button.NavLink;
                    //Console.WriteLine("URL: ", uri.ToString());
                    //MessageBox.Show("URL: " + uri.ToString());
                    
                    // Special case for photobooth touch interface
                    if (uri != null && uri.ToString() == "PHOTOBOOTH_TOUCH")
                    {
                        // Open photobooth in fullscreen window
                        var photoboothWindow = new Window
                        {
                            Title = "Photobooth Touch Interface",
                            WindowState = WindowState.Maximized,
                            WindowStyle = WindowStyle.None, // Fullscreen for touch
                            Content = new Pages.PhotoboothTouch(),
                            Background = new SolidColorBrush(Colors.Black)
                        };
                        photoboothWindow.Show();
                        return;
                    }
                    
                    // Special case for events browser
                    if (uri != null && uri.ToString() == "EVENTS_BROWSER")
                    {
                        // Open events browser window
                        var eventsBrowser = new Windows.EventBrowserWindow();
                        eventsBrowser.Owner = Application.Current.MainWindow;
                        eventsBrowser.ShowDialog();
                        return;
                    }
                    
                    if (uri != null)
                    {
                        frame.Navigate(uri);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }

            }
        }


        private void sidebar1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedButton = sidebar1.SelectedItem as NavButton;
            Navigate(frame, selectedButton);
        }

		private void MenuItem_Click(object sender, RoutedEventArgs e)
		{

		}

     

		private void MenuItem_Click_1(object sender, RoutedEventArgs e)
		{
            if (frame.Content != MainPage.Instance)
            {
				frame.Navigate(MainPage.Instance);
			}

            MainPage.Instance.dcvs.ImportImage();
		}

        private void MenuItem_Click_2(object sender, RoutedEventArgs e)
        {
            if (frame.Content != MainPage.Instance)
            {
                frame.Navigate(MainPage.Instance);
            }

            MainPage.Instance.dcvs.ExportImage();
        }

        private void MenuItem_Click_3(object sender, RoutedEventArgs e)
        {
            if (frame.Content != MainPage.Instance)
            {
                frame.Navigate(MainPage.Instance);
            }

            MainPage.Instance.dcvs.PrintImage();
        }
        
        private void OpenTemplateDesigner_Click(object sender, RoutedEventArgs e)
        {
            // Open the modern template designer in fullscreen mode
            var templateWindow = new Window
            {
                Title = "Template Designer",
                WindowState = WindowState.Maximized,
                WindowStyle = WindowStyle.None, // Fullscreen for touch - same as PhotoboothTouchModern
                Content = Pages.MainPage.Instance,
                Background = new SolidColorBrush(Colors.Black)
            };
            templateWindow.Show();
        }
        
        #region Modern Interface Event Handlers
        
        // Hamburger Menu Toggle
        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isSidebarExpanded = !_isSidebarExpanded;
                AnimateSidebar();
                UpdateSidebarState();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error toggling sidebar: {ex.Message}");
            }
        }
        
        // Search functionality
        private void Search_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // TODO: Implement search functionality
                MessageBox.Show("Search functionality will be implemented here.", "Search", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in search: {ex.Message}");
            }
        }
        
        // Notifications
        private void Notifications_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // TODO: Implement notifications panel
                MessageBox.Show("Notifications panel will be implemented here.", "Notifications", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in notifications: {ex.Message}");
            }
        }
        
        // User Profile
        private void UserProfile_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // TODO: Implement user profile menu
                MessageBox.Show("User profile menu will be implemented here.", "User Profile", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in user profile: {ex.Message}");
            }
        }
        
        // Window Controls
        private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.WindowState = WindowState.Minimized;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error minimizing window: {ex.Message}");
            }
        }
        
        private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error maximizing window: {ex.Message}");
            }
        }
        
        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error closing window: {ex.Message}");
            }
        }
        
        // Quick Capture
        private void QuickCapture_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Navigate to camera page if not already there
                if (frame?.Content?.GetType() != typeof(Camera))
                {
                    frame?.Navigate(new Camera());
                }
                
                // TODO: Trigger camera capture
                MessageBox.Show("Quick capture triggered!", "Camera", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in quick capture: {ex.Message}");
            }
        }
        
        // Modern Navigation Event Handlers
        private void NavigateToTemplates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (frame != null)
                {
                    frame.Navigate(MainPage.Instance);
                    UpdateBreadcrumb("📋 Templates");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to templates: {ex.Message}");
            }
        }
        
        private void NavigateToEvents_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Navigate to events - use existing logic from Navigate method
                var navButton = new NavButton { NavLink = new Uri("EVENTS_BROWSER", UriKind.RelativeOrAbsolute) };
                Navigate(frame, navButton);
                UpdateBreadcrumb("🎉 Events");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to events: {ex.Message}");
            }
        }
        
        private void NavigateToCamera_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (frame != null)
                {
                    frame.Navigate(new Camera());
                    UpdateBreadcrumb("📷 Camera");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to camera: {ex.Message}");
            }
        }
        
        private void NavigateToCameraSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (frame != null)
                {
                    frame.Navigate(new CameraSettings());
                    UpdateBreadcrumb("⚙️ Camera Settings");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to camera settings: {ex.Message}");
            }
        }
        
        private void NavigateToPhotoBooth_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Navigate to photobooth - use existing logic from Navigate method
                var navButton = new NavButton { NavLink = new Uri("PHOTOBOOTH_TOUCH", UriKind.RelativeOrAbsolute) };
                Navigate(frame, navButton);
                UpdateBreadcrumb("🎪 Photo Booth");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to photo booth: {ex.Message}");
            }
        }
        
        private void NavigateToSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open ModernSettingsWindow
                var settingsWindow = new ModernSettingsWindow();
                settingsWindow.Show();
                UpdateBreadcrumb("⚙️ Settings");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to settings: {ex.Message}");
            }
        }
        
        private void NavigateToAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // TODO: Implement account page
                MessageBox.Show("Account page will be implemented here.", "Account", MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateBreadcrumb("👤 Account");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to account: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Sidebar Animation and Management
        
        private void AnimateSidebar()
        {
            try
            {
                var storyboard = _isSidebarExpanded 
                    ? FindResource("SidebarExpandAnimation") as Storyboard
                    : FindResource("SidebarCollapseAnimation") as Storyboard;
                
                if (storyboard != null && SidebarColumn != null)
                {
                    Storyboard.SetTarget(storyboard, SidebarColumn);
                    storyboard.Begin();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error animating sidebar: {ex.Message}");
                // Fallback to direct width change
                if (SidebarColumn != null)
                {
                    SidebarColumn.Width = new GridLength(_isSidebarExpanded ? 280 : 72);
                }
            }
        }
        
        private void UpdateSidebarState()
        {
            try
            {
                // Update button labels visibility based on sidebar state
                if (MainNavigationPanel != null)
                {
                    foreach (Button button in MainNavigationPanel.Children.OfType<Button>())
                    {
                        // For collapsed state, could hide text labels if needed
                        // This would require template modifications
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating sidebar state: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Window Dragging and Status Updates
        
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Allow dragging the window from the title bar area
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    this.DragMove();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling window drag: {ex.Message}");
            }
        }
        
        private void UpdateTimeDisplay(object sender, EventArgs e)
        {
            try
            {
                if (TimeDisplay != null)
                {
                    TimeDisplay.Text = DateTime.Now.ToString("h:mm tt");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating time display: {ex.Message}");
            }
        }
        
        private void UpdateBreadcrumb(string breadcrumb)
        {
            try
            {
                if (BreadcrumbText != null)
                {
                    BreadcrumbText.Text = breadcrumb;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating breadcrumb: {ex.Message}");
            }
        }
        
        // Method to update camera status (can be called from other parts of the application)
        public void UpdateCameraStatus(string cameraName, string batteryLevel, bool isConnected)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Update status text
                    var statusPanel = this.FindName("StatusInfo") as StackPanel;
                    if (statusPanel?.Children.Count > 1 && statusPanel.Children[1] is TextBlock statusText)
                    {
                        statusText.Text = isConnected 
                            ? $"Camera Connected • Ready to Capture" 
                            : "Camera Disconnected";
                    }
                    
                    // Update camera info
                    var rightStatusPanel = this.FindName("RightStatusInfo") as StackPanel;
                    if (rightStatusPanel != null)
                    {
                        foreach (TextBlock textBlock in rightStatusPanel.Children.OfType<TextBlock>())
                        {
                            if (textBlock.Text.StartsWith("📷"))
                            {
                                textBlock.Text = $"📷 {cameraName}";
                            }
                            else if (textBlock.Text.StartsWith("🔋"))
                            {
                                textBlock.Text = $"🔋 {batteryLevel}";
                            }
                        }
                    }
                    
                    // Update status indicator color
                    if (statusPanel?.Children.Count > 0 && statusPanel.Children[0] is Ellipse statusIndicator)
                    {
                        statusIndicator.Fill = new SolidColorBrush(isConnected ? Colors.LimeGreen : Colors.Red);
                    }
                }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating camera status: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Cleanup
        
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // Clean up timer
                if (_timeUpdateTimer != null)
                {
                    _timeUpdateTimer.Stop();
                    _timeUpdateTimer = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during cleanup: {ex.Message}");
            }
            
            base.OnClosed(e);
        }
        
        #endregion
    }
}
