using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Photobooth.Database;
using Photobooth.Models;
using CameraControl.Devices;

namespace Photobooth.Services
{
    /// <summary>
    /// Service to handle event selection business logic with search and preview capabilities
    /// </summary>
    public class EventSelectionService : INotifyPropertyChanged
    {
        #region Singleton
        private static EventSelectionService _instance;
        private static readonly object _lock = new object();
        
        public static EventSelectionService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new EventSelectionService();
                        }
                    }
                }
                return _instance;
            }
        }
        #endregion
        
        #region Properties
        private readonly EventService _eventService;
        private readonly TemplateDatabase _templateDatabase;
        private ObservableCollection<EventData> _allEvents;
        private ObservableCollection<EventData> _filteredEvents;
        private string _searchText;
        private EventData _selectedEvent;
        private TemplateData _previewTemplate;
        private BitmapImage _templatePreviewImage;
        private System.Timers.Timer _eventExpirationTimer;
        private DateTime _eventSelectionTime;
        private bool _isRestoringFromSettings = false;
        
        public ObservableCollection<EventData> FilteredEvents
        {
            get => _filteredEvents;
            private set
            {
                _filteredEvents = value;
                OnPropertyChanged();
            }
        }
        
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                FilterEvents();
            }
        }
        
        public EventData SelectedEvent
        {
            get => _selectedEvent;
            set
            {
                bool isSameEvent = _selectedEvent != null && value != null && _selectedEvent.Id == value.Id;
                _selectedEvent = value;
                OnPropertyChanged();
                if (value != null)
                {
                    LoadTemplatePreview(value);
                    // Save the selected event ID for persistence
                    Properties.Settings.Default.SelectedEventId = value.Id;
                    Properties.Settings.Default.Save();

                    // Verify the save worked
                    var savedId = Properties.Settings.Default.SelectedEventId;
                    Log.Debug($"EventSelectionService: Saved EventId {value.Id} to settings, readback value: {savedId}");

                    if (savedId != value.Id)
                    {
                        Log.Error($"EventSelectionService: Settings save failed! Expected {value.Id}, got {savedId}");
                        // Try again with explicit reload
                        Properties.Settings.Default.Reload();
                        Properties.Settings.Default.SelectedEventId = value.Id;
                        Properties.Settings.Default.Save();
                        savedId = Properties.Settings.Default.SelectedEventId;
                        Log.Debug($"EventSelectionService: Retry save - readback value: {savedId}");
                    }

                    // Load the event into EventBackgroundService for background removal
                    Task.Run(async () => {
                        try
                        {
                            await EventBackgroundService.Instance.LoadEventAsync(value);
                            Log.Debug($"Loaded event {value.Name} into EventBackgroundService");
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Failed to load event into EventBackgroundService: {ex.Message}");
                        }
                    });

                    // Only update selection time if this is a new selection (not restoring from settings)
                    if (!_isRestoringFromSettings)
                    {
                        if (!isSameEvent)
                        {
                            _eventSelectionTime = DateTime.Now;
                            SaveEventSelectionTime();
                            Log.Debug($"Saved selected event ID: {value.Id} - {value.Name} at {_eventSelectionTime}");
                        }
                        else
                        {
                            Log.Debug($"EventSelectionService: Selected same event '{value.Name}', preserving existing selection time {_eventSelectionTime}");
                        }
                    }
                    else
                    {
                        Log.Debug($"Restored selected event ID: {value.Id} - {value.Name} (preserving original selection time)");
                    }
                }
                else
                {
                    // Clear the saved event ID and time
                    Properties.Settings.Default.SelectedEventId = 0;
                    // Use the default value from settings (year 2000) instead of MinValue
                    Properties.Settings.Default.EventSelectionTime = new DateTime(2000, 1, 1);
                    Properties.Settings.Default.Save();
                    _eventSelectionTime = new DateTime(2000, 1, 1);
                }
            }
        }
        
        public TemplateData PreviewTemplate
        {
            get => _previewTemplate;
            private set
            {
                _previewTemplate = value;
                OnPropertyChanged();
            }
        }
        
        public BitmapImage TemplatePreviewImage
        {
            get => _templatePreviewImage;
            private set
            {
                _templatePreviewImage = value;
                OnPropertyChanged();
            }
        }
        #endregion
        
        #region Events
        public event EventHandler<EventData> EventSelected;
        public event EventHandler EventExpired;
        public event EventHandler SearchCleared;
        public event PropertyChangedEventHandler PropertyChanged;
        #endregion
        
        private EventSelectionService()
        {
            _eventService = new EventService();
            _templateDatabase = new TemplateDatabase();
            _allEvents = new ObservableCollection<EventData>();
            _filteredEvents = new ObservableCollection<EventData>();

            // Initialize event expiration timer (5 hours = 18000000 milliseconds)
            _eventExpirationTimer = new System.Timers.Timer(5 * 60 * 60 * 1000);
            _eventExpirationTimer.Elapsed += OnEventExpired;
            _eventExpirationTimer.AutoReset = false;
        }
        
        /// <summary>
        /// Load all available events
        /// </summary>
        public void LoadEvents()
        {
            try
            {
                Log.Debug("EventSelectionService: Loading events");
                
                var events = _eventService.GetAllEvents() ?? new List<EventData>();
                
                // Load template preview for each event
                foreach (var eventData in events)
                {
                    LoadEventTemplatePreview(eventData);
                }
                
                _allEvents = new ObservableCollection<EventData>(events);
                FilteredEvents = new ObservableCollection<EventData>(_allEvents);
                
                Log.Debug($"EventSelectionService: Loaded {_allEvents.Count} events");
                // Do not auto-select an event here; selection should be explicit to
                // avoid unintentionally resetting the 5-hour timer on UI open.
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to load events: {ex.Message}");
                _allEvents = new ObservableCollection<EventData>();
                FilteredEvents = new ObservableCollection<EventData>();
            }
        }
        
        /// <summary>
        /// Load template preview for a specific event
        /// </summary>
        private void LoadEventTemplatePreview(EventData eventData)
        {
            try
            {
                if (eventData == null) return;
                
                // Get templates for this event
                var templates = _eventService.GetEventTemplates(eventData.Id);
                
                if (templates != null && templates.Any())
                {
                    // Get first active template and build robust preview
                    var template = templates.FirstOrDefault(t => t.IsActive) ?? templates.First();
                    var previewImage = GenerateTemplatePreviewImage(template, 220, 180);
                    if (previewImage != null) _eventPreviews[eventData.Id] = previewImage;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to load template preview for event {eventData.Name}: {ex.Message}");
            }
        }
        
        private Dictionary<int, BitmapImage> _eventPreviews = new Dictionary<int, BitmapImage>();
        
        /// <summary>
        /// Get template preview for an event
        /// </summary>
        public BitmapImage GetEventTemplatePreview(int eventId)
        {
            return _eventPreviews.ContainsKey(eventId) ? _eventPreviews[eventId] : null;
        }
        
        /// <summary>
        /// Filter events based on search text
        /// </summary>
        private void FilterEvents()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SearchText))
                {
                    FilteredEvents = new ObservableCollection<EventData>(_allEvents);
                    SearchCleared?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    var searchLower = SearchText.ToLower();
                    var filtered = _allEvents.Where(e => 
                        e.Name.ToLower().Contains(searchLower) || 
                        (e.Description?.ToLower().Contains(searchLower) ?? false)
                    ).ToList();
                    
                    FilteredEvents = new ObservableCollection<EventData>(filtered);
                }
                
                Log.Debug($"EventSelectionService: Filtered to {FilteredEvents.Count} events with search '{SearchText}'");
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to filter events: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Load template preview for selected event
        /// </summary>
        private void LoadTemplatePreview(EventData eventData)
        {
            try
            {
                if (eventData == null)
                {
                    PreviewTemplate = null;
                    TemplatePreviewImage = null;
                    return;
                }
                
                Log.Debug($"EventSelectionService: Loading template preview for event {eventData.Name}");
                
                // Get templates for this event
                var templates = _eventService.GetEventTemplates(eventData.Id);
                
                if (templates != null && templates.Any())
                {
                    // Get first active template
                    PreviewTemplate = templates.FirstOrDefault(t => t.IsActive) ?? templates.First();

                    var bmp = GenerateTemplatePreviewImage(PreviewTemplate, 320, 240);
                    if (bmp != null)
                    {
                        TemplatePreviewImage = bmp;
                    }
                    else
                    {
                        // Fallbacks
                        if (!string.IsNullOrEmpty(PreviewTemplate.ThumbnailImagePath) && File.Exists(PreviewTemplate.ThumbnailImagePath))
                            LoadPreviewImage(PreviewTemplate.ThumbnailImagePath);
                        else if (!string.IsNullOrEmpty(PreviewTemplate.BackgroundImagePath) && File.Exists(PreviewTemplate.BackgroundImagePath))
                            LoadPreviewImage(PreviewTemplate.BackgroundImagePath);
                        else
                            GenerateTemplatePreview(PreviewTemplate);
                    }

                    Log.Debug($"EventSelectionService: Loaded preview template '{PreviewTemplate.Name}' for event");
                }
                else
                {
                    PreviewTemplate = null;
                    TemplatePreviewImage = null;
                    Log.Debug("EventSelectionService: No templates found for event");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to load template preview: {ex.Message}");
                PreviewTemplate = null;
                TemplatePreviewImage = null;
            }
        }
        
        /// <summary>
        /// Load preview image from file
        /// </summary>
        private void LoadPreviewImage(string imagePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.DecodePixelWidth = 400; // Limit size for performance
                bitmap.EndInit();
                bitmap.Freeze();
                
                TemplatePreviewImage = bitmap;
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to load preview image: {ex.Message}");
                TemplatePreviewImage = null;
            }
        }
        
        /// <summary>
        /// Load image from file path
        /// </summary>
        private BitmapImage LoadImageFromFile(string imagePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.DecodePixelWidth = 400; // Limit size for performance
                bitmap.EndInit();
                bitmap.Freeze();
                
                return bitmap;
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to load image from file: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Generate a preview from template data
        /// </summary>
        private void GenerateTemplatePreview(TemplateData template)
        {
            try
            {
                TemplatePreviewImage = GenerateTemplatePreviewImage(template, 320, 240);
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to generate template preview: {ex.Message}");
                TemplatePreviewImage = null;
            }
        }

        // Public: generate robust preview with labels and consistent scaling
        public BitmapImage GenerateTemplatePreviewImage(TemplateData template, int maxWidth = 320, int maxHeight = 240)
        {
            try
            {
                if (template == null) return null;

                // Ignore stored thumbnails; generate preview dynamically for consistent scaling

                // Generate from template contents
                double tW = Math.Max(1, template.CanvasWidth);
                double tH = Math.Max(1, template.CanvasHeight);
                double scale = Math.Min((double)maxWidth / tW, (double)maxHeight / tH);
                int outW = Math.Max(1, (int)(tW * scale));
                int outH = Math.Max(1, (int)(tH * scale));

                var items = _templateDatabase.GetCanvasItems(template.Id) ?? new List<CanvasItemData>();
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    // Background: image if available, scaled Uniform
                    if (!string.IsNullOrEmpty(template.BackgroundImagePath) && File.Exists(template.BackgroundImagePath))
                    {
                        var bg = LoadImageFromFile(template.BackgroundImagePath);
                        if (bg != null)
                        {
                            double imgRatio = (double)bg.PixelWidth / Math.Max(1, bg.PixelHeight);
                            double outRatio = (double)outW / Math.Max(1, outH);
                            Rect dest;
                            if (imgRatio > outRatio)
                            {
                                double h = outW / imgRatio;
                                dest = new Rect(0, (outH - h) / 2, outW, h);
                            }
                            else
                            {
                                double w = outH * imgRatio;
                                dest = new Rect((outW - w) / 2, 0, w, outH);
                            }
                            dc.DrawImage(bg, dest);
                        }
                        else
                        {
                            dc.DrawRectangle(new SolidColorBrush(Colors.Black), null, new Rect(0, 0, outW, outH));
                        }
                    }
                    else
                    {
                        var brush = new LinearGradientBrush(Colors.DimGray, Colors.Black, 90);
                        dc.DrawRectangle(brush, null, new Rect(0, 0, outW, outH));
                    }

                // Draw all items in Z order (Image, Placeholder, Text, Shape)
                int i = 0;
                foreach (var item in items.OrderBy(x => x.ZIndex))
                {
                    double x = item.X * scale;
                    double y = item.Y * scale;
                    double w = Math.Max(1, item.Width * scale);
                    double h = Math.Max(1, item.Height * scale);
                    var rect = new Rect(x, y, w, h);

                    // Apply rotation origin at item center
                    if (item.Rotation != 0)
                    {
                        dc.PushTransform(new RotateTransform(item.Rotation, rect.Left + rect.Width / 2, rect.Top + rect.Height / 2));
                    }

                    switch (item.ItemType)
                    {
                        case "Image":
                            if (!string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
                            {
                                try
                                {
                                    var img = LoadImageFromFile(item.ImagePath);
                                    if (img != null)
                                    {
                                        dc.DrawImage(img, rect);
                                    }
                                }
                                catch { }
                            }
                            break;

                        case "Placeholder":
                            Color col;
                            if (!string.IsNullOrEmpty(item.PlaceholderColor))
                            {
                                try { col = (Color)ColorConverter.ConvertFromString(item.PlaceholderColor); }
                                catch { col = GetPaletteColor(item.PlaceholderNumber ?? (i + 1)); }
                            }
                            else
                            {
                                col = GetPaletteColor(item.PlaceholderNumber ?? (i + 1));
                            }

                            var geom = new RectangleGeometry(rect, 6, 6);
                            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(220, col.R, col.G, col.B)),
                                           new Pen(new SolidColorBrush(Colors.White), 2), geom);

                            int n = item.PlaceholderNumber ?? (i + 1);
                            var ft = new FormattedText(
                                $"Photo {n}",
                                System.Globalization.CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                new Typeface("Segoe UI"),
                                Math.Min(w, h) * 0.16,
                                new SolidColorBrush(Color.FromRgb(50, 50, 50)), 96);
                            dc.DrawText(ft, new Point(rect.Left + (rect.Width - ft.Width) / 2, rect.Top + (rect.Height - ft.Height) / 2));
                            break;

                        case "Text":
                            // Build typeface
                            var isBold = item.IsBold;
                            var isItalic = item.IsItalic;
                            var typeface = new Typeface(
                                new FontFamily(string.IsNullOrEmpty(item.FontFamily) ? "Segoe UI" : item.FontFamily),
                                isItalic ? FontStyles.Italic : FontStyles.Normal,
                                isBold ? FontWeights.Bold : FontWeights.Normal,
                                FontStretches.Normal);

                            // Text color
                            Brush textBrush = new SolidColorBrush(Colors.Black);
                            if (!string.IsNullOrEmpty(item.TextColor))
                            {
                                try { textBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.TextColor)); }
                                catch { }
                            }

                            double fontSize = Math.Max(8, (item.FontSize ?? 20) * scale);
                            var text = item.Text ?? string.Empty;
                            var ftText = new FormattedText(
                                text,
                                System.Globalization.CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                typeface,
                                fontSize,
                                textBrush,
                                96);
                            // Align inside rect
                            ftText.MaxTextWidth = rect.Width;
                            ftText.MaxTextHeight = rect.Height;
                            dc.DrawText(ftText, new Point(rect.Left, rect.Top));
                            break;

                        case "Shape":
                            // Basic rectangle/ellipse rendering
                            Brush fill = Brushes.Transparent;
                            if (!string.IsNullOrEmpty(item.FillColor))
                            {
                                try { fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.FillColor)); }
                                catch { }
                            }
                            Pen pen = null;
                            if (!string.IsNullOrEmpty(item.StrokeColor) && item.StrokeThickness > 0)
                            {
                                try { pen = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.StrokeColor)), item.StrokeThickness); }
                                catch { }
                            }

                            var shapeType = item.ShapeType?.Trim()?.ToLowerInvariant();
                            if (shapeType == "circle" || shapeType == "ellipse")
                            {
                                dc.DrawEllipse(fill, pen, new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2), rect.Width / 2, rect.Height / 2);
                            }
                            else
                            {
                                dc.DrawRectangle(fill, pen, rect);
                            }
                            break;
                    }

                    if (item.Rotation != 0)
                    {
                        dc.Pop();
                    }
                    i++;
                }
                }

                var rtb = new RenderTargetBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var ms = new System.IO.MemoryStream())
                {
                    encoder.Save(ms);
                    ms.Position = 0;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to generate template preview image: {ex.Message}");
                return null;
            }
        }

        private Color GetPaletteColor(int n)
        {
            Color[] palette = new Color[]
            {
                Color.FromRgb(255,182,193), // Light Pink
                Color.FromRgb(173,216,230), // Light Blue
                Color.FromRgb(144,238,144), // Light Green
                Color.FromRgb(255,228,181), // Peach
                Color.FromRgb(221,160,221), // Plum
                Color.FromRgb(240,230,140), // Khaki
            };
            int idx = Math.Max(0, (n - 1) % palette.Length);
            return palette[idx];
        }
        
        /// <summary>
        /// Select an event and notify subscribers
        /// </summary>
        public async void SelectEvent(EventData eventData)
        {
            if (eventData == null) return;

            Log.Debug($"EventSelectionService: Selecting event '{eventData.Name}'");
            bool wasSame = SelectedEvent != null && eventData != null && SelectedEvent.Id == eventData.Id;
            SelectedEvent = eventData;

            // Load event backgrounds
            try
            {
                await EventBackgroundService.Instance.LoadEventBackgroundsAsync(eventData);
                Log.Debug($"EventSelectionService: Loaded event backgrounds for '{eventData.Name}'");
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to load event backgrounds: {ex.Message}");
            }

            // Start the 5-hour expiration timer only if this is a new selection
            if (!wasSame)
            {
                StartEventExpirationTimer();
            }
            else
            {
                Log.Debug("EventSelectionService: Same event re-selected; preserving existing expiration timer");
            }

            EventSelected?.Invoke(this, eventData);
        }

        /// <summary>
        /// Start timer for already selected event (used by UI)
        /// </summary>
        public void StartTimerForCurrentEvent()
        {
            if (SelectedEvent != null)
            {
                Log.Debug($"EventSelectionService: Starting timer for current event '{SelectedEvent.Name}'");
                StartEventExpirationTimer();

                // Save the selection time to settings for persistence
                SaveEventSelectionTime();
            }
        }

        /// <summary>
        /// Save event selection time to settings
        /// </summary>
        private void SaveEventSelectionTime()
        {
            try
            {
                Properties.Settings.Default.EventSelectionTime = _eventSelectionTime;
                Properties.Settings.Default.Save();

                // Verify the save worked
                var savedTime = Properties.Settings.Default.EventSelectionTime;
                Log.Debug($"EventSelectionService: Saved event selection time: {_eventSelectionTime}, readback: {savedTime}");

                if (savedTime != _eventSelectionTime)
                {
                    Log.Error($"EventSelectionService: Time save failed! Expected {_eventSelectionTime}, got {savedTime}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to save event selection time: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if saved event has expired and restore if still valid
        /// </summary>
        public async Task CheckAndRestoreSavedEvent()
        {
            try
            {
                // Add detailed logging for debugging
                Log.Debug("EventSelectionService: CheckAndRestoreSavedEvent called");

                // Force a reload to ensure we get the latest saved values
                Properties.Settings.Default.Reload();

                var savedEventId = Properties.Settings.Default.SelectedEventId;
                var savedTime = Properties.Settings.Default.EventSelectionTime;

                Log.Debug($"EventSelectionService: Settings values after reload - SelectedEventId: {savedEventId}, EventSelectionTime: {savedTime}");

                // If EventSelectionTime is invalid (default), do NOT reset to now.
                // Treat as no valid persisted timer to preserve correct 5-hour window semantics.
                bool hasValidSavedTime = savedTime != DateTime.MinValue && savedTime.Year > 2000;

                // Check against the default DateTime value (year 2000)
                var defaultTime = new DateTime(2000, 1, 1);

                if (savedEventId > 0 && hasValidSavedTime)
                {
                    var elapsed = DateTime.Now - savedTime;
                    var expirationTime = TimeSpan.FromHours(5);

                    if (elapsed < expirationTime)
                    {
                        // Event is still valid, restore it
                        Log.Debug($"EventSelectionService: Restoring saved event {savedEventId}, selected {elapsed.TotalHours:F1} hours ago");

                        // Load the event from database
                        var eventData = _eventService.GetEvent(savedEventId);
                        if (eventData != null)
                        {
                            // Set flag to prevent overwriting selection time during restore
                            _isRestoringFromSettings = true;
                            _eventSelectionTime = savedTime; // Set the correct time BEFORE setting SelectedEvent
                            SelectedEvent = eventData;
                            _isRestoringFromSettings = false; // Reset flag

                            // Ensure the original selection time is preserved in settings
                            SaveEventSelectionTime();
                            Log.Debug($"EventSelectionService: Preserved original selection time: {_eventSelectionTime}");

                            // Load event backgrounds
                            try
                            {
                                await EventBackgroundService.Instance.LoadEventBackgroundsAsync(eventData);
                                Log.Debug($"EventSelectionService: Loaded event backgrounds for restored event '{eventData.Name}'");
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"EventSelectionService: Failed to load event backgrounds: {ex.Message}");
                            }

                            // Restart timer with remaining time
                            var remainingTime = expirationTime - elapsed;
                            RestartTimerWithRemainingTime(remainingTime);

                            Log.Debug($"EventSelectionService: Event restored successfully, {remainingTime.TotalHours:F1} hours remaining");
                        }
                        else
                        {
                            Log.Debug($"EventSelectionService: Saved event {savedEventId} not found in database");
                            ClearSavedEventSelection();
                        }
                    }
                    else
                    {
                        // Event has expired
                        Log.Debug($"EventSelectionService: Saved event has expired (selected {elapsed.TotalHours:F1} hours ago)");
                        ClearSavedEventSelection();
                    }
                }
                else
                {
                    // No valid saved event/time; do not reset timer implicitly.
                    // Optionally load backgrounds for compatibility if an event ID exists without a valid time.
                    Log.Debug("EventSelectionService: No valid saved event/time; skipping implicit timer reset");
                    try
                    {
                        if (savedEventId > 0)
                        {
                            await EventBackgroundService.Instance.LoadLastSelectedEventOnStartup();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"EventSelectionService: Failed to load from EventBackgroundService startup: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to restore saved event: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear saved event selection from settings
        /// </summary>
        private void ClearSavedEventSelection()
        {
            Properties.Settings.Default.SelectedEventId = 0;
            // Use the default value from settings (year 2000) instead of MinValue
            Properties.Settings.Default.EventSelectionTime = new DateTime(2000, 1, 1);
            Properties.Settings.Default.Save();
            Log.Debug("EventSelectionService: Cleared saved event selection");
        }

        /// <summary>
        /// Restart timer with specific remaining time
        /// </summary>
        private void RestartTimerWithRemainingTime(TimeSpan remainingTime)
        {
            try
            {
                StopEventExpirationTimer();

                // Set timer interval to remaining time
                _eventExpirationTimer.Interval = remainingTime.TotalMilliseconds;
                _eventExpirationTimer.Start();

                Log.Debug($"EventSelectionService: Timer restarted with {remainingTime.TotalMinutes:F0} minutes remaining");
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to restart timer: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Clear search and reset filter
        /// </summary>
        public void ClearSearch()
        {
            SearchText = string.Empty;
        }
        
        /// <summary>
        /// Reset service state
        /// </summary>
        public void Reset(bool preserveSelection = true)
        {
            SearchText = string.Empty;
            if (!preserveSelection)
            {
                // Explicit reset: clear selection and stop the timer
                SelectedEvent = null;
                StopEventExpirationTimer();
            }
            // Always clear transient preview state used by the overlay UI
            PreviewTemplate = null;
            TemplatePreviewImage = null;
            _allEvents.Clear();
            FilteredEvents.Clear();
        }

        /// <summary>
        /// Start the 5-hour event expiration timer
        /// </summary>
        private void StartEventExpirationTimer()
        {
            try
            {
                StopEventExpirationTimer();
                _eventSelectionTime = DateTime.Now;
                _eventExpirationTimer.Start();
                Log.Debug($"EventSelectionService: Started 5-hour expiration timer at {_eventSelectionTime}");
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to start expiration timer: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop the event expiration timer
        /// </summary>
        private void StopEventExpirationTimer()
        {
            if (_eventExpirationTimer != null)
            {
                _eventExpirationTimer.Stop();
                Log.Debug("EventSelectionService: Stopped expiration timer");
            }
        }

        /// <summary>
        /// Handle event expiration after 5 hours
        /// </summary>
        private void OnEventExpired(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                Log.Debug($"EventSelectionService: Event expired after 5 hours. Event was '{SelectedEvent?.Name}', selected at {_eventSelectionTime}");

                // Stop the timer first to prevent any re-triggering
                StopEventExpirationTimer();

                // Clear the current event
                SelectedEvent = null;
                PreviewTemplate = null;
                TemplatePreviewImage = null;

                // Clear PhotoboothService static event
                PhotoboothService.CurrentEvent = null;
                PhotoboothService.CurrentTemplate = null;

                // Clear saved settings
                ClearSavedEventSelection();

                // Notify subscribers that the event has expired
                EventExpired?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Error handling event expiration: {ex.Message}");
            }
        }

        /// <summary>
        /// Get remaining time for current event (if any)
        /// </summary>
        public TimeSpan? GetRemainingEventTime()
        {
            if (SelectedEvent == null || !_eventExpirationTimer.Enabled)
                return null;

            var elapsed = DateTime.Now - _eventSelectionTime;
            var remaining = TimeSpan.FromHours(5) - elapsed;

            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        /// <summary>
        /// Refresh the event selection time to prevent expiration during active use
        /// This resets the 5-hour timer from the current time
        /// </summary>
        public void RefreshEventSelection()
        {
            if (SelectedEvent == null)
            {
                Log.Debug("EventSelectionService: No event selected to refresh");
                return;
            }

            try
            {
                Log.Debug($"EventSelectionService: Refreshing event selection for '{SelectedEvent.Name}'");

                // Update the selection time to now
                _eventSelectionTime = DateTime.Now;
                SaveEventSelectionTime();

                // Restart the 5-hour timer
                _eventExpirationTimer.Stop();
                _eventExpirationTimer.Interval = 5 * 60 * 60 * 1000; // 5 hours
                _eventExpirationTimer.Start();

                Log.Debug($"EventSelectionService: Event selection refreshed, timer reset to 5 hours from {_eventSelectionTime}");
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to refresh event selection: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Duplicate an event with all its templates
        /// </summary>
        public void DuplicateEvent(EventData sourceEvent, string newEventName)
        {
            try
            {
                Log.Debug($"EventSelectionService: Duplicating event '{sourceEvent.Name}' as '{newEventName}'");

                // Create new event
                int newEventId = _eventService.CreateEvent(newEventName, sourceEvent.Description ?? "");

                if (newEventId > 0)
                {
                    // Get templates associated with source event
                    var sourceTemplates = _templateDatabase.GetEventTemplates(sourceEvent.Id);

                    // Associate same templates with new event
                    foreach (var template in sourceTemplates)
                    {
                        _templateDatabase.AssignTemplateToEvent(newEventId, template.Id, false);
                        Log.Debug($"EventSelectionService: Associated template {template.Id} with new event {newEventId}");
                    }

                    Log.Debug($"EventSelectionService: Successfully duplicated event with {sourceTemplates.Count} templates");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to duplicate event: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Rename an existing event
        /// </summary>
        public void RenameEvent(EventData eventToRename, string newName)
        {
            try
            {
                Log.Debug($"EventSelectionService: Renaming event '{eventToRename.Name}' to '{newName}'");

                eventToRename.Name = newName;
                _eventService.UpdateEvent(eventToRename);

                Log.Debug($"EventSelectionService: Successfully renamed event");
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to rename event: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Delete an event
        /// </summary>
        public void DeleteEvent(EventData eventToDelete)
        {
            try
            {
                Log.Debug($"EventSelectionService: Deleting event '{eventToDelete.Name}' (ID: {eventToDelete.Id})");

                // Delete the event (cascade delete will handle EventTemplates associations)
                _eventService.DeleteEvent(eventToDelete.Id);

                Log.Debug($"EventSelectionService: Successfully deleted event");
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to delete event: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Create a new event based on the last opened event
        /// </summary>
        public void CreateNewEventFromLast(string eventName)
        {
            try
            {
                Log.Debug($"EventSelectionService: Creating new event '{eventName}'");

                // Create the new event
                int newEventId = _eventService.CreateEvent(eventName, "Created on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

                if (newEventId > 0)
                {
                    // Get the last opened event
                    EventData lastEvent = null;

                    // Try to get from saved settings first (check for SelectedEventId)
                    var lastSelectedEventId = Properties.Settings.Default.SelectedEventId;
                    if (lastSelectedEventId > 0)
                    {
                        lastEvent = _eventService.GetEvent(lastSelectedEventId);
                    }

                    // If not found, get the most recent event
                    if (lastEvent == null && _allEvents != null && _allEvents.Count > 0)
                    {
                        lastEvent = _allEvents.OrderByDescending(e => e.Id).FirstOrDefault();
                    }

                    // Copy templates from last event if exists
                    if (lastEvent != null)
                    {
                        var lastEventTemplates = _templateDatabase.GetEventTemplates(lastEvent.Id);

                        foreach (var template in lastEventTemplates)
                        {
                            _templateDatabase.AssignTemplateToEvent(newEventId, template.Id, false);
                            Log.Debug($"EventSelectionService: Associated template {template.Id} with new event {newEventId}");
                        }

                        Log.Debug($"EventSelectionService: Created event with {lastEventTemplates.Count} templates from last event '{lastEvent.Name}'");
                    }
                    else
                    {
                        Log.Debug($"EventSelectionService: Created empty event (no last event found)");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to create new event: {ex.Message}");
                throw;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
