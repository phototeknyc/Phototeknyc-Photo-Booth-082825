using System;
using System.Threading.Tasks;
using Photobooth.Database;
using Photobooth.Models;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    /// <summary>
    /// Clean service that handles photo template composition
    /// Encapsulates all business logic for creating composed images from templates
    /// </summary>
    public class PhotoCompositionService
    {
        #region Events
        public event EventHandler<CompositionStartedEventArgs> CompositionStarted;
        public event EventHandler<CompositionCompletedEventArgs> CompositionCompleted;
        public event EventHandler<CompositionErrorEventArgs> CompositionError;
        #endregion

        private readonly TemplateDatabase _templateDatabase;

        public PhotoCompositionService()
        {
            _templateDatabase = new TemplateDatabase();
        }


        // Add properties to expose both paths
        public string LastDisplayPath { get; private set; }
        public string LastPrintPath { get; private set; }

        /// <summary>
        /// Compose template with captured photos
        /// </summary>
        public async Task<string> ComposeTemplateAsync(CompletedSessionData sessionData)
        {
            try
            {
                if (sessionData?.Template == null || sessionData.PhotoPaths?.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("PhotoCompositionService: No template or photos to compose");
                    return null;
                }

                System.Diagnostics.Debug.WriteLine($"PhotoCompositionService: Starting template composition for {sessionData.Template.Name}");
                
                // Notify composition started
                CompositionStarted?.Invoke(this, new CompositionStartedEventArgs
                {
                    SessionId = sessionData.SessionId,
                    TemplateName = sessionData.Template.Name
                });

                // For now, we'll create PhotoProcessingOperations with the service adapter
                // TODO: Refactor PhotoProcessingOperations to not require page dependency
                var adapter = new CompositionServiceAdapter();
                // Provide the session GUID so tokens can resolve during composition
                adapter.CurrentSessionGuid = sessionData.SessionId;
                var photoProcessing = new PhotoProcessingOperations(adapter);

                // Compose the template
                string composedPath = await photoProcessing.ComposeTemplateWithPhotos(
                    sessionData.Template,
                    sessionData.PhotoPaths,
                    sessionData.Event,
                    _templateDatabase
                );

                if (!string.IsNullOrEmpty(composedPath))
                {
                    System.Diagnostics.Debug.WriteLine($"PhotoCompositionService: Template composed successfully at {composedPath}");
                    
                    // Store both paths from the adapter
                    LastDisplayPath = adapter.DisplayPath;
                    LastPrintPath = adapter.PrintPath;
                    
                    System.Diagnostics.Debug.WriteLine($"PhotoCompositionService: DisplayPath={LastDisplayPath}, PrintPath={LastPrintPath}");
                    
                    // Notify completion with print path
                    CompositionCompleted?.Invoke(this, new CompositionCompletedEventArgs
                    {
                        SessionId = sessionData.SessionId,
                        ComposedImagePath = composedPath,
                        ComposedImagePrintPath = adapter.PrintPath, // Add print path to event args
                        Template = sessionData.Template
                    });
                    
                    return composedPath;
                }
                else
                {
                    throw new Exception("PhotoProcessingOperations returned empty path");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PhotoCompositionService: Template composition failed: {ex.Message}");
                CompositionError?.Invoke(this, new CompositionErrorEventArgs 
                { 
                    Error = ex, 
                    SessionId = sessionData?.SessionId 
                });
                return null;
            }
        }
    }

    /// <summary>
    /// Adapter class that provides the required interface for PhotoProcessingOperations
    /// This avoids requiring a WPF Page parent reference
    /// </summary>
    internal class CompositionServiceAdapter
    {
        // Provide a Dispatcher property for compatibility
        // We'll use a stub implementation since we don't need UI updates from here
        public DispatcherStub Dispatcher { get; } = new DispatcherStub();
        
        // Store paths to return them after composition
        public string DisplayPath { get; private set; }
        public string PrintPath { get; private set; }

        // Provide session context for token resolution
        public string CurrentSessionGuid { get; set; }

        public void UpdateProcessedImagePaths(string outputPath, string printPath)
        {
            System.Diagnostics.Debug.WriteLine($"CompositionServiceAdapter: UpdateProcessedImagePaths - output={outputPath}, print={printPath}");
            DisplayPath = outputPath;
            PrintPath = printPath;
        }

        public void SaveComposedImageToDatabase(string outputPath, string outputFormat)
        {
            System.Diagnostics.Debug.WriteLine($"CompositionServiceAdapter: SaveComposedImageToDatabase - path={outputPath}, format={outputFormat}");
        }

        public void AddComposedImageToPhotoStrip(string outputPath)
        {
            System.Diagnostics.Debug.WriteLine($"CompositionServiceAdapter: AddComposedImageToPhotoStrip - path={outputPath}");
        }

        // Method used by PhotoProcessingOperations via reflection/call to fetch session id
        public string GetCurrentSessionGuid()
        {
            return CurrentSessionGuid;
        }
    }

    /// <summary>
    /// Stub Dispatcher implementation to avoid null reference exceptions
    /// </summary>
    internal class DispatcherStub
    {
        public void BeginInvoke(Action action)
        {
            // Execute the action immediately (not on UI thread since we're in a service)
            // The actual UI update will be handled by the page through events
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DispatcherStub: Error executing action: {ex.Message}");
            }
        }
    }

    #region Event Args Classes
    public class CompositionStartedEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public string TemplateName { get; set; }
    }

    public class CompositionCompletedEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public string ComposedImagePath { get; set; }
        public string ComposedImagePrintPath { get; set; } // Path for printing (may be 4x6 duplicate)
        public TemplateData Template { get; set; }
    }

    public class CompositionErrorEventArgs : EventArgs
    {
        public Exception Error { get; set; }
        public string SessionId { get; set; }
    }
    #endregion
}
