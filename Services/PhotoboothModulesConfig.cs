using System;
using System.ComponentModel;
using System.Configuration;
using System.Runtime.CompilerServices;

namespace Photobooth.Services
{
    /// <summary>
    /// Configuration for photobooth modules (Video Recording and Boomerang)
    /// Integrates with existing camera settings
    /// </summary>
    public class PhotoboothModulesConfig : INotifyPropertyChanged
    {
        private static PhotoboothModulesConfig _instance;
        private bool _videoEnabled;
        private bool _boomerangEnabled;
        private int _videoDuration;
        private int _boomerangFrames;
        private int _boomerangSpeed;
        private bool _showVideoButton;
        private bool _showBoomerangButton;
        private bool _flipbookEnabled;
        private int _flipbookDuration;
        private bool _showFlipbookButton;
        
        public static PhotoboothModulesConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new PhotoboothModulesConfig();
                    _instance.LoadSettings();
                }
                return _instance;
            }
        }

        private PhotoboothModulesConfig()
        {
            // Default values
            _videoDuration = 30; // seconds
            _boomerangFrames = 10;
            _boomerangSpeed = 100; // milliseconds between frames
            _showVideoButton = true;
            _showBoomerangButton = true;
            _flipbookDuration = 4; // seconds
            _showFlipbookButton = true;
        }

        #region Properties

        /// <summary>
        /// Enable/disable video recording feature
        /// </summary>
        public bool VideoEnabled
        {
            get => _videoEnabled;
            set
            {
                if (_videoEnabled != value)
                {
                    _videoEnabled = value;
                    SaveSetting("VideoEnabled", value.ToString());
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Enable/disable boomerang feature
        /// </summary>
        public bool BoomerangEnabled
        {
            get => _boomerangEnabled;
            set
            {
                if (_boomerangEnabled != value)
                {
                    _boomerangEnabled = value;
                    SaveSetting("BoomerangEnabled", value.ToString());
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Maximum video recording duration in seconds
        /// </summary>
        public int VideoDuration
        {
            get => _videoDuration;
            set
            {
                value = Math.Max(5, Math.Min(300, value)); // 5 seconds to 5 minutes
                if (_videoDuration != value)
                {
                    _videoDuration = value;
                    SaveSetting("VideoDuration", value.ToString());
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Number of frames to capture for boomerang
        /// </summary>
        public int BoomerangFrames
        {
            get => _boomerangFrames;
            set
            {
                value = Math.Max(5, Math.Min(30, value)); // 5 to 30 frames
                if (_boomerangFrames != value)
                {
                    _boomerangFrames = value;
                    SaveSetting("BoomerangFrames", value.ToString());
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Speed between boomerang frames in milliseconds
        /// </summary>
        public int BoomerangSpeed
        {
            get => _boomerangSpeed;
            set
            {
                value = Math.Max(50, Math.Min(500, value)); // 50ms to 500ms
                if (_boomerangSpeed != value)
                {
                    _boomerangSpeed = value;
                    SaveSetting("BoomerangSpeed", value.ToString());
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Show/hide video button in UI
        /// </summary>
        public bool ShowVideoButton
        {
            get => _showVideoButton && _videoEnabled;
            set
            {
                if (_showVideoButton != value)
                {
                    _showVideoButton = value;
                    SaveSetting("ShowVideoButton", value.ToString());
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Show/hide boomerang button in UI
        /// </summary>
        public bool ShowBoomerangButton
        {
            get => _showBoomerangButton && _boomerangEnabled;
            set
            {
                if (_showBoomerangButton != value)
                {
                    _showBoomerangButton = value;
                    SaveSetting("ShowBoomerangButton", value.ToString());
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Enable/disable flipbook feature
        /// </summary>
        public bool FlipbookEnabled
        {
            get => _flipbookEnabled;
            set
            {
                if (_flipbookEnabled != value)
                {
                    _flipbookEnabled = value;
                    SaveSetting("FlipbookEnabled", value.ToString());
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Flipbook recording duration in seconds
        /// </summary>
        public int FlipbookDuration
        {
            get => _flipbookDuration;
            set
            {
                value = Math.Max(3, Math.Min(10, value)); // 3 to 10 seconds
                if (_flipbookDuration != value)
                {
                    _flipbookDuration = value;
                    SaveSetting("FlipbookDuration", value.ToString());
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Show/hide flipbook button in UI
        /// </summary>
        public bool ShowFlipbookButton
        {
            get => _showFlipbookButton && _flipbookEnabled;
            set
            {
                if (_showFlipbookButton != value)
                {
                    _showFlipbookButton = value;
                    SaveSetting("ShowFlipbookButton", value.ToString());
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region Settings Management

        private void LoadSettings()
        {
            try
            {
                // Load from Properties.Settings if available
                if (Properties.Settings.Default != null)
                {
                    _videoEnabled = GetBoolSetting("VideoEnabled", false);
                    _boomerangEnabled = GetBoolSetting("BoomerangEnabled", false);
                    _videoDuration = GetIntSetting("VideoDuration", 30);
                    _boomerangFrames = GetIntSetting("BoomerangFrames", 10);
                    _boomerangSpeed = GetIntSetting("BoomerangSpeed", 100);
                    _showVideoButton = GetBoolSetting("ShowVideoButton", true);
                    _showBoomerangButton = GetBoolSetting("ShowBoomerangButton", true);
                    _flipbookEnabled = GetBoolSetting("FlipbookEnabled", true);
                    _flipbookDuration = GetIntSetting("FlipbookDuration", 4);
                    _showFlipbookButton = GetBoolSetting("ShowFlipbookButton", true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading module settings: {ex.Message}");
            }
        }

        private bool GetBoolSetting(string key, bool defaultValue)
        {
            try
            {
                // Try to get the setting dynamically
                var property = Properties.Settings.Default.GetType().GetProperty(key);
                if (property != null && property.PropertyType == typeof(bool))
                {
                    return (bool)property.GetValue(Properties.Settings.Default);
                }
                
                // Fallback to indexer method
                if (Properties.Settings.Default[key] != null)
                {
                    return Convert.ToBoolean(Properties.Settings.Default[key]);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting bool setting {key}: {ex.Message}");
            }
            return defaultValue;
        }

        private int GetIntSetting(string key, int defaultValue)
        {
            try
            {
                // Try to get the setting dynamically
                var property = Properties.Settings.Default.GetType().GetProperty(key);
                if (property != null && property.PropertyType == typeof(int))
                {
                    return (int)property.GetValue(Properties.Settings.Default);
                }
                
                // Fallback to indexer method
                if (Properties.Settings.Default[key] != null)
                {
                    return Convert.ToInt32(Properties.Settings.Default[key]);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting int setting {key}: {ex.Message}");
            }
            return defaultValue;
        }

        private void SaveSetting(string key, string value)
        {
            try
            {
                // Try to set the property dynamically
                var property = Properties.Settings.Default.GetType().GetProperty(key);
                if (property != null)
                {
                    if (property.PropertyType == typeof(bool))
                    {
                        property.SetValue(Properties.Settings.Default, Convert.ToBoolean(value));
                    }
                    else if (property.PropertyType == typeof(int))
                    {
                        property.SetValue(Properties.Settings.Default, Convert.ToInt32(value));
                    }
                    else if (property.PropertyType == typeof(string))
                    {
                        property.SetValue(Properties.Settings.Default, value);
                    }
                    else
                    {
                        // Use indexer for other types
                        Properties.Settings.Default[key] = value;
                    }
                }
                else
                {
                    // Fallback to indexer
                    Properties.Settings.Default[key] = value;
                }
                
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving setting {key}: {ex.Message}");
            }
        }

        public void SaveAllSettings()
        {
            try
            {
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving all settings: {ex.Message}");
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    /// <summary>
    /// Capture mode enumeration for UI
    /// </summary>
    public enum CaptureMode
    {
        Photo,
        Video,
        Boomerang,
        Gif,
        GreenScreen,
        AI,
        Flipbook
    }
}