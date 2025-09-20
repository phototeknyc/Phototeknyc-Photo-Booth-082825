using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Photobooth.Controls
{
    /// <summary>
    /// Color picker dialog using PixiEditor ColorPicker
    /// </summary>
    public partial class PixiEditorColorPickerDialog : Window
    {
        private bool _isInDialogMode = false;
        private bool _okClicked = false;
        private bool _eyedropperSuccess = false;
        private Color? _eyedropperColor = null; // Store the eyedropper result separately
        private static Color? _lastEyedropperColor = null; // Static field to ensure it survives

        public Color SelectedColor
        {
            get { return ColorPicker.SelectedColor; }
            set { ColorPicker.SelectedColor = value; }
        }

        public bool WasOkClicked => _okClicked;
        public Color? EyedropperColor => _eyedropperColor;

        public PixiEditorColorPickerDialog()
        {
            InitializeComponent();

            // Add closing event handler to preserve flags during close process
            Closing += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"PixiEditorColorPickerDialog: Window closing, _okClicked = {_okClicked}, _eyedropperSuccess = {_eyedropperSuccess}");
            };
        }

        public PixiEditorColorPickerDialog(string title, Color initialColor) : this()
        {
            Title = title;
            SelectedColor = initialColor;
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"PixiEditorColorPickerDialog: OK clicked, SelectedColor = {SelectedColor}");
            _okClicked = true;
            if (_isInDialogMode && IsVisible)
            {
                try
                {
                    DialogResult = true;
                }
                catch (InvalidOperationException ex)
                {
                    // This can happen if the dialog is already closing or wasn't opened with ShowDialog
                    System.Diagnostics.Debug.WriteLine($"PixiEditorColorPickerDialog: Could not set DialogResult: {ex.Message}");
                }
            }

            // Only close if still visible
            if (IsVisible)
            {
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _okClicked = false;
            if (_isInDialogMode)
            {
                try
                {
                    DialogResult = false;
                }
                catch (InvalidOperationException)
                {
                    // Fallback if there's still an issue
                }
            }
            Close();
        }

        private void EyedropperButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Store current position
                var currentLeft = this.Left;
                var currentTop = this.Top;

                // Hide the dialog temporarily
                this.Visibility = Visibility.Collapsed;

                // Create a full-screen transparent window for capturing
                var captureWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), // Almost transparent
                    Topmost = true,
                    Left = 0,
                    Top = 0,
                    Width = SystemParameters.VirtualScreenWidth,
                    Height = SystemParameters.VirtualScreenHeight,
                    Cursor = Cursors.Cross,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual
                };

                bool colorCaptured = false;
                Color capturedColor = Colors.Black;

                // Add a canvas with visual feedback
                var canvas = new Canvas();

                // Add instruction text
                var instructionText = new TextBlock
                {
                    Text = "Click anywhere to pick a color (ESC to cancel)",
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                    Padding = new Thickness(10),
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 50, 0, 0)
                };
                canvas.Children.Add(instructionText);
                Canvas.SetLeft(instructionText, SystemParameters.VirtualScreenWidth / 2 - 150);
                Canvas.SetTop(instructionText, 50);

                captureWindow.Content = canvas;

                captureWindow.MouseMove += (s, args) =>
                {
                    // Get current color under cursor for preview (optional)
                    var screenPoint = captureWindow.PointToScreen(args.GetPosition(captureWindow));
                    var previewColor = GetPixelColor((int)screenPoint.X, (int)screenPoint.Y);

                    // Update window title with current color (for debugging)
                    captureWindow.Title = $"RGB: {previewColor.R}, {previewColor.G}, {previewColor.B}";
                };

                captureWindow.PreviewMouseLeftButtonDown += (s, args) =>
                {
                    try
                    {
                        // Get the position relative to the screen
                        var screenPoint = captureWindow.PointToScreen(args.GetPosition(captureWindow));

                        // Capture the pixel color at the cursor position
                        capturedColor = GetPixelColor((int)screenPoint.X, (int)screenPoint.Y);
                        colorCaptured = true;

                        captureWindow.Close();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error capturing color: {ex.Message}");
                        captureWindow.Close();
                    }
                };

                captureWindow.PreviewKeyDown += (s, args) =>
                {
                    if (args.Key == Key.Escape)
                    {
                        captureWindow.Close();
                    }
                };

                captureWindow.Closed += (s, args) =>
                {
                    if (colorCaptured)
                    {
                        try
                        {
                            // Store the captured color first to preserve it
                            _eyedropperColor = capturedColor;
                            _lastEyedropperColor = capturedColor; // Also store in static field
                            _eyedropperSuccess = true;
                            _okClicked = true;

                            // Update the color picker UI to show the captured color
                            SelectedColor = capturedColor;
                            ColorPicker.SelectedColor = capturedColor;

                            // Log the captured color for debugging
                            System.Diagnostics.Debug.WriteLine($"Eyedropper captured color: R={capturedColor.R}, G={capturedColor.G}, B={capturedColor.B}, A={capturedColor.A}");

                            // Restore the dialog position and visibility
                            this.Left = currentLeft;
                            this.Top = currentTop;
                            this.Visibility = Visibility.Visible;

                            System.Diagnostics.Debug.WriteLine($"Eyedropper: Closing dialog with _eyedropperSuccess = {_eyedropperSuccess}, captured color = {_eyedropperColor}, static color = {_lastEyedropperColor}");

                            // Set dialog result if in dialog mode
                            if (_isInDialogMode)
                            {
                                try
                                {
                                    DialogResult = true;
                                }
                                catch (InvalidOperationException)
                                {
                                    // If we can't set DialogResult, just close normally
                                }
                            }

                            // Close the dialog immediately (synchronously)
                            Close();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error processing captured color: {ex.Message}");
                            // Restore dialog even on error
                            this.Left = currentLeft;
                            this.Top = currentTop;
                            this.Visibility = Visibility.Visible;
                            this.Activate();
                        }
                    }
                    else
                    {
                        // Restore the dialog without closing (user cancelled)
                        this.Left = currentLeft;
                        this.Top = currentTop;
                        this.Visibility = Visibility.Visible;
                        this.Activate();
                        this.Focus();
                    }
                };

                // Show the capture window
                captureWindow.Show();
                captureWindow.Activate();
            }
            catch (Exception ex)
            {
                this.Visibility = Visibility.Visible;
                MessageBox.Show($"Error using eyedropper: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        private Color GetPixelColor(int x, int y)
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            uint pixel = GetPixel(hdc, x, y);
            ReleaseDC(IntPtr.Zero, hdc);

            byte r = (byte)(pixel & 0xFF);
            byte g = (byte)((pixel >> 8) & 0xFF);
            byte b = (byte)((pixel >> 16) & 0xFF);

            return Color.FromRgb(r, g, b);
        }

        /// <summary>
        /// Show the color picker dialog and return the selected color
        /// </summary>
        /// <param name="owner">Parent window</param>
        /// <param name="title">Dialog title</param>
        /// <param name="initialColor">Initial color to display</param>
        /// <returns>Selected color, or null if cancelled</returns>
        public static Color? ShowDialog(Window owner, string title, Color initialColor)
        {
            // Clear any previous eyedropper color
            _lastEyedropperColor = null;

            var dialog = new PixiEditorColorPickerDialog(title, initialColor)
            {
                Owner = owner
            };

            dialog._isInDialogMode = true;
            System.Diagnostics.Debug.WriteLine($"PixiEditorColorPickerDialog.ShowDialog: Opening with initial color {initialColor}");

            var result = dialog.ShowDialog();

            System.Diagnostics.Debug.WriteLine($"PixiEditorColorPickerDialog.ShowDialog: Dialog closed, result = {result}, _okClicked = {dialog._okClicked}, _eyedropperSuccess = {dialog._eyedropperSuccess}, EyedropperColor = {dialog.EyedropperColor}, LastEyedropperColor = {_lastEyedropperColor}, SelectedColor = {dialog.SelectedColor}");

            // Priority 1: Check if eyedropper captured a color successfully
            // First check static field which is guaranteed to survive
            if (_lastEyedropperColor.HasValue && (dialog._eyedropperSuccess || dialog._okClicked))
            {
                System.Diagnostics.Debug.WriteLine($"PixiEditorColorPickerDialog.ShowDialog: Returning eyedropper color from static field {_lastEyedropperColor.Value}");
                var color = _lastEyedropperColor.Value;
                _lastEyedropperColor = null; // Clear for next use
                return color;
            }

            // Fallback to instance field check
            if (dialog._eyedropperSuccess && dialog.EyedropperColor.HasValue)
            {
                System.Diagnostics.Debug.WriteLine($"PixiEditorColorPickerDialog.ShowDialog: Returning eyedropper color {dialog.EyedropperColor.Value}");
                return dialog.EyedropperColor.Value;
            }

            // Priority 2: Check if OK button was clicked or DialogResult is true
            if (result == true || dialog._okClicked)
            {
                System.Diagnostics.Debug.WriteLine($"PixiEditorColorPickerDialog.ShowDialog: Returning selected color {dialog.SelectedColor}");
                return dialog.SelectedColor;
            }

            // Priority 3: Dialog was cancelled
            System.Diagnostics.Debug.WriteLine("PixiEditorColorPickerDialog.ShowDialog: Returning null (cancelled)");
            return null;
        }
    }
}