using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Photobooth.Database;
using Photobooth.Services;
using Photobooth.Windows;

namespace Photobooth.Pages
{
    public partial class EventSelectionPage : Page
    {
        private readonly EventService eventService;
        private List<EventData> allEvents;
        private EventData selectedEvent;
        
        public EventData SelectedEvent => selectedEvent;
        
        public EventSelectionPage()
        {
            InitializeComponent();
            
            eventService = new EventService();
            allEvents = new List<EventData>();
            
            Loaded += EventSelectionPage_Loaded;
        }
        
        private void EventSelectionPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadEvents();
            
            // Animate entrance
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(500)
            };
            this.BeginAnimation(OpacityProperty, fadeIn);
        }
        
        private void LoadEvents()
        {
            try
            {
                allEvents = eventService.GetAllEvents().ToList();
                DisplayEvents(allEvents);
                
                if (allEvents.Count == 0)
                {
                    EmptyState.Visibility = Visibility.Visible;
                    EventsPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    EmptyState.Visibility = Visibility.Collapsed;
                    EventsPanel.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading events: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                EmptyState.Visibility = Visibility.Visible;
            }
        }
        
        private void DisplayEvents(List<EventData> events)
        {
            EventsPanel.Children.Clear();
            
            foreach (var eventData in events)
            {
                var eventCard = CreateEventCard(eventData);
                EventsPanel.Children.Add(eventCard);
            }
        }
        
        private Border CreateEventCard(EventData eventData)
        {
            var border = new Border
            {
                Style = FindResource("EventCardStyle") as Style,
                Tag = eventData
            };
            
            var grid = new Grid();
            
            // Background gradient overlay
            var gradientBorder = new Border
            {
                CornerRadius = new CornerRadius(20),
                Opacity = 0.9
            };
            gradientBorder.Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(121, 40, 202), 0),    // Purple
                    new GradientStop(Color.FromRgb(255, 0, 128), 0.5),   // Pink
                    new GradientStop(Color.FromRgb(0, 217, 255), 1)      // Cyan
                }
            };
            
            // Content stack panel
            var content = new StackPanel
            {
                Margin = new Thickness(25),
                VerticalAlignment = VerticalAlignment.Center
            };
            
            // Event icon based on type
            string eventIcon = GetEventIcon(eventData.EventType);
            var iconText = new TextBlock
            {
                Text = eventIcon,
                FontSize = 32,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 10)
            };
            
            // Event name
            var nameText = new TextBlock
            {
                Text = eventData.Name ?? "Unnamed Event",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 5)
            };
            
            // Event type and date
            var typeText = new TextBlock
            {
                Text = eventData.EventType ?? "General Event",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(204, 255, 255, 255)),
                Margin = new Thickness(0, 0, 0, 3)
            };
            
            var dateText = new TextBlock
            {
                Text = eventData.EventDate?.ToString("MMM dd, yyyy") ?? "No date set",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(204, 255, 255, 255))
            };
            
            // Location if available
            if (!string.IsNullOrEmpty(eventData.Location))
            {
                var locationText = new TextBlock
                {
                    Text = $"📍 {eventData.Location}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(179, 255, 255, 255)),
                    Margin = new Thickness(0, 5, 0, 0)
                };
                content.Children.Add(iconText);
                content.Children.Add(nameText);
                content.Children.Add(typeText);
                content.Children.Add(dateText);
                content.Children.Add(locationText);
            }
            else
            {
                content.Children.Add(iconText);
                content.Children.Add(nameText);
                content.Children.Add(typeText);
                content.Children.Add(dateText);
            }
            
            grid.Children.Add(gradientBorder);
            grid.Children.Add(content);
            
            border.Child = grid;
            
            // Click handler
            border.MouseLeftButtonUp += EventCard_Click;
            
            return border;
        }
        
        private string GetEventIcon(string eventType)
        {
            switch (eventType?.ToLower())
            {
                case "wedding":
                    return "💒";
                case "birthday":
                    return "🎂";
                case "corporate":
                case "business":
                    return "🏢";
                case "party":
                    return "🎉";
                case "graduation":
                    return "🎓";
                case "anniversary":
                    return "💑";
                case "holiday":
                    return "🎄";
                case "festival":
                    return "🎪";
                default:
                    return "📸";
            }
        }
        
        private void EventCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is EventData eventData)
            {
                selectedEvent = eventData;
                LaunchPhotoBooth(eventData);
            }
        }
        
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var searchText = SearchBox.Text?.ToLower() ?? "";
            
            if (string.IsNullOrEmpty(searchText))
            {
                DisplayEvents(allEvents);
            }
            else
            {
                var filtered = allEvents.Where(ev =>
                    (ev.Name?.ToLower().Contains(searchText) == true) ||
                    (ev.EventType?.ToLower().Contains(searchText) == true) ||
                    (ev.Location?.ToLower().Contains(searchText) == true) ||
                    (ev.HostName?.ToLower().Contains(searchText) == true)
                ).ToList();
                
                DisplayEvents(filtered);
                
                if (filtered.Count == 0)
                {
                    EmptyState.Visibility = Visibility.Visible;
                }
                else
                {
                    EmptyState.Visibility = Visibility.Collapsed;
                }
            }
        }
        
        private void CreateEvent_Click(object sender, RoutedEventArgs e)
        {
            var createDialog = new EventCreateEditDialog();
            createDialog.Owner = Window.GetWindow(this);
            
            if (createDialog.ShowDialog() == true)
            {
                // The dialog already saves the event internally
                // Reload events to show the new event
                LoadEvents();
                
                // Get the most recently created event
                var newEvent = allEvents.OrderByDescending(ev => ev.CreatedDate).FirstOrDefault();
                if (newEvent != null)
                {
                    selectedEvent = newEvent;
                    LaunchPhotoBooth(selectedEvent);
                }
            }
        }
        
        private void QuickStart_Click(object sender, RoutedEventArgs e)
        {
            // Create a quick session event using the service
            var eventId = eventService.CreateEvent(
                eventName: $"Quick Session {DateTime.Now:MMM dd, HH:mm}",
                eventType: "Quick Session",
                eventDate: DateTime.Now
            );
            
            // Get the created event
            var quickEvent = eventService.GetEvent(eventId);
            
            // Automatically assign all available templates to the quick session
            var templateDatabase = new TemplateDatabase();
            var allTemplates = templateDatabase.GetAllTemplates();
            
            if (allTemplates != null && allTemplates.Count > 0)
            {
                // Assign all templates to the quick session
                foreach (var template in allTemplates)
                {
                    eventService.AssignTemplateToEvent(eventId, template.Id, false);
                }
            }
            
            selectedEvent = quickEvent;
            LaunchPhotoBooth(quickEvent);
        }
        
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Navigate back
            var parentWindow = Window.GetWindow(this);
            if (parentWindow is SurfacePhotoBoothWindow surfaceWindow)
            {
                surfaceWindow.NavigateBack();
            }
        }
        
        private void LaunchPhotoBooth(EventData eventData)
        {
            // Store the selected event in a service or static property
            PhotoboothSessionService.CurrentEvent = eventData;
            
            // Create a new full screen window for the photobooth
            var photoboothWindow = new Window
            {
                Title = $"Photo Booth - {eventData.Name}",
                WindowState = WindowState.Maximized,
                WindowStyle = WindowStyle.None, // Remove window chrome for full screen
                Background = new SolidColorBrush(Colors.Black),
                Topmost = false // Set to true if you want it always on top
            };
            
            // Create and configure the photobooth page
            var photoboothPage = new PhotoboothTouchModern();
            photoboothPage.SetEvent(eventData);
            
            // Set the page as window content
            photoboothWindow.Content = photoboothPage;
            
            // Show the full screen photobooth window
            photoboothWindow.Show();
            
            // Close the parent surface window if needed
            var parentWindow = Window.GetWindow(this);
            if (parentWindow != null)
            {
                parentWindow.Close();
            }
        }
    }
    
    // Simple service to hold the current event
    public static class PhotoboothSessionService
    {
        public static EventData CurrentEvent { get; set; }
        public static int SessionId { get; set; } = new Random().Next(100000, 999999);
    }
}