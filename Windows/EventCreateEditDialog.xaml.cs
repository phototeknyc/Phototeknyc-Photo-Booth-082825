using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Photobooth.Database;
using Photobooth.Services;

namespace Photobooth.Windows
{
    public partial class EventCreateEditDialog : Window, INotifyPropertyChanged
    {
        private readonly EventService eventService;
        private readonly EventData originalEvent;
        private readonly bool isEditMode;

        private string _eventName;
        public string EventName
        {
            get => _eventName;
            set => SetProperty(ref _eventName, value);
        }

        private string _description;
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        private string _eventType;
        public string EventType
        {
            get => _eventType;
            set => SetProperty(ref _eventType, value);
        }

        private string _location;
        public string Location
        {
            get => _location;
            set => SetProperty(ref _location, value);
        }

        private DateTime? _eventDate;
        public DateTime? EventDate
        {
            get => _eventDate;
            set => SetProperty(ref _eventDate, value);
        }

        private string _hostName;
        public string HostName
        {
            get => _hostName;
            set => SetProperty(ref _hostName, value);
        }

        private string _contactEmail;
        public string ContactEmail
        {
            get => _contactEmail;
            set => SetProperty(ref _contactEmail, value);
        }

        private string _contactPhone;
        public string ContactPhone
        {
            get => _contactPhone;
            set => SetProperty(ref _contactPhone, value);
        }

        // Constructor for creating new event
        public EventCreateEditDialog() : this(null)
        {
        }

        // Constructor for editing existing event
        public EventCreateEditDialog(EventData eventToEdit)
        {
            InitializeComponent();
            DataContext = this;
            
            eventService = new EventService();
            originalEvent = eventToEdit;
            isEditMode = eventToEdit != null;
            
            InitializeTimeComboBoxes();
            
            if (isEditMode)
            {
                LoadEventData();
                TitleText.Text = "Edit Event";
                SaveButton.Content = "Save Changes";
            }
            else
            {
                // Set default values for new event
                EventType = "Wedding";
                EventDate = DateTime.Today.AddDays(7); // Default to next week
            }
        }

        private void InitializeTimeComboBoxes()
        {
            // Populate hour combo boxes (12-hour format)
            for (int i = 1; i <= 12; i++)
            {
                StartHourComboBox.Items.Add($"{i:00} AM");
                EndHourComboBox.Items.Add($"{i:00} AM");
            }
            for (int i = 1; i <= 12; i++)
            {
                StartHourComboBox.Items.Add($"{i:00} PM");
                EndHourComboBox.Items.Add($"{i:00} PM");
            }

            // Populate minute combo boxes
            for (int i = 0; i < 60; i += 15)
            {
                StartMinuteComboBox.Items.Add($"{i:00}");
                EndMinuteComboBox.Items.Add($"{i:00}");
            }

            // Set default times
            StartHourComboBox.SelectedIndex = 13; // 2:00 PM
            StartMinuteComboBox.SelectedIndex = 0; // :00
            EndHourComboBox.SelectedIndex = 17;   // 6:00 PM
            EndMinuteComboBox.SelectedIndex = 0;  // :00
        }

        private void LoadEventData()
        {
            EventName = originalEvent.Name;
            Description = originalEvent.Description;
            EventType = originalEvent.EventType;
            Location = originalEvent.Location;
            EventDate = originalEvent.EventDate;
            HostName = originalEvent.HostName;
            ContactEmail = originalEvent.ContactEmail;
            ContactPhone = originalEvent.ContactPhone;

            // Load times
            if (originalEvent.StartTime.HasValue)
            {
                var startTime = originalEvent.StartTime.Value;
                SetTimeComboBoxes(StartHourComboBox, StartMinuteComboBox, startTime);
            }

            if (originalEvent.EndTime.HasValue)
            {
                var endTime = originalEvent.EndTime.Value;
                SetTimeComboBoxes(EndHourComboBox, EndMinuteComboBox, endTime);
            }
        }

        private void SetTimeComboBoxes(ComboBox hourComboBox, ComboBox minuteComboBox, TimeSpan time)
        {
            var hour = time.Hours;
            var minute = time.Minutes;
            var isPM = hour >= 12;
            var displayHour = hour == 0 ? 12 : (hour > 12 ? hour - 12 : hour);
            
            var hourText = $"{displayHour:00} {(isPM ? "PM" : "AM")}";
            
            for (int i = 0; i < hourComboBox.Items.Count; i++)
            {
                if (hourComboBox.Items[i].ToString() == hourText)
                {
                    hourComboBox.SelectedIndex = i;
                    break;
                }
            }

            var minuteText = $"{minute:00}";
            for (int i = 0; i < minuteComboBox.Items.Count; i++)
            {
                if (minuteComboBox.Items[i].ToString() == minuteText)
                {
                    minuteComboBox.SelectedIndex = i;
                    break;
                }
            }
        }

        private TimeSpan? GetTimeFromComboBoxes(ComboBox hourComboBox, ComboBox minuteComboBox)
        {
            if (hourComboBox.SelectedItem == null || minuteComboBox.SelectedItem == null)
                return null;

            var hourText = hourComboBox.SelectedItem.ToString();
            var minuteText = minuteComboBox.SelectedItem.ToString();

            var isPM = hourText.EndsWith("PM");
            var hourValue = int.Parse(hourText.Substring(0, 2));
            var minuteValue = int.Parse(minuteText);

            if (isPM && hourValue != 12)
                hourValue += 12;
            else if (!isPM && hourValue == 12)
                hourValue = 0;

            return new TimeSpan(hourValue, minuteValue, 0);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate required fields
                if (string.IsNullOrWhiteSpace(EventName))
                {
                    MessageBox.Show("Event name is required.", "Validation Error", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    EventNameTextBox.Focus();
                    return;
                }

                var startTime = GetTimeFromComboBoxes(StartHourComboBox, StartMinuteComboBox);
                var endTime = GetTimeFromComboBoxes(EndHourComboBox, EndMinuteComboBox);

                if (isEditMode)
                {
                    // Update existing event
                    originalEvent.Name = EventName;
                    originalEvent.Description = Description;
                    originalEvent.EventType = EventType;
                    originalEvent.Location = Location;
                    originalEvent.EventDate = EventDate;
                    originalEvent.StartTime = startTime;
                    originalEvent.EndTime = endTime;
                    originalEvent.HostName = HostName;
                    originalEvent.ContactEmail = ContactEmail;
                    originalEvent.ContactPhone = ContactPhone;
                    originalEvent.ModifiedDate = DateTime.Now;

                    eventService.UpdateEvent(originalEvent);
                    // No message - just close
                }
                else
                {
                    // Create new event with simplified data (name only is required)
                    var eventId = eventService.CreateEvent(
                        EventName,
                        Description,
                        EventType ?? "General",  // Default to General if not specified
                        Location,
                        EventDate ?? DateTime.Today,  // Default to today if not specified
                        startTime,
                        endTime,
                        HostName,
                        ContactEmail,
                        ContactPhone);

                    if (eventId > 0)
                    {
                        // Try to copy templates from the last opened event
                        bool templatesAssigned = false;
                        
                        // Get the most recently created event (excluding the one we just created)
                        var allEvents = eventService.GetAllEvents()
                            .Where(ev => ev.Id != eventId)
                            .OrderByDescending(ev => ev.CreatedDate)
                            .ToList();
                        
                        if (allEvents.Any())
                        {
                            // Get templates from the most recent event
                            var lastEvent = allEvents.First();
                            var lastEventTemplates = eventService.GetEventTemplates(lastEvent.Id);
                            
                            if (lastEventTemplates != null && lastEventTemplates.Count > 0)
                            {
                                // Copy templates from last event
                                foreach (var template in lastEventTemplates)
                                {
                                    eventService.AssignTemplateToEvent(eventId, template.Id, false);
                                }
                                templatesAssigned = true;
                            }
                        }
                        
                        // If no templates were copied from last event, assign all available templates
                        if (!templatesAssigned)
                        {
                            var templateDatabase = new TemplateDatabase();
                            var allTemplates = templateDatabase.GetAllTemplates();
                            
                            if (allTemplates != null && allTemplates.Count > 0)
                            {
                                // Assign all templates to the event automatically
                                foreach (var template in allTemplates)
                                {
                                    eventService.AssignTemplateToEvent(eventId, template.Id, false);
                                }
                            }
                        }
                        
                        // No message - just close
                    }
                    else
                    {
                        // Silent failure - just return
                        return;
                    }
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while saving the event: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "")
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(backingStore, value))
                return false;

            backingStore = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}