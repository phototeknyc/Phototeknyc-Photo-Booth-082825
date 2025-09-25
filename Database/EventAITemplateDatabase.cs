using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace Photobooth.Database
{
    /// <summary>
    /// Database for managing AI transformation template assignments to events
    /// </summary>
    public class EventAITemplateDatabase
    {
        #region Singleton
        private static EventAITemplateDatabase _instance;
        private static readonly object _lock = new object();

        public static EventAITemplateDatabase Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new EventAITemplateDatabase();
                        }
                    }
                }
                return _instance;
            }
        }
        #endregion

        #region Properties
        private readonly string _databasePath;
        private readonly string _connectionString;
        #endregion

        #region Constructor
        private EventAITemplateDatabase()
        {
            string appDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Photobooth");

            if (!Directory.Exists(appDataFolder))
            {
                Directory.CreateDirectory(appDataFolder);
            }

            _databasePath = Path.Combine(appDataFolder, "event_ai_templates.db");
            _connectionString = $"Data Source={_databasePath};Version=3;";

            InitializeDatabase();
        }
        #endregion

        #region Database Initialization
        private void InitializeDatabase()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                // Create EventAITemplates table (many-to-many relationship between events and AI templates)
                string createEventAITemplatesTable = @"
                    CREATE TABLE IF NOT EXISTS EventAITemplates (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EventId INTEGER NOT NULL,
                        TemplateId INTEGER NOT NULL,
                        TemplateName TEXT,
                        CategoryId INTEGER,
                        CategoryName TEXT,
                        IsSelected BOOLEAN DEFAULT 0,
                        IsDefault BOOLEAN DEFAULT 0,
                        SortOrder INTEGER DEFAULT 0,
                        AssignedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        LastModified DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (EventId) REFERENCES Events(Id) ON DELETE CASCADE,
                        UNIQUE(EventId, TemplateId)
                    )";

                // Create EventAITemplateSettings table for event-specific AI settings
                string createEventAITemplateSettingsTable = @"
                    CREATE TABLE IF NOT EXISTS EventAITemplateSettings (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EventId INTEGER NOT NULL UNIQUE,
                        EnableAITransformation BOOLEAN DEFAULT 0,
                        AutoApplyDefault BOOLEAN DEFAULT 0,
                        ShowSelectionOverlay BOOLEAN DEFAULT 1,
                        SelectionTimeout INTEGER DEFAULT 120,
                        DefaultTemplateId INTEGER,
                        CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        LastModified DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (EventId) REFERENCES Events(Id) ON DELETE CASCADE
                    )";

                using (var command = new SQLiteCommand(createEventAITemplatesTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SQLiteCommand(createEventAITemplateSettingsTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                // Create indexes for performance
                string createIndexes = @"
                    CREATE INDEX IF NOT EXISTS idx_event_ai_templates_event_id ON EventAITemplates(EventId);
                    CREATE INDEX IF NOT EXISTS idx_event_ai_templates_selected ON EventAITemplates(EventId, IsSelected);
                    CREATE INDEX IF NOT EXISTS idx_event_ai_templates_default ON EventAITemplates(EventId, IsDefault);
                    CREATE INDEX IF NOT EXISTS idx_event_ai_template_settings_event_id ON EventAITemplateSettings(EventId);";

                using (var command = new SQLiteCommand(createIndexes, connection))
                {
                    command.ExecuteNonQuery();
                }

                System.Diagnostics.Debug.WriteLine("EventAITemplateDatabase: Database initialized successfully");
            }
        }
        #endregion

        #region Template Management

        /// <summary>
        /// Add or update an AI template assignment for an event
        /// </summary>
        public void AddOrUpdateTemplateForEvent(int eventId, int templateId, string templateName,
            int? categoryId = null, string categoryName = null, bool isSelected = false, bool isDefault = false)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                // Check if this template already exists for this event
                string checkExisting = @"
                    SELECT Id FROM EventAITemplates
                    WHERE EventId = @eventId AND TemplateId = @templateId";

                int existingId = 0;

                using (var checkCommand = new SQLiteCommand(checkExisting, connection))
                {
                    checkCommand.Parameters.AddWithValue("@eventId", eventId);
                    checkCommand.Parameters.AddWithValue("@templateId", templateId);

                    var result = checkCommand.ExecuteScalar();
                    if (result != null)
                    {
                        existingId = Convert.ToInt32(result);
                    }
                }

                if (existingId > 0)
                {
                    // Update existing record
                    string updateSql = @"
                        UPDATE EventAITemplates
                        SET TemplateName = @templateName,
                            CategoryId = @categoryId,
                            CategoryName = @categoryName,
                            IsSelected = @isSelected,
                            IsDefault = @isDefault,
                            LastModified = CURRENT_TIMESTAMP
                        WHERE Id = @id";

                    using (var updateCommand = new SQLiteCommand(updateSql, connection))
                    {
                        updateCommand.Parameters.AddWithValue("@id", existingId);
                        updateCommand.Parameters.AddWithValue("@templateName", templateName ?? "");
                        updateCommand.Parameters.AddWithValue("@categoryId", categoryId ?? (object)DBNull.Value);
                        updateCommand.Parameters.AddWithValue("@categoryName", categoryName ?? (object)DBNull.Value);
                        updateCommand.Parameters.AddWithValue("@isSelected", isSelected);
                        updateCommand.Parameters.AddWithValue("@isDefault", isDefault);
                        updateCommand.ExecuteNonQuery();
                    }
                }
                else
                {
                    // Insert new record
                    string insertSql = @"
                        INSERT INTO EventAITemplates (EventId, TemplateId, TemplateName, CategoryId, CategoryName, IsSelected, IsDefault, SortOrder)
                        VALUES (@eventId, @templateId, @templateName, @categoryId, @categoryName, @isSelected, @isDefault,
                                (SELECT COALESCE(MAX(SortOrder), 0) + 1 FROM EventAITemplates WHERE EventId = @eventId))";

                    using (var insertCommand = new SQLiteCommand(insertSql, connection))
                    {
                        insertCommand.Parameters.AddWithValue("@eventId", eventId);
                        insertCommand.Parameters.AddWithValue("@templateId", templateId);
                        insertCommand.Parameters.AddWithValue("@templateName", templateName ?? "");
                        insertCommand.Parameters.AddWithValue("@categoryId", categoryId ?? (object)DBNull.Value);
                        insertCommand.Parameters.AddWithValue("@categoryName", categoryName ?? (object)DBNull.Value);
                        insertCommand.Parameters.AddWithValue("@isSelected", isSelected);
                        insertCommand.Parameters.AddWithValue("@isDefault", isDefault);
                        insertCommand.ExecuteNonQuery();
                    }
                }

                // If this is default, unset all other defaults for this event
                if (isDefault)
                {
                    string unsetOtherDefaults = @"
                        UPDATE EventAITemplates
                        SET IsDefault = 0
                        WHERE EventId = @eventId AND TemplateId != @templateId";

                    using (var unsetCommand = new SQLiteCommand(unsetOtherDefaults, connection))
                    {
                        unsetCommand.Parameters.AddWithValue("@eventId", eventId);
                        unsetCommand.Parameters.AddWithValue("@templateId", templateId);
                        unsetCommand.ExecuteNonQuery();
                    }

                    // Update settings table with default template
                    UpdateEventSettings(eventId, defaultTemplateId: templateId);
                }

                System.Diagnostics.Debug.WriteLine($"EventAITemplateDatabase: Saved template {templateId} for event {eventId}");
            }
        }

        /// <summary>
        /// Get all AI templates assigned to an event
        /// </summary>
        public List<EventAITemplate> GetTemplatesForEvent(int eventId)
        {
            var templates = new List<EventAITemplate>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string sql = @"
                    SELECT Id, TemplateId, TemplateName, CategoryId, CategoryName,
                           IsSelected, IsDefault, SortOrder
                    FROM EventAITemplates
                    WHERE EventId = @eventId
                    ORDER BY SortOrder, TemplateName";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@eventId", eventId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            templates.Add(new EventAITemplate
                            {
                                Id = reader.GetInt32(0),
                                EventId = eventId,
                                TemplateId = reader.GetInt32(1),
                                TemplateName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                CategoryId = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
                                CategoryName = reader.IsDBNull(4) ? null : reader.GetString(4),
                                IsSelected = reader.GetBoolean(5),
                                IsDefault = reader.GetBoolean(6),
                                SortOrder = reader.GetInt32(7)
                            });
                        }
                    }
                }
            }

            return templates;
        }

        /// <summary>
        /// Get selected templates for an event
        /// </summary>
        public List<EventAITemplate> GetSelectedTemplatesForEvent(int eventId)
        {
            return GetTemplatesForEvent(eventId).Where(t => t.IsSelected).ToList();
        }

        /// <summary>
        /// Get the default template for an event
        /// </summary>
        public EventAITemplate GetDefaultTemplateForEvent(int eventId)
        {
            return GetTemplatesForEvent(eventId).FirstOrDefault(t => t.IsDefault);
        }

        /// <summary>
        /// Remove a template from an event
        /// </summary>
        public void RemoveTemplateFromEvent(int eventId, int templateId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string sql = @"
                    DELETE FROM EventAITemplates
                    WHERE EventId = @eventId AND TemplateId = @templateId";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@eventId", eventId);
                    command.Parameters.AddWithValue("@templateId", templateId);
                    command.ExecuteNonQuery();
                }

                System.Diagnostics.Debug.WriteLine($"EventAITemplateDatabase: Removed template {templateId} from event {eventId}");
            }
        }

        /// <summary>
        /// Clear all templates for an event
        /// </summary>
        public void ClearTemplatesForEvent(int eventId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string sql = @"
                    DELETE FROM EventAITemplates
                    WHERE EventId = @eventId";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@eventId", eventId);
                    command.ExecuteNonQuery();
                }

                System.Diagnostics.Debug.WriteLine($"EventAITemplateDatabase: Cleared all templates for event {eventId}");
            }
        }

        /// <summary>
        /// Set template selection status
        /// </summary>
        public void SetTemplateSelection(int eventId, int templateId, bool isSelected)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                // First check if the record exists
                string checkSql = @"
                    SELECT COUNT(*) FROM EventAITemplates
                    WHERE EventId = @eventId AND TemplateId = @templateId";

                bool exists = false;
                using (var checkCommand = new SQLiteCommand(checkSql, connection))
                {
                    checkCommand.Parameters.AddWithValue("@eventId", eventId);
                    checkCommand.Parameters.AddWithValue("@templateId", templateId);
                    exists = Convert.ToInt32(checkCommand.ExecuteScalar()) > 0;
                }

                if (exists)
                {
                    // Update existing record
                    string updateSql = @"
                        UPDATE EventAITemplates
                        SET IsSelected = @isSelected,
                            LastModified = CURRENT_TIMESTAMP
                        WHERE EventId = @eventId AND TemplateId = @templateId";

                    using (var updateCommand = new SQLiteCommand(updateSql, connection))
                    {
                        updateCommand.Parameters.AddWithValue("@eventId", eventId);
                        updateCommand.Parameters.AddWithValue("@templateId", templateId);
                        updateCommand.Parameters.AddWithValue("@isSelected", isSelected);
                        updateCommand.ExecuteNonQuery();
                    }
                }
                else
                {
                    // Insert new record if it doesn't exist
                    string insertSql = @"
                        INSERT INTO EventAITemplates (EventId, TemplateId, IsSelected, IsDefault, CreatedDate, LastModified)
                        VALUES (@eventId, @templateId, @isSelected, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)";

                    using (var insertCommand = new SQLiteCommand(insertSql, connection))
                    {
                        insertCommand.Parameters.AddWithValue("@eventId", eventId);
                        insertCommand.Parameters.AddWithValue("@templateId", templateId);
                        insertCommand.Parameters.AddWithValue("@isSelected", isSelected);
                        insertCommand.ExecuteNonQuery();
                    }
                }

                System.Diagnostics.Debug.WriteLine($"EventAITemplateDatabase: Set template {templateId} selection to {isSelected} for event {eventId}");
            }
        }

        #endregion

        #region Settings Management

        /// <summary>
        /// Get AI template settings for an event
        /// </summary>
        public EventAITemplateSettings GetEventSettings(int eventId)
        {
            System.Diagnostics.Debug.WriteLine($"EventAITemplateDatabase: GetEventSettings called for event {eventId}");

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string sql = @"
                    SELECT EnableAITransformation, AutoApplyDefault, ShowSelectionOverlay,
                           SelectionTimeout, DefaultTemplateId
                    FROM EventAITemplateSettings
                    WHERE EventId = @eventId";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@eventId", eventId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var settings = new EventAITemplateSettings
                            {
                                EventId = eventId,
                                EnableAITransformation = reader.GetBoolean(0),
                                AutoApplyDefault = reader.GetBoolean(1),
                                ShowSelectionOverlay = reader.GetBoolean(2),
                                SelectionTimeout = reader.GetInt32(3),
                                DefaultTemplateId = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4)
                            };
                            System.Diagnostics.Debug.WriteLine($"EventAITemplateDatabase: Found settings - EnableAITransformation={settings.EnableAITransformation}");
                            return settings;
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"EventAITemplateDatabase: No settings found for event {eventId}, returning defaults");
                // Return default settings if none exist
                return new EventAITemplateSettings
                {
                    EventId = eventId,
                    EnableAITransformation = false,
                    AutoApplyDefault = false,
                    ShowSelectionOverlay = true,
                    SelectionTimeout = 120,
                    DefaultTemplateId = null
                };
            }
        }

        /// <summary>
        /// Update AI template settings for an event
        /// </summary>
        public void UpdateEventSettings(int eventId, bool? enableAITransformation = null,
            bool? autoApplyDefault = null, bool? showSelectionOverlay = null,
            int? selectionTimeout = null, int? defaultTemplateId = null)
        {
            System.Diagnostics.Debug.WriteLine($"EventAITemplateDatabase: UpdateEventSettings called for event {eventId}");
            System.Diagnostics.Debug.WriteLine($"  EnableAITransformation: {enableAITransformation}");
            System.Diagnostics.Debug.WriteLine($"  AutoApplyDefault: {autoApplyDefault}");
            System.Diagnostics.Debug.WriteLine($"  ShowSelectionOverlay: {showSelectionOverlay}");

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                // Check if settings exist
                string checkSql = "SELECT COUNT(*) FROM EventAITemplateSettings WHERE EventId = @eventId";
                bool exists = false;

                using (var checkCommand = new SQLiteCommand(checkSql, connection))
                {
                    checkCommand.Parameters.AddWithValue("@eventId", eventId);
                    exists = Convert.ToInt32(checkCommand.ExecuteScalar()) > 0;
                }

                if (exists)
                {
                    // Build dynamic update query based on provided parameters
                    var updateParts = new List<string>();
                    var parameters = new Dictionary<string, object>();

                    if (enableAITransformation.HasValue)
                    {
                        updateParts.Add("EnableAITransformation = @enable");
                        parameters["@enable"] = enableAITransformation.Value;
                    }
                    if (autoApplyDefault.HasValue)
                    {
                        updateParts.Add("AutoApplyDefault = @auto");
                        parameters["@auto"] = autoApplyDefault.Value;
                    }
                    if (showSelectionOverlay.HasValue)
                    {
                        updateParts.Add("ShowSelectionOverlay = @show");
                        parameters["@show"] = showSelectionOverlay.Value;
                    }
                    if (selectionTimeout.HasValue)
                    {
                        updateParts.Add("SelectionTimeout = @timeout");
                        parameters["@timeout"] = selectionTimeout.Value;
                    }
                    if (defaultTemplateId.HasValue)
                    {
                        updateParts.Add("DefaultTemplateId = @defaultId");
                        parameters["@defaultId"] = defaultTemplateId.Value;
                    }

                    if (updateParts.Count > 0)
                    {
                        updateParts.Add("LastModified = CURRENT_TIMESTAMP");

                        string updateSql = $@"
                            UPDATE EventAITemplateSettings
                            SET {string.Join(", ", updateParts)}
                            WHERE EventId = @eventId";

                        using (var updateCommand = new SQLiteCommand(updateSql, connection))
                        {
                            updateCommand.Parameters.AddWithValue("@eventId", eventId);
                            foreach (var param in parameters)
                            {
                                updateCommand.Parameters.AddWithValue(param.Key, param.Value);
                            }
                            updateCommand.ExecuteNonQuery();
                            System.Diagnostics.Debug.WriteLine($"EventAITemplateDatabase: Updated settings for event {eventId}");
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"EventAITemplateDatabase: No existing settings for event {eventId}, inserting new");
                    // Insert new settings
                    string insertSql = @"
                        INSERT INTO EventAITemplateSettings
                        (EventId, EnableAITransformation, AutoApplyDefault, ShowSelectionOverlay, SelectionTimeout, DefaultTemplateId)
                        VALUES (@eventId, @enable, @auto, @show, @timeout, @defaultId)";

                    using (var insertCommand = new SQLiteCommand(insertSql, connection))
                    {
                        insertCommand.Parameters.AddWithValue("@eventId", eventId);
                        insertCommand.Parameters.AddWithValue("@enable", enableAITransformation ?? false);
                        insertCommand.Parameters.AddWithValue("@auto", autoApplyDefault ?? false);
                        insertCommand.Parameters.AddWithValue("@show", showSelectionOverlay ?? true);
                        insertCommand.Parameters.AddWithValue("@timeout", selectionTimeout ?? 120);
                        insertCommand.Parameters.AddWithValue("@defaultId", defaultTemplateId ?? (object)DBNull.Value);
                        insertCommand.ExecuteNonQuery();
                        System.Diagnostics.Debug.WriteLine($"EventAITemplateDatabase: Inserted new settings for event {eventId}");
                    }
                }
            }
        }

        #endregion
    }

    #region Model Classes

    public class EventAITemplate
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public int TemplateId { get; set; }
        public string TemplateName { get; set; }
        public int? CategoryId { get; set; }
        public string CategoryName { get; set; }
        public bool IsSelected { get; set; }
        public bool IsDefault { get; set; }
        public int SortOrder { get; set; }
    }

    public class EventAITemplateSettings
    {
        public int EventId { get; set; }
        public bool EnableAITransformation { get; set; }
        public bool AutoApplyDefault { get; set; }
        public bool ShowSelectionOverlay { get; set; }
        public int SelectionTimeout { get; set; }
        public int? DefaultTemplateId { get; set; }
    }

    #endregion
}