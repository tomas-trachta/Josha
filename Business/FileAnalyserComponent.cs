using System;
using System.IO;

namespace Josha.Business
{
    internal static class FileAnalyserComponent
    {
        public static bool FileExists(string path)
        {
            try { return File.Exists(path); }
            catch (UnauthorizedAccessException) { return false; }
            catch { return false; }
        }

        public static byte[] ReadFile(string path)
        {
            try { return File.ReadAllBytes(path); }
            catch (UnauthorizedAccessException) { return []; }
            catch { return []; }
        }

        public static void WriteFile(string path, byte[] contents)
        {
            try { File.WriteAllBytes(path, contents); }
            catch (UnauthorizedAccessException) { return; }
            catch { }
        }
    }
}
