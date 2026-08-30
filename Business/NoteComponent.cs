using Josha.Models;
using Josha.Services;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Josha.Business
{
    // Encrypted file/directory notes at C:\josha_data\notes.dans, DPAPI-protected
    // (CurrentUser scope) with per-component entropy. JSON array of Note, same
    // envelope/DPAPI shape as bookmarks.dans / history.dans.
    internal static class NoteComponent
    {
        private const string FileName = "notes.dans";
        private const string LogCat = "Notes";

        private static readonly byte[] DpapiEntropy =
            Encoding.UTF8.GetBytes("Josha/notes/v1");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
        };

        private static string GetFilePath() =>
            Path.Combine(DirectoryAnalyserComponent.WinRoot + "josha_data", FileName);

        public static List<Note> Load()
        {
            var text = PersistenceFile.LoadDecrypted(GetFilePath(), DpapiEntropy, LogCat);
            if (string.IsNullOrEmpty(text)) return new List<Note>();

            try
            {
                return JsonSerializer.Deserialize<List<Note>>(text, JsonOptions)
                    ?? new List<Note>();
            }
            catch (Exception ex)
            {
                Log.Error(LogCat, $"Deserialize failed for {FileName}", ex);
                return new List<Note>();
            }
        }

        public static void Save(IEnumerable<Note> notes)
        {
            var dir = DirectoryAnalyserComponent.WinRoot + "josha_data";
            if (!DirectoryAnalyserComponent.DirectoryExists(dir))
                DirectoryAnalyserComponent.CreateDirectory(dir);

            var text = JsonSerializer.Serialize(notes, JsonOptions);
            PersistenceFile.SaveEncrypted(GetFilePath(), text, DpapiEntropy, LogCat);
        }
    }
}
