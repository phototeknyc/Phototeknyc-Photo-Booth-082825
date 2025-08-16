using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CameraControl.Devices.Classes;
using CameraControl.Devices;

namespace Photobooth.Services
{
    /// <summary>
    /// Handles PIN entry and interface locking functionality
    /// </summary>
    public class PinLockService
    {
        public enum PinPadMode
        {
            Unlock,
            PhoneNumber
        }
        
        private readonly Pages.PhotoboothTouchModern _parent;
        private string _enteredPin = "";
        private PinPadMode _currentPinMode = PinPadMode.Unlock;
        private bool _isLocked = false;
        
        // UI Elements
        private readonly Grid pinEntryOverlay;
        private readonly TextBlock pinDotsDisplay;
        private readonly TextBlock pinDisplayBox;
        private readonly Button lockButton;
        private readonly StackPanel bottomControlBar;
        
        public bool IsLocked => _isLocked;
        public PinPadMode CurrentMode => _currentPinMode;
        public string EnteredPin => _enteredPin;
        
        public PinLockService(Pages.PhotoboothTouchModern parent)
        {
            _parent = parent;
            
            // Get UI elements from parent
            pinEntryOverlay = parent.FindName("pinEntryOverlay") as Grid;
            pinDotsDisplay = parent.FindName("pinDotsDisplay") as TextBlock;
            pinDisplayBox = parent.FindName("pinDisplayBox") as TextBlock;
            lockButton = parent.FindName("lockButton") as Button;
            bottomControlBar = parent.FindName("bottomControlBar") as StackPanel;
        }
        
        /// <summary>
        /// Toggle lock state
        /// </summary>
        public void ToggleLock()
        {
            if (!Properties.Settings.Default.EnableLockFeature)
            {
                _parent.ShowSimpleMessage("Lock feature is disabled in settings");
                return;
            }
            
            if (_isLocked)
            {
                // Show PIN entry dialog to unlock
                ShowPinEntryDialog();
            }
            else
            {
                // Lock the interface
                LockInterface();
            }
        }
        
        /// <summary>
        /// Lock the interface
        /// </summary>
        public void LockInterface()
        {
            _isLocked = true;
            
            if (lockButton != null)
            {
                lockButton.Content = "🔒";
                lockButton.ToolTip = "Unlock Interface";
            }
            
            // Disable critical controls
            DisableCriticalControls();
            
            // Hide bottom control bar
            if (bottomControlBar != null)
            {
                bottomControlBar.Visibility = Visibility.Collapsed;
            }
            
            _parent.ShowSimpleMessage("Interface locked");
            Log.Debug("PinLockService: Interface locked");
        }
        
        /// <summary>
        /// Unlock the interface
        /// </summary>
        public void UnlockInterface()
        {
            _isLocked = false;
            
            if (lockButton != null)
            {
                lockButton.Content = "🔓";
                lockButton.ToolTip = "Lock Interface";
            }
            
            // Enable critical controls
            EnableCriticalControls();
            
            _parent.ShowSimpleMessage("Interface unlocked");
            Log.Debug("PinLockService: Interface unlocked");
        }
        
        /// <summary>
        /// Show PIN entry dialog
        /// </summary>
        public void ShowPinEntryDialog()
        {
            _currentPinMode = PinPadMode.Unlock;
            _enteredPin = "";
            UpdatePinDots();
            
            if (pinDisplayBox != null)
                pinDisplayBox.Text = "Enter PIN to unlock";
            
            if (pinEntryOverlay != null)
                pinEntryOverlay.Visibility = Visibility.Visible;
            
            Log.Debug("PinLockService: PIN entry dialog shown");
        }
        
        /// <summary>
        /// Handle PIN pad button click
        /// </summary>
        public void HandlePinPadButton(string digit)
        {
            try
            {
                if (_currentPinMode == PinPadMode.Unlock)
                {
                    // PIN mode - max 6 digits
                    if (_enteredPin.Length < 6)
                    {
                        _enteredPin += digit;
                        UpdatePinDots();
                    }
                }
                else if (_currentPinMode == PinPadMode.PhoneNumber)
                {
                    // Phone number mode - different handling
                    if (_enteredPin.Length < 15)
                    {
                        _enteredPin += digit;
                        UpdatePinDots();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PinLockService: Error handling PIN pad button: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Clear entered PIN
        /// </summary>
        public void ClearPin()
        {
            _enteredPin = "";
            UpdatePinDots();
            Log.Debug("PinLockService: PIN cleared");
        }
        
        /// <summary>
        /// Submit entered PIN
        /// </summary>
        public bool SubmitPin()
        {
            try
            {
                if (_currentPinMode == PinPadMode.Unlock)
                {
                    // Check if PIN is correct
                    string correctPin = Properties.Settings.Default.LockPin;
                    
                    if (string.IsNullOrEmpty(correctPin))
                    {
                        correctPin = "1234"; // Default PIN
                    }
                    
                    if (_enteredPin == correctPin)
                    {
                        // Unlock the interface
                        UnlockInterface();
                        
                        // Close PIN entry overlay
                        if (pinEntryOverlay != null)
                            pinEntryOverlay.Visibility = Visibility.Collapsed;
                        
                        return true;
                    }
                    else
                    {
                        // Wrong PIN
                        _parent.ShowSimpleMessage("Incorrect PIN");
                        ClearPin();
                        return false;
                    }
                }
                else if (_currentPinMode == PinPadMode.PhoneNumber)
                {
                    // Phone number mode - validate and return
                    if (_enteredPin.Length >= 10)
                    {
                        // Close overlay
                        if (pinEntryOverlay != null)
                            pinEntryOverlay.Visibility = Visibility.Collapsed;
                        
                        return true;
                    }
                    else
                    {
                        _parent.ShowSimpleMessage("Please enter a valid phone number");
                        return false;
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"PinLockService: Error submitting PIN: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Cancel PIN entry
        /// </summary>
        public void CancelPinEntry()
        {
            _enteredPin = "";
            UpdatePinDots();
            
            if (pinEntryOverlay != null)
                pinEntryOverlay.Visibility = Visibility.Collapsed;
            
            Log.Debug("PinLockService: PIN entry cancelled");
        }
        
        /// <summary>
        /// Update PIN dots display
        /// </summary>
        private void UpdatePinDots()
        {
            if (pinDotsDisplay != null)
            {
                if (_currentPinMode == PinPadMode.Unlock)
                {
                    // Show dots for PIN
                    pinDotsDisplay.Text = new string('●', _enteredPin.Length);
                }
                else
                {
                    // Show formatted phone number
                    pinDotsDisplay.Text = FormatPhoneNumber(_enteredPin);
                }
            }
        }
        
        /// <summary>
        /// Format phone number for display
        /// </summary>
        private string FormatPhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrEmpty(phoneNumber))
                return "";
            
            // Remove any non-digit characters
            string digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
            
            // Format based on length
            if (digits.Length <= 3)
                return digits;
            else if (digits.Length <= 6)
                return $"({digits.Substring(0, 3)}) {digits.Substring(3)}";
            else if (digits.Length <= 10)
                return $"({digits.Substring(0, 3)}) {digits.Substring(3, 3)}-{digits.Substring(6)}";
            else
                return $"+{digits.Substring(0, 1)} ({digits.Substring(1, 3)}) {digits.Substring(4, 3)}-{digits.Substring(7)}";
        }
        
        /// <summary>
        /// Disable critical controls when locked
        /// </summary>
        private void DisableCriticalControls()
        {
            // Find and disable critical buttons
            var criticalButtons = new string[] 
            { 
                "startButton", "stopSessionButton", "eventSettingsButton", 
                "cameraSettingsButton", "modernSettingsButton", "exitButton",
                "galleryButton", "homeButton"
            };
            
            foreach (var buttonName in criticalButtons)
            {
                var button = _parent.FindName(buttonName) as Button;
                if (button != null)
                    button.IsEnabled = false;
            }
        }
        
        /// <summary>
        /// Enable critical controls when unlocked
        /// </summary>
        private void EnableCriticalControls()
        {
            // Find and enable critical buttons
            var criticalButtons = new string[] 
            { 
                "startButton", "stopSessionButton", "eventSettingsButton", 
                "cameraSettingsButton", "modernSettingsButton", "exitButton",
                "galleryButton", "homeButton"
            };
            
            foreach (var buttonName in criticalButtons)
            {
                var button = _parent.FindName(buttonName) as Button;
                if (button != null)
                    button.IsEnabled = true;
            }
        }
        
        /// <summary>
        /// Set PIN pad mode
        /// </summary>
        public void SetMode(PinPadMode mode)
        {
            _currentPinMode = mode;
            _enteredPin = "";
            UpdatePinDots();
            
            if (pinDisplayBox != null)
            {
                if (mode == PinPadMode.Unlock)
                    pinDisplayBox.Text = "Enter PIN to unlock";
                else
                    pinDisplayBox.Text = "Enter Phone Number";
            }
        }
    }
}