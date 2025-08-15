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

        public UILayoutDatabase()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth");

            if (!Directory.Exists(appDataPath))
                Directory.CreateDirectory(appDataPath);

            _databasePath = Path.Combine(appDataPath, "UILayouts.db");
            _connectionString = $"Data Source={_databasePath};Version=3;";

            InitializeDatabase();
            SeedDefaultTemplates();
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

        public void SaveLayout(UILayoutTemplate layout, bool isSystem = false)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
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
                                LayoutId, ElementId, ElementName, ElementType, AnchorPoint,
                                AnchorOffsetX, AnchorOffsetY, SizeMode, RelativeWidth, RelativeHeight,
                                MinWidth, MinHeight, MaxWidth, MaxHeight, ZIndex, IsVisible, IsEnabled,
                                ActionCommand, Properties
                            ) VALUES (
                                @LayoutId, @ElementId, @ElementName, @ElementType, @AnchorPoint,
                                @AnchorOffsetX, @AnchorOffsetY, @SizeMode, @RelativeWidth, @RelativeHeight,
                                @MinWidth, @MinHeight, @MaxWidth, @MaxHeight, @ZIndex, @IsVisible, @IsEnabled,
                                @ActionCommand, @Properties
                            );";

                        foreach (var element in layout.Elements)
                        {
                            using (var command = new SQLiteCommand(insertElement, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@LayoutId", layout.Id);
                                command.Parameters.AddWithValue("@ElementId", element.Id);
                                command.Parameters.AddWithValue("@ElementName", element.Name);
                                command.Parameters.AddWithValue("@ElementType", element.Type.ToString());
                                command.Parameters.AddWithValue("@AnchorPoint", (int)element.Anchor);
                                command.Parameters.AddWithValue("@AnchorOffsetX", element.AnchorOffset.X);
                                command.Parameters.AddWithValue("@AnchorOffsetY", element.AnchorOffset.Y);
                                command.Parameters.AddWithValue("@SizeMode", (int)element.SizeMode);
                                command.Parameters.AddWithValue("@RelativeWidth", element.RelativeSize.Width);
                                command.Parameters.AddWithValue("@RelativeHeight", element.RelativeSize.Height);
                                command.Parameters.AddWithValue("@MinWidth", element.MinSize.Width);
                                command.Parameters.AddWithValue("@MinHeight", element.MinSize.Height);
                                command.Parameters.AddWithValue("@MaxWidth", element.MaxSize.Width);
                                command.Parameters.AddWithValue("@MaxHeight", element.MaxSize.Height);
                                command.Parameters.AddWithValue("@ZIndex", element.ZIndex);
                                command.Parameters.AddWithValue("@IsVisible", element.IsVisible ? 1 : 0);
                                command.Parameters.AddWithValue("@IsEnabled", element.IsEnabled ? 1 : 0);
                                command.Parameters.AddWithValue("@ActionCommand", element.ActionCommand ?? "");
                                command.Parameters.AddWithValue("@Properties", JsonConvert.SerializeObject(element.Properties));

                                command.ExecuteNonQuery();
                            }
                        }

                        // Add history entry
                        AddHistoryEntry(layout.Id, layout.Version, "Modified", "Layout saved", connection, transaction);

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
    }
}