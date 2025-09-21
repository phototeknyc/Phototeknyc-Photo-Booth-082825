using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Photobooth.Models
{
    /// <summary>
    /// Defines the placement zones for photos on a background
    /// </summary>
    public class PhotoPlacementData
    {
        /// <summary>
        /// List of placement zones for photos
        /// </summary>
        public List<PhotoPlacementZone> PlacementZones { get; set; } = new List<PhotoPlacementZone>();

        /// <summary>
        /// Background width for relative positioning
        /// </summary>
        public double BackgroundWidth { get; set; }

        /// <summary>
        /// Background height for relative positioning
        /// </summary>
        public double BackgroundHeight { get; set; }

        /// <summary>
        /// Whether to maintain photo aspect ratio
        /// </summary>
        public bool MaintainAspectRatio { get; set; } = true;

        /// <summary>
        /// Default photo aspect ratio (width/height)
        /// </summary>
        public double DefaultAspectRatio { get; set; } = 1.5; // 3:2 ratio

        /// <summary>
        /// Serializes the placement data to JSON
        /// </summary>
        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.None);
        }

        /// <summary>
        /// Deserializes placement data from JSON
        /// </summary>
        public static PhotoPlacementData FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return new PhotoPlacementData();

            try
            {
                return JsonConvert.DeserializeObject<PhotoPlacementData>(json) ?? new PhotoPlacementData();
            }
            catch
            {
                return new PhotoPlacementData();
            }
        }
    }

    /// <summary>
    /// Defines a single placement zone for a photo
    /// </summary>
    public class PhotoPlacementZone
    {
        /// <summary>
        /// Unique identifier for this zone
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Display name for this zone (e.g., "Photo 1", "Main Photo")
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// X position (relative to background, 0-1)
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// Y position (relative to background, 0-1)
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// Width (relative to background, 0-1)
        /// </summary>
        public double Width { get; set; }

        /// <summary>
        /// Height (relative to background, 0-1)
        /// </summary>
        public double Height { get; set; }

        /// <summary>
        /// Rotation angle in degrees
        /// </summary>
        public double Rotation { get; set; } = 0;

        /// <summary>
        /// Z-order for layering (higher values on top)
        /// </summary>
        public int ZIndex { get; set; } = 0;

        /// <summary>
        /// Whether this zone is enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Photo index this zone is for (0-based)
        /// </summary>
        public int PhotoIndex { get; set; }

        /// <summary>
        /// Optional border/frame settings
        /// </summary>
        public PhotoBorderSettings BorderSettings { get; set; }
    }

    /// <summary>
    /// Settings for photo borders/frames
    /// </summary>
    public class PhotoBorderSettings
    {
        /// <summary>
        /// Whether to show a border
        /// </summary>
        public bool ShowBorder { get; set; } = false;

        /// <summary>
        /// Border thickness in pixels
        /// </summary>
        public double BorderThickness { get; set; } = 5;

        /// <summary>
        /// Border color (hex string)
        /// </summary>
        public string BorderColor { get; set; } = "#FFFFFF";

        /// <summary>
        /// Corner radius for rounded corners
        /// </summary>
        public double CornerRadius { get; set; } = 0;

        /// <summary>
        /// Whether to add a drop shadow
        /// </summary>
        public bool ShowShadow { get; set; } = false;

        /// <summary>
        /// Shadow blur radius
        /// </summary>
        public double ShadowBlur { get; set; } = 10;

        /// <summary>
        /// Shadow offset X
        /// </summary>
        public double ShadowOffsetX { get; set; } = 5;

        /// <summary>
        /// Shadow offset Y
        /// </summary>
        public double ShadowOffsetY { get; set; } = 5;

        /// <summary>
        /// Shadow color (hex string)
        /// </summary>
        public string ShadowColor { get; set; } = "#000000";

        /// <summary>
        /// Shadow opacity (0-1)
        /// </summary>
        public double ShadowOpacity { get; set; } = 0.5;
    }
}