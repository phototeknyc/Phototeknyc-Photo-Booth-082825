using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using CameraControl.Devices;

namespace Photobooth.Services
{
    /// <summary>
    /// Service for managing QR code and SMS sharing UI modals
    /// Follows clean architecture principles by separating UI management from business logic
    /// </summary>
    public class SharingUIService
    {
        private readonly FrameworkElement _parentContainer;
        private readonly PhotoboothSessionService _sessionService;
        private Grid _qrCodeOverlay;
        private Grid _smsPhonePadOverlay;
        private Image _qrCodeImage;
        private TextBlock _galleryUrlText;
        private TextBlock _smsPhoneDisplay;
        private string _currentPhoneNumber = "+1";
        
        // Events for parent page to handle
        public event Action QrCodeOverlayClosed;
        public event Action SmsOverlayClosed;
        public event Action<string> SmsPhoneNumberChanged;
        public event Action<string> SendSmsRequested;
        public event Action ShowSmsFromQrRequested;
        
        // SMS button state
        public event Action<bool, string> UpdateSmsButtonState;
        
        // QR button state
        public event Action<bool, string> UpdateQrButtonState;
        
        public SharingUIService(FrameworkElement parentContainer, PhotoboothSessionService sessionService = null)
        {
            _parentContainer = parentContainer ?? throw new ArgumentNullException(nameof(parentContainer));
            _sessionService = sessionService;
            Log.Debug($"SharingUIService: Initialized with parent container type: {_parentContainer.GetType().Name}");
        }
        
        /// <summary>
        /// Show QR code overlay with gallery URL and QR code image
        /// </summary>
        public void ShowQrCodeOverlay(string galleryUrl, BitmapImage qrCodeBitmap = null)
        {
            try
            {
                Log.Debug($"SharingUIService.ShowQrCodeOverlay: Showing QR overlay for URL: {galleryUrl}");
                
                // Stop auto-clear timer when showing overlay
                if (_sessionService != null)
                {
                    _sessionService.StopAutoClearTimer();
                    Log.Debug("Stopped auto-clear timer for QR overlay");
                }
                
                // Create overlay if not exists
                if (_qrCodeOverlay == null)
                {
                    Log.Debug("SharingUIService.ShowQrCodeOverlay: Creating QR overlay for first time");
                    CreateQrCodeOverlay();
                }
                else
                {
                    Log.Debug("SharingUIService.ShowQrCodeOverlay: Using existing QR overlay");
                }
                
                // Update content
                if (_galleryUrlText != null)
                {
                    _galleryUrlText.Text = !string.IsNullOrEmpty(galleryUrl) ? galleryUrl : "Generating link...";
                }
                
                if (_qrCodeImage != null && qrCodeBitmap != null)
                {
                    _qrCodeImage.Source = qrCodeBitmap;
                    Log.Debug("SharingUIService: QR code image set successfully");
                }
                else
                {
                    Log.Debug($"SharingUIService: QR code image not set - _qrCodeImage: {_qrCodeImage != null}, qrCodeBitmap: {qrCodeBitmap != null}");
                }
                
                // Show overlay
                _qrCodeOverlay.Visibility = Visibility.Visible;
                Log.Debug("SharingUIService: QR code overlay displayed");
            }
            catch (Exception ex)
            {
                Log.Error($"SharingUIService.ShowQrCodeOverlay: Error showing QR overlay: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Update SMS button availability based on queue status
        /// </summary>
        public void SetSmsButtonState(bool enabled, string tooltipMessage = null)
        {
            try
            {
                Log.Debug($"SharingUIService.SetSmsButtonState: Enabled={enabled}, Message={tooltipMessage}");
                UpdateSmsButtonState?.Invoke(enabled, tooltipMessage ?? (enabled ? "Send SMS" : "SMS not available"));
            }
            catch (Exception ex)
            {
                Log.Error($"SharingUIService.SetSmsButtonState: Error updating SMS button state: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Update QR button availability based on upload status
        /// </summary>
        public void SetQrButtonState(bool hasQr, string tooltipMessage = null)
        {
            try
            {
                Log.Debug($"SharingUIService.SetQrButtonState: HasQR={hasQr}, Message={tooltipMessage}");
                UpdateQrButtonState?.Invoke(hasQr, tooltipMessage ?? (hasQr ? "Show QR Code" : "QR Code not ready"));
            }
            catch (Exception ex)
            {
                Log.Error($"SharingUIService.SetQrButtonState: Error updating QR button state: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Hide QR code overlay
        /// </summary>
        public void HideQrCodeOverlay()
        {
            try
            {
                if (_qrCodeOverlay != null)
                {
                    _qrCodeOverlay.Visibility = Visibility.Collapsed;
                    Log.Debug("SharingUIService: QR code overlay hidden");
                }
                
                // Resume auto-clear timer when hiding overlay
                if (_sessionService != null && _sessionService.IsSessionActive)
                {
                    _sessionService.StartAutoClearTimer();
                    Log.Debug("Resumed auto-clear timer after QR overlay closed");
                }
                
                QrCodeOverlayClosed?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error($"SharingUIService.HideQrCodeOverlay: Error hiding QR overlay: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Show SMS phone pad overlay
        /// </summary>
        public void ShowSmsPhonePadOverlay()
        {
            try
            {
                Log.Debug("SharingUIService.ShowSmsPhonePadOverlay: Showing SMS phone pad");
                
                // Stop auto-clear timer when showing overlay
                if (_sessionService != null)
                {
                    _sessionService.StopAutoClearTimer();
                    Log.Debug("Stopped auto-clear timer for SMS overlay");
                }
                
                // Create overlay if not exists
                if (_smsPhonePadOverlay == null)
                {
                    Log.Debug("SharingUIService.ShowSmsPhonePadOverlay: Creating SMS overlay for first time");
                    CreateSmsPhonePadOverlay();
                }
                else
                {
                    Log.Debug("SharingUIService.ShowSmsPhonePadOverlay: Using existing SMS overlay");
                }
                
                // Reset phone number
                _currentPhoneNumber = "+1";
                UpdateSmsPhoneDisplay();
                
                // Show overlay
                _smsPhonePadOverlay.Visibility = Visibility.Visible;
                Log.Debug("SharingUIService: SMS phone pad overlay displayed");
            }
            catch (Exception ex)
            {
                Log.Error($"SharingUIService.ShowSmsPhonePadOverlay: Error showing SMS overlay: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Hide SMS phone pad overlay
        /// </summary>
        public void HideSmsPhonePadOverlay()
        {
            try
            {
                if (_smsPhonePadOverlay != null)
                {
                    _smsPhonePadOverlay.Visibility = Visibility.Collapsed;
                    Log.Debug("SharingUIService: SMS phone pad overlay hidden");
                }
                
                // Resume auto-clear timer when hiding overlay
                if (_sessionService != null && _sessionService.IsSessionActive)
                {
                    _sessionService.StartAutoClearTimer();
                    Log.Debug("Resumed auto-clear timer after SMS overlay closed");
                }
                
                SmsOverlayClosed?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error($"SharingUIService.HideSmsPhonePadOverlay: Error hiding SMS overlay: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Add a digit to the SMS phone number
        /// </summary>
        public void AddPhoneDigit(string digit)
        {
            try
            {
                if (_currentPhoneNumber.Length < 20) // Max phone number length
                {
                    _currentPhoneNumber += digit;
                    UpdateSmsPhoneDisplay();
                    SmsPhoneNumberChanged?.Invoke(_currentPhoneNumber);
                    Log.Debug($"SharingUIService: Added digit {digit}, phone number: {_currentPhoneNumber}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SharingUIService.AddPhoneDigit: Error adding digit: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Remove last digit from SMS phone number
        /// </summary>
        public void RemovePhoneDigit()
        {
            try
            {
                // Remove last digit, but keep "+1" as minimum
                if (_currentPhoneNumber.Length > 2)
                {
                    _currentPhoneNumber = _currentPhoneNumber.Substring(0, _currentPhoneNumber.Length - 1);
                    UpdateSmsPhoneDisplay();
                    SmsPhoneNumberChanged?.Invoke(_currentPhoneNumber);
                    Log.Debug($"SharingUIService: Removed digit, phone number: {_currentPhoneNumber}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SharingUIService.RemovePhoneDigit: Error removing digit: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get current phone number
        /// </summary>
        public string GetCurrentPhoneNumber()
        {
            return _currentPhoneNumber;
        }
        
        /// <summary>
        /// Create QR code overlay UI dynamically
        /// </summary>
        private void CreateQrCodeOverlay()
        {
            try
            {
                // Main overlay grid - make it semi-transparent so photos show through
                _qrCodeOverlay = new Grid
                {
                    Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), // More transparent
                    Visibility = Visibility.Collapsed
                };
                
                // Set grid to span all rows if parent is grid
                if (_parentContainer is Grid parentGrid)
                {
                    Grid.SetRowSpan(_qrCodeOverlay, parentGrid.RowDefinitions.Count > 0 ? parentGrid.RowDefinitions.Count : 1);
                }
                
                // Main content border - make it smaller and positioned higher
                var contentBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(240, 30, 30, 30)), // More opaque background for readability
                    CornerRadius = new CornerRadius(25),
                    Width = 450,
                    Height = 550,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 50, 0, 0), // Position from top so photo strip at bottom is visible
                    BorderBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Effect = new DropShadowEffect 
                    { 
                        Color = Color.FromArgb(128, 0, 0, 0), 
                        BlurRadius = 30, 
                        ShadowDepth = 0, 
                        Opacity = 0.5 
                    }
                };
                
                // Content grid
                var contentGrid = new Grid();
                for (int i = 0; i < 6; i++)
                {
                    contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                
                // Close button
                var closeButton = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Width = 40,
                    Height = 40,
                    Margin = new Thickness(0, 10, 10, 0),
                    Background = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    FontSize = 20,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Content = "\uE711",
                    Cursor = Cursors.Hand
                };
                closeButton.Click += (s, e) => HideQrCodeOverlay();
                Grid.SetRow(closeButton, 0);
                contentGrid.Children.Add(closeButton);
                
                // Title
                var titleText = new TextBlock
                {
                    Text = "Your Photos Are Ready!",
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 20, 0, 20)
                };
                Grid.SetRow(titleText, 0);
                contentGrid.Children.Add(titleText);
                
                // QR Code container - smaller to fit in reduced modal
                var qrCodeBorder = new Border
                {
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(15),
                    Width = 240,
                    Height = 240,
                    Margin = new Thickness(0, 10, 0, 15),
                    Padding = new Thickness(10)
                };
                
                _qrCodeImage = new Image { Stretch = Stretch.Uniform };
                qrCodeBorder.Child = _qrCodeImage;
                Grid.SetRow(qrCodeBorder, 1);
                contentGrid.Children.Add(qrCodeBorder);
                
                // Scan instruction
                var scanText = new TextBlock
                {
                    Text = "Scan with your phone camera",
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromArgb(224, 255, 255, 255)),
                    Margin = new Thickness(0, 0, 0, 15)
                };
                Grid.SetRow(scanText, 2);
                contentGrid.Children.Add(scanText);
                
                // Gallery URL display
                var urlBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)),
                    CornerRadius = new CornerRadius(20),
                    Padding = new Thickness(15, 8, 15, 8),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 20)
                };
                
                _galleryUrlText = new TextBlock
                {
                    Text = "Generating link...",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromArgb(176, 255, 255, 255)),
                    FontFamily = new FontFamily("Consolas")
                };
                urlBorder.Child = _galleryUrlText;
                Grid.SetRow(urlBorder, 3);
                contentGrid.Children.Add(urlBorder);
                
                // SMS Option button
                var smsButton = new Button
                {
                    Background = new SolidColorBrush(Color.FromRgb(37, 211, 102)), // WhatsApp green
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(30, 15, 30, 15),
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 20)
                };
                
                var smsButtonPanel = new StackPanel { Orientation = Orientation.Horizontal };
                smsButtonPanel.Children.Add(new TextBlock { Text = "📱", FontSize = 20, Margin = new Thickness(0, 0, 10, 0) });
                smsButtonPanel.Children.Add(new TextBlock { Text = "Send via SMS" });
                smsButton.Content = smsButtonPanel;
                smsButton.Click += (s, e) => 
                {
                    HideQrCodeOverlay();
                    ShowSmsFromQrRequested?.Invoke();
                };
                Grid.SetRow(smsButton, 4);
                contentGrid.Children.Add(smsButton);
                
                contentBorder.Child = contentGrid;
                _qrCodeOverlay.Children.Add(contentBorder);
                
                // Add to parent container
                if (_parentContainer is Panel parentPanel)
                {
                    parentPanel.Children.Add(_qrCodeOverlay);
                    Log.Debug("SharingUIService: QR code overlay added to panel container");
                }
                else
                {
                    Log.Error($"SharingUIService: Parent container is not a Panel, it's {_parentContainer.GetType().Name}");
                    throw new InvalidOperationException($"Parent container must be a Panel, but got {_parentContainer.GetType().Name}");
                }
                
                Log.Debug("SharingUIService: QR code overlay UI created");
            }
            catch (Exception ex)
            {
                Log.Error($"SharingUIService.CreateQrCodeOverlay: Error creating QR overlay: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Create SMS phone pad overlay UI dynamically
        /// </summary>
        private void CreateSmsPhonePadOverlay()
        {
            try
            {
                // Main overlay grid
                _smsPhonePadOverlay = new Grid
                {
                    Background = new SolidColorBrush(Color.FromArgb(224, 0, 0, 0)),
                    Visibility = Visibility.Collapsed
                };
                
                // Set grid to span all rows if parent is grid
                if (_parentContainer is Grid parentGrid)
                {
                    Grid.SetRowSpan(_smsPhonePadOverlay, parentGrid.RowDefinitions.Count > 0 ? parentGrid.RowDefinitions.Count : 1);
                }
                
                // Main content border
                var contentBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                    CornerRadius = new CornerRadius(25),
                    Width = 400,
                    Height = 600,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    BorderBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Effect = new DropShadowEffect 
                    { 
                        Color = Color.FromArgb(128, 0, 0, 0), 
                        BlurRadius = 30, 
                        ShadowDepth = 0, 
                        Opacity = 0.5 
                    }
                };
                
                // Content grid
                var contentGrid = new Grid { Margin = new Thickness(20) };
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header row
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Phone display
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Spacer
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Phone pad
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Send button
                
                // Close button
                var closeButton = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Width = 30,
                    Height = 30,
                    Background = Brushes.Transparent,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontSize = 16,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Content = "\uE711",
                    Cursor = Cursors.Hand
                };
                closeButton.Click += (s, e) => HideSmsPhonePadOverlay();
                Grid.SetRow(closeButton, 0);
                contentGrid.Children.Add(closeButton);
                
                // Title
                var titleText = new TextBlock
                {
                    Text = "Send SMS",
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 20)
                };
                Grid.SetRow(titleText, 0);
                contentGrid.Children.Add(titleText);
                
                // Phone number display
                var phoneBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(15, 10, 15, 10),
                    Margin = new Thickness(0, 0, 0, 20)
                };
                
                _smsPhoneDisplay = new TextBlock
                {
                    Text = "+1",
                    FontSize = 18,
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Consolas"),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                phoneBorder.Child = _smsPhoneDisplay;
                Grid.SetRow(phoneBorder, 1);
                contentGrid.Children.Add(phoneBorder);
                
                // Phone pad grid
                var phonePadGrid = new UniformGrid
                {
                    Columns = 3,
                    Rows = 4,
                    Margin = new Thickness(0, 0, 0, 20)
                };
                
                // Add number buttons 1-9
                for (int i = 1; i <= 9; i++)
                {
                    var btn = CreatePhonePadButton(i.ToString());
                    phonePadGrid.Children.Add(btn);
                }
                
                // Add *, 0, backspace
                phonePadGrid.Children.Add(CreatePhonePadButton("*"));
                phonePadGrid.Children.Add(CreatePhonePadButton("0"));
                
                var backspaceBtn = new Button
                {
                    Content = "⌫",
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontSize = 20,
                    Cursor = Cursors.Hand,
                    Height = 50
                };
                backspaceBtn.Click += (s, e) => RemovePhoneDigit();
                phonePadGrid.Children.Add(backspaceBtn);
                
                Grid.SetRow(phonePadGrid, 3);
                contentGrid.Children.Add(phonePadGrid);
                
                // Send SMS button
                var sendButton = new Button
                {
                    Content = "Send SMS",
                    Background = new SolidColorBrush(Color.FromRgb(37, 211, 102)), // WhatsApp green
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(30, 20, 30, 20),
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Height = 60,
                    Margin = new Thickness(0, 20, 0, 0)
                };
                sendButton.Click += (s, e) => 
                {
                    Log.Debug($"SharingUIService: Send SMS button clicked with phone number: {_currentPhoneNumber}");
                    SendSmsRequested?.Invoke(_currentPhoneNumber);
                };
                Grid.SetRow(sendButton, 4);
                contentGrid.Children.Add(sendButton);
                
                contentBorder.Child = contentGrid;
                _smsPhonePadOverlay.Children.Add(contentBorder);
                
                // Add to parent container
                if (_parentContainer is Panel parentPanel)
                {
                    parentPanel.Children.Add(_smsPhonePadOverlay);
                    Log.Debug("SharingUIService: SMS phone pad overlay added to panel container");
                }
                else
                {
                    Log.Error($"SharingUIService: Parent container is not a Panel, it's {_parentContainer.GetType().Name}");
                    throw new InvalidOperationException($"Parent container must be a Panel, but got {_parentContainer.GetType().Name}");
                }
                
                Log.Debug("SharingUIService: SMS phone pad overlay UI created");
            }
            catch (Exception ex)
            {
                Log.Error($"SharingUIService.CreateSmsPhonePadOverlay: Error creating SMS overlay: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Create a phone pad button
        /// </summary>
        private Button CreatePhonePadButton(string digit)
        {
            var btn = new Button
            {
                Content = digit,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Height = 50
            };
            
            btn.Click += (s, e) => AddPhoneDigit(digit);
            return btn;
        }
        
        /// <summary>
        /// Update SMS phone display with formatting
        /// </summary>
        private void UpdateSmsPhoneDisplay()
        {
            if (_smsPhoneDisplay == null) return;
            
            try
            {
                // Format phone number for display (e.g., +1 (555) 123-4567)
                string formatted = _currentPhoneNumber;
                if (_currentPhoneNumber.StartsWith("+1") && _currentPhoneNumber.Length > 2)
                {
                    string digits = _currentPhoneNumber.Substring(2);
                    if (digits.Length >= 10)
                    {
                        formatted = $"+1 ({digits.Substring(0, 3)}) {digits.Substring(3, 3)}-{digits.Substring(6)}";
                    }
                    else if (digits.Length >= 6)
                    {
                        formatted = $"+1 ({digits.Substring(0, 3)}) {digits.Substring(3)}";
                    }
                    else if (digits.Length >= 3)
                    {
                        formatted = $"+1 ({digits.Substring(0, 3)}) {digits.Substring(3)}";
                    }
                    else if (digits.Length > 0)
                    {
                        formatted = $"+1 ({digits}";
                    }
                }
                _smsPhoneDisplay.Text = formatted;
            }
            catch (Exception ex)
            {
                Log.Error($"SharingUIService.UpdateSmsPhoneDisplay: Error formatting phone: {ex.Message}");
                _smsPhoneDisplay.Text = _currentPhoneNumber; // Fallback to unformatted
            }
        }
        
        /// <summary>
        /// Cleanup resources
        /// </summary>
        public void Dispose()
        {
            try
            {
                // Remove from parent container
                if (_parentContainer is Panel parentPanel)
                {
                    if (_qrCodeOverlay != null && parentPanel.Children.Contains(_qrCodeOverlay))
                    {
                        parentPanel.Children.Remove(_qrCodeOverlay);
                    }
                    if (_smsPhonePadOverlay != null && parentPanel.Children.Contains(_smsPhonePadOverlay))
                    {
                        parentPanel.Children.Remove(_smsPhonePadOverlay);
                    }
                }
                
                // Clear references
                _qrCodeOverlay = null;
                _smsPhonePadOverlay = null;
                _qrCodeImage = null;
                _galleryUrlText = null;
                _smsPhoneDisplay = null;
                
                Log.Debug("SharingUIService: Disposed");
            }
            catch (Exception ex)
            {
                Log.Error($"SharingUIService.Dispose: Error during cleanup: {ex.Message}");
            }
        }
    }
}