using Josha.Models;
using Josha.Services;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Josha.Business
{
    // Encrypted remote-directory-visit log at C:\josha_data\remote_history.dans,
    // DPAPI-protected (CurrentUser scope) with per-component entropy. JSON array
    // of RemoteHistoryEntry, same envelope/DPAPI shape as history.dans.
    internal static class RemoteHistoryComponent
    {
        private const string FileName = "remote_history.dans";
        private const string LogCat = "RemoteHistory";

        private static readonly byte[] DpapiEntropy =
            Encoding.UTF8.GetBytes("Josha/remotehistory/v1");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
        };

        private static string GetFilePath() =>
            Path.Combine(DirectoryAnalyserComponent.WinRoot + "josha_data", FileName);

        public static List<RemoteHistoryEntry> Load()
        {
            var text = PersistenceFile.LoadDecrypted(GetFilePath(), DpapiEntropy, LogCat);
            if (string.IsNullOrEmpty(text)) return new List<RemoteHistoryEntry>();

            try
            {
                return JsonSerializer.Deserialize<List<RemoteHistoryEntry>>(text, JsonOptions)
                    ?? new List<RemoteHistoryEntry>();
            }
            catch (Exception ex)
            {
                Log.Error(LogCat, $"Deserialize failed for {FileName}", ex);
                return new List<RemoteHistoryEntry>();
            }
        }

        public static void Save(IEnumerable<RemoteHistoryEntry> entries)
        {
            var dir = DirectoryAnalyserComponent.WinRoot + "josha_data";
            if (!DirectoryAnalyserComponent.DirectoryExists(dir))
                DirectoryAnalyserComponent.CreateDirectory(dir);

            var text = JsonSerializer.Serialize(entries, JsonOptions);
            PersistenceFile.SaveEncrypted(GetFilePath(), text, DpapiEntropy, LogCat);
        }
    }
}
