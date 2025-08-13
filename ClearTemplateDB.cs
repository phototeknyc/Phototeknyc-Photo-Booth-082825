using System;
using Photobooth.Database;

namespace Photobooth.Utilities
{
    /// <summary>
    /// Utility to clear the template database
    /// Compile with: csc ClearTemplateDB.cs /reference:Database\TemplateDatabase.cs /reference:System.Data.SQLite.dll
    /// Or run from the application using the ClearAllTemplatesCmd command
    /// </summary>
    class ClearTemplateDB
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Template Database Cleaner");
            Console.WriteLine("========================");
            Console.WriteLine();
            Console.WriteLine("WARNING: This will delete ALL templates from the database!");
            Console.WriteLine("This action cannot be undone.");
            Console.WriteLine();
            Console.Write("Are you sure you want to continue? (yes/no): ");
            
            string response = Console.ReadLine()?.ToLower();
            
            if (response == "yes" || response == "y")
            {
                try
                {
                    var database = new TemplateDatabase();
                    database.ClearAllTemplates();
                    Console.WriteLine();
                    Console.WriteLine("SUCCESS: All templates have been cleared from the database.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine($"ERROR: Failed to clear database - {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("Operation cancelled.");
            }
            
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}