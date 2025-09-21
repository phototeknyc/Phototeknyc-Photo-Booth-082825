using System;
using System.Linq;
using CameraControl.Devices;
using Photobooth.Models;

namespace Photobooth.Services
{
    /// <summary>
    /// Service to synchronize background settings across different UI controls
    /// </summary>
    public static class BackgroundSettingsSyncService
    {
        /// <summary>
        /// Sync all background-related settings from Properties.Settings
        /// </summary>
        public static void SyncAllBackgroundSettings()
        {
            try
            {
                // Sync background removal enabled state
                var backgroundRemovalEnabled = Properties.Settings.Default.EnableBackgroundRemoval;

                // Sync selected virtual background
                var selectedBackground = Properties.Settings.Default.SelectedVirtualBackground;
                if (!string.IsNullOrEmpty(selectedBackground))
                {
                    VirtualBackgroundService.Instance.SetSelectedBackground(selectedBackground);
                    Log.Debug($"[BackgroundSync] Synced selected background: {selectedBackground}");
                }

                // Sync photo placement data
                var placementDataJson = Properties.Settings.Default.PhotoPlacementData;
                if (!string.IsNullOrEmpty(placementDataJson))
                {
                    try
                    {
                        var placementData = PhotoPlacementData.FromJson(placementDataJson);
                        // Store in event background service for access during composition
                        if (!string.IsNullOrEmpty(selectedBackground))
                        {
                            _ = EventBackgroundService.Instance.SavePhotoPlacementForBackground(selectedBackground, placementData);
                            Log.Debug($"[BackgroundSync] Synced photo placement data");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[BackgroundSync] Failed to sync placement data: {ex.Message}");
                    }
                }

                // Sync event background IDs
                var eventBackgroundIds = Properties.Settings.Default.EventBackgroundIds;
                if (!string.IsNullOrEmpty(eventBackgroundIds))
                {
                    Log.Debug($"[BackgroundSync] Event background IDs: {eventBackgroundIds}");
                }

                // Sync background removal settings
                Properties.Settings.Default.EnableBackgroundRemoval = backgroundRemovalEnabled;

                Log.Debug("[BackgroundSync] Background settings synchronized successfully");
            }
            catch (Exception ex)
            {
                Log.Error($"[BackgroundSync] Failed to sync background settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Save current background settings to Properties.Settings
        /// </summary>
        public static void SaveCurrentSettings(string backgroundPath, PhotoPlacementData placementData)
        {
            try
            {
                // Save selected background
                if (!string.IsNullOrEmpty(backgroundPath))
                {
                    Properties.Settings.Default.SelectedVirtualBackground = backgroundPath;
                }

                // Save placement data
                if (placementData != null)
                {
                    Properties.Settings.Default.PhotoPlacementData = placementData.ToJson();
                    Properties.Settings.Default.CurrentBackgroundPhotoPosition = Newtonsoft.Json.JsonConvert.SerializeObject(new
                    {
                        BackgroundPath = backgroundPath,
                        PlacementData = placementData,
                        LastUpdated = DateTime.Now
                    });
                }

                // Save settings
                Properties.Settings.Default.Save();

                Log.Debug($"[BackgroundSync] Settings saved for background: {backgroundPath}");
            }
            catch (Exception ex)
            {
                Log.Error($"[BackgroundSync] Failed to save settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Load and apply saved background settings
        /// </summary>
        public static PhotoPlacementData LoadSavedSettings()
        {
            try
            {
                // Load selected background
                var selectedBackground = Properties.Settings.Default.SelectedVirtualBackground;
                if (!string.IsNullOrEmpty(selectedBackground))
                {
                    VirtualBackgroundService.Instance.SetSelectedBackground(selectedBackground);
                }

                // Load placement data
                var placementDataJson = Properties.Settings.Default.PhotoPlacementData;
                if (!string.IsNullOrEmpty(placementDataJson))
                {
                    try
                    {
                        var placementData = PhotoPlacementData.FromJson(placementDataJson);
                        Log.Debug("[BackgroundSync] Loaded saved placement data");
                        return placementData;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[BackgroundSync] Failed to parse placement data: {ex.Message}");
                    }
                }

                // Return default placement if none saved
                return new PhotoPlacementData
                {
                    PlacementZones = new System.Collections.Generic.List<PhotoPlacementZone>
                    {
                        new PhotoPlacementZone
                        {
                            PhotoIndex = 0,
                            X = 0.1,
                            Y = 0.1,
                            Width = 0.8,
                            Height = 0.8,
                            Rotation = 0,
                            IsEnabled = true
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                Log.Error($"[BackgroundSync] Failed to load settings: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Clear all background settings
        /// </summary>
        public static void ClearBackgroundSettings()
        {
            try
            {
                Properties.Settings.Default.SelectedVirtualBackground = string.Empty;
                Properties.Settings.Default.PhotoPlacementData = string.Empty;
                Properties.Settings.Default.CurrentBackgroundPhotoPosition = string.Empty;
                Properties.Settings.Default.EventBackgroundIds = string.Empty;
                Properties.Settings.Default.EnableBackgroundRemoval = false;
                Properties.Settings.Default.Save();

                Log.Debug("[BackgroundSync] Background settings cleared");
            }
            catch (Exception ex)
            {
                Log.Error($"[BackgroundSync] Failed to clear settings: {ex.Message}");
            }
        }
    }
}