using Josha.Models;
using System.IO;

namespace Josha.Business
{
    internal class DirectoryAnalyserComponent
    {
        internal static string WinRoot = @"C:\";

        public DirOD? Root { get; set; }

        private ScanCore.ScanProgress _progress = new();

        public int DirectoriesScanned => _progress.Directories;
        public int FilesScanned => _progress.Files;

        public void Run(CancellationToken ct = default)
        {
            _progress = new ScanCore.ScanProgress();
            Root = new DirOD(WinRoot, WinRoot);
            ScanCore.DeepScan(Root, ct, _progress, level: 0);
            if (!ct.IsCancellationRequested)
                Root.GetDirSize();
        }

        public static bool DirectoryExists(string path)
        {
            try { return Directory.Exists(path); }
            catch { return false; }
        }

        public static bool CreateDirectory(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                return true;
            }
            catch { return false; }
        }
    }
}
