using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Photobooth.Services
{
    /// <summary>
    /// Helper class for displaying and managing gallery information
    /// </summary>
    public static class GalleryInfoHelper
    {
        /// <summary>
        /// Get gallery info for an event and copy to clipboard
        /// </summary>
        public static void CopyGalleryInfo(int eventId)
        {
            try
            {
                var eventService = new EventService();
                var (url, password) = eventService.GetEventGalleryInfo(eventId);
                
                if (!string.IsNullOrEmpty(url))
                {
                    string clipboardText;
                    string message;
                    
                    if (!string.IsNullOrEmpty(password))
                    {
                        clipboardText = $"{url}\nPassword: {password}";
                        message = $"Gallery URL and password copied to clipboard!\n\nURL: {url}\nPassword: {password}";
                    }
                    else
                    {
                        clipboardText = url;
                        message = $"Gallery URL copied to clipboard!\n\n{url}";
                    }
                    
                    Clipboard.SetText(clipboardText);
                    MessageBox.Show(message, "Gallery Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("No gallery URL found for this event. Gallery will be created automatically when photos are uploaded.", 
                        "Gallery Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error getting gallery info: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// Add password to existing gallery
        /// </summary>
        public static async void AddPasswordToGallery(int eventId)
        {
            try
            {
                var eventService = new EventService();
                var password = await eventService.AddPasswordToGallery(eventId);
                
                if (!string.IsNullOrEmpty(password))
                {
                    MessageBox.Show($"Password added to gallery!\n\nPassword: {password}\n\nThis has been copied to your clipboard.", 
                        "Password Added", MessageBoxButton.OK, MessageBoxImage.Information);
                    Clipboard.SetText($"Password: {password}");
                }
                else
                {
                    MessageBox.Show("Failed to add password to gallery.", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding password: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// Create a visual element showing gallery status
        /// </summary>
        public static UIElement CreateGalleryStatusIndicator(int eventId)
        {
            var eventService = new EventService();
            var (url, password) = eventService.GetEventGalleryInfo(eventId);
            
            var panel = new StackPanel 
            { 
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(5)
            };
            
            // Gallery icon
            var icon = new TextBlock
            {
                Text = "☁️",
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            };
            panel.Children.Add(icon);
            
            // Status text
            var statusText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            
            if (!string.IsNullOrEmpty(url))
            {
                statusText.Text = "Gallery Ready";
                statusText.Foreground = new SolidColorBrush(Colors.Green);
                
                // Add lock icon if password protected
                if (!string.IsNullOrEmpty(password))
                {
                    var lockIcon = new TextBlock
                    {
                        Text = " 🔒",
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    panel.Children.Add(lockIcon);
                }
            }
            else
            {
                statusText.Text = "Gallery Pending";
                statusText.Foreground = new SolidColorBrush(Colors.Gray);
            }
            
            panel.Children.Add(statusText);
            
            // Make clickable
            panel.Cursor = System.Windows.Input.Cursors.Hand;
            panel.ToolTip = "Click to copy gallery info";
            panel.MouseLeftButtonDown += (s, e) => CopyGalleryInfo(eventId);
            
            return panel;
        }
        
        /// <summary>
        /// Create buttons for gallery management
        /// </summary>
        public static Panel CreateGalleryButtons(int eventId)
        {
            var panel = new WrapPanel { Margin = new Thickness(5) };
            
            // Copy Link button
            var copyButton = new Button
            {
                Content = "📋 Copy Gallery Link",
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 5, 0),
                Background = new SolidColorBrush(Color.FromRgb(102, 126, 234)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            copyButton.Click += (s, e) => CopyGalleryInfo(eventId);
            panel.Children.Add(copyButton);
            
            // Add Password button (if gallery exists and has no password)
            var eventService = new EventService();
            var (url, password) = eventService.GetEventGalleryInfo(eventId);
            
            if (!string.IsNullOrEmpty(url) && string.IsNullOrEmpty(password))
            {
                var passwordButton = new Button
                {
                    Content = "🔒 Add Password",
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 5, 0),
                    Background = new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                passwordButton.Click += async (s, e) => AddPasswordToGallery(eventId);
                panel.Children.Add(passwordButton);
            }
            
            // Open Gallery button
            if (!string.IsNullOrEmpty(url))
            {
                var openButton = new Button
                {
                    Content = "🌐 Open Gallery",
                    Padding = new Thickness(10, 5, 10, 5),
                    Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                openButton.Click += (s, e) =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to open gallery: {ex.Message}", "Error", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                panel.Children.Add(openButton);
            }
            
            return panel;
        }
    }
}