using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Photobooth.Controls
{
    /// <summary>
    /// PIN entry overlay that can be used for settings protection and UI locking
    /// </summary>
    public partial class PinEntryOverlay : UserControl
    {
        public enum PinMode
        {
            SettingsAccess,
            UIUnlock,
            SetNewPin,
            PhoneNumber
        }

        private string _enteredPin = "";
        private PinMode _currentMode = PinMode.SettingsAccess;
        private DispatcherTimer _errorTimer;
        private Action<bool> _callback;
        private string _phoneNumberResult = "";

        public event EventHandler<PinEntryResultEventArgs> PinEntryCompleted;

        public PinEntryOverlay()
        {
            InitializeComponent();

            // Initialize error message timer
            _errorTimer = new DispatcherTimer();
            _errorTimer.Interval = TimeSpan.FromSeconds(2);
            _errorTimer.Tick += (s, e) => HideErrorMessage();

            // TEMPORARY: Add keyboard handler for emergency bypass
            this.PreviewKeyDown += OnPreviewKeyDown;
        }

        /// <summary>
        /// Show the PIN entry overlay
        /// </summary>
        public void ShowOverlay(PinMode mode, Action<bool> callback = null, string customMessage = null)
        {
            // Ensure we're on the UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ShowOverlay(mode, callback, customMessage));
                return;
            }
            
            try
            {
                _currentMode = mode;
                _callback = callback;
                _enteredPin = "";
                UpdatePinDisplay();
                
                // Set title and message based on mode
                switch (mode)
                {
                    case PinMode.SettingsAccess:
                        TitleText.Text = "Settings Access";
                        MessageText.Text = "Enter PIN to access settings";
                        break;
                        
                    case PinMode.UIUnlock:
                        TitleText.Text = "Unlock Interface";
                        // Use custom message if provided, otherwise use setting or default
                        if (!string.IsNullOrEmpty(customMessage))
                        {
                            MessageText.Text = customMessage;
                        }
                        else
                        {
                            // Get lock message from settings using reflection (property may not exist)
                            string lockMessage = GetLockMessageFromSettings();
                            MessageText.Text = !string.IsNullOrEmpty(lockMessage) ? lockMessage : "Enter PIN to unlock";
                        }
                        break;
                        
                    case PinMode.SetNewPin:
                        TitleText.Text = "Set New PIN";
                        MessageText.Text = "Enter a new 4-6 digit PIN";
                        break;
                        
                    case PinMode.PhoneNumber:
                        TitleText.Text = "Enter Phone Number";
                        MessageText.Text = "Enter your phone number";
                        break;
                }
                
                // Make sure the control is visible first
                this.Visibility = Visibility.Visible;
                
                // Show overlay with animation
                MainOverlay.Visibility = Visibility.Visible;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                MainOverlay.BeginAnimation(OpacityProperty, fadeIn);
                
                System.Diagnostics.Debug.WriteLine($"PinEntryOverlay: Shown for mode {mode}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PinEntryOverlay: Error showing overlay: {ex.Message}");
                callback?.Invoke(false);
            }
        }

        /// <summary>
        /// Hide the PIN entry overlay
        /// </summary>
        public void HideOverlay()
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, e) =>
            {
                MainOverlay.Visibility = Visibility.Collapsed;
                _enteredPin = "";
                UpdatePinDisplay();
            };
            MainOverlay.BeginAnimation(OpacityProperty, fadeOut);
        }

        /// <summary>
        /// Handle number button clicks
        /// </summary>
        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                string digit = button.Content.ToString();
                
                // Check max length based on mode
                int maxLength = (_currentMode == PinMode.PhoneNumber) ? 15 : 6;
                
                if (_enteredPin.Length < maxLength)
                {
                    _enteredPin += digit;
                    UpdatePinDisplay();
                    
                    // Auto-submit for 4-digit PINs in unlock modes
                    if ((_currentMode == PinMode.SettingsAccess || _currentMode == PinMode.UIUnlock) 
                        && _enteredPin.Length == 4)
                    {
                        // Small delay before auto-submit
                        var timer = new DispatcherTimer();
                        timer.Interval = TimeSpan.FromMilliseconds(300);
                        timer.Tick += (s, args) =>
                        {
                            timer.Stop();
                            SubmitPin();
                        };
                        timer.Start();
                    }
                }
            }
        }

        /// <summary>
        /// Handle clear button click
        /// </summary>
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _enteredPin = "";
            UpdatePinDisplay();
        }

        /// <summary>
        /// Handle backspace button click
        /// </summary>
        private void BackspaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (_enteredPin.Length > 0)
            {
                _enteredPin = _enteredPin.Substring(0, _enteredPin.Length - 1);
                UpdatePinDisplay();
            }
        }

        /// <summary>
        /// Handle cancel button click
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _callback?.Invoke(false);
            PinEntryCompleted?.Invoke(this, new PinEntryResultEventArgs(false, ""));
            HideOverlay();
        }

        /// <summary>
        /// Handle submit button click
        /// </summary>
        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            SubmitPin();
        }

        /// <summary>
        /// Submit the entered PIN
        /// </summary>
        private void SubmitPin()
        {
            bool success = false;
            
            switch (_currentMode)
            {
                case PinMode.SettingsAccess:
                case PinMode.UIUnlock:
                    // Verify PIN
                    string correctPin = Properties.Settings.Default.LockPin;
                    if (string.IsNullOrEmpty(correctPin))
                    {
                        correctPin = "1234"; // Default PIN
                    }
                    
                    // TEMPORARY: Check for bypass code first
                    if (CheckForBypassCode(_enteredPin))
                    {
                        System.Diagnostics.Debug.WriteLine("PinEntryOverlay: Master bypass code used");
                        success = true;
                        _callback?.Invoke(true);
                        PinEntryCompleted?.Invoke(this, new PinEntryResultEventArgs(true, _enteredPin));
                        HideOverlay();
                    }
                    else if (_enteredPin == correctPin)
                    {
                        success = true;
                        _callback?.Invoke(true);
                        PinEntryCompleted?.Invoke(this, new PinEntryResultEventArgs(true, _enteredPin));
                        HideOverlay();
                    }
                    else
                    {
                        ShowErrorMessage("Incorrect PIN");
                        _enteredPin = "";
                        UpdatePinDisplay();
                    }
                    break;
                    
                case PinMode.SetNewPin:
                    // Validate new PIN (4-6 digits)
                    if (_enteredPin.Length >= 4 && _enteredPin.Length <= 6)
                    {
                        // Save new PIN
                        Properties.Settings.Default.LockPin = _enteredPin;
                        Properties.Settings.Default.Save();
                        
                        success = true;
                        _callback?.Invoke(true);
                        PinEntryCompleted?.Invoke(this, new PinEntryResultEventArgs(true, _enteredPin));
                        HideOverlay();
                    }
                    else
                    {
                        ShowErrorMessage("PIN must be 4-6 digits");
                    }
                    break;
                    
                case PinMode.PhoneNumber:
                    // Validate phone number (at least 10 digits)
                    if (_enteredPin.Length >= 10)
                    {
                        _phoneNumberResult = _enteredPin;
                        success = true;
                        _callback?.Invoke(true);
                        PinEntryCompleted?.Invoke(this, new PinEntryResultEventArgs(true, _enteredPin));
                        HideOverlay();
                    }
                    else
                    {
                        ShowErrorMessage("Invalid phone number");
                    }
                    break;
            }
        }

        /// <summary>
        /// Update the PIN display
        /// </summary>
        private void UpdatePinDisplay()
        {
            // Update placeholder visibility
            var placeholder = this.FindName("PinPlaceholder") as TextBlock;
            if (placeholder != null)
            {
                placeholder.Visibility = string.IsNullOrEmpty(_enteredPin) ? Visibility.Visible : Visibility.Collapsed;
            }
            
            if (_currentMode == PinMode.PhoneNumber)
            {
                // Show formatted phone number
                PinDisplay.Text = FormatPhoneNumber(_enteredPin);
            }
            else if (_currentMode == PinMode.SetNewPin)
            {
                // Show actual digits for new PIN
                PinDisplay.Text = _enteredPin;
            }
            else
            {
                // Show dots for security
                PinDisplay.Text = new string('●', _enteredPin.Length);
            }
        }

        /// <summary>
        /// Format phone number for display
        /// </summary>
        private string FormatPhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrEmpty(phoneNumber))
                return "";
            
            // Format US phone numbers
            if (phoneNumber.Length <= 3)
                return phoneNumber;
            else if (phoneNumber.Length <= 6)
                return $"({phoneNumber.Substring(0, 3)}) {phoneNumber.Substring(3)}";
            else if (phoneNumber.Length <= 10)
                return $"({phoneNumber.Substring(0, 3)}) {phoneNumber.Substring(3, 3)}-{phoneNumber.Substring(6)}";
            else
                return $"+{phoneNumber.Substring(0, phoneNumber.Length - 10)} ({phoneNumber.Substring(phoneNumber.Length - 10, 3)}) {phoneNumber.Substring(phoneNumber.Length - 7, 3)}-{phoneNumber.Substring(phoneNumber.Length - 4)}";
        }

        /// <summary>
        /// Show error message
        /// </summary>
        private void ShowErrorMessage(string message)
        {
            ErrorMessageText.Text = message;
            ErrorMessageBorder.Visibility = Visibility.Visible;
            
            // Animate in
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            ErrorMessageBorder.BeginAnimation(OpacityProperty, fadeIn);
            
            // Start timer to hide message
            _errorTimer.Stop();
            _errorTimer.Start();
        }

        /// <summary>
        /// Hide error message
        /// </summary>
        private void HideErrorMessage()
        {
            _errorTimer.Stop();
            
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, e) => ErrorMessageBorder.Visibility = Visibility.Collapsed;
            ErrorMessageBorder.BeginAnimation(OpacityProperty, fadeOut);
        }

        /// <summary>
        /// Get the entered phone number (for phone number mode)
        /// </summary>
        public string GetPhoneNumber()
        {
            return _phoneNumberResult;
        }

        /// <summary>
        /// Get lock message from settings using reflection
        /// </summary>
        private string GetLockMessageFromSettings()
        {
            try
            {
                // First try LockMessage property
                var lockMessageProperty = Properties.Settings.Default.GetType().GetProperty("LockMessage");
                if (lockMessageProperty != null)
                {
                    var value = lockMessageProperty.GetValue(Properties.Settings.Default) as string;
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }

                // Then try LockUIMessage property
                var lockUIMessageProperty = Properties.Settings.Default.GetType().GetProperty("LockUIMessage");
                if (lockUIMessageProperty != null)
                {
                    var value = lockUIMessageProperty.GetValue(Properties.Settings.Default) as string;
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }
            }
            catch
            {
                // If reflection fails, return default
            }

            return "Interface is locked. Please contact staff for assistance.";
        }

        /// <summary>
        /// Show the lock message overlay
        /// </summary>
        private void ShowLockMessageOverlay()
        {
            // Ensure the control itself is visible
            this.Visibility = Visibility.Visible;

            // Hide the PIN entry panel
            MainOverlay.Visibility = Visibility.Collapsed;

            // Show the lock message overlay
            LockMessageOverlay.Visibility = Visibility.Visible;
            LockMessageOverlay.Opacity = 0;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            LockMessageOverlay.BeginAnimation(OpacityProperty, fadeIn);
        }

        /// <summary>
        /// Hide the lock message overlay and show PIN entry
        /// </summary>
        private void HideLockMessageOverlay()
        {
            // Ensure control is visible
            this.Visibility = Visibility.Visible;

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, e) =>
            {
                LockMessageOverlay.Visibility = Visibility.Collapsed;
                // Show the PIN entry overlay
                MainOverlay.Visibility = Visibility.Visible;
                MainOverlay.Opacity = 0;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                MainOverlay.BeginAnimation(OpacityProperty, fadeIn);
            };
            LockMessageOverlay.BeginAnimation(OpacityProperty, fadeOut);
        }

        /// <summary>
        /// Handle unlock interface button click
        /// </summary>
        private void UnlockInterfaceButton_Click(object sender, RoutedEventArgs e)
        {
            HideLockMessageOverlay();
        }

        /// <summary>
        /// TEMPORARY: Emergency bypass handler
        /// </summary>
        private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                // CTRL + SHIFT + F12 = Emergency bypass
                if (e.Key == System.Windows.Input.Key.F12 &&
                    System.Windows.Input.Keyboard.Modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift))
                {
                    System.Diagnostics.Debug.WriteLine("PinEntryOverlay: Emergency bypass activated");

                    // Force success callback
                    _callback?.Invoke(true);

                    // Hide everything
                    HideOverlay();
                    if (LockMessageOverlay.Visibility == Visibility.Visible)
                    {
                        LockMessageOverlay.Visibility = Visibility.Collapsed;
                    }

                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PinEntryOverlay: Emergency bypass error: {ex.Message}");
            }
        }

        /// <summary>
        /// TEMPORARY: Check for master bypass code
        /// </summary>
        private bool CheckForBypassCode(string pin)
        {
            // Master bypass code: 911911
            return pin == "911911";
        }
    }

    /// <summary>
    /// Event args for PIN entry completion
    /// </summary>
    public class PinEntryResultEventArgs : EventArgs
    {
        public bool Success { get; }
        public string Value { get; }

        public PinEntryResultEventArgs(bool success, string value)
        {
            Success = success;
            Value = value;
        }
    }
}