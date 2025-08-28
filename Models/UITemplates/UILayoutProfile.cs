using System;
using System.Collections.Generic;
using System.Windows;

namespace Photobooth.Models.UITemplates
{
    /// <summary>
    /// Represents a saved UI layout profile for different screen configurations
    /// </summary>
    public class UILayoutProfile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public ScreenConfiguration ScreenConfig { get; set; }
        public string ThumbnailPath { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastUsedDate { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
        public string Category { get; set; } // e.g., "Kiosk", "Tablet", "Desktop", "Custom"
        public Dictionary<string, UILayoutTemplate> Layouts { get; set; } // Key: "Portrait" or "Landscape"
        public ProfileMetadata Metadata { get; set; }

        public UILayoutProfile()
        {
            Id = Guid.NewGuid().ToString();
            CreatedDate = DateTime.Now;
            LastUsedDate = DateTime.Now;
            Layouts = new Dictionary<string, UILayoutTemplate>();
            Metadata = new ProfileMetadata();
            ScreenConfig = new ScreenConfiguration();
        }
    }

    /// <summary>
    /// Screen configuration for which this profile is optimized
    /// </summary>
    public class ScreenConfiguration
    {
        public string DeviceType { get; set; } // "Tablet", "Kiosk", "Desktop", "Surface"
        public Size Resolution { get; set; }
        public double DiagonalSize { get; set; } // Screen size in inches
        public double AspectRatio { get; set; }
        public bool IsTouchEnabled { get; set; }
        public double DPI { get; set; }
        public ScreenOrientation PreferredOrientation { get; set; }

        public ScreenConfiguration()
        {
            Resolution = new Size(1920, 1080);
            AspectRatio = 16.0 / 9.0;
            IsTouchEnabled = true;
            DPI = 96;
            PreferredOrientation = ScreenOrientation.Landscape;
        }

        public string GetDisplayName()
        {
            return $"{DeviceType} - {Resolution.Width}x{Resolution.Height}";
        }
    }

    public enum ScreenOrientation
    {
        Portrait,
        Landscape,
        Auto
    }

    /// <summary>
    /// Additional metadata for the profile
    /// </summary>
    public class ProfileMetadata
    {
        public string Author { get; set; }
        public string Version { get; set; }
        public List<string> Tags { get; set; }
        public Dictionary<string, object> CustomProperties { get; set; }
        public bool IsLocked { get; set; } // Prevent accidental modification
        public string Notes { get; set; }

        public ProfileMetadata()
        {
            Version = "1.0";
            Tags = new List<string>();
            CustomProperties = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Predefined screen profiles for common devices
    /// </summary>
    public static class PredefinedProfiles
    {
        public static UILayoutProfile CreateSurfacePro()
        {
            return new UILayoutProfile
            {
                Name = "Surface Pro",
                Description = "Optimized for Microsoft Surface Pro tablets",
                Category = "Tablet",
                ScreenConfig = new ScreenConfiguration
                {
                    DeviceType = "Surface",
                    Resolution = new Size(2736, 1824),
                    DiagonalSize = 12.3,
                    AspectRatio = 3.0 / 2.0,
                    IsTouchEnabled = true,
                    DPI = 267,
                    PreferredOrientation = ScreenOrientation.Auto
                }
            };
        }

        public static UILayoutProfile CreateiPadPro()
        {
            return new UILayoutProfile
            {
                Name = "iPad Pro 12.9\"",
                Description = "Optimized for iPad Pro 12.9 inch",
                Category = "Tablet",
                ScreenConfig = new ScreenConfiguration
                {
                    DeviceType = "Tablet",
                    Resolution = new Size(2732, 2048),
                    DiagonalSize = 12.9,
                    AspectRatio = 4.0 / 3.0,
                    IsTouchEnabled = true,
                    DPI = 264,
                    PreferredOrientation = ScreenOrientation.Auto
                }
            };
        }

        public static UILayoutProfile CreateKiosk1080p()
        {
            return new UILayoutProfile
            {
                Name = "Kiosk Full HD",
                Description = "Standard 1920x1080 kiosk display",
                Category = "Kiosk",
                ScreenConfig = new ScreenConfiguration
                {
                    DeviceType = "Kiosk",
                    Resolution = new Size(1920, 1080),
                    DiagonalSize = 32,
                    AspectRatio = 16.0 / 9.0,
                    IsTouchEnabled = true,
                    DPI = 96,
                    PreferredOrientation = ScreenOrientation.Landscape
                }
            };
        }

        public static UILayoutProfile CreateKiosk4K()
        {
            return new UILayoutProfile
            {
                Name = "Kiosk 4K",
                Description = "4K kiosk display (3840x2160)",
                Category = "Kiosk",
                ScreenConfig = new ScreenConfiguration
                {
                    DeviceType = "Kiosk",
                    Resolution = new Size(3840, 2160),
                    DiagonalSize = 43,
                    AspectRatio = 16.0 / 9.0,
                    IsTouchEnabled = true,
                    DPI = 103,
                    PreferredOrientation = ScreenOrientation.Landscape
                }
            };
        }

        public static UILayoutProfile CreatePortraitKiosk()
        {
            return new UILayoutProfile
            {
                Name = "Portrait Kiosk",
                Description = "Vertical kiosk display (1080x1920)",
                Category = "Kiosk",
                ScreenConfig = new ScreenConfiguration
                {
                    DeviceType = "Kiosk",
                    Resolution = new Size(1080, 1920),
                    DiagonalSize = 32,
                    AspectRatio = 9.0 / 16.0,
                    IsTouchEnabled = true,
                    DPI = 96,
                    PreferredOrientation = ScreenOrientation.Portrait
                }
            };
        }

        public static UILayoutProfile CreateDesktop()
        {
            return new UILayoutProfile
            {
                Name = "Desktop",
                Description = "Standard desktop monitor",
                Category = "Desktop",
                ScreenConfig = new ScreenConfiguration
                {
                    DeviceType = "Desktop",
                    Resolution = new Size(1920, 1080),
                    DiagonalSize = 24,
                    AspectRatio = 16.0 / 9.0,
                    IsTouchEnabled = false,
                    DPI = 96,
                    PreferredOrientation = ScreenOrientation.Landscape
                }
            };
        }

        public static List<UILayoutProfile> GetAllPredefined()
        {
            return new List<UILayoutProfile>
            {
                CreateSurfacePro(),
                CreateiPadPro(),
                CreateKiosk1080p(),
                CreateKiosk4K(),
                CreatePortraitKiosk(),
                CreateDesktop()
            };
        }
    }
}