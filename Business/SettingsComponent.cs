using Josha.Models;
using Josha.Services;
using System.IO;
using System.Text.Json;

namespace Josha.Business
{
    internal static class SettingsComponent
    {
        private const string FileName = "settings.json";
        private const string LogCat = "Settings";

        private static readonly JsonSerializerOptions Opts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        private static string GetFilePath() =>
            Path.Combine(DirectoryAnalyserComponent.WinRoot + "josha_data", FileName);

        public static AppSettings Load()
        {
            try
            {
                var path = GetFilePath();
                if (!File.Exists(path)) return new AppSettings();
                var text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) return new AppSettings();
                return JsonSerializer.Deserialize<AppSettings>(text, Opts) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                Log.Warn(LogCat, "Settings load failed; using defaults", ex);
                return new AppSettings();
            }
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                var dir = DirectoryAnalyserComponent.WinRoot + "josha_data";
                if (!DirectoryAnalyserComponent.DirectoryExists(dir))
                    DirectoryAnalyserComponent.CreateDirectory(dir);
                File.WriteAllText(GetFilePath(), JsonSerializer.Serialize(settings, Opts));
            }
            catch (Exception ex)
            {
                Log.Warn(LogCat, "Settings save failed", ex);
            }
        }
    }
}
