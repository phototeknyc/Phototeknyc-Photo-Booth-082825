using System;
using System.Windows;
using System.Windows.Controls;
using Photobooth.Controls;

namespace Photobooth.Services
{
    /// <summary>
    /// Generic PIN lock service for interface and settings protection
    /// Follows clean architecture - handles all PIN-related business logic
    /// </summary>
    public class PinLockService
    {
        private static PinLockService _instance;
        private bool _isInterfaceLocked = false;
        private PinEntryOverlay _pinEntryOverlay;
        
        // Events for state changes
        public event EventHandler<bool> InterfaceLockStateChanged;
        public event EventHandler<bool> SettingsAccessGranted;
        public event EventHandler<string> PinChanged;
        
        /// <summary>
        /// Singleton instance
        /// </summary>
        public static PinLockService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new PinLockService();
                }
                return _instance;
            }
        }
        
        private PinLockService()
        {
            // Initialize service
            LoadSettings();
        }
        
        /// <summary>
        /// Current lock state of the interface
        /// </summary>
        public bool IsInterfaceLocked
        {
            get => _isInterfaceLocked;
            set
            {
                if (_isInterfaceLocked != value)
                {
                    _isInterfaceLocked = value;
                    InterfaceLockStateChanged?.Invoke(this, value);
                    System.Diagnostics.Debug.WriteLine($"PinLockService: Interface lock state changed to {value}");
                }
            }
        }
        
        /// <summary>
        /// Check if PIN protection is enabled
        /// </summary>
        public bool IsPinProtectionEnabled => Properties.Settings.Default.EnableLockFeature;
        
        /// <summary>
        /// Get the current PIN (for validation)
        /// </summary>
        private string CurrentPin
        {
            get
            {
                string pin = Properties.Settings.Default.LockPin;
                return string.IsNullOrEmpty(pin) ? "1234" : pin; // Default PIN if not set
            }
        }
        
        /// <summary>
        /// Set the PIN entry overlay control
        /// </summary>
        public void SetPinEntryOverlay(PinEntryOverlay overlay)
        {
            _pinEntryOverlay = overlay;
        }
        
        /// <summary>
        /// Request settings access with PIN protection
        /// </summary>
        public void RequestSettingsAccess(Action<bool> callback)
        {
            try
            {
                // Check if PIN protection is enabled
                if (!IsPinProtectionEnabled)
                {
                    // No PIN protection, grant access immediately
                    callback?.Invoke(true);
                    SettingsAccessGranted?.Invoke(this, true);
                    return;
                }
                
                // Show PIN entry dialog
                if (_pinEntryOverlay != null)
                {
                    System.Diagnostics.Debug.WriteLine("PinLockService: Showing PIN entry overlay for settings access");
                    _pinEntryOverlay.ShowOverlay(PinEntryOverlay.PinMode.SettingsAccess, (success) =>
                    {
                        callback?.Invoke(success);
                        SettingsAccessGranted?.Invoke(this, success);
                        
                        if (success)
                        {
                            System.Diagnostics.Debug.WriteLine("PinLockService: Settings access granted");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("PinLockService: Settings access denied");
                        }
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("PinLockService: PIN entry overlay not set");
                    callback?.Invoke(false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PinLockService: Error requesting settings access: {ex.Message}");
                callback?.Invoke(false);
            }
        }
        
        /// <summary>
        /// Lock the interface
        /// </summary>
        public void LockInterface()
        {
            try
            {
                if (!IsPinProtectionEnabled)
                {
                    System.Diagnostics.Debug.WriteLine("PinLockService: Lock feature is disabled");
                    return;
                }
                
                IsInterfaceLocked = true;
                System.Diagnostics.Debug.WriteLine("PinLockService: Interface locked");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PinLockService: Error locking interface: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Request to set a new PIN
        /// </summary>
        public void RequestSetNewPin(Action<bool> callback)
        {
            try
            {
                if (_pinEntryOverlay != null)
                {
                    System.Diagnostics.Debug.WriteLine("PinLockService: Requesting new PIN setup");
                    _pinEntryOverlay.ShowOverlay(
                        PinEntryOverlay.PinMode.SetNewPin, 
                        callback,
                        "Enter a new 4-digit PIN"
                    );
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("PinLockService: PIN entry overlay not set");
                    callback?.Invoke(false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PinLockService: Error requesting new PIN: {ex.Message}");
                callback?.Invoke(false);
            }
        }
        
        /// <summary>
        /// Request PIN to unlock settings
        /// </summary>
        public void RequestPinForUnlock(Action<bool> callback)
        {
            try
            {
                if (!IsInterfaceLocked)
                {
                    callback?.Invoke(true);
                    return;
                }
                
                // Show PIN entry dialog for unlocking
                if (_pinEntryOverlay != null)
                {
                    System.Diagnostics.Debug.WriteLine("PinLockService: Requesting PIN to unlock settings");
                    _pinEntryOverlay.ShowOverlay(
                        PinEntryOverlay.PinMode.UIUnlock, 
                        callback,
                        "Enter PIN to unlock settings"
                    );
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("PinLockService: PIN entry overlay not set");
                    callback?.Invoke(false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PinLockService: Error requesting unlock: {ex.Message}");
                callback?.Invoke(false);
            }
        }
        
        /// <summary>
        /// Request to unlock the interface
        /// </summary>
        public void RequestInterfaceUnlock(Action<bool> callback)
        {
            try
            {
                if (!IsInterfaceLocked)
                {
                    callback?.Invoke(true);
                    return;
                }
                
                // Show PIN entry dialog with custom message from settings
                if (_pinEntryOverlay != null)
                {
                    string customMessage = "Enter PIN to unlock"; // TODO: Properties.Settings.Default.LockMessage;
                    _pinEntryOverlay.ShowOverlay(PinEntryOverlay.PinMode.UIUnlock, (success) =>
                    {
                        if (success)
                        {
                            IsInterfaceLocked = false;
                            System.Diagnostics.Debug.WriteLine("PinLockService: Interface unlocked");
                        }
                        
                        callback?.Invoke(success);
                    }, customMessage);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("PinLockService: PIN entry overlay not set");
                    callback?.Invoke(false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PinLockService: Error unlocking interface: {ex.Message}");
                callback?.Invoke(false);
            }
        }
        
        /// <summary>
        /// Toggle interface lock state
        /// </summary>
        public void ToggleInterfaceLock(Action<bool> callback = null)
        {
            if (IsInterfaceLocked)
            {
                RequestInterfaceUnlock(callback);
            }
            else
            {
                LockInterface();
                callback?.Invoke(true);
            }
        }
        
        /// <summary>
        /// Change the PIN
        /// </summary>
        public void ChangePin(Action<bool> callback)
        {
            try
            {
                // First verify current PIN
                if (_pinEntryOverlay != null)
                {
                    _pinEntryOverlay.ShowOverlay(PinEntryOverlay.PinMode.SettingsAccess, (verified) =>
                    {
                        if (verified)
                        {
                            // Now let user set new PIN
                            _pinEntryOverlay.ShowOverlay(PinEntryOverlay.PinMode.SetNewPin, (success) =>
                            {
                                if (success)
                                {
                                    PinChanged?.Invoke(this, Properties.Settings.Default.LockPin);
                                    System.Diagnostics.Debug.WriteLine("PinLockService: PIN changed successfully");
                                }
                                
                                callback?.Invoke(success);
                            });
                        }
                        else
                        {
                            callback?.Invoke(false);
                        }
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("PinLockService: PIN entry overlay not set");
                    callback?.Invoke(false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PinLockService: Error changing PIN: {ex.Message}");
                callback?.Invoke(false);
            }
        }
        
        /// <summary>
        /// Request phone number entry
        /// </summary>
        public void RequestPhoneNumber(Action<string> callback)
        {
            try
            {
                if (_pinEntryOverlay != null)
                {
                    _pinEntryOverlay.ShowOverlay(PinEntryOverlay.PinMode.PhoneNumber, (success) =>
                    {
                        if (success)
                        {
                            string phoneNumber = _pinEntryOverlay.GetPhoneNumber();
                            callback?.Invoke(phoneNumber);
                        }
                        else
                        {
                            callback?.Invoke(null);
                        }
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("PinLockService: PIN entry overlay not set");
                    callback?.Invoke(null);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PinLockService: Error requesting phone number: {ex.Message}");
                callback?.Invoke(null);
            }
        }
        
        /// <summary>
        /// Validate a PIN
        /// </summary>
        public bool ValidatePin(string pin)
        {
            return pin == CurrentPin;
        }
        
        /// <summary>
        /// Enable PIN protection
        /// </summary>
        public void EnablePinProtection(bool enable)
        {
            Properties.Settings.Default.EnableLockFeature = enable;
            Properties.Settings.Default.Save();
            
            if (!enable && IsInterfaceLocked)
            {
                // Unlock interface if disabling PIN protection
                IsInterfaceLocked = false;
            }
            
            System.Diagnostics.Debug.WriteLine($"PinLockService: PIN protection {(enable ? "enabled" : "disabled")}");
        }
        
        /// <summary>
        /// Load settings
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                // Ensure default PIN is set if none exists
                if (string.IsNullOrEmpty(Properties.Settings.Default.LockPin))
                {
                    Properties.Settings.Default.LockPin = "1234";
                    Properties.Settings.Default.Save();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PinLockService: Error loading settings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Reset PIN to default
        /// </summary>
        public void ResetToDefaultPin()
        {
            Properties.Settings.Default.LockPin = "1234";
            Properties.Settings.Default.Save();
            PinChanged?.Invoke(this, "1234");
            System.Diagnostics.Debug.WriteLine("PinLockService: PIN reset to default");
        }
    }
}