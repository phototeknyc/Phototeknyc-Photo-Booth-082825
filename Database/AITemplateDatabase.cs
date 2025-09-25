using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Photobooth.Services;

namespace Photobooth.Database
{
    public class AITemplateDatabase
    {
        #region Singleton

        private static AITemplateDatabase _instance;
        private static readonly object _lock = new object();

        public static AITemplateDatabase Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new AITemplateDatabase();
                        }
                    }
                }
                return _instance;
            }
        }

        #endregion

        private readonly string _connectionString;
        private readonly string _databasePath;

        private AITemplateDatabase()
        {
            _databasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Photobooth",
                "ai_templates.db");

            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath));

            _connectionString = $"Data Source={_databasePath};Version=3;";

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                // Create Categories table
                string createCategoriesTable = @"
                    CREATE TABLE IF NOT EXISTS AITemplateCategories (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL UNIQUE,
                        Description TEXT,
                        Icon TEXT,
                        SortOrder INTEGER DEFAULT 0,
                        IsActive INTEGER DEFAULT 1,
                        CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                        UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
                    )";

                using (var command = new SQLiteCommand(createCategoriesTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                // Create Templates table
                string createTemplatesTable = @"
                    CREATE TABLE IF NOT EXISTS AITemplates (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        CategoryId INTEGER NOT NULL,
                        Name TEXT NOT NULL,
                        Description TEXT,
                        Prompt TEXT NOT NULL,
                        NegativePrompt TEXT,
                        ModelVersion TEXT,
                        Steps INTEGER DEFAULT 20,
                        GuidanceScale REAL DEFAULT 7.5,
                        PromptStrength REAL DEFAULT 0.8,
                        Seed INTEGER DEFAULT -1,
                        ThumbnailPath TEXT,
                        IsActive INTEGER DEFAULT 1,
                        UsageCount INTEGER DEFAULT 0,
                        CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                        UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (CategoryId) REFERENCES AITemplateCategories(Id) ON DELETE CASCADE
                    )";

                using (var command = new SQLiteCommand(createTemplatesTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                // Create Template Presets table (for user customizations)
                string createPresetsTable = @"
                    CREATE TABLE IF NOT EXISTS AITemplatePresets (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TemplateId INTEGER NOT NULL,
                        Name TEXT NOT NULL,
                        CustomPrompt TEXT,
                        CustomNegativePrompt TEXT,
                        CustomSteps INTEGER,
                        CustomGuidanceScale REAL,
                        CustomPromptStrength REAL,
                        CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (TemplateId) REFERENCES AITemplates(Id) ON DELETE CASCADE
                    )";

                using (var command = new SQLiteCommand(createPresetsTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                // Insert default categories if they don't exist
                InsertDefaultCategories(connection);
            }
        }

        private void InsertDefaultCategories(SQLiteConnection connection)
        {
            string checkCategories = "SELECT COUNT(*) FROM AITemplateCategories";
            using (var command = new SQLiteCommand(checkCategories, connection))
            {
                long count = (long)command.ExecuteScalar();
                if (count == 0)
                {
                    var defaultCategories = new List<(string Name, string Description, int Order)>
                    {
                        ("Characters", "Transform into different characters and personas", 1),
                        ("Scenery", "Change backgrounds and environments", 2),
                        ("Artistic Styles", "Apply various artistic styles and effects", 3),
                        ("Fantasy", "Magical and fantastical transformations", 4),
                        ("Professional", "Business and professional themes", 5),
                        ("Seasonal", "Holiday and seasonal themes", 6),
                        ("Custom", "User-created custom transformations", 999)
                    };

                    foreach (var category in defaultCategories)
                    {
                        string insertCategory = @"
                            INSERT INTO AITemplateCategories (Name, Description, SortOrder)
                            VALUES (@name, @description, @order)";

                        using (var cmd = new SQLiteCommand(insertCategory, connection))
                        {
                            cmd.Parameters.AddWithValue("@name", category.Name);
                            cmd.Parameters.AddWithValue("@description", category.Description);
                            cmd.Parameters.AddWithValue("@order", category.Order);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            }
        }

        #region Category Methods

        public List<AITransformationTemplate> GetAllTemplates()
        {
            var templates = new List<AITransformationTemplate>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = @"
                    SELECT
                        t.Id,
                        t.CategoryId,
                        t.Name,
                        t.Description,
                        t.Prompt,
                        t.NegativePrompt,
                        t.ModelVersion,
                        t.Steps,
                        t.GuidanceScale,
                        t.PromptStrength,
                        t.Seed,
                        t.ThumbnailPath,
                        t.IsActive
                    FROM AITemplates t
                    WHERE t.IsActive = 1
                    ORDER BY t.Name";

                using (var command = new SQLiteCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        templates.Add(new AITransformationTemplate
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Name = reader["Name"].ToString(),
                            Description = reader["Description"]?.ToString(),
                            Prompt = reader["Prompt"].ToString(),
                            NegativePrompt = reader["NegativePrompt"]?.ToString(),
                            ThumbnailPath = reader["ThumbnailPath"]?.ToString(),
                            IsActive = Convert.ToBoolean(reader["IsActive"])
                        });
                    }
                }
            }

            return templates;
        }

        public List<AITemplateCategory> GetCategories(bool activeOnly = true)
        {
            var categories = new List<AITemplateCategory>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string query = activeOnly
                    ? "SELECT * FROM AITemplateCategories WHERE IsActive = 1 ORDER BY SortOrder, Name"
                    : "SELECT * FROM AITemplateCategories ORDER BY SortOrder, Name";

                using (var command = new SQLiteCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        categories.Add(new AITemplateCategory
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Name = reader["Name"].ToString(),
                            Description = reader["Description"]?.ToString(),
                            Icon = reader["Icon"]?.ToString(),
                            SortOrder = Convert.ToInt32(reader["SortOrder"]),
                            IsActive = Convert.ToBoolean(reader["IsActive"])
                        });
                    }
                }
            }

            return categories;
        }

        public int SaveCategory(AITemplateCategory category)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string query = @"
                    INSERT INTO AITemplateCategories (Name, Description, Icon, SortOrder, IsActive)
                    VALUES (@name, @description, @icon, @sortOrder, @isActive);
                    SELECT last_insert_rowid();";

                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@name", category.Name);
                    command.Parameters.AddWithValue("@description", category.Description ?? "");
                    command.Parameters.AddWithValue("@icon", category.Icon ?? "");
                    command.Parameters.AddWithValue("@sortOrder", category.SortOrder);
                    command.Parameters.AddWithValue("@isActive", category.IsActive ? 1 : 0);

                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }

        #endregion

        #region Template Methods

        public List<AITransformationTemplate> GetTemplates(int? categoryId = null, bool activeOnly = true)
        {
            var templates = new List<AITransformationTemplate>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string query = "SELECT t.*, c.Name as CategoryName FROM AITemplates t " +
                              "LEFT JOIN AITemplateCategories c ON t.CategoryId = c.Id WHERE 1=1";

                if (categoryId.HasValue)
                    query += " AND t.CategoryId = @categoryId";

                if (activeOnly)
                    query += " AND t.IsActive = 1";

                query += " ORDER BY t.UsageCount DESC, t.Name";

                using (var command = new SQLiteCommand(query, connection))
                {
                    if (categoryId.HasValue)
                        command.Parameters.AddWithValue("@categoryId", categoryId.Value);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            templates.Add(new AITransformationTemplate
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Name = reader["Name"].ToString(),
                                Category = reader["CategoryId"] != DBNull.Value ? new AITemplateCategory { Id = Convert.ToInt32(reader["CategoryId"]), Name = reader["CategoryName"]?.ToString() } : null,
                                Description = reader["Description"]?.ToString(),
                                Prompt = reader["Prompt"].ToString(),
                                NegativePrompt = reader["NegativePrompt"]?.ToString(),
                                ModelVersion = reader["ModelVersion"]?.ToString(),
                                Steps = reader["Steps"] != DBNull.Value ? Convert.ToInt32(reader["Steps"]) : (int?)null,
                                GuidanceScale = reader["GuidanceScale"] != DBNull.Value ? Convert.ToDouble(reader["GuidanceScale"]) : (double?)null,
                                PromptStrength = reader["PromptStrength"] != DBNull.Value ? Convert.ToDouble(reader["PromptStrength"]) : (double?)null,
                                Seed = reader["Seed"] != DBNull.Value ? Convert.ToInt32(reader["Seed"]) : (int?)null,
                                ThumbnailPath = reader["ThumbnailPath"]?.ToString(),
                                IsActive = Convert.ToBoolean(reader["IsActive"])
                            });
                        }
                    }
                }
            }

            return templates;
        }

        public AITransformationTemplate GetTemplate(int templateId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string query = @"
                    SELECT t.*, c.Name as CategoryName
                    FROM AITemplates t
                    LEFT JOIN AITemplateCategories c ON t.CategoryId = c.Id
                    WHERE t.Id = @id";

                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@id", templateId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new AITransformationTemplate
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Name = reader["Name"].ToString(),
                                Category = reader["CategoryId"] != DBNull.Value ? new AITemplateCategory { Id = Convert.ToInt32(reader["CategoryId"]), Name = reader["CategoryName"]?.ToString() } : null,
                                Description = reader["Description"]?.ToString(),
                                Prompt = reader["Prompt"].ToString(),
                                NegativePrompt = reader["NegativePrompt"]?.ToString(),
                                ModelVersion = reader["ModelVersion"]?.ToString(),
                                Steps = reader["Steps"] != DBNull.Value ? Convert.ToInt32(reader["Steps"]) : (int?)null,
                                GuidanceScale = reader["GuidanceScale"] != DBNull.Value ? Convert.ToDouble(reader["GuidanceScale"]) : (double?)null,
                                PromptStrength = reader["PromptStrength"] != DBNull.Value ? Convert.ToDouble(reader["PromptStrength"]) : (double?)null,
                                Seed = reader["Seed"] != DBNull.Value ? Convert.ToInt32(reader["Seed"]) : (int?)null,
                                ThumbnailPath = reader["ThumbnailPath"]?.ToString(),
                                IsActive = Convert.ToBoolean(reader["IsActive"])
                            };
                        }
                    }
                }
            }

            return null;
        }

        public int SaveTemplate(AITransformationTemplate template, int categoryId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string query = @"
                    INSERT INTO AITemplates (
                        CategoryId, Name, Description, Prompt, NegativePrompt,
                        ModelVersion, Steps, GuidanceScale, PromptStrength, Seed,
                        ThumbnailPath, IsActive
                    ) VALUES (
                        @categoryId, @name, @description, @prompt, @negativePrompt,
                        @modelVersion, @steps, @guidanceScale, @promptStrength, @seed,
                        @thumbnailPath, @isActive
                    );
                    SELECT last_insert_rowid();";

                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@categoryId", categoryId);
                    command.Parameters.AddWithValue("@name", template.Name);
                    command.Parameters.AddWithValue("@description", template.Description ?? "");
                    command.Parameters.AddWithValue("@prompt", template.Prompt);
                    command.Parameters.AddWithValue("@negativePrompt", template.NegativePrompt ?? "");
                    command.Parameters.AddWithValue("@modelVersion", template.ModelVersion ?? "");
                    command.Parameters.AddWithValue("@steps", template.Steps.HasValue ? (object)template.Steps.Value : 20);
                    command.Parameters.AddWithValue("@guidanceScale", template.GuidanceScale.HasValue ? (object)template.GuidanceScale.Value : 7.5);
                    command.Parameters.AddWithValue("@promptStrength", template.PromptStrength.HasValue ? (object)template.PromptStrength.Value : 0.8);
                    command.Parameters.AddWithValue("@seed", template.Seed.HasValue ? (object)template.Seed.Value : -1);
                    command.Parameters.AddWithValue("@thumbnailPath", template.ThumbnailPath ?? "");
                    command.Parameters.AddWithValue("@isActive", template.IsActive ? 1 : 0);

                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }

        public void UpdateTemplate(AITransformationTemplate template)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string query = @"
                    UPDATE AITemplates SET
                        Name = @name,
                        Description = @description,
                        Prompt = @prompt,
                        NegativePrompt = @negativePrompt,
                        ModelVersion = @modelVersion,
                        Steps = @steps,
                        GuidanceScale = @guidanceScale,
                        PromptStrength = @promptStrength,
                        Seed = @seed,
                        ThumbnailPath = @thumbnailPath,
                        IsActive = @isActive,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE Id = @id";

                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@id", template.Id);
                    command.Parameters.AddWithValue("@name", template.Name);
                    command.Parameters.AddWithValue("@description", template.Description ?? "");
                    command.Parameters.AddWithValue("@prompt", template.Prompt);
                    command.Parameters.AddWithValue("@negativePrompt", template.NegativePrompt ?? "");
                    command.Parameters.AddWithValue("@modelVersion", template.ModelVersion ?? "");
                    command.Parameters.AddWithValue("@steps", template.Steps.HasValue ? (object)template.Steps.Value : 20);
                    command.Parameters.AddWithValue("@guidanceScale", template.GuidanceScale.HasValue ? (object)template.GuidanceScale.Value : 7.5);
                    command.Parameters.AddWithValue("@promptStrength", template.PromptStrength.HasValue ? (object)template.PromptStrength.Value : 0.8);
                    command.Parameters.AddWithValue("@seed", template.Seed.HasValue ? (object)template.Seed.Value : -1);
                    command.Parameters.AddWithValue("@thumbnailPath", template.ThumbnailPath ?? "");
                    command.Parameters.AddWithValue("@isActive", template.IsActive ? 1 : 0);

                    command.ExecuteNonQuery();
                }
            }
        }

        public void IncrementUsageCount(int templateId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string query = "UPDATE AITemplates SET UsageCount = UsageCount + 1 WHERE Id = @id";

                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@id", templateId);
                    command.ExecuteNonQuery();
                }
            }
        }

        public void DeleteTemplate(int templateId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string query = "DELETE FROM AITemplates WHERE Id = @id";

                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@id", templateId);
                    command.ExecuteNonQuery();
                }
            }
        }

        #endregion

        #region Sample Data

        public void InsertSampleTemplates()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                // Check if templates already exist
                string checkTemplates = "SELECT COUNT(*) FROM AITemplates";
                using (var command = new SQLiteCommand(checkTemplates, connection))
                {
                    long count = (long)command.ExecuteScalar();
                    if (count > 0)
                        return; // Already have templates
                }

                // Get category IDs
                var categories = GetCategories();
                var categoryMap = categories.ToDictionary(c => c.Name, c => c.Id);

                // Insert sample templates
                var sampleTemplates = new List<(string Category, AITransformationTemplate Template)>
                {
                    // Characters
                    ("Characters", new AITransformationTemplate
                    {
                        Name = "Superhero",
                        Description = "Transform into a superhero with cape and costume",
                        Prompt = "professional photo of person as superhero, wearing superhero costume, cape, heroic pose, city background, cinematic lighting, high quality",
                        NegativePrompt = "cartoon, anime, low quality, blurry, deformed",
                        Steps = 30,
                        GuidanceScale = 7.5,
                        PromptStrength = 0.85
                    }),

                    ("Characters", new AITransformationTemplate
                    {
                        Name = "Medieval Knight",
                        Description = "Become a medieval knight in armor",
                        Prompt = "Make the person wear medieval knight armor with metal breastplate and sword. Add a castle background. Keep the person's face unchanged.",
                        NegativePrompt = "",  // Nano Banana doesn't use negative prompts
                        Steps = 30,
                        GuidanceScale = 7.5,
                        PromptStrength = 0.8
                    }),

                    // Scenery
                    ("Scenery", new AITransformationTemplate
                    {
                        Name = "Tropical Beach",
                        Description = "Place subject on a beautiful tropical beach",
                        Prompt = "professional photo of person on tropical beach, palm trees, clear blue water, sunset, golden hour lighting, vacation photo",
                        NegativePrompt = "cold, winter, snow, indoor, dark",
                        Steps = 25,
                        GuidanceScale = 7.0,
                        PromptStrength = 0.75
                    }),

                    ("Scenery", new AITransformationTemplate
                    {
                        Name = "Space Station",
                        Description = "Transport to a futuristic space station",
                        Prompt = "professional photo of person in space station, futuristic interior, earth visible through window, sci-fi setting, cinematic lighting",
                        NegativePrompt = "outdoors, nature, primitive, low tech",
                        Steps = 30,
                        GuidanceScale = 8.5,
                        PromptStrength = 0.85
                    }),

                    // Artistic Styles
                    ("Artistic Styles", new AITransformationTemplate
                    {
                        Name = "Oil Painting",
                        Description = "Transform photo into classic oil painting style",
                        Prompt = "oil painting portrait of person, classical art style, brush strokes, renaissance painting, museum quality, ornate frame",
                        NegativePrompt = "photo, digital, modern, computer generated",
                        Steps = 35,
                        GuidanceScale = 9.0,
                        PromptStrength = 0.9
                    }),

                    ("Artistic Styles", new AITransformationTemplate
                    {
                        Name = "Watercolor",
                        Description = "Soft watercolor painting effect",
                        Prompt = "watercolor painting of person, soft colors, artistic brush strokes, paper texture, delicate art style",
                        NegativePrompt = "photo, harsh, digital, oil painting",
                        Steps = 30,
                        GuidanceScale = 8.0,
                        PromptStrength = 0.85
                    }),

                    // Fantasy
                    ("Fantasy", new AITransformationTemplate
                    {
                        Name = "Fairy Tale Princess",
                        Description = "Become a fairy tale princess/prince",
                        Prompt = "professional photo of person as fairy tale royalty, wearing elegant gown/suit, crown, magical castle background, enchanted lighting",
                        NegativePrompt = "modern, casual, ordinary, dark, scary",
                        Steps = 30,
                        GuidanceScale = 7.5,
                        PromptStrength = 0.8
                    }),

                    ("Fantasy", new AITransformationTemplate
                    {
                        Name = "Wizard",
                        Description = "Transform into a powerful wizard",
                        Prompt = "professional photo of person as wizard, wearing robes, holding staff, magical effects, ancient library background, mystical lighting",
                        NegativePrompt = "modern, technology, mundane, ordinary",
                        Steps = 35,
                        GuidanceScale = 8.5,
                        PromptStrength = 0.85
                    }),

                    // Professional
                    ("Professional", new AITransformationTemplate
                    {
                        Name = "Corporate Headshot",
                        Description = "Professional business portrait",
                        Prompt = "professional corporate headshot of person, business attire, office background, confident expression, studio lighting",
                        NegativePrompt = "casual, unprofessional, messy, dark",
                        Steps = 25,
                        GuidanceScale = 6.5,
                        PromptStrength = 0.7
                    }),

                    // Seasonal
                    ("Seasonal", new AITransformationTemplate
                    {
                        Name = "Christmas Holiday",
                        Description = "Festive Christmas themed transformation",
                        Prompt = "professional photo of person in Christmas setting, wearing Santa outfit or ugly sweater, Christmas tree, presents, warm lighting, festive",
                        NegativePrompt = "summer, beach, halloween, easter",
                        Steps = 30,
                        GuidanceScale = 7.5,
                        PromptStrength = 0.8
                    })
                };

                foreach (var (category, template) in sampleTemplates)
                {
                    if (categoryMap.ContainsKey(category))
                    {
                        template.IsActive = true;
                        SaveTemplate(template, categoryMap[category]);
                    }
                }
            }
        }

        #endregion
    }

    public class AITemplateCategory
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Icon { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    public class AITransformationTemplate
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public AITemplateCategory Category { get; set; }
        public string Prompt { get; set; }
        public string NegativePrompt { get; set; }
        public string ModelVersion { get; set; }
        public int? Steps { get; set; }
        public double? GuidanceScale { get; set; }
        public double? PromptStrength { get; set; }
        public string ThumbnailPath { get; set; }
        public int? Seed { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastModified { get; set; }
    }
}