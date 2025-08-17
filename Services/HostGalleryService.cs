using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Photobooth.Database;

namespace Photobooth.Services
{
    /// <summary>
    /// Service for generating host/admin gallery views of all events and sessions
    /// </summary>
    public class HostGalleryService
    {
        private readonly TemplateDatabase database;
        private readonly EventService eventService;
        private readonly string baseUrl;
        
        public HostGalleryService()
        {
            database = new TemplateDatabase();
            eventService = new EventService();
            baseUrl = Environment.GetEnvironmentVariable("GALLERY_BASE_URL", EnvironmentVariableTarget.User) 
                     ?? "https://phototeknyc.s3.amazonaws.com";
        }
        
        /// <summary>
        /// Generate a master HTML gallery showing all events and sessions
        /// </summary>
        public async Task<string> GenerateMasterGalleryHtml()
        {
            var events = eventService.GetAllEvents();
            var html = new StringBuilder();
            
            html.AppendLine(@"<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Photobooth Master Gallery - All Events</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
            padding: 20px;
        }
        .container {
            max-width: 1400px;
            margin: 0 auto;
        }
        h1 {
            text-align: center;
            color: white;
            margin-bottom: 30px;
            font-size: 2.5em;
            text-shadow: 2px 2px 4px rgba(0,0,0,0.3);
        }
        .stats {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 20px;
            margin-bottom: 30px;
        }
        .stat-card {
            background: rgba(255,255,255,0.95);
            padding: 20px;
            border-radius: 15px;
            text-align: center;
            box-shadow: 0 10px 30px rgba(0,0,0,0.2);
        }
        .stat-number {
            font-size: 2.5em;
            font-weight: bold;
            color: #667eea;
        }
        .stat-label {
            color: #666;
            margin-top: 5px;
        }
        .event-section {
            background: rgba(255,255,255,0.95);
            border-radius: 20px;
            padding: 25px;
            margin-bottom: 30px;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
        }
        .event-header {
            border-bottom: 2px solid #e0e0e0;
            padding-bottom: 15px;
            margin-bottom: 20px;
            display: flex;
            justify-content: space-between;
            align-items: center;
        }
        .event-title {
            font-size: 1.8em;
            color: #333;
        }
        .event-meta {
            color: #666;
            font-size: 0.9em;
        }
        .sessions-grid {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(250px, 1fr));
            gap: 20px;
        }
        .session-card {
            background: #f8f8f8;
            border-radius: 12px;
            padding: 15px;
            cursor: pointer;
            transition: all 0.3s ease;
            border: 2px solid transparent;
        }
        .session-card:hover {
            transform: translateY(-5px);
            box-shadow: 0 8px 25px rgba(0,0,0,0.15);
            border-color: #667eea;
        }
        .session-info {
            margin-bottom: 10px;
        }
        .session-name {
            font-weight: bold;
            color: #333;
            margin-bottom: 5px;
        }
        .session-time {
            color: #666;
            font-size: 0.85em;
        }
        .session-stats {
            display: flex;
            justify-content: space-between;
            padding-top: 10px;
            border-top: 1px solid #ddd;
        }
        .session-photos, .session-composed {
            font-size: 0.9em;
            color: #666;
        }
        .view-gallery-btn {
            display: block;
            width: 100%;
            padding: 8px;
            margin-top: 10px;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            border: none;
            border-radius: 8px;
            cursor: pointer;
            text-decoration: none;
            text-align: center;
            transition: transform 0.2s ease;
        }
        .view-gallery-btn:hover {
            transform: scale(1.05);
        }
        .filter-bar {
            background: white;
            padding: 20px;
            border-radius: 15px;
            margin-bottom: 30px;
            box-shadow: 0 10px 30px rgba(0,0,0,0.2);
        }
        .filter-controls {
            display: flex;
            gap: 20px;
            flex-wrap: wrap;
            align-items: center;
        }
        .filter-group {
            display: flex;
            flex-direction: column;
            gap: 5px;
        }
        .filter-label {
            font-size: 0.9em;
            color: #666;
        }
        .filter-input {
            padding: 8px 12px;
            border: 1px solid #ddd;
            border-radius: 8px;
            font-size: 1em;
        }
        .no-sessions {
            color: #999;
            font-style: italic;
            padding: 20px;
            text-align: center;
        }
    </style>
</head>
<body>
    <div class='container'>
        <h1>📸 Photobooth Master Gallery</h1>");
            
            // Add statistics
            int totalEvents = events.Count;
            int totalSessions = 0;
            int totalPhotos = 0;
            
            foreach (var evt in events)
            {
                var sessions = database.GetPhotoSessions(evt.Id);
                totalSessions += sessions.Count;
                foreach (var session in sessions)
                {
                    totalPhotos += session.ActualPhotoCount;
                }
            }
            
            html.AppendLine($@"
        <div class='stats'>
            <div class='stat-card'>
                <div class='stat-number'>{totalEvents}</div>
                <div class='stat-label'>Total Events</div>
            </div>
            <div class='stat-card'>
                <div class='stat-number'>{totalSessions}</div>
                <div class='stat-label'>Total Sessions</div>
            </div>
            <div class='stat-card'>
                <div class='stat-number'>{totalPhotos}</div>
                <div class='stat-label'>Total Photos</div>
            </div>
            <div class='stat-card'>
                <div class='stat-number'>{DateTime.Now:MMM dd, yyyy}</div>
                <div class='stat-label'>Last Updated</div>
            </div>
        </div>
        
        <div class='filter-bar'>
            <div class='filter-controls'>
                <div class='filter-group'>
                    <label class='filter-label'>Search Events</label>
                    <input type='text' class='filter-input' id='eventSearch' placeholder='Type to search...' onkeyup='filterEvents()'>
                </div>
                <div class='filter-group'>
                    <label class='filter-label'>Date Range</label>
                    <input type='date' class='filter-input' id='dateFrom' onchange='filterEvents()'>
                </div>
                <div class='filter-group'>
                    <label class='filter-label'>&nbsp;</label>
                    <input type='date' class='filter-input' id='dateTo' onchange='filterEvents()'>
                </div>
            </div>
        </div>");
            
            // Add each event
            foreach (var evt in events.OrderByDescending(e => e.EventDate))
            {
                var sessions = database.GetPhotoSessions(evt.Id);
                var eventFolderName = SanitizeForS3Key(evt.Name);
                
                html.AppendLine($@"
        <div class='event-section' data-event-name='{evt.Name.ToLower()}' data-event-date='{evt.EventDate:yyyy-MM-dd}'>
            <div class='event-header'>
                <div>
                    <div class='event-title'>{evt.Name}</div>
                    <div class='event-meta'>{evt.EventDate:MMMM dd, yyyy} • {evt.EventType} • {sessions.Count} sessions</div>
                </div>
                <div style='display: flex; gap: 10px; flex-wrap: wrap;'>
                    <a href='{baseUrl}/events/{eventFolderName}/' target='_blank' class='view-gallery-btn' style='width: auto; padding: 8px 16px;'>
                        View All Event Photos →
                    </a>
                    <button onclick='downloadAllEventPhotos(""{baseUrl}/events/{eventFolderName}/"", ""{evt.Name}"")' class='view-gallery-btn' style='width: auto; padding: 8px 16px; background: linear-gradient(135deg, #4CAF50 0%, #45a049 100%);'>
                        📥 Download All
                    </button>
                    <button onclick='copyCustomerLink(""{baseUrl}/events/{eventFolderName}/index.html"", ""{evt.Name}"")' class='view-gallery-btn' style='width: auto; padding: 8px 16px; background: linear-gradient(135deg, #FF6B6B 0%, #FF8A65 100%);'>
                        📋 Copy Customer Link
                    </button>
                    <button onclick='generateEventGallery({evt.Id}, ""{evt.Name}"")' class='view-gallery-btn' style='width: auto; padding: 8px 16px; background: linear-gradient(135deg, #00D9FF 0%, #7928CA 100%);'>
                        🔄 Generate Customer Gallery
                    </button>
                </div>
            </div>");
                
                if (sessions.Any())
                {
                    html.AppendLine(@"            <div class='sessions-grid'>");
                    
                    foreach (var session in sessions.OrderByDescending(s => s.StartTime))
                    {
                        // Simplified URL structure: /events/eventname/
                        var galleryUrl = $"{baseUrl}/events/{eventFolderName}/";
                        
                        // If you want individual session galleries, you can still link to them
                        var sessionGalleryUrl = $"{baseUrl}/events/{eventFolderName}/sessions/{session.SessionGuid}/index.html";
                        
                        html.AppendLine($@"
                <div class='session-card'>
                    <div class='session-info'>
                        <div class='session-name'>{session.SessionName}</div>
                        <div class='session-time'>{session.StartTime:MMM dd, h:mm tt} - {session.EndTime:h:mm tt}</div>
                    </div>
                    <div class='session-stats'>
                        <span class='session-photos'>📷 {session.ActualPhotoCount} photos</span>
                        <span class='session-composed'>🖼️ {session.ComposedImageCount} composed</span>
                    </div>
                    <a href='{sessionGalleryUrl}' target='_blank' class='view-gallery-btn'>View Session →</a>
                    <button onclick='downloadSessionPhotos(""{sessionGalleryUrl}"", ""{session.SessionName}"")' class='view-gallery-btn' style='margin-top: 5px;'>
                        📥 Download All
                    </button>
                </div>");
                    }
                    
                    html.AppendLine(@"            </div>");
                }
                else
                {
                    html.AppendLine(@"            <div class='no-sessions'>No sessions recorded for this event</div>");
                }
                
                html.AppendLine(@"        </div>");
            }
            
            html.AppendLine(@"
    </div>
    
    <script>
        function filterEvents() {
            const searchTerm = document.getElementById('eventSearch').value.toLowerCase();
            const dateFrom = document.getElementById('dateFrom').value;
            const dateTo = document.getElementById('dateTo').value;
            
            document.querySelectorAll('.event-section').forEach(section => {
                const eventName = section.dataset.eventName;
                const eventDate = section.dataset.eventDate;
                
                let show = true;
                
                // Search filter
                if (searchTerm && !eventName.includes(searchTerm)) {
                    show = false;
                }
                
                // Date range filter
                if (dateFrom && eventDate < dateFrom) {
                    show = false;
                }
                if (dateTo && eventDate > dateTo) {
                    show = false;
                }
                
                section.style.display = show ? 'block' : 'none';
            });
        }
        
        async function downloadSessionPhotos(sessionUrl, sessionName) {
            try {
                // Show loading message
                const btn = event.target;
                const originalText = btn.innerHTML;
                btn.innerHTML = '⏳ Preparing download...';
                btn.disabled = true;
                
                // Extract base URL from session gallery URL
                const baseUrl = sessionUrl.replace('/index.html', '');
                
                // Note: Direct download from S3 requires CORS configuration
                // For now, we'll open the session gallery where users can use the Download All button
                window.open(sessionUrl, '_blank');
                
                // Restore button
                setTimeout(() => {
                    btn.innerHTML = originalText;
                    btn.disabled = false;
                }, 2000);
                
                // Alternative: Create a download instruction message
                alert(`Session gallery opened in new tab.\n\nUse the 'Download All Photos' button in the gallery to download all photos for ${sessionName}.`);
                
            } catch (error) {
                console.error('Download error:', error);
                alert('Failed to initiate download. Please try viewing the gallery instead.');
            }
        }
        
        async function downloadAllEventPhotos(eventUrl, eventName) {
            try {
                const btn = event.target;
                const originalText = btn.innerHTML;
                btn.innerHTML = '⏳ Opening event gallery...';
                btn.disabled = true;
                
                // Open the event gallery in S3
                window.open(eventUrl, '_blank');
                
                setTimeout(() => {
                    btn.innerHTML = originalText;
                    btn.disabled = false;
                }, 2000);
                
                alert(`Event gallery opened in new tab.\n\nYou can browse and download all photos for ${eventName} from there.`);
                
            } catch (error) {
                console.error('Download error:', error);
                alert('Failed to open event gallery.');
            }
        }
        
        function copyCustomerLink(galleryUrl, eventName) {
            try {
                // Create a temporary textarea to copy the URL
                const textarea = document.createElement('textarea');
                textarea.value = galleryUrl;
                document.body.appendChild(textarea);
                textarea.select();
                document.execCommand('copy');
                document.body.removeChild(textarea);
                
                // Visual feedback
                const btn = event.target;
                const originalText = btn.innerHTML;
                btn.innerHTML = '✅ Copied!';
                btn.style.background = 'linear-gradient(135deg, #4CAF50 0%, #45a049 100%)';
                
                setTimeout(() => {
                    btn.innerHTML = originalText;
                    btn.style.background = 'linear-gradient(135deg, #FF6B6B 0%, #FF8A65 100%)';
                }, 2000);
                
                // Show alert with the link
                alert(`Customer gallery link copied to clipboard!\n\n${galleryUrl}\n\nYou can now share this link with customers to view their ${eventName} photos.`);
                
            } catch (error) {
                console.error('Copy error:', error);
                alert('Failed to copy link. Please copy manually: ' + galleryUrl);
            }
        }
        
        async function generateEventGallery(eventId, eventName) {
            try {
                const btn = event.target;
                const originalText = btn.innerHTML;
                btn.innerHTML = '⏳ Generating...';
                btn.disabled = true;
                
                // Note: This would need to call back to your application to trigger the gallery generation
                // For now, we'll show instructions
                alert(`To generate the customer gallery for ${eventName}:\n\n1. This will create a beautiful gallery page at /events/${eventName.toLowerCase().replace(/ /g, '-')}/index.html\n2. The gallery will show all sessions with photos\n3. Customers can view and download all their photos\n\nNote: This requires the application to upload the gallery HTML to S3.`);
                
                btn.innerHTML = originalText;
                btn.disabled = false;
                
            } catch (error) {
                console.error('Generate error:', error);
                alert('Failed to generate gallery.');
            }
        }
    </script>
</body>
</html>");
            
            return html.ToString();
        }
        
        /// <summary>
        /// Generate and save master gallery to a local HTML file
        /// </summary>
        public async Task<string> GenerateAndSaveLocalGallery()
        {
            try
            {
                var html = await GenerateMasterGalleryHtml();
                
                // Save to local file
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var galleryPath = Path.Combine(documentsPath, "Photobooth", "MasterGallery");
                Directory.CreateDirectory(galleryPath);
                
                var fileName = $"master_gallery_{DateTime.Now:yyyyMMdd_HHmmss}.html";
                var filePath = Path.Combine(galleryPath, fileName);
                
                File.WriteAllText(filePath, html);
                
                // Open in default browser
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
                
                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to generate master gallery: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Open the S3 bucket directly in browser
        /// </summary>
        public void OpenS3BucketInBrowser()
        {
            try
            {
                var s3ConsoleUrl = "https://s3.console.aws.amazon.com/s3/buckets/phototeknyc?region=us-east-1&tab=objects";
                Process.Start(new ProcessStartInfo
                {
                    FileName = s3ConsoleUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open S3 console: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get the base gallery URL for an event
        /// </summary>
        public string GetEventGalleryUrl(string eventName)
        {
            var eventFolderName = SanitizeForS3Key(eventName);
            return $"{baseUrl}/events/{eventFolderName}/";
        }
        
        /// <summary>
        /// Get all gallery URLs for current events
        /// </summary>
        public Dictionary<string, string> GetAllEventGalleryUrls()
        {
            var urls = new Dictionary<string, string>();
            var events = eventService.GetAllEvents();
            
            foreach (var evt in events)
            {
                urls[evt.Name] = GetEventGalleryUrl(evt.Name);
            }
            
            return urls;
        }
        
        private string SanitizeForS3Key(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "general";
            
            var sanitized = input.Trim()
                .Replace(" ", "-")
                .Replace("/", "-")
                .Replace("\\", "-")
                .Replace(":", "-")
                .Replace("*", "-")
                .Replace("?", "-")
                .Replace("\"", "-")
                .Replace("<", "-")
                .Replace(">", "-")
                .Replace("|", "-");
            
            while (sanitized.Contains("--"))
                sanitized = sanitized.Replace("--", "-");
            
            sanitized = sanitized.Trim('-');
            
            if (string.IsNullOrEmpty(sanitized))
                return "general";
            
            if (sanitized.Length > 50)
                sanitized = sanitized.Substring(0, 50).TrimEnd('-');
            
            return sanitized.ToLower();
        }
    }
}