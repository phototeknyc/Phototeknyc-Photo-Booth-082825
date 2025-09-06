using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Newtonsoft.Json;
using System.Diagnostics;

namespace Photobooth.Services
{
    /// <summary>
    /// Handles synchronization of templates across multiple photo booths
    /// </summary>
    public class TemplateSyncService
    {
        private static TemplateSyncService _instance;
        public static TemplateSyncService Instance => _instance ?? (_instance = new TemplateSyncService());
        
        private readonly string _templatesDirectory;
        private readonly string _manifestPath;
        private Dictionary<string, TemplateManifestEntry> _localManifest;
        
        public event EventHandler<TemplateSyncProgressEventArgs> SyncProgress;
        public event EventHandler<TemplateSyncCompletedEventArgs> SyncCompleted;
        
        private TemplateSyncService()
        {
            _templatesDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhotoBooth", "Templates"
            );
            
            _manifestPath = Path.Combine(_templatesDirectory, "sync_manifest.json");
            
            if (!Directory.Exists(_templatesDirectory))
            {
                Directory.CreateDirectory(_templatesDirectory);
            }
            
            LoadManifest();
        }
        
        /// <summary>
        /// Template manifest entry
        /// </summary>
        public class TemplateManifestEntry
        {
            public string TemplateId { get; set; }
            public string Name { get; set; }
            public string FilePath { get; set; }
            public string Hash { get; set; }
            public DateTime LastModified { get; set; }
            public long FileSize { get; set; }
            public string Category { get; set; }
            public bool IsActive { get; set; }
            public Dictionary<string, object> Metadata { get; set; }
        }
        
        /// <summary>
        /// Get all local templates for sync
        /// </summary>
        public async Task<List<TemplateManifestEntry>> GetLocalTemplatesAsync()
        {
            var templates = new List<TemplateManifestEntry>();
            
            try
            {
                // Scan templates directory
                var templateFiles = Directory.GetFiles(_templatesDirectory, "*.template", SearchOption.AllDirectories);
                
                foreach (var file in templateFiles)
                {
                    var entry = await CreateManifestEntryAsync(file);
                    if (entry != null)
                    {
                        templates.Add(entry);
                    }
                }
                
                // Also check for XML templates (legacy format)
                var xmlFiles = Directory.GetFiles(_templatesDirectory, "*.xml", SearchOption.AllDirectories);
                foreach (var file in xmlFiles)
                {
                    if (IsTemplateFile(file))
                    {
                        var entry = await CreateManifestEntryAsync(file);
                        if (entry != null)
                        {
                            templates.Add(entry);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TemplateSyncService: Error getting local templates: {ex.Message}");
            }
            
            return templates;
        }
        
        /// <summary>
        /// Create manifest entry for a template file
        /// </summary>
        private async Task<TemplateManifestEntry> CreateManifestEntryAsync(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var hash = await CalculateFileHashAsync(filePath);
                var relativePath = GetRelativePath(filePath);
                
                return new TemplateManifestEntry
                {
                    TemplateId = Path.GetFileNameWithoutExtension(filePath),
                    Name = Path.GetFileNameWithoutExtension(filePath),
                    FilePath = relativePath,
                    Hash = hash,
                    LastModified = fileInfo.LastWriteTime,
                    FileSize = fileInfo.Length,
                    Category = GetTemplateCategory(filePath),
                    IsActive = true,
                    Metadata = ExtractTemplateMetadata(filePath)
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TemplateSyncService: Error creating manifest entry: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Calculate SHA256 hash of file
        /// </summary>
        private async Task<string> CalculateFileHashAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                using (var sha256 = SHA256.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    var hash = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            });
        }
        
        /// <summary>
        /// Compare local and remote manifests to find differences
        /// </summary>
        public SyncDifferences CompareManifests(List<TemplateManifestEntry> localTemplates, List<TemplateManifestEntry> remoteTemplates)
        {
            var differences = new SyncDifferences();
            
            // Build lookup dictionaries
            var localDict = localTemplates.ToDictionary(t => t.TemplateId);
            var remoteDict = remoteTemplates.ToDictionary(t => t.TemplateId);
            
            // Find templates to upload (local only)
            foreach (var local in localTemplates)
            {
                if (!remoteDict.ContainsKey(local.TemplateId))
                {
                    differences.ToUpload.Add(local);
                }
                else if (remoteDict[local.TemplateId].Hash != local.Hash)
                {
                    // Check which is newer
                    var remote = remoteDict[local.TemplateId];
                    if (local.LastModified > remote.LastModified)
                    {
                        differences.ToUpload.Add(local);
                    }
                    else
                    {
                        differences.ToDownload.Add(remote);
                    }
                    differences.Conflicts.Add(new SyncConflict
                    {
                        ItemId = local.TemplateId,
                        LocalVersion = local,
                        RemoteVersion = remote
                    });
                }
            }
            
            // Find templates to download (remote only)
            foreach (var remote in remoteTemplates)
            {
                if (!localDict.ContainsKey(remote.TemplateId))
                {
                    differences.ToDownload.Add(remote);
                }
            }
            
            return differences;
        }
        
        /// <summary>
        /// Apply template from remote
        /// </summary>
        public async Task<bool> ApplyRemoteTemplateAsync(TemplateManifestEntry remoteTemplate, byte[] templateData)
        {
            try
            {
                var localPath = Path.Combine(_templatesDirectory, remoteTemplate.FilePath);
                var directory = Path.GetDirectoryName(localPath);
                
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                // Write template file
                await Task.Run(() => File.WriteAllBytes(localPath, templateData));
                
                // Update local manifest
                _localManifest[remoteTemplate.TemplateId] = remoteTemplate;
                SaveManifest();
                
                // Notify UI if needed
                OnSyncProgress(new TemplateSyncProgressEventArgs
                {
                    Message = $"Applied template: {remoteTemplate.Name}",
                    Progress = 100,
                    CurrentItem = remoteTemplate.Name
                });
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TemplateSyncService: Error applying remote template: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Export template for upload
        /// </summary>
        public async Task<byte[]> ExportTemplateAsync(TemplateManifestEntry template)
        {
            try
            {
                var fullPath = Path.Combine(_templatesDirectory, template.FilePath);
                if (File.Exists(fullPath))
                {
                    return await Task.Run(() => File.ReadAllBytes(fullPath));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TemplateSyncService: Error exporting template: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Load manifest from disk
        /// </summary>
        private void LoadManifest()
        {
            try
            {
                if (File.Exists(_manifestPath))
                {
                    var json = File.ReadAllText(_manifestPath);
                    _localManifest = JsonConvert.DeserializeObject<Dictionary<string, TemplateManifestEntry>>(json) 
                        ?? new Dictionary<string, TemplateManifestEntry>();
                }
                else
                {
                    _localManifest = new Dictionary<string, TemplateManifestEntry>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TemplateSyncService: Error loading manifest: {ex.Message}");
                _localManifest = new Dictionary<string, TemplateManifestEntry>();
            }
        }
        
        /// <summary>
        /// Save manifest to disk
        /// </summary>
        private void SaveManifest()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_localManifest, Formatting.Indented);
                File.WriteAllText(_manifestPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TemplateSyncService: Error saving manifest: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get relative path from templates directory
        /// </summary>
        private string GetRelativePath(string fullPath)
        {
            if (fullPath.StartsWith(_templatesDirectory))
            {
                return fullPath.Substring(_templatesDirectory.Length).TrimStart('\\', '/');
            }
            return Path.GetFileName(fullPath);
        }
        
        /// <summary>
        /// Determine template category from path or content
        /// </summary>
        private string GetTemplateCategory(string filePath)
        {
            // Check if path contains category folder
            var relativePath = GetRelativePath(filePath);
            var parts = relativePath.Split('\\', '/');
            
            if (parts.Length > 1)
            {
                return parts[0]; // First folder is category
            }
            
            // Default category based on filename patterns
            var fileName = Path.GetFileNameWithoutExtension(filePath).ToLower();
            
            if (fileName.Contains("4x6") || fileName.Contains("print"))
                return "Prints";
            if (fileName.Contains("strip") || fileName.Contains("2x6"))
                return "Strips";
            if (fileName.Contains("social") || fileName.Contains("instagram"))
                return "Social";
            if (fileName.Contains("gif") || fileName.Contains("boomerang"))
                return "Animated";
            if (fileName.Contains("green") || fileName.Contains("screen"))
                return "GreenScreen";
            
            return "General";
        }
        
        /// <summary>
        /// Check if XML file is a template
        /// </summary>
        private bool IsTemplateFile(string filePath)
        {
            try
            {
                // Quick check for template-specific content
                var content = File.ReadAllText(filePath);
                return content.Contains("<Template") || 
                       content.Contains("<PhotoTemplate") ||
                       content.Contains("<CanvasLayout");
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Extract metadata from template file
        /// </summary>
        private Dictionary<string, object> ExtractTemplateMetadata(string filePath)
        {
            var metadata = new Dictionary<string, object>();
            
            try
            {
                // Add basic metadata
                metadata["FileExtension"] = Path.GetExtension(filePath);
                metadata["CreatedDate"] = File.GetCreationTime(filePath);
                
                // Try to extract template-specific metadata
                if (filePath.EndsWith(".template") || filePath.EndsWith(".xml"))
                {
                    var content = File.ReadAllText(filePath);
                    
                    // Extract dimensions if present
                    if (content.Contains("Width="))
                    {
                        var widthMatch = System.Text.RegularExpressions.Regex.Match(content, @"Width=""(\d+)""");
                        if (widthMatch.Success)
                        {
                            metadata["Width"] = int.Parse(widthMatch.Groups[1].Value);
                        }
                    }
                    
                    if (content.Contains("Height="))
                    {
                        var heightMatch = System.Text.RegularExpressions.Regex.Match(content, @"Height=""(\d+)""");
                        if (heightMatch.Success)
                        {
                            metadata["Height"] = int.Parse(heightMatch.Groups[1].Value);
                        }
                    }
                    
                    // Extract photo count
                    var photoCount = System.Text.RegularExpressions.Regex.Matches(content, @"<Photo|<PlaceholderCanvasItem").Count;
                    if (photoCount > 0)
                    {
                        metadata["PhotoCount"] = photoCount;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TemplateSyncService: Error extracting metadata: {ex.Message}");
            }
            
            return metadata;
        }
        
        /// <summary>
        /// Sync differences container
        /// </summary>
        public class SyncDifferences
        {
            public List<TemplateManifestEntry> ToUpload { get; set; } = new List<TemplateManifestEntry>();
            public List<TemplateManifestEntry> ToDownload { get; set; } = new List<TemplateManifestEntry>();
            public List<SyncConflict> Conflicts { get; set; } = new List<SyncConflict>();
        }
        
        /// <summary>
        /// Sync conflict information
        /// </summary>
        public class SyncConflict
        {
            public string ItemId { get; set; }
            public object LocalVersion { get; set; }
            public object RemoteVersion { get; set; }
        }
        
        protected virtual void OnSyncProgress(TemplateSyncProgressEventArgs e)
        {
            SyncProgress?.Invoke(this, e);
        }
        
        protected virtual void OnSyncCompleted(TemplateSyncCompletedEventArgs e)
        {
            SyncCompleted?.Invoke(this, e);
        }
    }
    
    /// <summary>
    /// Template sync progress event arguments
    /// </summary>
    public class TemplateSyncProgressEventArgs : EventArgs
    {
        public string Message { get; set; }
        public int Progress { get; set; }
        public string CurrentItem { get; set; }
    }
    
    /// <summary>
    /// Template sync completed event arguments
    /// </summary>
    public class TemplateSyncCompletedEventArgs : EventArgs
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int ItemsSynced { get; set; }
        public List<string> Errors { get; set; }
    }
}