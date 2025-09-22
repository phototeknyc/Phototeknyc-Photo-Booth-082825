using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using CameraControl.Devices;
using Newtonsoft.Json;
using Photobooth.Models;

namespace Photobooth.Database
{
    /// <summary>
    /// Database manager for event-specific backgrounds and photo positioning
    /// Following the same pattern as EventTemplates
    /// </summary>
    public class EventBackgroundDatabase
    {
        private readonly string connectionString;
        private readonly string dbPath;

        public EventBackgroundDatabase()
        {
            string databaseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth", "Database");

            Directory.CreateDirectory(databaseDir);
            dbPath = Path.Combine(databaseDir, "photobooth.db");
            connectionString = $"Data Source={dbPath};Version=3;";

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                // Create EventBackgrounds table (many-to-many relationship between events and backgrounds)
                string createEventBackgroundsTable = @"
                    CREATE TABLE IF NOT EXISTS EventBackgrounds (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EventId INTEGER NOT NULL,
                        BackgroundPath TEXT NOT NULL,
                        BackgroundName TEXT,
                        IsSelected BOOLEAN DEFAULT 0,
                        SortOrder INTEGER DEFAULT 0,
                        PhotoPlacementData TEXT, -- JSON serialized placement data
                        AssignedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        LastModified DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (EventId) REFERENCES Events(Id) ON DELETE CASCADE,
                        UNIQUE(EventId, BackgroundPath)
                    )";

                // Create EventBackgroundSettings table for event-specific settings
                string createEventBackgroundSettingsTable = @"
                    CREATE TABLE IF NOT EXISTS EventBackgroundSettings (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EventId INTEGER NOT NULL UNIQUE,
                        EnableBackgroundRemoval BOOLEAN DEFAULT 0,
                        UseGuestBackgroundPicker BOOLEAN DEFAULT 1,
                        DefaultBackgroundPath TEXT,
                        BackgroundRemovalQuality TEXT DEFAULT 'Low',
                        BackgroundRemovalEdgeRefinement INTEGER DEFAULT 0,
                        LastModified DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (EventId) REFERENCES Events(Id) ON DELETE CASCADE
                    )";

                using (var command = new SQLiteCommand(createEventBackgroundsTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SQLiteCommand(createEventBackgroundSettingsTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                // Create indexes for performance
                string createIndexes = @"
                    CREATE INDEX IF NOT EXISTS idx_event_backgrounds_event_id ON EventBackgrounds(EventId);
                    CREATE INDEX IF NOT EXISTS idx_event_backgrounds_selected ON EventBackgrounds(EventId, IsSelected);
                    CREATE INDEX IF NOT EXISTS idx_event_background_settings_event_id ON EventBackgroundSettings(EventId);";

                using (var command = new SQLiteCommand(createIndexes, connection))
                {
                    command.ExecuteNonQuery();
                }

                Log.Debug("EventBackgroundDatabase: Database initialized successfully");
            }
        }

        #region Background Management

        /// <summary>
        /// Add or update a background for an event
        /// </summary>
        public void SaveEventBackground(int eventId, string backgroundPath, string backgroundName = null, string photoPlacementData = null, bool isSelected = false)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                // Check if this background already exists for this event
                string checkExisting = @"
                    SELECT Id, PhotoPlacementData FROM EventBackgrounds
                    WHERE EventId = @eventId AND BackgroundPath = @backgroundPath";

                int existingId = 0;
                string existingPlacementData = null;

                using (var checkCommand = new SQLiteCommand(checkExisting, connection))
                {
                    checkCommand.Parameters.AddWithValue("@eventId", eventId);
                    checkCommand.Parameters.AddWithValue("@backgroundPath", backgroundPath);

                    using (var reader = checkCommand.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            existingId = reader.GetInt32(0);
                            existingPlacementData = reader.IsDBNull(1) ? null : reader.GetString(1);
                        }
                    }
                }

                if (existingId > 0)
                {
                    // Update existing record
                    // Only update placement data if provided, otherwise keep existing
                    string updateSql = @"
                        UPDATE EventBackgrounds
                        SET BackgroundName = @backgroundName,
                            PhotoPlacementData = @placementData,
                            IsSelected = @isSelected,
                            LastModified = CURRENT_TIMESTAMP
                        WHERE Id = @id";

                    using (var updateCommand = new SQLiteCommand(updateSql, connection))
                    {
                        updateCommand.Parameters.AddWithValue("@id", existingId);
                        updateCommand.Parameters.AddWithValue("@backgroundName", backgroundName ?? Path.GetFileNameWithoutExtension(backgroundPath));
                        updateCommand.Parameters.AddWithValue("@placementData", photoPlacementData ?? existingPlacementData ?? (object)DBNull.Value);
                        updateCommand.Parameters.AddWithValue("@isSelected", isSelected);
                        updateCommand.ExecuteNonQuery();
                    }
                }
                else
                {
                    // Insert new record
                    string insertSql = @"
                        INSERT INTO EventBackgrounds (EventId, BackgroundPath, BackgroundName, PhotoPlacementData, IsSelected, SortOrder)
                        VALUES (@eventId, @backgroundPath, @backgroundName, @placementData, @isSelected,
                                (SELECT COALESCE(MAX(SortOrder), 0) + 1 FROM EventBackgrounds WHERE EventId = @eventId))";

                    using (var insertCommand = new SQLiteCommand(insertSql, connection))
                    {
                        insertCommand.Parameters.AddWithValue("@eventId", eventId);
                        insertCommand.Parameters.AddWithValue("@backgroundPath", backgroundPath);
                        insertCommand.Parameters.AddWithValue("@backgroundName", backgroundName ?? Path.GetFileNameWithoutExtension(backgroundPath));
                        insertCommand.Parameters.AddWithValue("@placementData", photoPlacementData ?? (object)DBNull.Value);
                        insertCommand.Parameters.AddWithValue("@isSelected", isSelected);
                        insertCommand.ExecuteNonQuery();
                    }
                }

                // If this is selected, unselect all others for this event
                if (isSelected)
                {
                    string unselectOthers = @"
                        UPDATE EventBackgrounds
                        SET IsSelected = 0
                        WHERE EventId = @eventId AND BackgroundPath != @backgroundPath";

                    using (var unselectCommand = new SQLiteCommand(unselectOthers, connection))
                    {
                        unselectCommand.Parameters.AddWithValue("@eventId", eventId);
                        unselectCommand.Parameters.AddWithValue("@backgroundPath", backgroundPath);
                        unselectCommand.ExecuteNonQuery();
                    }
                }

                Log.Debug($"EventBackgroundDatabase: Saved background for event {eventId}: {backgroundPath}");
            }
        }

        /// <summary>
        /// Get all backgrounds for an event
        /// </summary>
        public List<EventBackgroundData> GetEventBackgrounds(int eventId)
        {
            var backgrounds = new List<EventBackgroundData>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string selectSql = @"
                    SELECT Id, BackgroundPath, BackgroundName, PhotoPlacementData, IsSelected, SortOrder, LastModified
                    FROM EventBackgrounds
                    WHERE EventId = @eventId
                    ORDER BY SortOrder, Id";

                using (var command = new SQLiteCommand(selectSql, connection))
                {
                    command.Parameters.AddWithValue("@eventId", eventId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var background = new EventBackgroundData
                            {
                                Id = reader.GetInt32(0),
                                EventId = eventId,
                                BackgroundPath = reader.GetString(1),
                                BackgroundName = reader.IsDBNull(2) ? null : reader.GetString(2),
                                PhotoPlacementDataJson = reader.IsDBNull(3) ? null : reader.GetString(3),
                                IsSelected = reader.GetBoolean(4),
                                SortOrder = reader.GetInt32(5),
                                LastModified = reader.GetDateTime(6)
                            };

                            // Deserialize placement data if present
                            if (!string.IsNullOrEmpty(background.PhotoPlacementDataJson))
                            {
                                try
                                {
                                    background.PhotoPlacementData = JsonConvert.DeserializeObject<PhotoPlacementData>(background.PhotoPlacementDataJson);
                                }
                                catch (Exception ex)
                                {
                                    Log.Error($"Failed to deserialize placement data: {ex.Message}");
                                }
                            }

                            backgrounds.Add(background);
                        }
                    }
                }
            }

            return backgrounds;
        }

        /// <summary>
        /// Update photo placement data for a background
        /// </summary>
        public void UpdatePhotoPlacement(int eventId, string backgroundPath, PhotoPlacementData placementData)
        {
            string placementJson = placementData != null ? JsonConvert.SerializeObject(placementData) : null;
            SaveEventBackground(eventId, backgroundPath, photoPlacementData: placementJson);
        }

        /// <summary>
        /// Remove a background from an event
        /// </summary>
        public void RemoveEventBackground(int eventId, string backgroundPath)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string deleteSql = @"
                    DELETE FROM EventBackgrounds
                    WHERE EventId = @eventId AND BackgroundPath = @backgroundPath";

                using (var command = new SQLiteCommand(deleteSql, connection))
                {
                    command.Parameters.AddWithValue("@eventId", eventId);
                    command.Parameters.AddWithValue("@backgroundPath", backgroundPath);
                    command.ExecuteNonQuery();
                }

                Log.Debug($"EventBackgroundDatabase: Removed background from event {eventId}: {backgroundPath}");
            }
        }

        /// <summary>
        /// Set the selected background for an event
        /// </summary>
        public void SetSelectedBackground(int eventId, string backgroundPath)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Unselect all backgrounds for this event
                        string unselectAll = @"
                            UPDATE EventBackgrounds
                            SET IsSelected = 0
                            WHERE EventId = @eventId";

                        using (var unselectCommand = new SQLiteCommand(unselectAll, connection, transaction))
                        {
                            unselectCommand.Parameters.AddWithValue("@eventId", eventId);
                            unselectCommand.ExecuteNonQuery();
                        }

                        // Select the specified background
                        string selectOne = @"
                            UPDATE EventBackgrounds
                            SET IsSelected = 1, LastModified = CURRENT_TIMESTAMP
                            WHERE EventId = @eventId AND BackgroundPath = @backgroundPath";

                        using (var selectCommand = new SQLiteCommand(selectOne, connection, transaction))
                        {
                            selectCommand.Parameters.AddWithValue("@eventId", eventId);
                            selectCommand.Parameters.AddWithValue("@backgroundPath", backgroundPath);
                            selectCommand.ExecuteNonQuery();
                        }

                        transaction.Commit();
                        Log.Debug($"EventBackgroundDatabase: Set selected background for event {eventId}: {backgroundPath}");
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        #endregion

        #region Event Settings

        /// <summary>
        /// Get or create settings for an event
        /// </summary>
        public EventBackgroundSettings GetEventSettings(int eventId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string selectSql = @"
                    SELECT EnableBackgroundRemoval, UseGuestBackgroundPicker, DefaultBackgroundPath,
                           BackgroundRemovalQuality, BackgroundRemovalEdgeRefinement
                    FROM EventBackgroundSettings
                    WHERE EventId = @eventId";

                using (var command = new SQLiteCommand(selectSql, connection))
                {
                    command.Parameters.AddWithValue("@eventId", eventId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new EventBackgroundSettings
                            {
                                EventId = eventId,
                                EnableBackgroundRemoval = reader.GetBoolean(0),
                                UseGuestBackgroundPicker = reader.GetBoolean(1),
                                DefaultBackgroundPath = reader.IsDBNull(2) ? null : reader.GetString(2),
                                BackgroundRemovalQuality = reader.IsDBNull(3) ? "Low" : reader.GetString(3),
                                BackgroundRemovalEdgeRefinement = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
                            };
                        }
                    }
                }

                // Create default settings if not found
                var defaultSettings = new EventBackgroundSettings
                {
                    EventId = eventId,
                    EnableBackgroundRemoval = false,
                    UseGuestBackgroundPicker = true,
                    BackgroundRemovalQuality = "Low",
                    BackgroundRemovalEdgeRefinement = 0
                };

                SaveEventSettings(defaultSettings);
                return defaultSettings;
            }
        }

        /// <summary>
        /// Save event-specific settings
        /// </summary>
        public void SaveEventSettings(EventBackgroundSettings settings)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string upsertSql = @"
                    INSERT OR REPLACE INTO EventBackgroundSettings
                    (EventId, EnableBackgroundRemoval, UseGuestBackgroundPicker, DefaultBackgroundPath,
                     BackgroundRemovalQuality, BackgroundRemovalEdgeRefinement, LastModified)
                    VALUES
                    (@eventId, @enableRemoval, @useGuestPicker, @defaultPath,
                     @quality, @edgeRefinement, CURRENT_TIMESTAMP)";

                using (var command = new SQLiteCommand(upsertSql, connection))
                {
                    command.Parameters.AddWithValue("@eventId", settings.EventId);
                    command.Parameters.AddWithValue("@enableRemoval", settings.EnableBackgroundRemoval);
                    command.Parameters.AddWithValue("@useGuestPicker", settings.UseGuestBackgroundPicker);
                    command.Parameters.AddWithValue("@defaultPath", settings.DefaultBackgroundPath ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@quality", settings.BackgroundRemovalQuality);
                    command.Parameters.AddWithValue("@edgeRefinement", settings.BackgroundRemovalEdgeRefinement);
                    command.ExecuteNonQuery();
                }

                Log.Debug($"EventBackgroundDatabase: Saved settings for event {settings.EventId}");
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Copy all backgrounds from one event to another
        /// </summary>
        public void CopyEventBackgrounds(int sourceEventId, int targetEventId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string copySql = @"
                    INSERT INTO EventBackgrounds (EventId, BackgroundPath, BackgroundName, PhotoPlacementData, IsSelected, SortOrder)
                    SELECT @targetEventId, BackgroundPath, BackgroundName, PhotoPlacementData, IsSelected, SortOrder
                    FROM EventBackgrounds
                    WHERE EventId = @sourceEventId";

                using (var command = new SQLiteCommand(copySql, connection))
                {
                    command.Parameters.AddWithValue("@sourceEventId", sourceEventId);
                    command.Parameters.AddWithValue("@targetEventId", targetEventId);
                    command.ExecuteNonQuery();
                }

                // Also copy settings
                string copySettingsSql = @"
                    INSERT OR REPLACE INTO EventBackgroundSettings
                    (EventId, EnableBackgroundRemoval, UseGuestBackgroundPicker, DefaultBackgroundPath,
                     BackgroundRemovalQuality, BackgroundRemovalEdgeRefinement)
                    SELECT @targetEventId, EnableBackgroundRemoval, UseGuestBackgroundPicker, DefaultBackgroundPath,
                           BackgroundRemovalQuality, BackgroundRemovalEdgeRefinement
                    FROM EventBackgroundSettings
                    WHERE EventId = @sourceEventId";

                using (var command = new SQLiteCommand(copySettingsSql, connection))
                {
                    command.Parameters.AddWithValue("@sourceEventId", sourceEventId);
                    command.Parameters.AddWithValue("@targetEventId", targetEventId);
                    command.ExecuteNonQuery();
                }

                Log.Debug($"EventBackgroundDatabase: Copied backgrounds from event {sourceEventId} to {targetEventId}");
            }
        }

        /// <summary>
        /// Clear all backgrounds for an event
        /// </summary>
        public void ClearEventBackgrounds(int eventId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string deleteSql = @"DELETE FROM EventBackgrounds WHERE EventId = @eventId";

                using (var command = new SQLiteCommand(deleteSql, connection))
                {
                    command.Parameters.AddWithValue("@eventId", eventId);
                    command.ExecuteNonQuery();
                }

                Log.Debug($"EventBackgroundDatabase: Cleared all backgrounds for event {eventId}");
            }
        }

        #endregion
    }

    #region Data Classes

    public class EventBackgroundData
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public string BackgroundPath { get; set; }
        public string BackgroundName { get; set; }
        public PhotoPlacementData PhotoPlacementData { get; set; }
        public string PhotoPlacementDataJson { get; set; }
        public bool IsSelected { get; set; }
        public int SortOrder { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class EventBackgroundSettings
    {
        public int EventId { get; set; }
        public bool EnableBackgroundRemoval { get; set; }
        public bool UseGuestBackgroundPicker { get; set; }
        public string DefaultBackgroundPath { get; set; }
        public string BackgroundRemovalQuality { get; set; }
        public int BackgroundRemovalEdgeRefinement { get; set; }
    }

    #endregion
}