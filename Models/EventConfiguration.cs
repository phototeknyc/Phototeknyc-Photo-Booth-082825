using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Photobooth.Models
{
    /// <summary>
    /// Complete configuration for an event including all workflow settings
    /// </summary>
    public class EventConfiguration
    {
        // Event Basic Info
        public int EventId { get; set; }
        public string EventName { get; set; }
        public string EventType { get; set; }
        public string Location { get; set; }
        public DateTime? EventDate { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
        public string HostName { get; set; }
        public string ContactEmail { get; set; }
        public string ContactPhone { get; set; }
        public bool IsActive { get; set; } = true;
        
        // Template Configuration
        public List<EventTemplateConfig> Templates { get; set; } = new List<EventTemplateConfig>();
        
        // Photo Session Settings
        public int PhotosPerSession { get; set; } = 4;
        public int CountdownSeconds { get; set; } = 5;
        public bool ShowCountdown { get; set; } = true;
        public int DelayBetweenPhotos { get; set; } = 2;
        public int PhotoDisplayDuration { get; set; } = 3;
        public bool AutoClearSession { get; set; } = true;
        public int AutoClearTimeout { get; set; } = 60;
        
        // Capture Mode Settings
        public string CaptureMode { get; set; } = "Standard"; // Standard, Burst, GIF, Video, Boomerang
        public bool EnableRetake { get; set; } = true;
        public int RetakeTimeout { get; set; } = 30;
        public bool AllowMultipleRetakes { get; set; } = false;
        public int MaxRetakes { get; set; } = 1;
        
        // Print Settings (excludes physical printer selection - that's system-specific)
        public bool EnablePrinting { get; set; } = true;
        public bool ShowPrintButton { get; set; } = true;
        public int MaxSessionPrints { get; set; } = 2;
        public int MaxEventPrints { get; set; } = 0; // 0 = unlimited
        public int DefaultPrintCopies { get; set; } = 1;
        public bool AllowReprints { get; set; } = true;
        public bool ShowPrintDialog { get; set; } = false;
        public string PrintPaperSize { get; set; } = "4x6";
        public bool AutoPrint { get; set; } = false;
        public int AutoPrintCopies { get; set; } = 1;
        
        // Filter Settings
        public bool EnableFilters { get; set; } = true;
        public int DefaultFilter { get; set; } = 0;
        public int FilterIntensity { get; set; } = 100;
        public bool AllowFilterChange { get; set; } = true;
        public bool AutoApplyFilters { get; set; } = false;
        public bool ShowFilterPreview { get; set; } = true;
        public List<string> EnabledFilters { get; set; } = new List<string>();
        
        // Beauty Mode Settings
        public bool BeautyModeEnabled { get; set; } = false;
        public int BeautyModeIntensity { get; set; } = 50;
        public bool AutoApplyBeautyMode { get; set; } = false;
        
        // Sharing Settings
        public bool EnableSharing { get; set; } = true;
        public bool AutoUploadToCloud { get; set; } = true;
        public bool RequireConsent { get; set; } = false;
        
        // Gallery Settings
        public bool CreateEventGallery { get; set; } = true;
        public string GalleryUrl { get; set; }
        public string GalleryPassword { get; set; }
        public bool GalleryIsPublic { get; set; } = false;
        public int GalleryExpirationDays { get; set; } = 30;
        
        // UI/UX Settings
        public string BackgroundColor { get; set; } = "#333333";
        public string BackgroundImage { get; set; }
        public string AccentColor { get; set; } = "#007ACC";
        public bool FullscreenMode { get; set; } = true;
        public bool HideCursor { get; set; } = true;
        public double ButtonSizeScale { get; set; } = 1.0;
        public string WelcomeMessage { get; set; }
        public string ThankYouMessage { get; set; }
        public bool ShowEventInfo { get; set; } = true;
        
        // Live View Settings (camera selection is system-specific)
        public bool MirrorLiveView { get; set; } = false;
        public int LiveViewFrameRate { get; set; } = 30;
        
        // Advanced Workflow Settings
        public bool RequireEventSelection { get; set; } = false;
        public bool RequirePinCode { get; set; } = false;
        public string EventPinCode { get; set; }
        public bool EnableGuestMode { get; set; } = true;
        public bool TrackGuestInfo { get; set; } = false;
        public bool EnableWatermark { get; set; } = false;
        public string WatermarkImage { get; set; }
        public int WatermarkOpacity { get; set; } = 50;
        public string WatermarkPosition { get; set; } = "BottomRight";
        
        // Statistics Tracking
        public bool TrackStatistics { get; set; } = true;
        public int TotalPhotosTaken { get; set; } = 0;
        public int TotalPrintsMade { get; set; } = 0;
        public int TotalSessionsCompleted { get; set; } = 0;
        public DateTime? LastActivityTime { get; set; }
        
        // Sync Metadata
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime ModifiedDate { get; set; } = DateTime.Now;
        public string CreatedByBoothId { get; set; }
        public string LastModifiedByBoothId { get; set; }
        public string ConfigurationHash { get; set; }
        
        /// <summary>
        /// Creates a hash of the configuration for comparison
        /// </summary>
        public string GenerateHash()
        {
            var json = JsonConvert.SerializeObject(this, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Include
            });
            
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                var hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }
        
        /// <summary>
        /// Copies workflow settings from application settings
        /// </summary>
        public static EventConfiguration FromApplicationSettings(string eventName)
        {
            var settings = Properties.Settings.Default;
            var config = new EventConfiguration
            {
                EventName = eventName,
                
                // Photo Session Settings
                CountdownSeconds = settings.CountdownSeconds,
                ShowCountdown = settings.ShowCountdown,
                DelayBetweenPhotos = settings.DelayBetweenPhotos,
                PhotoDisplayDuration = settings.PhotoDisplayDuration,
                AutoClearSession = settings.AutoClearSession,
                AutoClearTimeout = settings.AutoClearTimeout,
                
                // Retake Settings
                EnableRetake = settings.EnableRetake,
                RetakeTimeout = settings.RetakeTimeout,
                AllowMultipleRetakes = settings.AllowMultipleRetakes,
                
                // Print Settings
                EnablePrinting = settings.EnablePrinting,
                ShowPrintButton = settings.ShowPrintButton,
                MaxSessionPrints = settings.MaxSessionPrints,
                MaxEventPrints = settings.MaxEventPrints,
                DefaultPrintCopies = settings.DefaultPrintCopies,
                AllowReprints = settings.AllowReprints,
                ShowPrintDialog = settings.ShowPrintDialog,
                PrintPaperSize = settings.PrintPaperSize,
                
                // Filter Settings
                EnableFilters = settings.EnableFilters,
                DefaultFilter = settings.DefaultFilter,
                FilterIntensity = settings.FilterIntensity,
                AllowFilterChange = settings.AllowFilterChange,
                AutoApplyFilters = settings.AutoApplyFilters,
                ShowFilterPreview = settings.ShowFilterPreview,
                
                // Beauty Mode
                BeautyModeEnabled = settings.BeautyModeEnabled,
                BeautyModeIntensity = settings.BeautyModeIntensity,
                
                // Sharing Settings (most sharing settings are system-level, not event-specific)
                
                // UI Settings
                BackgroundColor = settings.BackgroundColor,
                BackgroundImage = settings.BackgroundImage,
                FullscreenMode = settings.FullscreenMode,
                HideCursor = settings.HideCursor,
                ButtonSizeScale = settings.ButtonSizeScale,
                
                // Camera Settings
                MirrorLiveView = settings.MirrorLiveView,
                LiveViewFrameRate = settings.LiveViewFrameRate
            };
            
            return config;
        }
        
        /// <summary>
        /// Applies this configuration to application settings
        /// </summary>
        public void ApplyToApplicationSettings()
        {
            var settings = Properties.Settings.Default;
            
            // Photo Session Settings
            settings.CountdownSeconds = CountdownSeconds;
            settings.ShowCountdown = ShowCountdown;
            settings.DelayBetweenPhotos = DelayBetweenPhotos;
            settings.PhotoDisplayDuration = PhotoDisplayDuration;
            settings.AutoClearSession = AutoClearSession;
            settings.AutoClearTimeout = AutoClearTimeout;
            
            // Retake Settings
            settings.EnableRetake = EnableRetake;
            settings.RetakeTimeout = RetakeTimeout;
            settings.AllowMultipleRetakes = AllowMultipleRetakes;
            
            // Print Settings
            settings.EnablePrinting = EnablePrinting;
            settings.ShowPrintButton = ShowPrintButton;
            settings.MaxSessionPrints = MaxSessionPrints;
            settings.MaxEventPrints = MaxEventPrints;
            settings.DefaultPrintCopies = DefaultPrintCopies;
            settings.AllowReprints = AllowReprints;
            settings.ShowPrintDialog = ShowPrintDialog;
            settings.PrintPaperSize = PrintPaperSize;
            
            // Filter Settings
            settings.EnableFilters = EnableFilters;
            settings.DefaultFilter = DefaultFilter;
            settings.FilterIntensity = FilterIntensity;
            settings.AllowFilterChange = AllowFilterChange;
            settings.AutoApplyFilters = AutoApplyFilters;
            settings.ShowFilterPreview = ShowFilterPreview;
            
            // Beauty Mode
            settings.BeautyModeEnabled = BeautyModeEnabled;
            settings.BeautyModeIntensity = BeautyModeIntensity;
            
            // Sharing settings are mostly system-level (not event-specific)
            
            // UI Settings
            settings.BackgroundColor = BackgroundColor;
            settings.BackgroundImage = BackgroundImage;
            settings.FullscreenMode = FullscreenMode;
            settings.HideCursor = HideCursor;
            settings.ButtonSizeScale = ButtonSizeScale;
            
            // Camera Settings
            settings.MirrorLiveView = MirrorLiveView;
            settings.LiveViewFrameRate = LiveViewFrameRate;
            
            // Save settings
            settings.Save();
        }
    }
    
    /// <summary>
    /// Template configuration for an event
    /// </summary>
    public class EventTemplateConfig
    {
        public int TemplateId { get; set; }
        public string TemplateName { get; set; }
        public string TemplateFile { get; set; }
        public bool IsDefault { get; set; }
        public int SortOrder { get; set; }
        public Dictionary<string, object> TemplateSettings { get; set; } = new Dictionary<string, object>();
    }
}