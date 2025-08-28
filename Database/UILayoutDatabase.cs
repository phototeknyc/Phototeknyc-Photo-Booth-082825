using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using Newtonsoft.Json;
using Photobooth.Models.UITemplates;

namespace Photobooth.Database
{
    public class UILayoutDatabase
    {
        private readonly string _connectionString;
        private readonly string _databasePath;
        private static readonly object _lockObject = new object();
        private static bool _isInitialized = false;

        public UILayoutDatabase()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth");

            if (!Directory.Exists(appDataPath))
                Directory.CreateDirectory(appDataPath);

            _databasePath = Path.Combine(appDataPath, "UILayouts.db");
            _connectionString = $"Data Source={_databasePath};Version=3;Journal Mode=WAL;";

            lock (_lockObject)
            {
                if (!_isInitialized)
                {
                    InitializeDatabase();
                    SeedDefaultTemplates();
                    _isInitialized = true;
                }
            }
        }

        private void InitializeDatabase()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                // Main layouts table
                string createLayoutsTable = @"
                    CREATE TABLE IF NOT EXISTS UILayouts (
                        Id TEXT PRIMARY KEY,
                        Name TEXT NOT NULL,
                        Description TEXT,
                        PreferredOrientation INTEGER, -- 0=Horizontal, 1=Vertical
                        IsDefault INTEGER DEFAULT 0,
                        IsActive INTEGER DEFAULT 0,
                        IsSystem INTEGER DEFAULT 0, -- System templates can't be deleted
                        Version TEXT,
                        CreatedDate TEXT,
                        ModifiedDate TEXT,
                        ThumbnailPath TEXT,
                        LayoutData TEXT NOT NULL, -- JSON serialized layout
                        ThemeData TEXT, -- JSON serialized theme
                        Metadata TEXT -- JSON for additional properties
                    );";

                // Layout elements table (for querying specific elements)
                string createElementsTable = @"
                    CREATE TABLE IF NOT EXISTS UIElements (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        LayoutId TEXT NOT NULL,
                        ElementId TEXT NOT NULL,
                        ElementName TEXT,
                        ElementType TEXT,
                        AnchorPoint INTEGER,
                        AnchorOffsetX REAL,
                        AnchorOffsetY REAL,
                        SizeMode INTEGER,
                        RelativeWidth REAL,
                        RelativeHeight REAL,
                        MinWidth REAL,
                        MinHeight REAL,
                        MaxWidth REAL,
                        MaxHeight REAL,
                        ZIndex INTEGER,
                        IsVisible INTEGER,
                        IsEnabled INTEGER,
                        ActionCommand TEXT,
                        Properties TEXT, -- JSON
                        FOREIGN KEY (LayoutId) REFERENCES UILayouts(Id) ON DELETE CASCADE
                    );";

                // Layout history table (track changes)
                string createHistoryTable = @"
                    CREATE TABLE IF NOT EXISTS UILayoutHistory (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        LayoutId TEXT NOT NULL,
                        Version TEXT,
                        ChangeDate TEXT,
                        ChangeType TEXT, -- Created, Modified, Activated, Deactivated
                        UserId TEXT,
                        LayoutDataSnapshot TEXT, -- Full JSON backup
                        ChangeDescription TEXT,
                        FOREIGN KEY (LayoutId) REFERENCES UILayouts(Id) ON DELETE CASCADE
                    );";

                // Layout profiles table
                string createProfilesTable = @"
                    CREATE TABLE IF NOT EXISTS UILayoutProfiles (
                        Id TEXT PRIMARY KEY,
                        Name TEXT NOT NULL,
                        Description TEXT,
                        Category TEXT,
                        DeviceType TEXT,
                        ResolutionWidth REAL,
                        ResolutionHeight REAL,
                        DiagonalSize REAL,
                        AspectRatio REAL,
                        IsTouchEnabled INTEGER,
                        DPI REAL,
                        PreferredOrientation INTEGER,
                        IsDefault INTEGER DEFAULT 0,
                        IsActive INTEGER DEFAULT 0,
                        ThumbnailPath TEXT,
                        CreatedDate TEXT,
                        LastUsedDate TEXT,
                        Author TEXT,
                        Version TEXT,
                        IsLocked INTEGER DEFAULT 0,
                        Notes TEXT,
                        Metadata TEXT -- JSON for additional properties
                    );";

                // Profile layouts mapping table
                string createProfileLayoutsTable = @"
                    CREATE TABLE IF NOT EXISTS ProfileLayoutMappings (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ProfileId TEXT NOT NULL,
                        LayoutId TEXT NOT NULL,
                        Orientation TEXT NOT NULL, -- 'Portrait' or 'Landscape'
                        FOREIGN KEY (ProfileId) REFERENCES UILayoutProfiles(Id) ON DELETE CASCADE,
                        FOREIGN KEY (LayoutId) REFERENCES UILayouts(Id) ON DELETE CASCADE,
                        UNIQUE(ProfileId, Orientation)
                    );";

                // Layout categories/tags
                string createCategoriesTable = @"
                    CREATE TABLE IF NOT EXISTS UILayoutCategories (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        LayoutId TEXT NOT NULL,
                        Category TEXT NOT NULL,
                        FOREIGN KEY (LayoutId) REFERENCES UILayouts(Id) ON DELETE CASCADE
                    );";

                // User preferences
                string createPreferencesTable = @"
                    CREATE TABLE IF NOT EXISTS UIPreferences (
                        Key TEXT PRIMARY KEY,
                        Value TEXT,
                        ModifiedDate TEXT
                    );";

                // Create indices for performance
                string createIndices = @"
                    CREATE INDEX IF NOT EXISTS idx_layouts_orientation ON UILayouts(PreferredOrientation);
                    CREATE INDEX IF NOT EXISTS idx_layouts_active ON UILayouts(IsActive);
                    CREATE INDEX IF NOT EXISTS idx_elements_layout ON UIElements(LayoutId);
                    CREATE INDEX IF NOT EXISTS idx_elements_type ON UIElements(ElementType);
                    CREATE INDEX IF NOT EXISTS idx_history_layout ON UILayoutHistory(LayoutId);
                    CREATE INDEX IF NOT EXISTS idx_categories_layout ON UILayoutCategories(LayoutId);
                ";

                using (var command = new SQLiteCommand(connection))
                {
                    command.CommandText = createLayoutsTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createElementsTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createHistoryTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createProfilesTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createProfileLayoutsTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createCategoriesTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createPreferencesTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createIndices;
                    command.ExecuteNonQuery();
                }
            }
        }

        private void SeedDefaultTemplates()
        {
            // Check if default templates already exist
            if (GetLayout("default-portrait") != null && GetLayout("default-landscape") != null)
                return;

            // Insert default portrait template
            var portraitTemplate = DefaultTemplates.CreatePortraitTemplate();
            SaveLayout(portraitTemplate, isSystem: true);

            // Insert default landscape template
            var landscapeTemplate = DefaultTemplates.CreateLandscapeTemplate();
            SaveLayout(landscapeTemplate, isSystem: true);

            // Set default active layouts
            SetActiveLayout("default-portrait", Orientation.Vertical);
            SetActiveLayout("default-landscape", Orientation.Horizontal);
        }

        private void SaveLayoutInternal(UILayoutTemplate layout, bool isSystem, SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // Save or update main layout
            string upsertLayout = @"
                INSERT OR REPLACE INTO UILayouts (
                    Id, Name, Description, PreferredOrientation, IsDefault, IsActive, IsSystem,
                    Version, CreatedDate, ModifiedDate, LayoutData, ThemeData
                ) VALUES (
                    @Id, @Name, @Description, @PreferredOrientation, @IsDefault, @IsActive, @IsSystem,
                    @Version, @CreatedDate, @ModifiedDate, @LayoutData, @ThemeData
                );";

            using (var command = new SQLiteCommand(upsertLayout, connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", layout.Id);
                command.Parameters.AddWithValue("@Name", layout.Name);
                command.Parameters.AddWithValue("@Description", layout.Description ?? "");
                command.Parameters.AddWithValue("@PreferredOrientation", (int)layout.PreferredOrientation);
                command.Parameters.AddWithValue("@IsDefault", 0);
                command.Parameters.AddWithValue("@IsActive", 0);
                command.Parameters.AddWithValue("@IsSystem", isSystem ? 1 : 0);
                command.Parameters.AddWithValue("@Version", layout.Version);
                command.Parameters.AddWithValue("@CreatedDate", layout.CreatedDate.ToString("O"));
                command.Parameters.AddWithValue("@ModifiedDate", DateTime.Now.ToString("O"));
                command.Parameters.AddWithValue("@LayoutData", JsonConvert.SerializeObject(layout.Elements));
                command.Parameters.AddWithValue("@ThemeData", JsonConvert.SerializeObject(layout.Theme));

                command.ExecuteNonQuery();
            }

            // Delete existing elements for this layout
            string deleteElements = "DELETE FROM UIElements WHERE LayoutId = @LayoutId;";
            using (var command = new SQLiteCommand(deleteElements, connection, transaction))
            {
                command.Parameters.AddWithValue("@LayoutId", layout.Id);
                command.ExecuteNonQuery();
            }

            // Insert individual elements for searching/filtering
            string insertElement = @"
                INSERT INTO UIElements (
                    LayoutId, ElementId, ElementType, ZIndex, Properties
                ) VALUES (
                    @LayoutId, @ElementId, @ElementType, @ZIndex, @Properties
                );";

            foreach (var element in layout.Elements)
            {
                using (var command = new SQLiteCommand(insertElement, connection, transaction))
                {
                    command.Parameters.AddWithValue("@LayoutId", layout.Id);
                    command.Parameters.AddWithValue("@ElementId", element.Id);
                    command.Parameters.AddWithValue("@ElementType", element.Type.ToString());
                    command.Parameters.AddWithValue("@ZIndex", element.ZIndex);
                    command.Parameters.AddWithValue("@Properties", JsonConvert.SerializeObject(element.Properties));
                    command.ExecuteNonQuery();
                }
            }
        }

        public void SaveLayout(UILayoutTemplate layout, bool isSystem = false)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        SaveLayoutInternal(layout, isSystem, connection, transaction);
                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        public UILayoutTemplate GetLayout(string layoutId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string query = @"
                    SELECT Id, Name, Description, PreferredOrientation, Version,
                           CreatedDate, ModifiedDate, LayoutData, ThemeData
                    FROM UILayouts
                    WHERE Id = @Id;";

                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", layoutId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var layout = new UILayoutTemplate
                            {
                                Id = reader.GetString(0),
                                Name = reader.GetString(1),
                                Description = reader.GetString(2),
                                PreferredOrientation = (Orientation)reader.GetInt32(3),
                                Version = reader.GetString(4),
                                CreatedDate = DateTime.Parse(reader.GetString(5)),
                                ModifiedDate = DateTime.Parse(reader.GetString(6)),
                                Elements = JsonConvert.DeserializeObject<List<UIElementTemplate>>(reader.GetString(7)),
                                Theme = JsonConvert.DeserializeObject<UITheme>(reader.GetString(8))
                            };

                            return layout;
                        }
                    }
                }
            }

            return null;
        }

        public UILayoutTemplate GetActiveLayout(Orientation orientation)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string query = @"
                    SELECT Id, Name, Description, PreferredOrientation, Version,
                           CreatedDate, ModifiedDate, LayoutData, ThemeData
                    FROM UILayouts
                    WHERE PreferredOrientation = @Orientation AND IsActive = 1
                    LIMIT 1;";

                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Orientation", (int)orientation);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var layout = new UILayoutTemplate
                            {
                                Id = reader.GetString(0),
                                Name = reader.GetString(1),
                                Description = reader.GetString(2),
                                PreferredOrientation = (Orientation)reader.GetInt32(3),
                                Version = reader.GetString(4),
                                CreatedDate = DateTime.Parse(reader.GetString(5)),
                                ModifiedDate = DateTime.Parse(reader.GetString(6)),
                                Elements = JsonConvert.DeserializeObject<List<UIElementTemplate>>(reader.GetString(7)),
                                Theme = JsonConvert.DeserializeObject<UITheme>(reader.GetString(8))
                            };

                            return layout;
                        }
                    }
                }
            }

            // Return default if no active layout found
            return orientation == Orientation.Vertical 
                ? GetLayout("default-portrait") 
                : GetLayout("default-landscape");
        }

        public void SetActiveLayout(string layoutId, Orientation orientation)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Deactivate all layouts for this orientation
                        string deactivateQuery = @"
                            UPDATE UILayouts 
                            SET IsActive = 0 
                            WHERE PreferredOrientation = @Orientation;";

                        using (var command = new SQLiteCommand(deactivateQuery, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Orientation", (int)orientation);
                            command.ExecuteNonQuery();
                        }

                        // Activate the selected layout
                        string activateQuery = @"
                            UPDATE UILayouts 
                            SET IsActive = 1 
                            WHERE Id = @Id;";

                        using (var command = new SQLiteCommand(activateQuery, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Id", layoutId);
                            command.ExecuteNonQuery();
                        }

                        // Add history entry
                        AddHistoryEntry(layoutId, "", "Activated", $"Layout activated for {orientation}", connection, transaction);

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        public List<UILayoutTemplate> GetAllLayouts(Orientation? orientation = null)
        {
            var layouts = new List<UILayoutTemplate>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string query = @"
                    SELECT Id, Name, Description, PreferredOrientation, Version,
                           CreatedDate, ModifiedDate, LayoutData, ThemeData
                    FROM UILayouts";

                if (orientation.HasValue)
                {
                    query += " WHERE PreferredOrientation = @Orientation";
                }

                query += " ORDER BY ModifiedDate DESC;";

                using (var command = new SQLiteCommand(query, connection))
                {
                    if (orientation.HasValue)
                    {
                        command.Parameters.AddWithValue("@Orientation", (int)orientation.Value);
                    }

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var layout = new UILayoutTemplate
                            {
                                Id = reader.GetString(0),
                                Name = reader.GetString(1),
                                Description = reader.GetString(2),
                                PreferredOrientation = (Orientation)reader.GetInt32(3),
                                Version = reader.GetString(4),
                                CreatedDate = DateTime.Parse(reader.GetString(5)),
                                ModifiedDate = DateTime.Parse(reader.GetString(6)),
                                Elements = JsonConvert.DeserializeObject<List<UIElementTemplate>>(reader.GetString(7)),
                                Theme = JsonConvert.DeserializeObject<UITheme>(reader.GetString(8))
                            };

                            layouts.Add(layout);
                        }
                    }
                }
            }

            return layouts;
        }

        public void DeleteLayout(string layoutId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                // Check if it's a system layout
                string checkQuery = "SELECT IsSystem FROM UILayouts WHERE Id = @Id;";
                using (var command = new SQLiteCommand(checkQuery, connection))
                {
                    command.Parameters.AddWithValue("@Id", layoutId);
                    var result = command.ExecuteScalar();
                    if (result != null && Convert.ToInt32(result) == 1)
                    {
                        throw new InvalidOperationException("Cannot delete system layouts");
                    }
                }

                string deleteQuery = "DELETE FROM UILayouts WHERE Id = @Id;";
                using (var command = new SQLiteCommand(deleteQuery, connection))
                {
                    command.Parameters.AddWithValue("@Id", layoutId);
                    command.ExecuteNonQuery();
                }
            }
        }

        public UILayoutTemplate DuplicateLayout(string layoutId, string newName)
        {
            var original = GetLayout(layoutId);
            if (original == null)
                return null;

            var duplicate = new UILayoutTemplate
            {
                Id = Guid.NewGuid().ToString(),
                Name = newName,
                Description = original.Description + " (Copy)",
                PreferredOrientation = original.PreferredOrientation,
                Version = "1.0",
                CreatedDate = DateTime.Now,
                ModifiedDate = DateTime.Now,
                Elements = JsonConvert.DeserializeObject<List<UIElementTemplate>>(
                    JsonConvert.SerializeObject(original.Elements)), // Deep copy
                Theme = JsonConvert.DeserializeObject<UITheme>(
                    JsonConvert.SerializeObject(original.Theme)) // Deep copy
            };

            SaveLayout(duplicate, isSystem: false);
            return duplicate;
        }

        private void AddHistoryEntry(string layoutId, string version, string changeType, string description,
            SQLiteConnection connection, SQLiteTransaction transaction)
        {
            string insertHistory = @"
                INSERT INTO UILayoutHistory (
                    LayoutId, Version, ChangeDate, ChangeType, ChangeDescription
                ) VALUES (
                    @LayoutId, @Version, @ChangeDate, @ChangeType, @ChangeDescription
                );";

            using (var command = new SQLiteCommand(insertHistory, connection, transaction))
            {
                command.Parameters.AddWithValue("@LayoutId", layoutId);
                command.Parameters.AddWithValue("@Version", version);
                command.Parameters.AddWithValue("@ChangeDate", DateTime.Now.ToString("O"));
                command.Parameters.AddWithValue("@ChangeType", changeType);
                command.Parameters.AddWithValue("@ChangeDescription", description);

                command.ExecuteNonQuery();
            }
        }

        public void ExportLayout(string layoutId, string filePath)
        {
            var layout = GetLayout(layoutId);
            if (layout != null)
            {
                var json = JsonConvert.SerializeObject(layout, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
        }

        public UILayoutTemplate ImportLayout(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            var json = File.ReadAllText(filePath);
            var layout = JsonConvert.DeserializeObject<UILayoutTemplate>(json);
            
            // Generate new ID to avoid conflicts
            layout.Id = Guid.NewGuid().ToString();
            layout.Name = layout.Name + " (Imported)";
            layout.CreatedDate = DateTime.Now;
            layout.ModifiedDate = DateTime.Now;

            SaveLayout(layout, isSystem: false);
            return layout;
        }

        #region Profile Management

        /// <summary>
        /// Save a UI layout profile to the database
        /// </summary>
        public void SaveProfile(UILayoutProfile profile)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Insert or update profile
                        string sql = @"
                            INSERT OR REPLACE INTO UILayoutProfiles (
                                Id, Name, Description, Category, DeviceType,
                                ResolutionWidth, ResolutionHeight, DiagonalSize,
                                AspectRatio, IsTouchEnabled, DPI, PreferredOrientation,
                                IsDefault, IsActive, ThumbnailPath,
                                CreatedDate, LastUsedDate, Author, Version,
                                IsLocked, Notes, Metadata
                            ) VALUES (
                                @Id, @Name, @Description, @Category, @DeviceType,
                                @ResolutionWidth, @ResolutionHeight, @DiagonalSize,
                                @AspectRatio, @IsTouchEnabled, @DPI, @PreferredOrientation,
                                @IsDefault, @IsActive, @ThumbnailPath,
                                @CreatedDate, @LastUsedDate, @Author, @Version,
                                @IsLocked, @Notes, @Metadata
                            )";

                        using (var command = new SQLiteCommand(sql, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Id", profile.Id);
                            command.Parameters.AddWithValue("@Name", profile.Name);
                            command.Parameters.AddWithValue("@Description", profile.Description ?? string.Empty);
                            command.Parameters.AddWithValue("@Category", profile.Category ?? "Custom");
                            command.Parameters.AddWithValue("@DeviceType", profile.ScreenConfig.DeviceType);
                            command.Parameters.AddWithValue("@ResolutionWidth", profile.ScreenConfig.Resolution.Width);
                            command.Parameters.AddWithValue("@ResolutionHeight", profile.ScreenConfig.Resolution.Height);
                            command.Parameters.AddWithValue("@DiagonalSize", profile.ScreenConfig.DiagonalSize);
                            command.Parameters.AddWithValue("@AspectRatio", profile.ScreenConfig.AspectRatio);
                            command.Parameters.AddWithValue("@IsTouchEnabled", profile.ScreenConfig.IsTouchEnabled ? 1 : 0);
                            command.Parameters.AddWithValue("@DPI", profile.ScreenConfig.DPI);
                            command.Parameters.AddWithValue("@PreferredOrientation", (int)profile.ScreenConfig.PreferredOrientation);
                            command.Parameters.AddWithValue("@IsDefault", profile.IsDefault ? 1 : 0);
                            command.Parameters.AddWithValue("@IsActive", profile.IsActive ? 1 : 0);
                            command.Parameters.AddWithValue("@ThumbnailPath", profile.ThumbnailPath ?? string.Empty);
                            command.Parameters.AddWithValue("@CreatedDate", profile.CreatedDate.ToString("o"));
                            command.Parameters.AddWithValue("@LastUsedDate", profile.LastUsedDate.ToString("o"));
                            command.Parameters.AddWithValue("@Author", profile.Metadata.Author ?? string.Empty);
                            command.Parameters.AddWithValue("@Version", profile.Metadata.Version ?? "1.0");
                            command.Parameters.AddWithValue("@IsLocked", profile.Metadata.IsLocked ? 1 : 0);
                            command.Parameters.AddWithValue("@Notes", profile.Metadata.Notes ?? string.Empty);
                            command.Parameters.AddWithValue("@Metadata", JsonConvert.SerializeObject(profile.Metadata));

                            command.ExecuteNonQuery();
                        }

                        // Delete existing mappings
                        string deleteMappings = "DELETE FROM ProfileLayoutMappings WHERE ProfileId = @ProfileId";
                        using (var command = new SQLiteCommand(deleteMappings, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@ProfileId", profile.Id);
                            command.ExecuteNonQuery();
                        }

                        // Save layout mappings
                        foreach (var kvp in profile.Layouts)
                        {
                            // Save the layout itself within the same transaction
                            SaveLayoutInternal(kvp.Value, isSystem: false, connection, transaction);

                            // Create mapping
                            string insertMapping = @"
                                INSERT INTO ProfileLayoutMappings (ProfileId, LayoutId, Orientation)
                                VALUES (@ProfileId, @LayoutId, @Orientation)";

                            using (var command = new SQLiteCommand(insertMapping, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@ProfileId", profile.Id);
                                command.Parameters.AddWithValue("@LayoutId", kvp.Value.Id);
                                command.Parameters.AddWithValue("@Orientation", kvp.Key);
                                command.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Load a profile by ID
        /// </summary>
        public UILayoutProfile LoadProfile(string profileId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string sql = "SELECT * FROM UILayoutProfiles WHERE Id = @Id";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Id", profileId);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return ReadProfileFromDatabase(reader, connection);
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Get all available profiles
        /// </summary>
        public List<UILayoutProfile> GetAllProfiles()
        {
            var profiles = new List<UILayoutProfile>();
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string sql = "SELECT * FROM UILayoutProfiles ORDER BY Category, Name";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            profiles.Add(ReadProfileFromDatabase(reader, connection));
                        }
                    }
                }
            }
            return profiles;
        }

        /// <summary>
        /// Get profiles by category
        /// </summary>
        public List<UILayoutProfile> GetProfilesByCategory(string category)
        {
            var profiles = new List<UILayoutProfile>();
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string sql = "SELECT * FROM UILayoutProfiles WHERE Category = @Category ORDER BY Name";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Category", category);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            profiles.Add(ReadProfileFromDatabase(reader, connection));
                        }
                    }
                }
            }
            return profiles;
        }

        /// <summary>
        /// Get the active profile
        /// </summary>
        public UILayoutProfile GetActiveProfile()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string sql = "SELECT * FROM UILayoutProfiles WHERE IsActive = 1 LIMIT 1";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return ReadProfileFromDatabase(reader, connection);
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Set a profile as active
        /// </summary>
        public void SetActiveProfile(string profileId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Deactivate all profiles
                        string deactivateAll = "UPDATE UILayoutProfiles SET IsActive = 0";
                        using (var command = new SQLiteCommand(deactivateAll, connection, transaction))
                        {
                            command.ExecuteNonQuery();
                        }

                        // Activate selected profile
                        string activate = "UPDATE UILayoutProfiles SET IsActive = 1, LastUsedDate = @LastUsedDate WHERE Id = @Id";
                        using (var command = new SQLiteCommand(activate, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Id", profileId);
                            command.Parameters.AddWithValue("@LastUsedDate", DateTime.Now.ToString("o"));
                            command.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Delete a profile
        /// </summary>
        public void DeleteProfile(string profileId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                // Check if locked
                string checkLocked = "SELECT IsLocked FROM UILayoutProfiles WHERE Id = @Id";
                using (var command = new SQLiteCommand(checkLocked, connection))
                {
                    command.Parameters.AddWithValue("@Id", profileId);
                    var result = command.ExecuteScalar();
                    if (result != null && Convert.ToInt32(result) == 1)
                    {
                        throw new InvalidOperationException("Cannot delete a locked profile");
                    }
                }

                // Delete profile (cascades to mappings)
                string sql = "DELETE FROM UILayoutProfiles WHERE Id = @Id";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Id", profileId);
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Export profile to file
        /// </summary>
        public void ExportProfile(string profileId, string filePath)
        {
            var profile = LoadProfile(profileId);
            if (profile != null)
            {
                var json = JsonConvert.SerializeObject(profile, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
        }

        /// <summary>
        /// Import profile from file
        /// </summary>
        public UILayoutProfile ImportProfile(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            var json = File.ReadAllText(filePath);
            var profile = JsonConvert.DeserializeObject<UILayoutProfile>(json);
            
            // Generate new ID to avoid conflicts
            profile.Id = Guid.NewGuid().ToString();
            profile.Name = profile.Name + " (Imported)";
            profile.CreatedDate = DateTime.Now;
            profile.LastUsedDate = DateTime.Now;
            profile.IsActive = false;

            SaveProfile(profile);
            return profile;
        }

        /// <summary>
        /// Initialize predefined profiles if not exists
        /// </summary>
        public void InitializePredefinedProfiles()
        {
            try
            {
                var existingProfiles = GetAllProfiles();
                
                if (existingProfiles.Count == 0)
                {
                    var predefinedProfiles = PredefinedProfiles.GetAllPredefined();
                    foreach (var profile in predefinedProfiles)
                    {
                        try
                        {
                            // Add default layouts for each profile
                            profile.Layouts["Portrait"] = DefaultTemplates.CreatePortraitTemplate();
                            profile.Layouts["Landscape"] = DefaultTemplates.CreateLandscapeTemplate();
                            SaveProfile(profile);
                        }
                        catch (SQLiteException ex) when (ex.Message.Contains("database is locked"))
                        {
                            // Wait and retry once
                            System.Threading.Thread.Sleep(100);
                            try
                            {
                                SaveProfile(profile);
                            }
                            catch
                            {
                                // Skip this profile if still locked
                                System.Diagnostics.Debug.WriteLine($"Could not save profile {profile.Name}: database locked");
                            }
                        }
                    }

                    // Set first profile as active
                    if (predefinedProfiles.Count > 0)
                    {
                        try
                        {
                            SetActiveProfile(predefinedProfiles[0].Id);
                        }
                        catch
                        {
                            // Ignore if can't set active
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing predefined profiles: {ex.Message}");
                // Don't throw - allow app to continue without predefined profiles
            }
        }

        private UILayoutProfile ReadProfileFromDatabase(SQLiteDataReader reader, SQLiteConnection connection)
        {
            var profile = new UILayoutProfile
            {
                Id = reader["Id"].ToString(),
                Name = reader["Name"].ToString(),
                Description = reader["Description"].ToString(),
                Category = reader["Category"].ToString(),
                IsDefault = Convert.ToInt32(reader["IsDefault"]) == 1,
                IsActive = Convert.ToInt32(reader["IsActive"]) == 1,
                ThumbnailPath = reader["ThumbnailPath"].ToString(),
                CreatedDate = DateTime.Parse(reader["CreatedDate"].ToString()),
                LastUsedDate = DateTime.Parse(reader["LastUsedDate"].ToString()),
                ScreenConfig = new ScreenConfiguration
                {
                    DeviceType = reader["DeviceType"].ToString(),
                    Resolution = new System.Windows.Size(
                        Convert.ToDouble(reader["ResolutionWidth"]),
                        Convert.ToDouble(reader["ResolutionHeight"])),
                    DiagonalSize = Convert.ToDouble(reader["DiagonalSize"]),
                    AspectRatio = Convert.ToDouble(reader["AspectRatio"]),
                    IsTouchEnabled = Convert.ToInt32(reader["IsTouchEnabled"]) == 1,
                    DPI = Convert.ToDouble(reader["DPI"]),
                    PreferredOrientation = (ScreenOrientation)Convert.ToInt32(reader["PreferredOrientation"])
                },
                Metadata = JsonConvert.DeserializeObject<ProfileMetadata>(reader["Metadata"].ToString()) 
                    ?? new ProfileMetadata()
            };

            // Load associated layouts
            string sql = @"
                SELECT l.*, plm.Orientation 
                FROM ProfileLayoutMappings plm
                JOIN UILayouts l ON plm.LayoutId = l.Id
                WHERE plm.ProfileId = @ProfileId";

            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@ProfileId", profile.Id);
                using (var layoutReader = command.ExecuteReader())
                {
                    while (layoutReader.Read())
                    {
                        var orientation = layoutReader["Orientation"].ToString();
                        
                        // Reconstruct the UILayoutTemplate from the database
                        var layout = new UILayoutTemplate
                        {
                            Id = layoutReader["Id"].ToString(),
                            Name = layoutReader["Name"].ToString(),
                            Description = layoutReader["Description"].ToString(),
                            PreferredOrientation = (Orientation)Convert.ToInt32(layoutReader["PreferredOrientation"]),
                            Version = layoutReader["Version"].ToString(),
                            CreatedDate = DateTime.Parse(layoutReader["CreatedDate"].ToString()),
                            ModifiedDate = DateTime.Parse(layoutReader["ModifiedDate"].ToString()),
                            Elements = new List<UIElementTemplate>()
                        };

                        // Deserialize elements array from LayoutData
                        var layoutDataJson = layoutReader["LayoutData"].ToString();
                        if (!string.IsNullOrEmpty(layoutDataJson))
                        {
                            layout.Elements = JsonConvert.DeserializeObject<List<UIElementTemplate>>(layoutDataJson) ?? new List<UIElementTemplate>();
                        }

                        // Deserialize theme from ThemeData
                        var themeDataJson = layoutReader["ThemeData"].ToString();
                        if (!string.IsNullOrEmpty(themeDataJson))
                        {
                            layout.Theme = JsonConvert.DeserializeObject<UITheme>(themeDataJson);
                        }

                        profile.Layouts[orientation] = layout;
                    }
                }
            }

            return profile;
        }

        #endregion
    }
}