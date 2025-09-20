using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;

namespace Photobooth.Database
{
    public class TemplateDatabase
    {
        private string connectionString;
        
        public TemplateDatabase(string databasePath = "templates.db")
        {
            connectionString = $"Data Source={databasePath};Version=3;";
            try
            {
                InitializeDatabase();
            }
            catch (DllNotFoundException ex)
            {
                // SQLite native library not found - show user-friendly error
                MessageBox.Show(
                    "SQLite database is not available. Template saving/loading will not work.\n\n" +
                    "To fix this, please install the SQLite runtime or restart the application.\n\n" +
                    $"Technical details: {ex.Message}", 
                    "Database Unavailable", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                // Other database initialization errors
                MessageBox.Show(
                    $"Failed to initialize database: {ex.Message}\n\n" +
                    "Template saving/loading may not work properly.", 
                    "Database Error", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
            }
        }
        
        private void InitializeDatabase()
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                // Create Templates table
                string createTemplatesTable = @"
                    CREATE TABLE IF NOT EXISTS Templates (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        Description TEXT,
                        CanvasWidth REAL NOT NULL,
                        CanvasHeight REAL NOT NULL,
                        BackgroundColor TEXT,
                        BackgroundImagePath TEXT,
                        ThumbnailImagePath TEXT,
                        CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        IsActive BOOLEAN DEFAULT 1
                    )";
                
                // Create CanvasItems table for layout positions and properties
                string createCanvasItemsTable = @"
                    CREATE TABLE IF NOT EXISTS CanvasItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TemplateId INTEGER NOT NULL,
                        ItemType TEXT NOT NULL, -- 'Image', 'Text', 'Placeholder', 'Shape'
                        Name TEXT NOT NULL,
                        
                        -- Position and Size
                        X REAL NOT NULL,
                        Y REAL NOT NULL,
                        Width REAL NOT NULL,
                        Height REAL NOT NULL,
                        Rotation REAL DEFAULT 0,
                        ZIndex INTEGER DEFAULT 0,
                        
                        -- Lock States
                        LockedPosition BOOLEAN DEFAULT 0,
                        LockedSize BOOLEAN DEFAULT 0,
                        LockedAspectRatio BOOLEAN DEFAULT 0,
                        IsVisible BOOLEAN DEFAULT 1,
                        IsLocked BOOLEAN DEFAULT 0,
                        
                        -- Image Properties
                        ImagePath TEXT,
                        ImageHash TEXT,
                        
                        -- Text Properties
                        Text TEXT,
                        FontFamily TEXT,
                        FontSize REAL,
                        FontWeight TEXT,
                        FontStyle TEXT,
                        TextColor TEXT,
                        TextAlignment TEXT,
                        IsBold BOOLEAN DEFAULT 0,
                        IsItalic BOOLEAN DEFAULT 0,
                        IsUnderlined BOOLEAN DEFAULT 0,
                        
                        -- Text Effects
                        HasShadow BOOLEAN DEFAULT 0,
                        ShadowOffsetX REAL DEFAULT 0,
                        ShadowOffsetY REAL DEFAULT 0,
                        ShadowBlurRadius REAL DEFAULT 0,
                        ShadowColor TEXT,
                        HasOutline BOOLEAN DEFAULT 0,
                        OutlineThickness REAL DEFAULT 0,
                        OutlineColor TEXT,
                        
                        -- Placeholder Properties
                        PlaceholderNumber INTEGER,
                        PlaceholderColor TEXT,
                        
                        -- Shape Properties
                        ShapeType TEXT,
                        FillColor TEXT,
                        StrokeColor TEXT,
                        StrokeThickness REAL DEFAULT 0,
                        HasNoFill INTEGER DEFAULT 0,
                        HasNoStroke INTEGER DEFAULT 0,
                        
                        -- Additional Properties (JSON for extensibility)
                        CustomProperties TEXT,
                        
                        CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (TemplateId) REFERENCES Templates(Id) ON DELETE CASCADE
                    )";
                
                // Create TemplateCategories table
                string createCategoriesTable = @"
                    CREATE TABLE IF NOT EXISTS TemplateCategories (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL UNIQUE,
                        Description TEXT,
                        Color TEXT,
                        SortOrder INTEGER DEFAULT 0
                    )";
                
                // Create TemplateCategoryMapping table
                string createCategoryMappingTable = @"
                    CREATE TABLE IF NOT EXISTS TemplateCategoryMapping (
                        TemplateId INTEGER NOT NULL,
                        CategoryId INTEGER NOT NULL,
                        PRIMARY KEY (TemplateId, CategoryId),
                        FOREIGN KEY (TemplateId) REFERENCES Templates(Id) ON DELETE CASCADE,
                        FOREIGN KEY (CategoryId) REFERENCES TemplateCategories(Id) ON DELETE CASCADE
                    )";
                
                // Create Events table
                string createEventsTable = @"
                    CREATE TABLE IF NOT EXISTS Events (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        Description TEXT,
                        EventType TEXT, -- 'Wedding', 'Birthday', 'Corporate', 'Holiday', etc.
                        Location TEXT,
                        EventDate DATE,
                        StartTime TIME,
                        EndTime TIME,
                        HostName TEXT,
                        ContactEmail TEXT,
                        ContactPhone TEXT,
                        IsActive BOOLEAN DEFAULT 1,
                        CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        ModifiedDate DATETIME DEFAULT CURRENT_TIMESTAMP
                    )";
                
                // Create EventTemplates table (many-to-many relationship)
                string createEventTemplatesTable = @"
                    CREATE TABLE IF NOT EXISTS EventTemplates (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EventId INTEGER NOT NULL,
                        TemplateId INTEGER NOT NULL,
                        TemplateName TEXT NOT NULL, -- Copy of template name at time of assignment
                        IsDefaultTemplate BOOLEAN DEFAULT 0,
                        SortOrder INTEGER DEFAULT 0,
                        AssignedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (EventId) REFERENCES Events(Id) ON DELETE CASCADE,
                        FOREIGN KEY (TemplateId) REFERENCES Templates(Id) ON DELETE CASCADE
                    )";
                
                // Create PhotoSessions table
                string createPhotoSessionsTable = @"
                    CREATE TABLE IF NOT EXISTS PhotoSessions (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EventId INTEGER NOT NULL,
                        TemplateId INTEGER NOT NULL,
                        SessionName TEXT,
                        PhotosTaken INTEGER DEFAULT 0,
                        StartTime DATETIME DEFAULT CURRENT_TIMESTAMP,
                        EndTime DATETIME,
                        SessionGuid TEXT UNIQUE, -- Unique identifier for file organization
                        IsActive BOOLEAN DEFAULT 1,
                        FOREIGN KEY (EventId) REFERENCES Events(Id) ON DELETE CASCADE,
                        FOREIGN KEY (TemplateId) REFERENCES Templates(Id) ON DELETE RESTRICT
                    )";

                // Create Photos table for individual photos within sessions
                string createPhotosTable = @"
                    CREATE TABLE IF NOT EXISTS Photos (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SessionId INTEGER NOT NULL,
                        FilePath TEXT NOT NULL,
                        FileName TEXT NOT NULL,
                        FileSize INTEGER,
                        PhotoType TEXT DEFAULT 'Original', -- 'Original', 'Filtered', 'Preview'
                        SequenceNumber INTEGER DEFAULT 1,
                        CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        ThumbnailPath TEXT,
                        CameraSettings TEXT, -- JSON for camera metadata
                        IsActive BOOLEAN DEFAULT 1,
                        FOREIGN KEY (SessionId) REFERENCES PhotoSessions(Id) ON DELETE CASCADE
                    )";

                // Create ComposedImages table for final layout images
                string createComposedImagesTable = @"
                    CREATE TABLE IF NOT EXISTS ComposedImages (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SessionId INTEGER NOT NULL,
                        FilePath TEXT NOT NULL,
                        FileName TEXT NOT NULL,
                        FileSize INTEGER,
                        TemplateId INTEGER NOT NULL,
                        OutputFormat TEXT DEFAULT '4x6', -- '4x6', '2x6', 'Custom'
                        CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        PrintCount INTEGER DEFAULT 0,
                        LastPrintDate DATETIME,
                        ThumbnailPath TEXT,
                        IsActive BOOLEAN DEFAULT 1,
                        FOREIGN KEY (SessionId) REFERENCES PhotoSessions(Id) ON DELETE CASCADE,
                        FOREIGN KEY (TemplateId) REFERENCES Templates(Id) ON DELETE RESTRICT
                    )";

                // Create ComposedImagePhotos junction table
                string createComposedImagePhotosTable = @"
                    CREATE TABLE IF NOT EXISTS ComposedImagePhotos (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ComposedImageId INTEGER NOT NULL,
                        PhotoId INTEGER NOT NULL,
                        PlaceholderIndex INTEGER, -- Which placeholder in template this photo fills
                        FOREIGN KEY (ComposedImageId) REFERENCES ComposedImages(Id) ON DELETE CASCADE,
                        FOREIGN KEY (PhotoId) REFERENCES Photos(Id) ON DELETE CASCADE,
                        UNIQUE(ComposedImageId, PhotoId)
                    )";

                // Create SMS log table for tracking sent messages
                string createSMSLogTable = @"
                    CREATE TABLE IF NOT EXISTS SMSLog (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SessionGuid TEXT NOT NULL,
                        PhoneNumber TEXT NOT NULL,
                        GalleryUrl TEXT NOT NULL,
                        SentDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        Success BOOLEAN DEFAULT 0,
                        ErrorMessage TEXT,
                        FOREIGN KEY (SessionGuid) REFERENCES PhotoSessions(SessionGuid) ON DELETE CASCADE
                    )";
                
                // Create indexes
                string createIndexes = @"
                    CREATE INDEX IF NOT EXISTS idx_templates_name ON Templates(Name);
                    CREATE INDEX IF NOT EXISTS idx_templates_active ON Templates(IsActive);
                    CREATE INDEX IF NOT EXISTS idx_canvasitems_template ON CanvasItems(TemplateId);
                    CREATE INDEX IF NOT EXISTS idx_canvasitems_type ON CanvasItems(ItemType);
                    CREATE INDEX IF NOT EXISTS idx_canvasitems_zindex ON CanvasItems(ZIndex);
                    CREATE INDEX IF NOT EXISTS idx_canvasitems_hash ON CanvasItems(ImageHash);
                    CREATE INDEX IF NOT EXISTS idx_events_date ON Events(EventDate);
                    CREATE INDEX IF NOT EXISTS idx_events_active ON Events(IsActive);
                    CREATE INDEX IF NOT EXISTS idx_eventtemplates_event ON EventTemplates(EventId);
                    CREATE INDEX IF NOT EXISTS idx_eventtemplates_template ON EventTemplates(TemplateId);
                    CREATE INDEX IF NOT EXISTS idx_photosessions_event ON PhotoSessions(EventId);
                    CREATE INDEX IF NOT EXISTS idx_photosessions_guid ON PhotoSessions(SessionGuid);
                    CREATE INDEX IF NOT EXISTS idx_photos_session ON Photos(SessionId);
                    CREATE INDEX IF NOT EXISTS idx_photos_type ON Photos(PhotoType);
                    CREATE INDEX IF NOT EXISTS idx_photos_sequence ON Photos(SequenceNumber);
                    CREATE INDEX IF NOT EXISTS idx_composedimages_session ON ComposedImages(SessionId);
                    CREATE INDEX IF NOT EXISTS idx_composedimages_template ON ComposedImages(TemplateId);
                    CREATE INDEX IF NOT EXISTS idx_composedimagephotos_composed ON ComposedImagePhotos(ComposedImageId);
                    CREATE INDEX IF NOT EXISTS idx_composedimagephotos_photo ON ComposedImagePhotos(PhotoId);
                    CREATE INDEX IF NOT EXISTS idx_smslog_session ON SMSLog(SessionGuid);
                    CREATE INDEX IF NOT EXISTS idx_smslog_date ON SMSLog(SentDate);
                ";
                
                using (var command = new SQLiteCommand(createTemplatesTable, connection))
                {
                    command.ExecuteNonQuery();
                }
                
                using (var command = new SQLiteCommand(createCanvasItemsTable, connection))
                {
                    command.ExecuteNonQuery();
                }
                
                using (var command = new SQLiteCommand(createCategoriesTable, connection))
                {
                    command.ExecuteNonQuery();
                }
                
                using (var command = new SQLiteCommand(createCategoryMappingTable, connection))
                {
                    command.ExecuteNonQuery();
                }
                
                using (var command = new SQLiteCommand(createEventsTable, connection))
                {
                    command.ExecuteNonQuery();
                }
                
                using (var command = new SQLiteCommand(createEventTemplatesTable, connection))
                {
                    command.ExecuteNonQuery();
                }
                
                using (var command = new SQLiteCommand(createPhotoSessionsTable, connection))
                {
                    command.ExecuteNonQuery();
                }
                
                using (var command = new SQLiteCommand(createPhotosTable, connection))
                {
                    command.ExecuteNonQuery();
                }
                
                using (var command = new SQLiteCommand(createComposedImagesTable, connection))
                {
                    command.ExecuteNonQuery();
                }
                
                using (var command = new SQLiteCommand(createComposedImagePhotosTable, connection))
                {
                    command.ExecuteNonQuery();
                }
                
                using (var command = new SQLiteCommand(createSMSLogTable, connection))
                {
                    command.ExecuteNonQuery();
                }
                
                // Run database migrations BEFORE creating indexes
                MigrateDatabase(connection);
                
                using (var command = new SQLiteCommand(createIndexes, connection))
                {
                    command.ExecuteNonQuery();
                }
                
                // Add video columns to PhotoSessions table if they don't exist
                // First check which columns already exist to avoid SQLite errors
                var videoColumnsToAdd = new Dictionary<string, string>
                {
                    { "IsVideoSession", "BOOLEAN DEFAULT 0" },
                    { "VideoPath", "TEXT" },
                    { "VideoThumbnailPath", "TEXT" },
                    { "VideoFileSize", "INTEGER DEFAULT 0" },
                    { "VideoDurationSeconds", "INTEGER DEFAULT 0" },
                    { "VideoCloudUrl", "TEXT" }
                };
                
                // Check existing columns
                string checkColumnsQuery = "PRAGMA table_info(PhotoSessions)";
                var existingColumns = new HashSet<string>();
                using (var checkCmd = new SQLiteCommand(checkColumnsQuery, connection))
                using (var reader = checkCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        existingColumns.Add(reader.GetString(1)); // Column name is at index 1
                    }
                }
                
                // Only add columns that don't exist
                foreach (var column in videoColumnsToAdd)
                {
                    if (!existingColumns.Contains(column.Key))
                    {
                        string alterCmd = $"ALTER TABLE PhotoSessions ADD COLUMN {column.Key} {column.Value}";
                        using (var command = new SQLiteCommand(alterCmd, connection))
                        {
                            command.ExecuteNonQuery();
                        }
                        System.Diagnostics.Debug.WriteLine($"Added {column.Key} column to PhotoSessions table");
                    }
                }
                
                // Insert default categories
                InsertDefaultCategories(connection);
            }
        }
        
        private void MigrateDatabase(SQLiteConnection connection)
        {
            try
            {
                // Check if we need to migrate from BLOB columns to path columns
                string checkColumns = "PRAGMA table_info(Templates)";
                bool hasBackgroundImagePath = false;
                bool hasBackgroundImage = false;
                bool hasThumbnailImagePath = false;
                bool hasThumbnailImage = false;
                
                using (var command = new SQLiteCommand(checkColumns, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string columnName = reader.GetString(1); // Column 1 is 'name' in PRAGMA table_info
                        if (columnName == "BackgroundImagePath") hasBackgroundImagePath = true;
                        if (columnName == "BackgroundImage") hasBackgroundImage = true;
                        if (columnName == "ThumbnailImagePath") hasThumbnailImagePath = true;
                        if (columnName == "ThumbnailImage") hasThumbnailImage = true;
                    }
                }
                
                // Migration 1: Add path columns if they don't exist
                if (!hasBackgroundImagePath)
                {
                    string addColumn = "ALTER TABLE Templates ADD COLUMN BackgroundImagePath TEXT";
                    using (var command = new SQLiteCommand(addColumn, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    System.Diagnostics.Debug.WriteLine("Added BackgroundImagePath column to Templates table");
                }
                
                if (!hasThumbnailImagePath)
                {
                    string addColumn = "ALTER TABLE Templates ADD COLUMN ThumbnailImagePath TEXT";
                    using (var command = new SQLiteCommand(addColumn, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    System.Diagnostics.Debug.WriteLine("Added ThumbnailImagePath column to Templates table");
                }
                
                // Check CanvasItems table for Shape columns
                string checkCanvasColumns = "PRAGMA table_info(CanvasItems)";
                bool hasImageData = false;
                bool hasHasNoFill = false;
                bool hasHasNoStroke = false;
                
                using (var command = new SQLiteCommand(checkCanvasColumns, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string columnName = reader.GetString(1); // Column 1 is 'name' in PRAGMA table_info
                        if (columnName == "ImageData") hasImageData = true;
                        if (columnName == "HasNoFill") hasHasNoFill = true;
                        if (columnName == "HasNoStroke") hasHasNoStroke = true;
                    }
                }
                
                // Add HasNoFill column if it doesn't exist
                if (!hasHasNoFill)
                {
                    string addColumn = "ALTER TABLE CanvasItems ADD COLUMN HasNoFill INTEGER DEFAULT 0";
                    using (var command = new SQLiteCommand(addColumn, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    System.Diagnostics.Debug.WriteLine("Added HasNoFill column to CanvasItems table");
                }
                
                // Add HasNoStroke column if it doesn't exist
                if (!hasHasNoStroke)
                {
                    string addColumn = "ALTER TABLE CanvasItems ADD COLUMN HasNoStroke INTEGER DEFAULT 0";
                    using (var command = new SQLiteCommand(addColumn, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    System.Diagnostics.Debug.WriteLine("Added HasNoStroke column to CanvasItems table");
                }
                
                // Migration 3: Add SessionGuid column to PhotoSessions table if it doesn't exist
                string checkPhotoSessionsColumns = "PRAGMA table_info(PhotoSessions)";
                bool hasSessionGuid = false;
                
                try
                {
                    using (var command = new SQLiteCommand(checkPhotoSessionsColumns, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string columnName = reader.GetString(1); // Column 1 is 'name' in PRAGMA table_info
                            if (columnName == "SessionGuid") hasSessionGuid = true;
                        }
                    }
                    
                    if (!hasSessionGuid)
                    {
                        string addSessionGuidColumn = "ALTER TABLE PhotoSessions ADD COLUMN SessionGuid TEXT";
                        using (var command = new SQLiteCommand(addSessionGuidColumn, connection))
                        {
                            command.ExecuteNonQuery();
                        }
                        System.Diagnostics.Debug.WriteLine("Added SessionGuid column to PhotoSessions table");
                        
                        // Generate GUIDs for existing sessions
                        string updateExistingSessions = @"
                            UPDATE PhotoSessions 
                            SET SessionGuid = LOWER(HEX(RANDOMBLOB(4)) || '-' || HEX(RANDOMBLOB(2)) || '-4' || SUBSTR(HEX(RANDOMBLOB(2)), 2) || '-' || SUBSTR('89AB', ABS(RANDOM()) % 4 + 1, 1) || SUBSTR(HEX(RANDOMBLOB(2)), 2) || '-' || HEX(RANDOMBLOB(6)))
                            WHERE SessionGuid IS NULL OR SessionGuid = ''";
                        using (var command = new SQLiteCommand(updateExistingSessions, connection))
                        {
                            command.ExecuteNonQuery();
                        }
                        System.Diagnostics.Debug.WriteLine("Generated GUIDs for existing PhotoSessions");
                    }
                }
                catch (Exception sessionMigrationEx)
                {
                    System.Diagnostics.Debug.WriteLine($"PhotoSessions migration failed: {sessionMigrationEx.Message}");
                    // Continue anyway - table might not exist yet
                }
                
                // Migration 2: Remove BLOB columns if path columns exist (optional cleanup)
                // Note: SQLite doesn't support DROP COLUMN, so we'll just ignore the old columns
                
                // Migration 3: Add GalleryUrl column to PhotoSessions table for cloud sharing
                bool hasGalleryUrl = false;
                try
                {
                    string checkGalleryUrlColumn = "PRAGMA table_info(PhotoSessions)";
                    using (var command = new SQLiteCommand(checkGalleryUrlColumn, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader["name"].ToString() == "GalleryUrl")
                            {
                                hasGalleryUrl = true;
                                break;
                            }
                        }
                    }
                    
                    if (!hasGalleryUrl)
                    {
                        string addGalleryUrlColumn = "ALTER TABLE PhotoSessions ADD COLUMN GalleryUrl TEXT";
                        using (var command = new SQLiteCommand(addGalleryUrlColumn, connection))
                        {
                            command.ExecuteNonQuery();
                        }
                        System.Diagnostics.Debug.WriteLine("Added GalleryUrl column to PhotoSessions table");
                    }
                }
                catch (Exception galleryUrlMigrationEx)
                {
                    System.Diagnostics.Debug.WriteLine($"PhotoSessions GalleryUrl migration failed: {galleryUrlMigrationEx.Message}");
                    // Continue anyway - table might not exist yet
                }
                
                System.Diagnostics.Debug.WriteLine($"Database migration check complete. BackgroundImagePath: {hasBackgroundImagePath}, ThumbnailImagePath: {hasThumbnailImagePath}, SessionGuid: {hasSessionGuid}, GalleryUrl: {hasGalleryUrl}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Database migration failed: {ex.Message}");
                // Continue anyway - worst case is we have both old and new columns
            }
        }
        
        private void InsertDefaultCategories(SQLiteConnection connection)
        {
            string[] defaultCategories = {
                "Portrait;Photo templates for single person photos;#4A90E2",
                "Group;Templates for group photos;#50C878",
                "Event;Special event templates;#FF6B6B",
                "Holiday;Holiday themed templates;#FFD93D",
                "Wedding;Wedding photo templates;#FF69B4",
                "Birthday;Birthday celebration templates;#9B59B6",
                "Corporate;Business and corporate templates;#34495E"
            };
            
            foreach (string categoryData in defaultCategories)
            {
                string[] parts = categoryData.Split(';');
                string insertCategory = @"
                    INSERT OR IGNORE INTO TemplateCategories (Name, Description, Color) 
                    VALUES (@name, @description, @color)";
                
                using (var command = new SQLiteCommand(insertCategory, connection))
                {
                    command.Parameters.AddWithValue("@name", parts[0]);
                    command.Parameters.AddWithValue("@description", parts[1]);
                    command.Parameters.AddWithValue("@color", parts[2]);
                    command.ExecuteNonQuery();
                }
            }
        }
        
        public int SaveTemplateWithId(TemplateData template)
        {
            // This method preserves the template ID for sync purposes
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                // IMPORTANT: Don't delete the template here because it will CASCADE DELETE the canvas items
                // Instead, we'll use INSERT OR REPLACE which updates if exists, inserts if not

                // Reset the autoincrement counter if needed to allow reuse of the ID
                string resetAutoIncrement = $"UPDATE sqlite_sequence SET seq = {template.Id - 1} WHERE name = 'Templates' AND seq < {template.Id}";
                using (var resetCommand = new SQLiteCommand(resetAutoIncrement, connection))
                {
                    try
                    {
                        resetCommand.ExecuteNonQuery();
                    }
                    catch
                    {
                        // Ignore if sqlite_sequence doesn't exist or update fails
                    }
                }

                // Check if new columns exist before using them
                string checkColumns = "PRAGMA table_info(Templates)";
                bool hasPathColumns = false;
                using (var checkCommand = new SQLiteCommand(checkColumns, connection))
                using (var reader = checkCommand.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.GetString(1) == "BackgroundImagePath")
                        {
                            hasPathColumns = true;
                            break;
                        }
                    }
                }

                string insertTemplate;
                if (hasPathColumns)
                {
                    // Use INSERT OR REPLACE to update if exists, insert if not
                    insertTemplate = @"
                        INSERT OR REPLACE INTO Templates (Id, Name, Description, CanvasWidth, CanvasHeight,
                                             BackgroundColor, BackgroundImagePath, ThumbnailImagePath,
                                             CreatedDate, ModifiedDate, IsActive)
                        VALUES (@id, @name, @description, @width, @height, @bgColor, @bgImagePath, @thumbnailPath,
                                @createdDate, @modifiedDate, @isActive);";
                }
                else
                {
                    // Fallback for old schema
                    insertTemplate = @"
                        INSERT OR REPLACE INTO Templates (Id, Name, Description, CanvasWidth, CanvasHeight, BackgroundColor)
                        VALUES (@id, @name, @description, @width, @height, @bgColor);";
                }

                using (var command = new SQLiteCommand(insertTemplate, connection))
                {
                    command.Parameters.AddWithValue("@id", template.Id);
                    command.Parameters.AddWithValue("@name", template.Name);
                    command.Parameters.AddWithValue("@description", template.Description ?? "");
                    command.Parameters.AddWithValue("@width", template.CanvasWidth);
                    command.Parameters.AddWithValue("@height", template.CanvasHeight);
                    command.Parameters.AddWithValue("@bgColor", template.BackgroundColor);

                    if (hasPathColumns)
                    {
                        command.Parameters.AddWithValue("@bgImagePath", template.BackgroundImagePath ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@thumbnailPath", template.ThumbnailImagePath ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@createdDate", template.CreatedDate);
                        command.Parameters.AddWithValue("@modifiedDate", template.ModifiedDate);
                        command.Parameters.AddWithValue("@isActive", template.IsActive);
                    }

                    command.ExecuteNonQuery();
                }

                return template.Id;
            }
        }

        public int SaveTemplate(TemplateData template)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                // Check if new columns exist before using them
                string checkColumns = "PRAGMA table_info(Templates)";
                bool hasPathColumns = false;
                using (var checkCommand = new SQLiteCommand(checkColumns, connection))
                using (var reader = checkCommand.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.GetString(1) == "BackgroundImagePath") // Column 1 is 'name' in PRAGMA table_info
                        {
                            hasPathColumns = true;
                            break;
                        }
                    }
                }
                
                string insertTemplate;
                if (hasPathColumns)
                {
                    insertTemplate = @"
                        INSERT INTO Templates (Name, Description, CanvasWidth, CanvasHeight, 
                                             BackgroundColor, BackgroundImagePath, ThumbnailImagePath)
                        VALUES (@name, @description, @width, @height, @bgColor, @bgImagePath, @thumbnailPath);
                        SELECT last_insert_rowid();";
                }
                else
                {
                    // Fallback for old schema
                    insertTemplate = @"
                        INSERT INTO Templates (Name, Description, CanvasWidth, CanvasHeight, BackgroundColor)
                        VALUES (@name, @description, @width, @height, @bgColor);
                        SELECT last_insert_rowid();";
                }
                
                using (var command = new SQLiteCommand(insertTemplate, connection))
                {
                    command.Parameters.AddWithValue("@name", template.Name);
                    command.Parameters.AddWithValue("@description", template.Description ?? "");
                    command.Parameters.AddWithValue("@width", template.CanvasWidth);
                    command.Parameters.AddWithValue("@height", template.CanvasHeight);
                    command.Parameters.AddWithValue("@bgColor", template.BackgroundColor ?? "");
                    
                    if (hasPathColumns)
                    {
                        command.Parameters.AddWithValue("@bgImagePath", template.BackgroundImagePath ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@thumbnailPath", template.ThumbnailImagePath ?? (object)DBNull.Value);
                    }
                    
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }
        
        public int SaveCanvasItem(CanvasItemData item)
        {
            // Convert image path to template asset folder for organized storage
            ConvertImagePathToAssetFolder(item);
            
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string insertItem = @"
                    INSERT INTO CanvasItems (
                        TemplateId, ItemType, Name, X, Y, Width, Height, Rotation, ZIndex,
                        LockedPosition, LockedSize, LockedAspectRatio, IsVisible, IsLocked,
                        ImagePath, ImageHash,
                        Text, FontFamily, FontSize, FontWeight, FontStyle, TextColor, TextAlignment,
                        IsBold, IsItalic, IsUnderlined,
                        HasShadow, ShadowOffsetX, ShadowOffsetY, ShadowBlurRadius, ShadowColor,
                        HasOutline, OutlineThickness, OutlineColor,
                        PlaceholderNumber, PlaceholderColor,
                        ShapeType, FillColor, StrokeColor, StrokeThickness, HasNoFill, HasNoStroke,
                        CustomProperties
                    ) VALUES (
                        @templateId, @itemType, @name, @x, @y, @width, @height, @rotation, @zIndex,
                        @lockedPos, @lockedSize, @lockedAspect, @isVisible, @isLocked,
                        @imagePath, @imageHash,
                        @text, @fontFamily, @fontSize, @fontWeight, @fontStyle, @textColor, @textAlign,
                        @isBold, @isItalic, @isUnderlined,
                        @hasShadow, @shadowX, @shadowY, @shadowBlur, @shadowColor,
                        @hasOutline, @outlineThickness, @outlineColor,
                        @placeholderNum, @placeholderColor,
                        @shapeType, @fillColor, @strokeColor, @strokeThickness, @hasNoFill, @hasNoStroke,
                        @customProps
                    );
                    SELECT last_insert_rowid();";
                
                using (var command = new SQLiteCommand(insertItem, connection))
                {
                    // Basic properties
                    command.Parameters.AddWithValue("@templateId", item.TemplateId);
                    command.Parameters.AddWithValue("@itemType", item.ItemType);
                    command.Parameters.AddWithValue("@name", item.Name);
                    command.Parameters.AddWithValue("@x", item.X);
                    command.Parameters.AddWithValue("@y", item.Y);
                    command.Parameters.AddWithValue("@width", item.Width);
                    command.Parameters.AddWithValue("@height", item.Height);
                    command.Parameters.AddWithValue("@rotation", item.Rotation);
                    command.Parameters.AddWithValue("@zIndex", item.ZIndex);
                    
                    // Lock states
                    command.Parameters.AddWithValue("@lockedPos", item.LockedPosition);
                    command.Parameters.AddWithValue("@lockedSize", item.LockedSize);
                    command.Parameters.AddWithValue("@lockedAspect", item.LockedAspectRatio);
                    command.Parameters.AddWithValue("@isVisible", item.IsVisible);
                    command.Parameters.AddWithValue("@isLocked", item.IsLocked);
                    
                    // Image properties
                    command.Parameters.AddWithValue("@imagePath", item.ImagePath ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@imageHash", item.ImageHash ?? (object)DBNull.Value);
                    
                    // Text properties
                    command.Parameters.AddWithValue("@text", item.Text ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@fontFamily", item.FontFamily ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@fontSize", item.FontSize ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@fontWeight", item.FontWeight ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@fontStyle", item.FontStyle ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@textColor", item.TextColor ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@textAlign", item.TextAlignment ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@isBold", item.IsBold);
                    command.Parameters.AddWithValue("@isItalic", item.IsItalic);
                    command.Parameters.AddWithValue("@isUnderlined", item.IsUnderlined);
                    
                    // Text effects
                    command.Parameters.AddWithValue("@hasShadow", item.HasShadow);
                    command.Parameters.AddWithValue("@shadowX", item.ShadowOffsetX);
                    command.Parameters.AddWithValue("@shadowY", item.ShadowOffsetY);
                    command.Parameters.AddWithValue("@shadowBlur", item.ShadowBlurRadius);
                    command.Parameters.AddWithValue("@shadowColor", item.ShadowColor ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@hasOutline", item.HasOutline);
                    command.Parameters.AddWithValue("@outlineThickness", item.OutlineThickness);
                    command.Parameters.AddWithValue("@outlineColor", item.OutlineColor ?? (object)DBNull.Value);
                    
                    // Placeholder properties
                    command.Parameters.AddWithValue("@placeholderNum", item.PlaceholderNumber ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@placeholderColor", item.PlaceholderColor ?? (object)DBNull.Value);
                    
                    // Shape properties
                    command.Parameters.AddWithValue("@shapeType", item.ShapeType ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@fillColor", item.FillColor ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@strokeColor", item.StrokeColor ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@strokeThickness", item.StrokeThickness);
                    command.Parameters.AddWithValue("@hasNoFill", item.HasNoFill);
                    command.Parameters.AddWithValue("@hasNoStroke", item.HasNoStroke);
                    
                    // Custom properties
                    command.Parameters.AddWithValue("@customProps", item.CustomProperties ?? (object)DBNull.Value);
                    
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }
        
        public void UpdateTemplate(int templateId, TemplateData template)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                // Check if new columns exist before using them
                string checkColumns = "PRAGMA table_info(Templates)";
                bool hasPathColumns = false;
                using (var checkCommand = new SQLiteCommand(checkColumns, connection))
                using (var reader = checkCommand.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.GetString(1) == "BackgroundImagePath") // Column 1 is 'name' in PRAGMA table_info
                        {
                            hasPathColumns = true;
                            break;
                        }
                    }
                }
                
                string updateTemplate;
                if (hasPathColumns)
                {
                    updateTemplate = @"
                        UPDATE Templates 
                        SET Name = @name, Description = @description, CanvasWidth = @width, 
                            CanvasHeight = @height, BackgroundColor = @bgColor, BackgroundImagePath = @bgImagePath,
                            ThumbnailImagePath = @thumbnailPath, ModifiedDate = CURRENT_TIMESTAMP
                        WHERE Id = @id";
                }
                else
                {
                    updateTemplate = @"
                        UPDATE Templates 
                        SET Name = @name, Description = @description, CanvasWidth = @width, 
                            CanvasHeight = @height, BackgroundColor = @bgColor, ModifiedDate = CURRENT_TIMESTAMP
                        WHERE Id = @id";
                }
                
                using (var command = new SQLiteCommand(updateTemplate, connection))
                {
                    command.Parameters.AddWithValue("@id", templateId);
                    command.Parameters.AddWithValue("@name", template.Name);
                    command.Parameters.AddWithValue("@description", template.Description ?? "");
                    command.Parameters.AddWithValue("@width", template.CanvasWidth);
                    command.Parameters.AddWithValue("@height", template.CanvasHeight);
                    command.Parameters.AddWithValue("@bgColor", template.BackgroundColor ?? "");
                    
                    if (hasPathColumns)
                    {
                        command.Parameters.AddWithValue("@bgImagePath", template.BackgroundImagePath ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@thumbnailPath", template.ThumbnailImagePath ?? (object)DBNull.Value);
                    }
                    
                    command.ExecuteNonQuery();
                }
            }
        }
        
        public void DeleteTemplate(int templateId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string deleteTemplate = "UPDATE Templates SET IsActive = 0 WHERE Id = @id";
                
                using (var command = new SQLiteCommand(deleteTemplate, connection))
                {
                    command.Parameters.AddWithValue("@id", templateId);
                    command.ExecuteNonQuery();
                }
            }
            
            // Cleanup associated asset files
            CleanupTemplateAssets(templateId);
        }
        
        public void DeleteCanvasItems(int templateId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string deleteItems = "DELETE FROM CanvasItems WHERE TemplateId = @templateId";
                
                using (var command = new SQLiteCommand(deleteItems, connection))
                {
                    command.Parameters.AddWithValue("@templateId", templateId);
                    command.ExecuteNonQuery();
                }
            }
        }
        
        public void ClearAllTemplates()
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    
                    // Delete all canvas items first (foreign key constraint)
                    string deleteAllItems = "DELETE FROM CanvasItems";
                    using (var command = new SQLiteCommand(deleteAllItems, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    
                    // Delete all templates
                    string deleteAllTemplates = "DELETE FROM Templates";
                    using (var command = new SQLiteCommand(deleteAllTemplates, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    
                    // Reset the auto-increment counters
                    string resetItemsCounter = "DELETE FROM sqlite_sequence WHERE name='CanvasItems'";
                    using (var command = new SQLiteCommand(resetItemsCounter, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                    
                    string resetTemplatesCounter = "DELETE FROM sqlite_sequence WHERE name='Templates'";
                    using (var command = new SQLiteCommand(resetTemplatesCounter, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
                
                // Clean up any thumbnail files
                CleanupAllThumbnails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to clear template database: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void CleanupAllThumbnails()
        {
            try
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string thumbnailsPath = Path.Combine(appDataPath, "Photobooth", "Thumbnails");
                
                if (Directory.Exists(thumbnailsPath))
                {
                    // Delete all thumbnail files
                    var thumbnailFiles = Directory.GetFiles(thumbnailsPath, "*.png");
                    foreach (var file in thumbnailFiles)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch { /* Ignore individual file deletion errors */ }
                    }
                }
            }
            catch { /* Ignore cleanup errors */ }
        }
        
        public List<TemplateData> GetAllTemplates()
        {
            var templates = new List<TemplateData>();
            
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string selectTemplates = @"
                    SELECT Id, Name, Description, CanvasWidth, CanvasHeight, BackgroundColor, 
                           BackgroundImagePath, ThumbnailImagePath, CreatedDate, ModifiedDate, IsActive
                    FROM Templates 
                    WHERE IsActive = 1 
                    ORDER BY ModifiedDate DESC";
                
                using (var command = new SQLiteCommand(selectTemplates, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        templates.Add(new TemplateData
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Name = reader.GetString(reader.GetOrdinal("Name")),
                            Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                            CanvasWidth = Convert.ToDouble(reader["CanvasWidth"]),
                            CanvasHeight = Convert.ToDouble(reader["CanvasHeight"]),
                            BackgroundColor = reader.IsDBNull(reader.GetOrdinal("BackgroundColor")) ? null : reader.GetString(reader.GetOrdinal("BackgroundColor")),
                            BackgroundImagePath = SafeGetString(reader, "BackgroundImagePath"),
                            ThumbnailImagePath = SafeGetString(reader, "ThumbnailImagePath"),
                            CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                            ModifiedDate = Convert.ToDateTime(reader["ModifiedDate"]),
                            IsActive = Convert.ToBoolean(reader["IsActive"])
                        });
                    }
                }
            }
            
            return templates;
        }
        
        public TemplateData GetTemplate(int templateId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string selectTemplate = @"
                    SELECT Id, Name, Description, CanvasWidth, CanvasHeight, BackgroundColor, 
                           BackgroundImagePath, ThumbnailImagePath, CreatedDate, ModifiedDate, IsActive
                    FROM Templates 
                    WHERE Id = @id AND IsActive = 1";
                
                using (var command = new SQLiteCommand(selectTemplate, connection))
                {
                    command.Parameters.AddWithValue("@id", templateId);
                    
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new TemplateData
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Name = reader.GetString(reader.GetOrdinal("Name")),
                                Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                                CanvasWidth = Convert.ToDouble(reader["CanvasWidth"]),
                                CanvasHeight = Convert.ToDouble(reader["CanvasHeight"]),
                                BackgroundColor = reader.IsDBNull(reader.GetOrdinal("BackgroundColor")) ? null : reader.GetString(reader.GetOrdinal("BackgroundColor")),
                                BackgroundImagePath = SafeGetString(reader, "BackgroundImagePath"),
                                ThumbnailImagePath = SafeGetString(reader, "ThumbnailImagePath"),
                                CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                                ModifiedDate = Convert.ToDateTime(reader["ModifiedDate"]),
                                IsActive = Convert.ToBoolean(reader["IsActive"])
                            };
                        }
                    }
                }
            }
            
            return null;
        }
        
        public List<CanvasItemData> GetCanvasItems(int templateId)
        {
            var items = new List<CanvasItemData>();
            
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string selectItems = @"
                    SELECT * FROM CanvasItems 
                    WHERE TemplateId = @templateId 
                    ORDER BY ZIndex ASC";
                
                using (var command = new SQLiteCommand(selectItems, connection))
                {
                    command.Parameters.AddWithValue("@templateId", templateId);
                    
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            items.Add(MapReaderToCanvasItem(reader));
                        }
                    }
                }
            }
            
            return items;
        }
        
        public void UpdateCanvasItem(int itemId, CanvasItemData item)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string updateItem = @"
                    UPDATE CanvasItems 
                    SET Name = @name, X = @x, Y = @y, Width = @width, Height = @height, 
                        Rotation = @rotation, ZIndex = @zIndex, LockedPosition = @lockedPos, 
                        LockedSize = @lockedSize, LockedAspectRatio = @lockedAspect, 
                        IsVisible = @isVisible, IsLocked = @isLocked,
                        Text = @text, FontFamily = @fontFamily, FontSize = @fontSize,
                        FontWeight = @fontWeight, FontStyle = @fontStyle, TextColor = @textColor,
                        TextAlignment = @textAlign, IsBold = @isBold, IsItalic = @isItalic,
                        IsUnderlined = @isUnderlined, HasShadow = @hasShadow, ShadowOffsetX = @shadowX,
                        ShadowOffsetY = @shadowY, ShadowBlurRadius = @shadowBlur, ShadowColor = @shadowColor,
                        HasOutline = @hasOutline, OutlineThickness = @outlineThickness, OutlineColor = @outlineColor,
                        PlaceholderNumber = @placeholderNum, PlaceholderColor = @placeholderColor,
                        ModifiedDate = CURRENT_TIMESTAMP
                    WHERE Id = @id";
                
                using (var command = new SQLiteCommand(updateItem, connection))
                {
                    command.Parameters.AddWithValue("@id", itemId);
                    command.Parameters.AddWithValue("@name", item.Name);
                    command.Parameters.AddWithValue("@x", item.X);
                    command.Parameters.AddWithValue("@y", item.Y);
                    command.Parameters.AddWithValue("@width", item.Width);
                    command.Parameters.AddWithValue("@height", item.Height);
                    command.Parameters.AddWithValue("@rotation", item.Rotation);
                    command.Parameters.AddWithValue("@zIndex", item.ZIndex);
                    command.Parameters.AddWithValue("@lockedPos", item.LockedPosition);
                    command.Parameters.AddWithValue("@lockedSize", item.LockedSize);
                    command.Parameters.AddWithValue("@lockedAspect", item.LockedAspectRatio);
                    command.Parameters.AddWithValue("@isVisible", item.IsVisible);
                    command.Parameters.AddWithValue("@isLocked", item.IsLocked);
                    command.Parameters.AddWithValue("@text", item.Text ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@fontFamily", item.FontFamily ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@fontSize", item.FontSize ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@fontWeight", item.FontWeight ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@fontStyle", item.FontStyle ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@textColor", item.TextColor ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@textAlign", item.TextAlignment ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@isBold", item.IsBold);
                    command.Parameters.AddWithValue("@isItalic", item.IsItalic);
                    command.Parameters.AddWithValue("@isUnderlined", item.IsUnderlined);
                    command.Parameters.AddWithValue("@hasShadow", item.HasShadow);
                    command.Parameters.AddWithValue("@shadowX", item.ShadowOffsetX);
                    command.Parameters.AddWithValue("@shadowY", item.ShadowOffsetY);
                    command.Parameters.AddWithValue("@shadowBlur", item.ShadowBlurRadius);
                    command.Parameters.AddWithValue("@shadowColor", item.ShadowColor ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@hasOutline", item.HasOutline);
                    command.Parameters.AddWithValue("@outlineThickness", item.OutlineThickness);
                    command.Parameters.AddWithValue("@outlineColor", item.OutlineColor ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@placeholderNum", item.PlaceholderNumber ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@placeholderColor", item.PlaceholderColor ?? (object)DBNull.Value);
                    
                    command.ExecuteNonQuery();
                }
            }
        }
        
        public void DeleteCanvasItem(int itemId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string deleteItem = "DELETE FROM CanvasItems WHERE Id = @id";
                
                using (var command = new SQLiteCommand(deleteItem, connection))
                {
                    command.Parameters.AddWithValue("@id", itemId);
                    command.ExecuteNonQuery();
                }
            }
        }
        
        private CanvasItemData MapReaderToCanvasItem(SQLiteDataReader reader)
        {
            return new CanvasItemData
            {
                Id = Convert.ToInt32(reader["Id"]),
                TemplateId = Convert.ToInt32(reader["TemplateId"]),
                ItemType = reader.GetString(reader.GetOrdinal("ItemType")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                
                // Position and Size
                X = Convert.ToDouble(reader["X"]),
                Y = Convert.ToDouble(reader["Y"]),
                Width = Convert.ToDouble(reader["Width"]),
                Height = Convert.ToDouble(reader["Height"]),
                Rotation = Convert.ToDouble(reader["Rotation"]),
                ZIndex = Convert.ToInt32(reader["ZIndex"]),
                
                // Lock States
                LockedPosition = Convert.ToBoolean(reader["LockedPosition"]),
                LockedSize = Convert.ToBoolean(reader["LockedSize"]),
                LockedAspectRatio = Convert.ToBoolean(reader["LockedAspectRatio"]),
                IsVisible = Convert.ToBoolean(reader["IsVisible"]),
                IsLocked = Convert.ToBoolean(reader["IsLocked"]),
                
                // Image Properties
                ImagePath = reader.IsDBNull(reader.GetOrdinal("ImagePath")) ? null : reader.GetString(reader.GetOrdinal("ImagePath")),
                ImageHash = reader.IsDBNull(reader.GetOrdinal("ImageHash")) ? null : reader.GetString(reader.GetOrdinal("ImageHash")),
                
                // Text Properties
                Text = reader.IsDBNull(reader.GetOrdinal("Text")) ? null : reader.GetString(reader.GetOrdinal("Text")),
                FontFamily = reader.IsDBNull(reader.GetOrdinal("FontFamily")) ? null : reader.GetString(reader.GetOrdinal("FontFamily")),
                FontSize = reader.IsDBNull(reader.GetOrdinal("FontSize")) ? null : (double?)Convert.ToDouble(reader["FontSize"]),
                FontWeight = reader.IsDBNull(reader.GetOrdinal("FontWeight")) ? null : reader.GetString(reader.GetOrdinal("FontWeight")),
                FontStyle = reader.IsDBNull(reader.GetOrdinal("FontStyle")) ? null : reader.GetString(reader.GetOrdinal("FontStyle")),
                TextColor = reader.IsDBNull(reader.GetOrdinal("TextColor")) ? null : reader.GetString(reader.GetOrdinal("TextColor")),
                TextAlignment = reader.IsDBNull(reader.GetOrdinal("TextAlignment")) ? null : reader.GetString(reader.GetOrdinal("TextAlignment")),
                IsBold = Convert.ToBoolean(reader["IsBold"]),
                IsItalic = Convert.ToBoolean(reader["IsItalic"]),
                IsUnderlined = Convert.ToBoolean(reader["IsUnderlined"]),
                
                // Text Effects
                HasShadow = Convert.ToBoolean(reader["HasShadow"]),
                ShadowOffsetX = Convert.ToDouble(reader["ShadowOffsetX"]),
                ShadowOffsetY = Convert.ToDouble(reader["ShadowOffsetY"]),
                ShadowBlurRadius = Convert.ToDouble(reader["ShadowBlurRadius"]),
                ShadowColor = reader.IsDBNull(reader.GetOrdinal("ShadowColor")) ? null : reader.GetString(reader.GetOrdinal("ShadowColor")),
                HasOutline = Convert.ToBoolean(reader["HasOutline"]),
                OutlineThickness = Convert.ToDouble(reader["OutlineThickness"]),
                OutlineColor = reader.IsDBNull(reader.GetOrdinal("OutlineColor")) ? null : reader.GetString(reader.GetOrdinal("OutlineColor")),
                
                // Placeholder Properties
                PlaceholderNumber = reader.IsDBNull(reader.GetOrdinal("PlaceholderNumber")) ? null : (int?)Convert.ToInt32(reader["PlaceholderNumber"]),
                PlaceholderColor = reader.IsDBNull(reader.GetOrdinal("PlaceholderColor")) ? null : reader.GetString(reader.GetOrdinal("PlaceholderColor")),
                
                // Shape Properties
                ShapeType = reader.IsDBNull(reader.GetOrdinal("ShapeType")) ? null : reader.GetString(reader.GetOrdinal("ShapeType")),
                FillColor = reader.IsDBNull(reader.GetOrdinal("FillColor")) ? null : reader.GetString(reader.GetOrdinal("FillColor")),
                StrokeColor = reader.IsDBNull(reader.GetOrdinal("StrokeColor")) ? null : reader.GetString(reader.GetOrdinal("StrokeColor")),
                StrokeThickness = Convert.ToDouble(reader["StrokeThickness"]),
                HasNoFill = TryGetBoolean(reader, "HasNoFill", false),
                HasNoStroke = TryGetBoolean(reader, "HasNoStroke", false),
                
                // Additional Properties
                CustomProperties = reader.IsDBNull(reader.GetOrdinal("CustomProperties")) ? null : reader.GetString(reader.GetOrdinal("CustomProperties")),
                
                CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                ModifiedDate = Convert.ToDateTime(reader["ModifiedDate"])
            };
        }
        
        #region Event Management Methods
        
        public int CreateEvent(EventData eventData)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string insertEvent = @"
                    INSERT INTO Events (Name, Description, EventType, Location, EventDate, 
                                      StartTime, EndTime, HostName, ContactEmail, ContactPhone)
                    VALUES (@name, @description, @eventType, @location, @eventDate, 
                            @startTime, @endTime, @hostName, @email, @phone);
                    SELECT last_insert_rowid();";
                
                using (var command = new SQLiteCommand(insertEvent, connection))
                {
                    command.Parameters.AddWithValue("@name", eventData.Name);
                    command.Parameters.AddWithValue("@description", eventData.Description ?? "");
                    command.Parameters.AddWithValue("@eventType", eventData.EventType ?? "");
                    command.Parameters.AddWithValue("@location", eventData.Location ?? "");
                    command.Parameters.AddWithValue("@eventDate", eventData.EventDate.HasValue ? (object)eventData.EventDate.Value.ToString("yyyy-MM-dd") : DBNull.Value);
                    command.Parameters.AddWithValue("@startTime", eventData.StartTime.HasValue ? (object)eventData.StartTime.Value.ToString(@"hh\:mm") : DBNull.Value);
                    command.Parameters.AddWithValue("@endTime", eventData.EndTime.HasValue ? (object)eventData.EndTime.Value.ToString(@"hh\:mm") : DBNull.Value);
                    command.Parameters.AddWithValue("@hostName", eventData.HostName ?? "");
                    command.Parameters.AddWithValue("@email", eventData.ContactEmail ?? "");
                    command.Parameters.AddWithValue("@phone", eventData.ContactPhone ?? "");
                    
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }
        
        public List<EventData> GetAllEvents()
        {
            var events = new List<EventData>();
            
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string selectEvents = @"
                    SELECT Id, Name, Description, EventType, Location, EventDate, StartTime, EndTime,
                           HostName, ContactEmail, ContactPhone, IsActive, CreatedDate, ModifiedDate
                    FROM Events 
                    WHERE IsActive = 1 
                    ORDER BY EventDate DESC, CreatedDate DESC";
                
                using (var command = new SQLiteCommand(selectEvents, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        events.Add(MapReaderToEventData(reader));
                    }
                }
            }
            
            return events;
        }
        
        public EventData GetEvent(int eventId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string selectEvent = @"
                    SELECT Id, Name, Description, EventType, Location, EventDate, StartTime, EndTime,
                           HostName, ContactEmail, ContactPhone, IsActive, CreatedDate, ModifiedDate
                    FROM Events 
                    WHERE Id = @id AND IsActive = 1";
                
                using (var command = new SQLiteCommand(selectEvent, connection))
                {
                    command.Parameters.AddWithValue("@id", eventId);
                    
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return MapReaderToEventData(reader);
                        }
                    }
                }
            }
            
            return null;
        }
        
        public void UpdateEvent(int eventId, EventData eventData)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string updateQuery = @"
                    UPDATE Events SET 
                        Name = @name,
                        Description = @description,
                        EventType = @eventType,
                        Location = @location,
                        EventDate = @eventDate,
                        StartTime = @startTime,
                        EndTime = @endTime,
                        HostName = @hostName,
                        ContactEmail = @contactEmail,
                        ContactPhone = @contactPhone,
                        ModifiedDate = CURRENT_TIMESTAMP
                    WHERE Id = @id";
                
                using (var command = new SQLiteCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@id", eventId);
                    command.Parameters.AddWithValue("@name", eventData.Name ?? "");
                    command.Parameters.AddWithValue("@description", eventData.Description ?? "");
                    command.Parameters.AddWithValue("@eventType", eventData.EventType ?? "");
                    command.Parameters.AddWithValue("@location", eventData.Location ?? "");
                    command.Parameters.AddWithValue("@eventDate", eventData.EventDate.HasValue ? (object)eventData.EventDate.Value.ToString("yyyy-MM-dd") : DBNull.Value);
                    command.Parameters.AddWithValue("@startTime", eventData.StartTime.HasValue ? (object)eventData.StartTime.Value.ToString(@"hh\:mm") : DBNull.Value);
                    command.Parameters.AddWithValue("@endTime", eventData.EndTime.HasValue ? (object)eventData.EndTime.Value.ToString(@"hh\:mm") : DBNull.Value);
                    command.Parameters.AddWithValue("@hostName", eventData.HostName ?? "");
                    command.Parameters.AddWithValue("@contactEmail", eventData.ContactEmail ?? "");
                    command.Parameters.AddWithValue("@contactPhone", eventData.ContactPhone ?? "");
                    
                    command.ExecuteNonQuery();
                }
            }
        }
        
        public void AssignTemplateToEvent(int eventId, int templateId, bool isDefault = false)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                // Get template name
                string getTemplateName = "SELECT Name FROM Templates WHERE Id = @templateId";
                string templateName = "";
                using (var command = new SQLiteCommand(getTemplateName, connection))
                {
                    command.Parameters.AddWithValue("@templateId", templateId);
                    templateName = command.ExecuteScalar()?.ToString() ?? "Unknown Template";
                }
                
                // If setting as default, clear other defaults first
                if (isDefault)
                {
                    string clearDefaults = "UPDATE EventTemplates SET IsDefaultTemplate = 0 WHERE EventId = @eventId";
                    using (var command = new SQLiteCommand(clearDefaults, connection))
                    {
                        command.Parameters.AddWithValue("@eventId", eventId);
                        command.ExecuteNonQuery();
                    }
                }
                
                // Insert event-template relationship
                string insertEventTemplate = @"
                    INSERT OR REPLACE INTO EventTemplates (EventId, TemplateId, TemplateName, IsDefaultTemplate)
                    VALUES (@eventId, @templateId, @templateName, @isDefault)";
                
                using (var command = new SQLiteCommand(insertEventTemplate, connection))
                {
                    command.Parameters.AddWithValue("@eventId", eventId);
                    command.Parameters.AddWithValue("@templateId", templateId);
                    command.Parameters.AddWithValue("@templateName", templateName);
                    command.Parameters.AddWithValue("@isDefault", isDefault);
                    command.ExecuteNonQuery();
                }
            }
        }
        
        public void RemoveTemplateFromEvent(int eventId, int templateId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string deleteEventTemplate = @"
                    DELETE FROM EventTemplates 
                    WHERE EventId = @eventId AND TemplateId = @templateId";
                
                using (var command = new SQLiteCommand(deleteEventTemplate, connection))
                {
                    command.Parameters.AddWithValue("@eventId", eventId);
                    command.Parameters.AddWithValue("@templateId", templateId);
                    command.ExecuteNonQuery();
                }
            }
        }
        
        public List<TemplateData> GetEventTemplates(int eventId)
        {
            var templates = new List<TemplateData>();
            
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string selectTemplates = @"
                    SELECT t.Id, t.Name, t.Description, t.CanvasWidth, t.CanvasHeight, 
                           t.BackgroundColor, t.BackgroundImagePath, t.ThumbnailImagePath, 
                           t.CreatedDate, t.ModifiedDate, t.IsActive,
                           et.IsDefaultTemplate
                    FROM Templates t
                    INNER JOIN EventTemplates et ON t.Id = et.TemplateId
                    WHERE et.EventId = @eventId AND t.IsActive = 1
                    ORDER BY et.IsDefaultTemplate DESC, et.SortOrder, et.AssignedDate";
                
                using (var command = new SQLiteCommand(selectTemplates, connection))
                {
                    command.Parameters.AddWithValue("@eventId", eventId);
                    
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var template = new TemplateData
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Name = reader.GetString(reader.GetOrdinal("Name")),
                                Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                                CanvasWidth = Convert.ToDouble(reader["CanvasWidth"]),
                                CanvasHeight = Convert.ToDouble(reader["CanvasHeight"]),
                                BackgroundColor = reader.IsDBNull(reader.GetOrdinal("BackgroundColor")) ? null : reader.GetString(reader.GetOrdinal("BackgroundColor")),
                                BackgroundImagePath = SafeGetString(reader, "BackgroundImagePath"),
                                ThumbnailImagePath = SafeGetString(reader, "ThumbnailImagePath"),
                                CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                                ModifiedDate = Convert.ToDateTime(reader["ModifiedDate"]),
                                IsActive = Convert.ToBoolean(reader["IsActive"]),
                                IsDefault = Convert.ToBoolean(reader["IsDefaultTemplate"])
                            };
                            templates.Add(template);
                        }
                    }
                }
            }
            
            return templates;
        }
        
        public int DuplicateTemplate(int templateId, string newName = null)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Get original template
                        var originalTemplate = GetTemplate(templateId);
                        if (originalTemplate == null)
                            throw new Exception("Original template not found");
                        
                        // Create new template with modified name
                        originalTemplate.Name = newName ?? $"{originalTemplate.Name} (Copy)";
                        var newTemplateId = SaveTemplate(originalTemplate);
                        
                        // Copy all canvas items
                        var canvasItems = GetCanvasItems(templateId);
                        foreach (var item in canvasItems)
                        {
                            item.TemplateId = newTemplateId;
                            item.Id = 0; // Reset ID for new insert
                            SaveCanvasItem(item);
                        }
                        
                        transaction.Commit();
                        return newTemplateId;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }
        
        public void DeleteEvent(int eventId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string deleteEvent = "UPDATE Events SET IsActive = 0 WHERE Id = @id";
                
                using (var command = new SQLiteCommand(deleteEvent, connection))
                {
                    command.Parameters.AddWithValue("@id", eventId);
                    command.ExecuteNonQuery();
                }
            }
        }
        
        private EventData MapReaderToEventData(SQLiteDataReader reader)
        {
            return new EventData
            {
                Id = Convert.ToInt32(reader["Id"]),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                EventType = reader.IsDBNull(reader.GetOrdinal("EventType")) ? null : reader.GetString(reader.GetOrdinal("EventType")),
                Location = reader.IsDBNull(reader.GetOrdinal("Location")) ? null : reader.GetString(reader.GetOrdinal("Location")),
                EventDate = reader.IsDBNull(reader.GetOrdinal("EventDate")) ? (DateTime?)null : DateTime.Parse(reader.GetString(reader.GetOrdinal("EventDate"))),
                StartTime = reader.IsDBNull(reader.GetOrdinal("StartTime")) ? (TimeSpan?)null : TimeSpan.Parse(reader.GetString(reader.GetOrdinal("StartTime"))),
                EndTime = reader.IsDBNull(reader.GetOrdinal("EndTime")) ? (TimeSpan?)null : TimeSpan.Parse(reader.GetString(reader.GetOrdinal("EndTime"))),
                HostName = reader.IsDBNull(reader.GetOrdinal("HostName")) ? null : reader.GetString(reader.GetOrdinal("HostName")),
                ContactEmail = reader.IsDBNull(reader.GetOrdinal("ContactEmail")) ? null : reader.GetString(reader.GetOrdinal("ContactEmail")),
                ContactPhone = reader.IsDBNull(reader.GetOrdinal("ContactPhone")) ? null : reader.GetString(reader.GetOrdinal("ContactPhone")),
                IsActive = Convert.ToBoolean(reader["IsActive"]),
                CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                ModifiedDate = Convert.ToDateTime(reader["ModifiedDate"])
            };
        }
        
        private void ConvertImagePathToAssetFolder(CanvasItemData item)
        {
            // Only convert if we have an image path that's not already in our asset folder
            if (!string.IsNullOrEmpty(item.ImagePath) && !item.ImagePath.Contains("TemplateAssets"))
            {
                try
                {
                    string sourcePath = item.ImagePath;
                    
                    // Convert URI paths like "file:///C:/path" to local paths
                    if (sourcePath.StartsWith("file:///"))
                    {
                        sourcePath = new Uri(sourcePath).LocalPath;
                    }
                    
                    // Check if source file exists
                    if (File.Exists(sourcePath))
                    {
                        // Create template asset folder
                        string templateAssetPath = GetTemplateAssetPath(item.TemplateId);
                        if (!Directory.Exists(templateAssetPath))
                        {
                            Directory.CreateDirectory(templateAssetPath);
                        }
                        
                        // Generate unique filename based on hash to avoid duplicates
                        byte[] imageData = File.ReadAllBytes(sourcePath);
                        string hash;
                        using (var sha256 = SHA256.Create())
                        {
                            hash = Convert.ToBase64String(sha256.ComputeHash(imageData))
                                .Replace("/", "_").Replace("+", "-").Replace("=", "");
                        }
                        
                        // Get file extension
                        string extension = Path.GetExtension(sourcePath);
                        string assetFileName = $"{hash}{extension}";
                        string assetFilePath = Path.Combine(templateAssetPath, assetFileName);
                        
                        // Copy file to asset folder if it doesn't already exist
                        if (!File.Exists(assetFilePath))
                        {
                            File.Copy(sourcePath, assetFilePath, true);
                            System.Diagnostics.Debug.WriteLine($"Copied asset {item.Name} to: {assetFilePath}");
                        }
                        
                        // Update item to point to asset folder
                        item.ImagePath = assetFilePath;
                        item.ImageHash = Convert.ToBase64String(SHA256.Create().ComputeHash(imageData));
                        
                        System.Diagnostics.Debug.WriteLine($"Moved image asset for {item.Name} to template folder: {assetFileName}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Warning: Image file not found: {sourcePath}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error moving image to asset folder for {item.Name}: {ex.Message}");
                    // Leave ImagePath as is if conversion fails
                }
            }
        }
        
        private string GetTemplateAssetPath(int templateId)
        {
            // Create assets directory structure: %APPDATA%/Photobooth/TemplateAssets/Template_{ID}/
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string photoboothPath = Path.Combine(appDataPath, "Photobooth");
            string assetsPath = Path.Combine(photoboothPath, "TemplateAssets");
            string templatePath = Path.Combine(assetsPath, $"Template_{templateId}");
            
            return templatePath;
        }
        
        public void CleanupTemplateAssets(int templateId)
        {
            // Remove template asset folder when template is deleted
            try
            {
                string templateAssetPath = GetTemplateAssetPath(templateId);
                if (Directory.Exists(templateAssetPath))
                {
                    Directory.Delete(templateAssetPath, true);
                    System.Diagnostics.Debug.WriteLine($"Cleaned up assets for template {templateId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Warning: Failed to cleanup assets for template {templateId}: {ex.Message}");
            }
        }
        
        public List<string> GetTemplateAssetFiles(int templateId)
        {
            // Get all asset files for a template (useful for export/backup)
            var assetFiles = new List<string>();
            try
            {
                string templateAssetPath = GetTemplateAssetPath(templateId);
                if (Directory.Exists(templateAssetPath))
                {
                    assetFiles.AddRange(Directory.GetFiles(templateAssetPath, "*", SearchOption.AllDirectories));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting asset files for template {templateId}: {ex.Message}");
            }
            
            return assetFiles;
        }
        
        public List<ComposedImageData> GetRecentComposedImages(int limit = 10)
        {
            var images = new List<ComposedImageData>();
            try
            {
                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    string query = @"
                        SELECT Id, SessionId, FilePath, FileName, FileSize, TemplateId, 
                               OutputFormat, CreatedDate, PrintCount, LastPrintDate, 
                               ThumbnailPath, IsActive
                        FROM ComposedImages
                        WHERE IsActive = 1
                        ORDER BY CreatedDate DESC
                        LIMIT @limit";
                    
                    using (var cmd = new SQLiteCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@limit", limit);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var img = new ComposedImageData
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    SessionId = Convert.ToInt32(reader["SessionId"]),
                                    FilePath = reader["FilePath"].ToString(),
                                    FileName = reader["FileName"].ToString(),
                                    FileSize = reader["FileSize"] != DBNull.Value ? Convert.ToInt64(reader["FileSize"]) : (long?)null,
                                    TemplateId = Convert.ToInt32(reader["TemplateId"]),
                                    OutputFormat = reader["OutputFormat"].ToString(),
                                    CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                                    PrintCount = Convert.ToInt32(reader["PrintCount"]),
                                    LastPrintDate = reader["LastPrintDate"] != DBNull.Value ? Convert.ToDateTime(reader["LastPrintDate"]) : (DateTime?)null,
                                    ThumbnailPath = reader["ThumbnailPath"]?.ToString(),
                                    IsActive = Convert.ToBoolean(reader["IsActive"])
                                };
                                images.Add(img);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting recent composed images: {ex.Message}");
            }
            return images;
        }
        
        private string SafeGetString(SQLiteDataReader reader, string columnName)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
            }
            catch (IndexOutOfRangeException)
            {
                // Column doesn't exist - this is expected during migration
                return null;
            }
        }
        
        private static bool TryGetBoolean(SQLiteDataReader reader, string columnName, bool defaultValue)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                if (!reader.IsDBNull(ordinal))
                {
                    return Convert.ToBoolean(reader[columnName]);
                }
            }
            catch (IndexOutOfRangeException)
            {
                // Column doesn't exist yet - return default
            }
            return defaultValue;
        }
        
        #endregion
        
        #region Photo Session Management Methods
        
        public void SavePhotoSession(int eventId, int templateId, string sessionName, string sessionGuid, DateTime startTime)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                // Check if session already exists
                string checkSession = "SELECT COUNT(*) FROM PhotoSessions WHERE SessionGuid = @sessionGuid";
                bool sessionExists = false;
                
                using (var checkCmd = new SQLiteCommand(checkSession, connection))
                {
                    checkCmd.Parameters.AddWithValue("@sessionGuid", sessionGuid);
                    sessionExists = Convert.ToInt32(checkCmd.ExecuteScalar()) > 0;
                }
                
                if (!sessionExists)
                {
                    string insertSession = @"
                        INSERT INTO PhotoSessions (EventId, TemplateId, SessionName, SessionGuid, StartTime, IsActive)
                        VALUES (@eventId, @templateId, @sessionName, @sessionGuid, @startTime, 1)";
                    
                    using (var command = new SQLiteCommand(insertSession, connection))
                    {
                        command.Parameters.AddWithValue("@eventId", eventId);
                        command.Parameters.AddWithValue("@templateId", templateId);
                        command.Parameters.AddWithValue("@sessionName", sessionName);
                        command.Parameters.AddWithValue("@sessionGuid", sessionGuid);
                        command.Parameters.AddWithValue("@startTime", startTime);
                        command.ExecuteNonQuery();
                    }
                }
            }
        }
        
        public void SaveSessionPhoto(string sessionGuid, string fileName, string filePath, long fileSize, string photoType)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                // First get the session ID
                string getSessionId = "SELECT Id FROM PhotoSessions WHERE SessionGuid = @sessionGuid";
                int sessionId = 0;
                
                using (var getCmd = new SQLiteCommand(getSessionId, connection))
                {
                    getCmd.Parameters.AddWithValue("@sessionGuid", sessionGuid);
                    var result = getCmd.ExecuteScalar();
                    if (result != null)
                    {
                        sessionId = Convert.ToInt32(result);
                    }
                }
                
                if (sessionId > 0)
                {
                    // Check if photo already exists
                    string checkPhoto = "SELECT COUNT(*) FROM Photos WHERE SessionId = @sessionId AND FilePath = @filePath";
                    bool photoExists = false;
                    
                    using (var checkCmd = new SQLiteCommand(checkPhoto, connection))
                    {
                        checkCmd.Parameters.AddWithValue("@sessionId", sessionId);
                        checkCmd.Parameters.AddWithValue("@filePath", filePath);
                        photoExists = Convert.ToInt32(checkCmd.ExecuteScalar()) > 0;
                    }
                    
                    if (!photoExists)
                    {
                        string insertPhoto = @"
                            INSERT INTO Photos (SessionId, FileName, FilePath, FileSize, PhotoType, IsActive, SequenceNumber)
                            VALUES (@sessionId, @fileName, @filePath, @fileSize, @photoType, 1, 
                                    (SELECT COALESCE(MAX(SequenceNumber), 0) + 1 FROM Photos WHERE SessionId = @sessionId))";
                        
                        using (var command = new SQLiteCommand(insertPhoto, connection))
                        {
                            command.Parameters.AddWithValue("@sessionId", sessionId);
                            command.Parameters.AddWithValue("@fileName", fileName);
                            command.Parameters.AddWithValue("@filePath", filePath);
                            command.Parameters.AddWithValue("@fileSize", fileSize);
                            command.Parameters.AddWithValue("@photoType", photoType);
                            command.ExecuteNonQuery();
                        }
                    }
                }
            }
        }
        
        public PhotoSessionData GetPhotoSessionByGuid(string sessionGuid)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string selectSession = @"
                    SELECT ps.*, e.Name as EventName, t.Name as TemplateName
                    FROM PhotoSessions ps
                    LEFT JOIN Events e ON ps.EventId = e.Id
                    LEFT JOIN Templates t ON ps.TemplateId = t.Id
                    WHERE ps.SessionGuid = @sessionGuid AND ps.IsActive = 1";
                    
                using (var command = new SQLiteCommand(selectSession, connection))
                {
                    command.Parameters.AddWithValue("@sessionGuid", sessionGuid);
                    
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return MapReaderToPhotoSessionData(reader);
                        }
                    }
                }
            }
            return null;
        }
        
        public int CreatePhotoSession(int eventId, int templateId, string sessionName = null, string sessionGuid = null)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                // Use provided GUID or generate new one if not provided
                sessionGuid = sessionGuid ?? Guid.NewGuid().ToString();
                sessionName = sessionName ?? $"Session {DateTime.Now:yyyy-MM-dd HH:mm}";
                
                System.Diagnostics.Debug.WriteLine($"TemplateDatabase.CreatePhotoSession: Creating session with GUID {sessionGuid}");
                
                string insertSession = @"
                    INSERT INTO PhotoSessions (EventId, TemplateId, SessionName, SessionGuid, StartTime)
                    VALUES (@eventId, @templateId, @sessionName, @sessionGuid, CURRENT_TIMESTAMP);
                    SELECT last_insert_rowid();";
                
                using (var command = new SQLiteCommand(insertSession, connection))
                {
                    command.Parameters.AddWithValue("@eventId", eventId);
                    command.Parameters.AddWithValue("@templateId", templateId);
                    command.Parameters.AddWithValue("@sessionName", sessionName);
                    command.Parameters.AddWithValue("@sessionGuid", sessionGuid);
                    
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }
        
        public void UpdateSessionWithVideoData(int sessionId, string videoPath, string thumbnailPath, long fileSize, int durationSeconds)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string updateSession = @"
                    UPDATE PhotoSessions 
                    SET IsVideoSession = 1,
                        VideoPath = @videoPath,
                        VideoThumbnailPath = @thumbnailPath,
                        VideoFileSize = @fileSize,
                        VideoDurationSeconds = @duration
                    WHERE Id = @sessionId";
                
                using (var command = new SQLiteCommand(updateSession, connection))
                {
                    command.Parameters.AddWithValue("@sessionId", sessionId);
                    command.Parameters.AddWithValue("@videoPath", videoPath ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@thumbnailPath", thumbnailPath ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@fileSize", fileSize);
                    command.Parameters.AddWithValue("@duration", durationSeconds);
                    command.ExecuteNonQuery();
                    
                    System.Diagnostics.Debug.WriteLine($"TemplateDatabase: Updated session {sessionId} with video data");
                }
            }
        }
        
        public void UpdateSessionVideoCloudUrl(int sessionId, string videoCloudUrl)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string updateSession = @"
                    UPDATE PhotoSessions 
                    SET VideoCloudUrl = @videoCloudUrl
                    WHERE Id = @sessionId";
                
                using (var command = new SQLiteCommand(updateSession, connection))
                {
                    command.Parameters.AddWithValue("@sessionId", sessionId);
                    command.Parameters.AddWithValue("@videoCloudUrl", videoCloudUrl ?? (object)DBNull.Value);
                    command.ExecuteNonQuery();
                    
                    System.Diagnostics.Debug.WriteLine($"TemplateDatabase: Updated session {sessionId} with video cloud URL: {videoCloudUrl}");
                }
            }
        }
        
        public void UpdateSessionVideoCloudUrlByGuid(string sessionGuid, string videoCloudUrl)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string updateSession = @"
                    UPDATE PhotoSessions 
                    SET VideoCloudUrl = @videoCloudUrl
                    WHERE SessionGuid = @sessionGuid";
                
                using (var command = new SQLiteCommand(updateSession, connection))
                {
                    command.Parameters.AddWithValue("@sessionGuid", sessionGuid);
                    command.Parameters.AddWithValue("@videoCloudUrl", videoCloudUrl ?? (object)DBNull.Value);
                    int rowsAffected = command.ExecuteNonQuery();
                    
                    System.Diagnostics.Debug.WriteLine($"TemplateDatabase: Updated session {sessionGuid} with video cloud URL: {videoCloudUrl} ({rowsAffected} rows)");
                }
            }
        }
        
        public void EndPhotoSession(int sessionId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string updateSession = @"
                    UPDATE PhotoSessions 
                    SET EndTime = CURRENT_TIMESTAMP,
                        PhotosTaken = (SELECT COUNT(*) FROM Photos WHERE SessionId = @sessionId AND IsActive = 1)
                    WHERE Id = @sessionId";
                
                using (var command = new SQLiteCommand(updateSession, connection))
                {
                    command.Parameters.AddWithValue("@sessionId", sessionId);
                    command.ExecuteNonQuery();
                }
            }
        }
        
        public void UpdatePhotoSessionGalleryUrl(string sessionGuid, string galleryUrl)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                // First, check if the session exists
                string checkSession = "SELECT COUNT(*) FROM PhotoSessions WHERE SessionGuid = @sessionGuid";
                bool sessionExists = false;
                
                using (var checkCmd = new SQLiteCommand(checkSession, connection))
                {
                    checkCmd.Parameters.AddWithValue("@sessionGuid", sessionGuid);
                    sessionExists = Convert.ToInt32(checkCmd.ExecuteScalar()) > 0;
                }
                
                if (!sessionExists)
                {
                    // Session doesn't exist, create it with minimal required fields
                    string insertSession = @"
                        INSERT INTO PhotoSessions (EventId, TemplateId, SessionName, SessionGuid, GalleryUrl, StartTime)
                        VALUES (
                            (SELECT Id FROM Events WHERE IsActive = 1 LIMIT 1),  -- Use active event
                            (SELECT Id FROM Templates LIMIT 1),                  -- Use any template
                            @sessionName,
                            @sessionGuid,
                            @galleryUrl,
                            CURRENT_TIMESTAMP
                        )";
                    
                    using (var insertCmd = new SQLiteCommand(insertSession, connection))
                    {
                        insertCmd.Parameters.AddWithValue("@sessionName", $"Session {DateTime.Now:yyyy-MM-dd HH:mm}");
                        insertCmd.Parameters.AddWithValue("@sessionGuid", sessionGuid);
                        insertCmd.Parameters.AddWithValue("@galleryUrl", galleryUrl);
                        
                        insertCmd.ExecuteNonQuery();
                        System.Diagnostics.Debug.WriteLine($"Created new session {sessionGuid} with GalleryUrl: {galleryUrl}");
                    }
                }
                else
                {
                    // Session exists, update it
                    string updateGalleryUrl = @"
                        UPDATE PhotoSessions 
                        SET GalleryUrl = @galleryUrl 
                        WHERE SessionGuid = @sessionGuid";
                        
                    using (var command = new SQLiteCommand(updateGalleryUrl, connection))
                    {
                        command.Parameters.AddWithValue("@galleryUrl", galleryUrl);
                        command.Parameters.AddWithValue("@sessionGuid", sessionGuid);
                        
                        int rowsAffected = command.ExecuteNonQuery();
                        System.Diagnostics.Debug.WriteLine($"Updated GalleryUrl for session {sessionGuid}: {rowsAffected} rows affected");
                    }
                }
            }
        }
        
        public string GetPhotoSessionGalleryUrl(string sessionGuid)
        {
            // System.Diagnostics.Debug.WriteLine($"TemplateDatabase.GetPhotoSessionGalleryUrl: Looking up gallery URL for sessionGuid: {sessionGuid}");
            
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string selectGalleryUrl = @"
                    SELECT GalleryUrl 
                    FROM PhotoSessions 
                    WHERE SessionGuid = @sessionGuid";
                    
                using (var command = new SQLiteCommand(selectGalleryUrl, connection))
                {
                    command.Parameters.AddWithValue("@sessionGuid", sessionGuid);
                    
                    var result = command.ExecuteScalar();
                    string galleryUrl = result?.ToString();
                    
                    // System.Diagnostics.Debug.WriteLine($"TemplateDatabase.GetPhotoSessionGalleryUrl: Result for {sessionGuid}: {galleryUrl ?? "NULL"}");
                    
                    // Also check if the session exists at all
                    if (string.IsNullOrEmpty(galleryUrl))
                    {
                        string checkSession = "SELECT COUNT(*) FROM PhotoSessions WHERE SessionGuid = @sessionGuid";
                        using (var checkCmd = new SQLiteCommand(checkSession, connection))
                        {
                            checkCmd.Parameters.AddWithValue("@sessionGuid", sessionGuid);
                            int count = Convert.ToInt32(checkCmd.ExecuteScalar());
                            // System.Diagnostics.Debug.WriteLine($"TemplateDatabase.GetPhotoSessionGalleryUrl: Session exists in DB: {count > 0} (count: {count})");
                        }
                    }
                    
                    return galleryUrl;
                }
            }
        }
        
        public void LogSMSSend(string sessionGuid, string phoneNumber, string galleryUrl, bool success, string errorMessage = null)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string insertSMSLog = @"
                    INSERT INTO SMSLog (SessionGuid, PhoneNumber, GalleryUrl, Success, ErrorMessage)
                    VALUES (@sessionGuid, @phoneNumber, @galleryUrl, @success, @errorMessage)";
                    
                using (var command = new SQLiteCommand(insertSMSLog, connection))
                {
                    command.Parameters.AddWithValue("@sessionGuid", sessionGuid);
                    command.Parameters.AddWithValue("@phoneNumber", phoneNumber);
                    command.Parameters.AddWithValue("@galleryUrl", galleryUrl);
                    command.Parameters.AddWithValue("@success", success);
                    command.Parameters.AddWithValue("@errorMessage", errorMessage);
                    
                    command.ExecuteNonQuery();
                    System.Diagnostics.Debug.WriteLine($"Logged SMS send to {phoneNumber} for session {sessionGuid}: {(success ? "Success" : "Failed")}");
                }
            }
        }
        
        public List<(string PhoneNumber, DateTime SentDate, bool Success)> GetSMSLogForSession(string sessionGuid)
        {
            var smsLog = new List<(string PhoneNumber, DateTime SentDate, bool Success)>();
            
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string selectSMSLog = @"
                    SELECT PhoneNumber, SentDate, Success 
                    FROM SMSLog 
                    WHERE SessionGuid = @sessionGuid 
                    ORDER BY SentDate DESC";
                    
                using (var command = new SQLiteCommand(selectSMSLog, connection))
                {
                    command.Parameters.AddWithValue("@sessionGuid", sessionGuid);
                    
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            smsLog.Add((
                                reader["PhoneNumber"].ToString(),
                                DateTime.Parse(reader["SentDate"].ToString()),
                                Convert.ToBoolean(reader["Success"])
                            ));
                        }
                    }
                }
            }
            
            return smsLog;
        }
        
        public void DeletePhotoSession(int sessionId)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    
                    // Delete the session (cascade will handle related records)
                    string deleteSession = "DELETE FROM PhotoSessions WHERE Id = @sessionId";
                    
                    using (var command = new SQLiteCommand(deleteSession, connection))
                    {
                        command.Parameters.AddWithValue("@sessionId", sessionId);
                        int rowsAffected = command.ExecuteNonQuery();
                        
                        if (rowsAffected > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"DeletePhotoSession: Deleted empty session {sessionId}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DeletePhotoSession: Failed to delete session {sessionId}: {ex.Message}");
            }
        }
        
        public int SavePhoto(PhotoData photo)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string insertPhoto = @"
                    INSERT INTO Photos (SessionId, FilePath, FileName, FileSize, PhotoType, 
                                      SequenceNumber, ThumbnailPath, CameraSettings)
                    VALUES (@sessionId, @filePath, @fileName, @fileSize, @photoType, 
                            @sequenceNumber, @thumbnailPath, @cameraSettings);
                    SELECT last_insert_rowid();";
                
                using (var command = new SQLiteCommand(insertPhoto, connection))
                {
                    command.Parameters.AddWithValue("@sessionId", photo.SessionId);
                    command.Parameters.AddWithValue("@filePath", photo.FilePath);
                    command.Parameters.AddWithValue("@fileName", photo.FileName);
                    command.Parameters.AddWithValue("@fileSize", photo.FileSize ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@photoType", photo.PhotoType ?? "Original");
                    command.Parameters.AddWithValue("@sequenceNumber", photo.SequenceNumber);
                    command.Parameters.AddWithValue("@thumbnailPath", photo.ThumbnailPath ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@cameraSettings", photo.CameraSettings ?? (object)DBNull.Value);
                    
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }
        
        public int SaveComposedImage(ComposedImageData composedImage)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string insertComposed = @"
                    INSERT INTO ComposedImages (SessionId, FilePath, FileName, FileSize, TemplateId, 
                                              OutputFormat, ThumbnailPath)
                    VALUES (@sessionId, @filePath, @fileName, @fileSize, @templateId, 
                            @outputFormat, @thumbnailPath);
                    SELECT last_insert_rowid();";
                
                using (var command = new SQLiteCommand(insertComposed, connection))
                {
                    command.Parameters.AddWithValue("@sessionId", composedImage.SessionId);
                    command.Parameters.AddWithValue("@filePath", composedImage.FilePath);
                    command.Parameters.AddWithValue("@fileName", composedImage.FileName);
                    command.Parameters.AddWithValue("@fileSize", composedImage.FileSize ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@templateId", composedImage.TemplateId);
                    command.Parameters.AddWithValue("@outputFormat", composedImage.OutputFormat ?? "4x6");
                    command.Parameters.AddWithValue("@thumbnailPath", composedImage.ThumbnailPath ?? (object)DBNull.Value);
                    
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }
        
        public void LinkPhotosToComposedImage(int composedImageId, List<int> photoIds)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                for (int i = 0; i < photoIds.Count; i++)
                {
                    string insertLink = @"
                        INSERT OR REPLACE INTO ComposedImagePhotos (ComposedImageId, PhotoId, PlaceholderIndex)
                        VALUES (@composedImageId, @photoId, @placeholderIndex)";
                    
                    using (var command = new SQLiteCommand(insertLink, connection))
                    {
                        command.Parameters.AddWithValue("@composedImageId", composedImageId);
                        command.Parameters.AddWithValue("@photoId", photoIds[i]);
                        command.Parameters.AddWithValue("@placeholderIndex", i);
                        command.ExecuteNonQuery();
                    }
                }
            }
        }
        
        public List<PhotoSessionData> GetPhotoSessions(int? eventId = null, int limit = 100, int offset = 0)
        {
            System.Diagnostics.Debug.WriteLine($"★★★ GetPhotoSessions called with eventId={eventId}, limit={limit}, offset={offset}");
            
            var sessions = new List<PhotoSessionData>();
            
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                // Optimized query with limit and offset for pagination
                string selectSessions = @"
                    SELECT ps.*, e.Name as EventName, t.Name as TemplateName,
                           (SELECT COUNT(*) FROM Photos p WHERE p.SessionId = ps.Id AND p.IsActive = 1) as ActualPhotoCount,
                           (SELECT COUNT(*) FROM ComposedImages ci WHERE ci.SessionId = ps.Id AND ci.IsActive = 1) as ComposedImageCount
                    FROM PhotoSessions ps
                    LEFT JOIN Events e ON ps.EventId = e.Id
                    LEFT JOIN Templates t ON ps.TemplateId = t.Id
                    WHERE ps.IsActive = 1" + (eventId.HasValue ? " AND ps.EventId = @eventId" : "") + @"
                    ORDER BY ps.StartTime DESC
                    LIMIT @limit OFFSET @offset";
                    
                System.Diagnostics.Debug.WriteLine($"★★★ SQL Query will filter by eventId: {eventId.HasValue}, eventId value: {eventId}");
                
                using (var command = new SQLiteCommand(selectSessions, connection))
                {
                    if (eventId.HasValue)
                        command.Parameters.AddWithValue("@eventId", eventId.Value);
                    command.Parameters.AddWithValue("@limit", limit);
                    command.Parameters.AddWithValue("@offset", offset);
                    
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var session = MapReaderToPhotoSessionData(reader);
                            sessions.Add(session);
                            System.Diagnostics.Debug.WriteLine($"★★★ Found session: Id={session.Id}, EventId={session.EventId}, Name={session.SessionName}");
                        }
                    }
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"★★★ GetPhotoSessions returning {sessions.Count} sessions for eventId={eventId}");
            return sessions;
        }
        
        public List<PhotoData> GetSessionPhotos(int sessionId)
        {
            var photos = new List<PhotoData>();
            
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string selectPhotos = @"
                    SELECT * FROM Photos 
                    WHERE SessionId = @sessionId AND IsActive = 1
                    ORDER BY SequenceNumber";
                
                using (var command = new SQLiteCommand(selectPhotos, connection))
                {
                    command.Parameters.AddWithValue("@sessionId", sessionId);
                    
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            photos.Add(MapReaderToPhotoData(reader));
                        }
                    }
                }
            }
            
            return photos;
        }
        
        public List<ComposedImageData> GetSessionComposedImages(int sessionId)
        {
            var composedImages = new List<ComposedImageData>();
            
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string selectComposed = @"
                    SELECT ci.*, t.Name as TemplateName
                    FROM ComposedImages ci
                    LEFT JOIN Templates t ON ci.TemplateId = t.Id
                    WHERE ci.SessionId = @sessionId AND ci.IsActive = 1
                    ORDER BY ci.CreatedDate";
                
                using (var command = new SQLiteCommand(selectComposed, connection))
                {
                    command.Parameters.AddWithValue("@sessionId", sessionId);
                    
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            composedImages.Add(MapReaderToComposedImageData(reader));
                        }
                    }
                }
            }
            
            return composedImages;
        }
        
        public void UpdateComposedImagePrintCount(int composedImageId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                
                string updatePrintCount = @"
                    UPDATE ComposedImages 
                    SET PrintCount = PrintCount + 1, LastPrintDate = CURRENT_TIMESTAMP
                    WHERE Id = @composedImageId";
                
                using (var command = new SQLiteCommand(updatePrintCount, connection))
                {
                    command.Parameters.AddWithValue("@composedImageId", composedImageId);
                    command.ExecuteNonQuery();
                }
            }
        }
        
        private PhotoSessionData MapReaderToPhotoSessionData(SQLiteDataReader reader)
        {
            return new PhotoSessionData
            {
                Id = Convert.ToInt32(reader["Id"]),
                EventId = Convert.ToInt32(reader["EventId"]),
                TemplateId = Convert.ToInt32(reader["TemplateId"]),
                SessionName = reader.IsDBNull(reader.GetOrdinal("SessionName")) ? null : reader.GetString(reader.GetOrdinal("SessionName")),
                SessionGuid = reader.IsDBNull(reader.GetOrdinal("SessionGuid")) ? null : reader.GetString(reader.GetOrdinal("SessionGuid")),
                PhotosTaken = reader.IsDBNull(reader.GetOrdinal("PhotosTaken")) ? 0 : Convert.ToInt32(reader["PhotosTaken"]),
                ActualPhotoCount = reader.IsDBNull(reader.GetOrdinal("ActualPhotoCount")) ? 0 : Convert.ToInt32(reader["ActualPhotoCount"]),
                ComposedImageCount = reader.IsDBNull(reader.GetOrdinal("ComposedImageCount")) ? 0 : Convert.ToInt32(reader["ComposedImageCount"]),
                StartTime = Convert.ToDateTime(reader["StartTime"]),
                EndTime = reader.IsDBNull(reader.GetOrdinal("EndTime")) ? (DateTime?)null : Convert.ToDateTime(reader["EndTime"]),
                EventName = reader.IsDBNull(reader.GetOrdinal("EventName")) ? null : reader.GetString(reader.GetOrdinal("EventName")),
                TemplateName = reader.IsDBNull(reader.GetOrdinal("TemplateName")) ? null : reader.GetString(reader.GetOrdinal("TemplateName")),
                IsActive = Convert.ToBoolean(reader["IsActive"]),
                // Video-related fields
                IsVideoSession = reader.IsDBNull(reader.GetOrdinal("IsVideoSession")) ? false : Convert.ToBoolean(reader["IsVideoSession"]),
                VideoPath = reader.IsDBNull(reader.GetOrdinal("VideoPath")) ? null : reader.GetString(reader.GetOrdinal("VideoPath")),
                VideoThumbnailPath = reader.IsDBNull(reader.GetOrdinal("VideoThumbnailPath")) ? null : reader.GetString(reader.GetOrdinal("VideoThumbnailPath")),
                VideoFileSize = reader.IsDBNull(reader.GetOrdinal("VideoFileSize")) ? 0 : Convert.ToInt64(reader["VideoFileSize"]),
                VideoDurationSeconds = reader.IsDBNull(reader.GetOrdinal("VideoDurationSeconds")) ? 0 : Convert.ToInt32(reader["VideoDurationSeconds"])
            };
        }
        
        private PhotoData MapReaderToPhotoData(SQLiteDataReader reader)
        {
            return new PhotoData
            {
                Id = Convert.ToInt32(reader["Id"]),
                SessionId = Convert.ToInt32(reader["SessionId"]),
                FilePath = reader.GetString(reader.GetOrdinal("FilePath")),
                FileName = reader.GetString(reader.GetOrdinal("FileName")),
                FileSize = reader.IsDBNull(reader.GetOrdinal("FileSize")) ? (long?)null : Convert.ToInt64(reader["FileSize"]),
                PhotoType = reader.IsDBNull(reader.GetOrdinal("PhotoType")) ? "Original" : reader.GetString(reader.GetOrdinal("PhotoType")),
                SequenceNumber = Convert.ToInt32(reader["SequenceNumber"]),
                CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                ThumbnailPath = reader.IsDBNull(reader.GetOrdinal("ThumbnailPath")) ? null : reader.GetString(reader.GetOrdinal("ThumbnailPath")),
                CameraSettings = reader.IsDBNull(reader.GetOrdinal("CameraSettings")) ? null : reader.GetString(reader.GetOrdinal("CameraSettings")),
                IsActive = Convert.ToBoolean(reader["IsActive"])
            };
        }
        
        private ComposedImageData MapReaderToComposedImageData(SQLiteDataReader reader)
        {
            return new ComposedImageData
            {
                Id = Convert.ToInt32(reader["Id"]),
                SessionId = Convert.ToInt32(reader["SessionId"]),
                FilePath = reader.GetString(reader.GetOrdinal("FilePath")),
                FileName = reader.GetString(reader.GetOrdinal("FileName")),
                FileSize = reader.IsDBNull(reader.GetOrdinal("FileSize")) ? (long?)null : Convert.ToInt64(reader["FileSize"]),
                TemplateId = Convert.ToInt32(reader["TemplateId"]),
                TemplateName = reader.IsDBNull(reader.GetOrdinal("TemplateName")) ? null : reader.GetString(reader.GetOrdinal("TemplateName")),
                OutputFormat = reader.IsDBNull(reader.GetOrdinal("OutputFormat")) ? "4x6" : reader.GetString(reader.GetOrdinal("OutputFormat")),
                CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                PrintCount = Convert.ToInt32(reader["PrintCount"]),
                LastPrintDate = reader.IsDBNull(reader.GetOrdinal("LastPrintDate")) ? (DateTime?)null : Convert.ToDateTime(reader["LastPrintDate"]),
                ThumbnailPath = reader.IsDBNull(reader.GetOrdinal("ThumbnailPath")) ? null : reader.GetString(reader.GetOrdinal("ThumbnailPath")),
                IsActive = Convert.ToBoolean(reader["IsActive"])
            };
        }
        
        #region Session Cleanup
        
        public void CleanupOldSessions(int ageInHours = 24)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    
                    var cutoffTime = DateTime.Now.AddHours(-ageInHours);
                    
                    // Get sessions older than cutoff time
                    string selectOldSessions = @"
                        SELECT Id, SessionGuid FROM PhotoSessions 
                        WHERE StartTime < @cutoffTime";
                    
                    var oldSessionIds = new List<int>();
                    var oldSessionGuids = new List<string>();
                    
                    using (var command = new SQLiteCommand(selectOldSessions, connection))
                    {
                        command.Parameters.AddWithValue("@cutoffTime", cutoffTime);
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                oldSessionIds.Add(reader.GetInt32(0));
                                var guid = reader.IsDBNull(1) ? null : reader.GetString(1);
                                if (!string.IsNullOrEmpty(guid))
                                {
                                    oldSessionGuids.Add(guid);
                                }
                            }
                        }
                    }
                    
                    if (oldSessionIds.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"CleanupOldSessions: Found {oldSessionIds.Count} sessions older than {ageInHours} hours");
                        
                        // Delete from database (cascade will handle related records)
                        string deleteOldSessions = @"
                            DELETE FROM PhotoSessions 
                            WHERE StartTime < @cutoffTime";
                        
                        using (var command = new SQLiteCommand(deleteOldSessions, connection))
                        {
                            command.Parameters.AddWithValue("@cutoffTime", cutoffTime);
                            int deletedCount = command.ExecuteNonQuery();
                            System.Diagnostics.Debug.WriteLine($"CleanupOldSessions: Deleted {deletedCount} old sessions from database");
                        }
                        
                        // Clean up associated files
                        CleanupOldSessionFiles(oldSessionGuids);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"CleanupOldSessions: No sessions older than {ageInHours} hours found");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CleanupOldSessions: Error during cleanup: {ex.Message}");
                // Don't throw - cleanup is not critical to app functionality
            }
        }
        
        private void CleanupOldSessionFiles(List<string> sessionGuids)
        {
            try
            {
                foreach (var sessionGuid in sessionGuids)
                {
                    if (string.IsNullOrEmpty(sessionGuid)) continue;
                    
                    // Look for session directories in common photo locations
                    var photoLocations = new[]
                    {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Photobooth", "Sessions", sessionGuid),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Photobooth", sessionGuid),
                        Path.Combine("Photos", "Sessions", sessionGuid),
                        Path.Combine("Photos", sessionGuid)
                    };
                    
                    foreach (var location in photoLocations)
                    {
                        if (Directory.Exists(location))
                        {
                            try
                            {
                                Directory.Delete(location, true);
                                System.Diagnostics.Debug.WriteLine($"CleanupOldSessionFiles: Deleted directory {location}");
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"CleanupOldSessionFiles: Failed to delete {location}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CleanupOldSessionFiles: Error during file cleanup: {ex.Message}");
            }
        }
        
        public void RunPeriodicCleanup()
        {
            try
            {
                // Run cleanup on a background thread
                Task.Run(() =>
                {
                    CleanupOldSessions(24); // Clean sessions older than 24 hours
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RunPeriodicCleanup: Error starting cleanup task: {ex.Message}");
            }
        }
        
        #endregion
        
        #endregion
    }
    
    // Data classes
    public class TemplateData
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public double CanvasWidth { get; set; }
        public double CanvasHeight { get; set; }
        public string BackgroundColor { get; set; }
        public string BackgroundImagePath { get; set; }
        public string ThumbnailImagePath { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public bool IsActive { get; set; }
        public bool IsDefault { get; set; } // For event templates

        // Asset mappings for import/export (not stored in database)
        public Dictionary<string, string> AssetMappings { get; set; }
        
        // Canvas items for import/export (not stored in database - items are stored separately)
        public List<CanvasItemData> CanvasItems { get; set; }
    }
    
    public class CanvasItemData
    {
        public int Id { get; set; }
        public int TemplateId { get; set; }
        public string ItemType { get; set; } // 'Image', 'Text', 'Placeholder', 'Shape'
        public string Name { get; set; }
        
        // Position and Size
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Rotation { get; set; }
        public int ZIndex { get; set; }
        
        // Lock States
        public bool LockedPosition { get; set; }
        public bool LockedSize { get; set; }
        public bool LockedAspectRatio { get; set; }
        public bool IsVisible { get; set; } = true;
        public bool IsLocked { get; set; }
        
        // Image Properties
        public string ImagePath { get; set; }
        public string ImageHash { get; set; }
        
        // Text Properties
        public string Text { get; set; }
        public string FontFamily { get; set; }
        public double? FontSize { get; set; }
        public string FontWeight { get; set; }
        public string FontStyle { get; set; }
        public string TextColor { get; set; }
        public string TextAlignment { get; set; }
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public bool IsUnderlined { get; set; }
        
        // Text Effects
        public bool HasShadow { get; set; }
        public double ShadowOffsetX { get; set; }
        public double ShadowOffsetY { get; set; }
        public double ShadowBlurRadius { get; set; }
        public string ShadowColor { get; set; }
        public bool HasOutline { get; set; }
        public double OutlineThickness { get; set; }
        public string OutlineColor { get; set; }
        
        // Placeholder Properties
        public int? PlaceholderNumber { get; set; }
        public string PlaceholderColor { get; set; }
        
        // Shape Properties
        public string ShapeType { get; set; }
        public string FillColor { get; set; }
        public string StrokeColor { get; set; }
        public double StrokeThickness { get; set; }
        public bool HasNoFill { get; set; }
        public bool HasNoStroke { get; set; }
        
        // Additional Properties
        public string CustomProperties { get; set; } // JSON for extensibility
        
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
    }
    
    public class EventData
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string EventType { get; set; } // 'Wedding', 'Birthday', 'Corporate', 'Holiday', etc.
        public string Location { get; set; }
        public DateTime? EventDate { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
        public string HostName { get; set; }
        public string ContactEmail { get; set; }
        public string ContactPhone { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public string GalleryUrl { get; set; }
        public string GalleryPassword { get; set; }
    }
    
    // New data classes for photo session management
    public class PhotoSessionData
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public int TemplateId { get; set; }
        public string SessionName { get; set; }
        public string SessionGuid { get; set; }
        public int PhotosTaken { get; set; }
        public int ActualPhotoCount { get; set; }
        public int ComposedImageCount { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string EventName { get; set; }
        public string TemplateName { get; set; }
        public bool IsActive { get; set; }
        
        // Video session fields
        public bool IsVideoSession { get; set; }
        public string VideoPath { get; set; }
        public string VideoThumbnailPath { get; set; }
        public long VideoFileSize { get; set; }
        public int VideoDurationSeconds { get; set; }
        public string VideoCloudUrl { get; set; }
    }
    
    public class PhotoData
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public long? FileSize { get; set; }
        public string PhotoType { get; set; } // 'Original', 'Filtered', 'Preview'
        public int SequenceNumber { get; set; }
        public DateTime CreatedDate { get; set; }
        public string ThumbnailPath { get; set; }
        public string CameraSettings { get; set; } // JSON for camera metadata
        public bool IsActive { get; set; }
    }
    
    public class ComposedImageData
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public long? FileSize { get; set; }
        public int TemplateId { get; set; }
        public string TemplateName { get; set; }
        public string OutputFormat { get; set; } // '4x6', '2x6', 'Custom'
        public DateTime CreatedDate { get; set; }
        public int PrintCount { get; set; }
        public DateTime? LastPrintDate { get; set; }
        public string ThumbnailPath { get; set; }
        public bool IsActive { get; set; }
    }
}