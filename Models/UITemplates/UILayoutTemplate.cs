using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Photobooth.Models.UITemplates
{
    public enum AnchorPoint
    {
        TopLeft, TopCenter, TopRight,
        MiddleLeft, Center, MiddleRight,  
        BottomLeft, BottomCenter, BottomRight
    }

    public enum SizeMode
    {
        Fixed,      // Always same pixel size
        Relative,   // Percentage of screen
        Stretch,    // Fill available space
        AspectFit   // Scale maintaining aspect ratio
    }

    public enum ElementType
    {
        Button,
        Image,
        Text,
        Countdown,
        Gallery,
        Camera,
        Background
    }

    public class UILayoutTemplate
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public Orientation PreferredOrientation { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public string Version { get; set; }
        public List<UIElementTemplate> Elements { get; set; }
        public UITheme Theme { get; set; }

        public UILayoutTemplate()
        {
            Elements = new List<UIElementTemplate>();
            Theme = new UITheme();
            Version = "1.0";
            CreatedDate = DateTime.Now;
            ModifiedDate = DateTime.Now;
        }
    }

    public class UIElementTemplate
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public ElementType Type { get; set; }
        public AnchorPoint Anchor { get; set; }
        public Point AnchorOffset { get; set; } // Percentage offset from anchor
        public SizeMode SizeMode { get; set; }
        public Size RelativeSize { get; set; } // Size as percentage of screen
        public Size MinSize { get; set; } // Minimum size in pixels
        public Size MaxSize { get; set; } // Maximum size in pixels
        public int ZIndex { get; set; }
        public double Opacity { get; set; }
        public double Rotation { get; set; }
        public bool IsVisible { get; set; }
        public bool IsEnabled { get; set; }
        public string ActionCommand { get; set; } // For buttons
        public Dictionary<string, object> Properties { get; set; }

        public UIElementTemplate()
        {
            Properties = new Dictionary<string, object>();
            IsVisible = true;
            IsEnabled = true;
            Opacity = 1.0;
            ZIndex = 0;
        }
    }

    public class UITheme
    {
        public Color PrimaryColor { get; set; }
        public Color SecondaryColor { get; set; }
        public Color AccentColor { get; set; }
        public Color BackgroundColor { get; set; }
        public Color TextColor { get; set; }
        public string FontFamily { get; set; }
        public double BaseFontSize { get; set; }
        public string BackgroundImage { get; set; }
        public double BackgroundOpacity { get; set; }

        public UITheme()
        {
            PrimaryColor = Color.FromRgb(76, 175, 80);     // Green
            SecondaryColor = Color.FromRgb(33, 150, 243);   // Blue
            AccentColor = Color.FromRgb(255, 152, 0);       // Orange
            BackgroundColor = Color.FromRgb(30, 30, 30);    // Dark Gray
            TextColor = Colors.White;
            FontFamily = "Segoe UI";
            BaseFontSize = 16;
            BackgroundOpacity = 1.0;
        }
    }

    public static class DefaultTemplates
    {
        public static UILayoutTemplate CreatePortraitTemplate()
        {
            return new UILayoutTemplate
            {
                Id = "default-portrait",
                Name = "Default Portrait Layout",
                Description = "Optimized layout for portrait orientation displays",
                PreferredOrientation = Orientation.Vertical,
                Elements = new List<UIElementTemplate>
                {
                    // Background
                    new UIElementTemplate
                    {
                        Id = "background",
                        Name = "Background",
                        Type = ElementType.Background,
                        Anchor = AnchorPoint.Center,
                        SizeMode = SizeMode.Stretch,
                        ZIndex = -100,
                        Properties = new Dictionary<string, object>
                        {
                            ["ImagePath"] = "Resources/Images/default-background.jpg",
                            ["StretchMode"] = "UniformToFill"
                        }
                    },

                    // Logo at top
                    new UIElementTemplate
                    {
                        Id = "logo",
                        Name = "Logo",
                        Type = ElementType.Image,
                        Anchor = AnchorPoint.TopCenter,
                        AnchorOffset = new Point(0, 5), // 5% from top
                        SizeMode = SizeMode.Relative,
                        RelativeSize = new Size(40, 15), // 40% width, 15% height
                        MinSize = new Size(200, 75),
                        MaxSize = new Size(600, 225),
                        ZIndex = 10,
                        Properties = new Dictionary<string, object>
                        {
                            ["ImagePath"] = "Resources/Images/logo.png",
                            ["PreserveAspectRatio"] = true
                        }
                    },

                    // Camera preview (larger in portrait)
                    new UIElementTemplate
                    {
                        Id = "camera-preview",
                        Name = "Camera Preview",
                        Type = ElementType.Camera,
                        Anchor = AnchorPoint.Center,
                        AnchorOffset = new Point(0, -5), // Slightly above center
                        SizeMode = SizeMode.Relative,
                        RelativeSize = new Size(85, 45), // 85% width, 45% height
                        MinSize = new Size(320, 240),
                        ZIndex = 5,
                        IsVisible = false, // Hidden until photo session starts
                        Properties = new Dictionary<string, object>
                        {
                            ["BorderThickness"] = 5,
                            ["BorderColor"] = "#FFFFFF",
                            ["CornerRadius"] = 20
                        }
                    },

                    // Start button (wider in portrait)
                    new UIElementTemplate
                    {
                        Id = "start-button",
                        Name = "Start Button",
                        Type = ElementType.Button,
                        Anchor = AnchorPoint.BottomCenter,
                        AnchorOffset = new Point(0, -15), // 15% from bottom
                        SizeMode = SizeMode.Relative,
                        RelativeSize = new Size(60, 12), // Wide button
                        MinSize = new Size(250, 80),
                        MaxSize = new Size(600, 150),
                        ZIndex = 20,
                        ActionCommand = "StartPhotoSession",
                        Properties = new Dictionary<string, object>
                        {
                            ["Text"] = "START",
                            ["FontSize"] = 36,
                            ["BackgroundColor"] = "#4CAF50",
                            ["HoverColor"] = "#66BB6A",
                            ["PressedColor"] = "#388E3C",
                            ["CornerRadius"] = 25,
                            ["IconPath"] = "Resources/Icons/camera.png"
                        }
                    },

                    // Settings button (top right corner)
                    new UIElementTemplate
                    {
                        Id = "settings-button",
                        Name = "Settings Button",
                        Type = ElementType.Button,
                        Anchor = AnchorPoint.TopRight,
                        AnchorOffset = new Point(-5, 5), // 5% margin
                        SizeMode = SizeMode.Relative,
                        RelativeSize = new Size(8, 8), // Square button
                        MinSize = new Size(50, 50),
                        MaxSize = new Size(80, 80),
                        ZIndex = 30,
                        ActionCommand = "OpenSettings",
                        Properties = new Dictionary<string, object>
                        {
                            ["IconPath"] = "Resources/Icons/settings.png",
                            ["BackgroundColor"] = "#424242",
                            ["Shape"] = "Circle"
                        }
                    },

                    // Gallery button (bottom left)
                    new UIElementTemplate
                    {
                        Id = "gallery-button",
                        Name = "Gallery Button",
                        Type = ElementType.Button,
                        Anchor = AnchorPoint.BottomLeft,
                        AnchorOffset = new Point(5, -5),
                        SizeMode = SizeMode.Relative,
                        RelativeSize = new Size(15, 10),
                        MinSize = new Size(100, 60),
                        MaxSize = new Size(200, 100),
                        ZIndex = 20,
                        ActionCommand = "OpenGallery",
                        Properties = new Dictionary<string, object>
                        {
                            ["Text"] = "Gallery",
                            ["IconPath"] = "Resources/Icons/gallery.png",
                            ["BackgroundColor"] = "#2196F3"
                        }
                    },

                    // Countdown display (centered, shows during capture)
                    new UIElementTemplate
                    {
                        Id = "countdown",
                        Name = "Countdown Display",
                        Type = ElementType.Countdown,
                        Anchor = AnchorPoint.Center,
                        AnchorOffset = new Point(0, 0),
                        SizeMode = SizeMode.Relative,
                        RelativeSize = new Size(30, 30),
                        MinSize = new Size(150, 150),
                        MaxSize = new Size(400, 400),
                        ZIndex = 100,
                        IsVisible = false, // Hidden until countdown starts
                        Properties = new Dictionary<string, object>
                        {
                            ["FontSize"] = 120,
                            ["TextColor"] = "#FFFFFF",
                            ["BackgroundColor"] = "#000000",
                            ["BackgroundOpacity"] = 0.7,
                            ["Shape"] = "Circle",
                            ["Animation"] = "Pulse"
                        }
                    }
                }
            };
        }

        public static UILayoutTemplate CreateLandscapeTemplate()
        {
            return new UILayoutTemplate
            {
                Id = "default-landscape",
                Name = "Default Landscape Layout",
                Description = "Optimized layout for landscape orientation displays",
                PreferredOrientation = Orientation.Horizontal,
                Elements = new List<UIElementTemplate>
                {
                    // Background
                    new UIElementTemplate
                    {
                        Id = "background",
                        Name = "Background",
                        Type = ElementType.Background,
                        Anchor = AnchorPoint.Center,
                        SizeMode = SizeMode.Stretch,
                        ZIndex = -100,
                        Properties = new Dictionary<string, object>
                        {
                            ["ImagePath"] = "Resources/Images/default-background.jpg",
                            ["StretchMode"] = "UniformToFill"
                        }
                    },

                    // Logo (smaller, top left in landscape)
                    new UIElementTemplate
                    {
                        Id = "logo",
                        Name = "Logo",
                        Type = ElementType.Image,
                        Anchor = AnchorPoint.TopLeft,
                        AnchorOffset = new Point(3, 3), // 3% margin
                        SizeMode = SizeMode.Relative,
                        RelativeSize = new Size(15, 12), // Smaller in landscape
                        MinSize = new Size(150, 60),
                        MaxSize = new Size(300, 120),
                        ZIndex = 10,
                        Properties = new Dictionary<string, object>
                        {
                            ["ImagePath"] = "Resources/Images/logo.png",
                            ["PreserveAspectRatio"] = true
                        }
                    },

                    // Live View Image (camera preview)
                    new UIElementTemplate
                    {
                        Id = "liveViewImage",
                        Name = "Live View",
                        Type = ElementType.Camera,
                        Anchor = AnchorPoint.Center,
                        AnchorOffset = new Point(0, 0),
                        SizeMode = SizeMode.Stretch,
                        RelativeSize = new Size(100, 100),
                        MinSize = new Size(480, 360),
                        ZIndex = 5,
                        IsVisible = true,
                        Properties = new Dictionary<string, object>
                        {
                            ["Stretch"] = "Uniform",
                            ["HorizontalAlignment"] = "Center",
                            ["VerticalAlignment"] = "Center"
                        }
                    },

                    // Start button overlay (centered like in PhotoboothTouchModern)
                    new UIElementTemplate
                    {
                        Id = "startButtonOverlay",
                        Name = "Start Button",
                        Type = ElementType.Button,
                        Anchor = AnchorPoint.Center,
                        AnchorOffset = new Point(0, 0),
                        SizeMode = SizeMode.Fixed,
                        MinSize = new Size(300, 300),
                        MaxSize = new Size(300, 300),
                        ZIndex = 20,
                        ActionCommand = "StartPhotoSession",
                        IsVisible = true,
                        Properties = new Dictionary<string, object>
                        {
                            ["Text"] = "TOUCH TO\nSTART",
                            ["FontSize"] = 48,
                            ["BackgroundColor"] = "#CC4CAF50",
                            ["HoverColor"] = "#DD5CBF5E",
                            ["PressedColor"] = "#FF388E3C",
                            ["CornerRadius"] = 150
                        }
                    },

                    // Modern Settings button (bottom right corner)
                    new UIElementTemplate
                    {
                        Id = "modernSettingsButton",
                        Name = "Settings",
                        Type = ElementType.Button,
                        Anchor = AnchorPoint.BottomRight,
                        AnchorOffset = new Point(-90, -90),
                        SizeMode = SizeMode.Fixed,
                        MinSize = new Size(70, 70),
                        MaxSize = new Size(70, 70),
                        ZIndex = 30,
                        ActionCommand = "OpenSettings",
                        IsVisible = true,
                        Properties = new Dictionary<string, object>
                        {
                            ["Text"] = "⚙",
                            ["FontSize"] = 35,
                            ["BackgroundColor"] = "#FF2196F3",
                            ["HoverColor"] = "#FF42A5F5",
                            ["PressedColor"] = "#FF1976D2",
                            ["CornerRadius"] = 35
                        }
                    },

                    // Cloud Settings button (top right)
                    new UIElementTemplate
                    {
                        Id = "cloudSettingsButton",
                        Name = "Cloud Settings",
                        Type = ElementType.Button,
                        Anchor = AnchorPoint.TopRight,
                        AnchorOffset = new Point(-20, 20),
                        SizeMode = SizeMode.Fixed,
                        MinSize = new Size(60, 60),
                        MaxSize = new Size(60, 60),
                        ZIndex = 20,
                        ActionCommand = "OpenCloudSettings",
                        IsVisible = true,
                        Properties = new Dictionary<string, object>
                        {
                            ["Text"] = "☁",
                            ["FontSize"] = 28,
                            ["BackgroundColor"] = "#323C4F",
                            ["HoverColor"] = "#3F4A5F",
                            ["BorderColor"] = "#576170",
                            ["BorderThickness"] = 2,
                            ["CornerRadius"] = 30
                        }
                    },

                    // Countdown overlay (centered)
                    new UIElementTemplate
                    {
                        Id = "countdownOverlay",
                        Name = "Countdown",
                        Type = ElementType.Countdown,
                        Anchor = AnchorPoint.Center,
                        AnchorOffset = new Point(0, 0),
                        SizeMode = SizeMode.Fixed,
                        MinSize = new Size(200, 200),
                        MaxSize = new Size(200, 200),
                        ZIndex = 50,
                        IsVisible = false,
                        Properties = new Dictionary<string, object>
                        {
                            ["FontSize"] = 120,
                            ["BackgroundColor"] = "#CC000000",
                            ["TextColor"] = "#FFFFFF",
                            ["CornerRadius"] = 100
                        }
                    },

                    // Info/Help button (bottom right)
                    new UIElementTemplate
                    {
                        Id = "info-button",
                        Name = "Info Button",
                        Type = ElementType.Button,
                        Anchor = AnchorPoint.BottomRight,
                        AnchorOffset = new Point(-3, -3),
                        SizeMode = SizeMode.Relative,
                        RelativeSize = new Size(5, 8),
                        MinSize = new Size(50, 50),
                        MaxSize = new Size(70, 70),
                        ZIndex = 20,
                        ActionCommand = "ShowInfo",
                        Properties = new Dictionary<string, object>
                        {
                            ["IconPath"] = "Resources/Icons/info.png",
                            ["BackgroundColor"] = "#9C27B0",
                            ["Shape"] = "Circle"
                        }
                    },

                    // Photo strip preview (bottom, horizontal in landscape)
                    new UIElementTemplate
                    {
                        Id = "photo-strip",
                        Name = "Photo Strip Preview",
                        Type = ElementType.Gallery,
                        Anchor = AnchorPoint.BottomCenter,
                        AnchorOffset = new Point(0, -3),
                        SizeMode = SizeMode.Relative,
                        RelativeSize = new Size(50, 12), // Wide strip at bottom
                        MinSize = new Size(400, 80),
                        ZIndex = 15,
                        IsVisible = false, // Shows after photos taken
                        Properties = new Dictionary<string, object>
                        {
                            ["Orientation"] = "Horizontal",
                            ["ItemSpacing"] = 10,
                            ["BackgroundOpacity"] = 0.8
                        }
                    },

                    // Countdown display
                    new UIElementTemplate
                    {
                        Id = "countdown",
                        Name = "Countdown Display",
                        Type = ElementType.Countdown,
                        Anchor = AnchorPoint.Center,
                        AnchorOffset = new Point(0, 0),
                        SizeMode = SizeMode.Relative,
                        RelativeSize = new Size(20, 25), // Slightly rectangular
                        MinSize = new Size(150, 150),
                        MaxSize = new Size(300, 350),
                        ZIndex = 100,
                        IsVisible = false,
                        Properties = new Dictionary<string, object>
                        {
                            ["FontSize"] = 100,
                            ["TextColor"] = "#FFFFFF",
                            ["BackgroundColor"] = "#000000",
                            ["BackgroundOpacity"] = 0.7,
                            ["Shape"] = "Circle",
                            ["Animation"] = "Pulse"
                        }
                    }
                }
            };
        }
    }
}