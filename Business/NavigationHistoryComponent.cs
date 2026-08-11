using Josha.Models;
using Josha.Services;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Josha.Business
{
    // Encrypted directory-visit log at C:\josha_data\history.dans, DPAPI-protected
    // (CurrentUser scope) with per-component entropy. JSON array of
    // NavigationHistoryEntry, same envelope/DPAPI shape as bookmarks.dans.
    internal static class NavigationHistoryComponent
    {
        private const string FileName = "history.dans";
        private const string LogCat = "History";

        private static readonly byte[] DpapiEntropy =
            Encoding.UTF8.GetBytes("Josha/history/v1");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
        };

        private static string GetFilePath() =>
            Path.Combine(DirectoryAnalyserComponent.WinRoot + "josha_data", FileName);

        public static List<NavigationHistoryEntry> Load()
        {
            var text = PersistenceFile.LoadDecrypted(GetFilePath(), DpapiEntropy, LogCat);
            if (string.IsNullOrEmpty(text)) return new List<NavigationHistoryEntry>();

            try
            {
                return JsonSerializer.Deserialize<List<NavigationHistoryEntry>>(text, JsonOptions)
                    ?? new List<NavigationHistoryEntry>();
            }
            catch (Exception ex)
            {
                Log.Error(LogCat, $"Deserialize failed for {FileName}", ex);
                return new List<NavigationHistoryEntry>();
            }
        }

        public static void Save(IEnumerable<NavigationHistoryEntry> entries)
        {
            var dir = DirectoryAnalyserComponent.WinRoot + "josha_data";
            if (!DirectoryAnalyserComponent.DirectoryExists(dir))
                DirectoryAnalyserComponent.CreateDirectory(dir);

            var text = JsonSerializer.Serialize(entries, JsonOptions);
            PersistenceFile.SaveEncrypted(GetFilePath(), text, DpapiEntropy, LogCat);
        }
    }
}
