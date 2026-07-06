using System;
using System.IO;
using System.Text.Json;

namespace RepoCosmeticTracker.Services
{
    public class AppSettings
    {
        public string? LastExportFolder { get; set; }

        private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch
            {
                // Corrupt/unreadable settings file — fall back to defaults
                // rather than crashing the app over a convenience feature.
            }

            return new AppSettings();
        }

        public void Save()
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
    }
}
