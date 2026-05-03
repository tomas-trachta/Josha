using Josha.Models;
using System.IO;
using System.Text;

namespace Josha.Business
{
    // Encrypted bookmark list at C:\josha_data\bookmarks.dans, DPAPI-protected
    // (CurrentUser scope) with per-component entropy. Format: one line per
    // bookmark, "Name<TAB>TargetPath".
    internal static class BookmarkComponent
    {
        private const string FileName = "bookmarks.dans";
        private const string LogCat = "Bookmarks";

        private static readonly byte[] DpapiEntropy =
            Encoding.UTF8.GetBytes("Josha/bookmarks/v1");

        private static string GetFilePath() =>
            Path.Combine(DirectoryAnalyserComponent.WinRoot + "josha_data", FileName);

        public static List<Bookmark> Load()
        {
            var text = PersistenceFile.LoadDecrypted(GetFilePath(), DpapiEntropy, LogCat);
            var list = new List<Bookmark>();
            if (string.IsNullOrEmpty(text)) return list;

            foreach (var line in text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t', 2);
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                    list.Add(new Bookmark(parts[0], parts[1]));
            }
            return list;
        }

        public static void Save(IEnumerable<Bookmark> bookmarks)
        {
            var dir = DirectoryAnalyserComponent.WinRoot + "josha_data";
            if (!DirectoryAnalyserComponent.DirectoryExists(dir))
                DirectoryAnalyserComponent.CreateDirectory(dir);

            var text = string.Join(Environment.NewLine,
                bookmarks.Select(b => $"{b.Name.Replace('\t', ' ')}\t{b.TargetPath}"));
            PersistenceFile.SaveEncrypted(GetFilePath(), text, DpapiEntropy, LogCat);
        }
    }
}
