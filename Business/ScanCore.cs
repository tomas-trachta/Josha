using Josha.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Josha.Business
{
    internal static class ScanCore
    {
        internal class ScanProgress
        {
            public int Directories;
            public int Files;
            public int Changes;
        }

        internal static void DeepScan(DirOD dir, CancellationToken ct, ScanProgress? progress, int level)
        {
            if (ct.IsCancellationRequested) return;

            if (!dir.IsScanned)
                ScanOneLevel(dir, progress);

            RecurseIntoChildren(dir, ct, progress, level);
        }

        // mtime + file length come for free from WIN32_FIND_DATA during enumeration.
        internal static void ScanOneLevel(DirOD dir, ScanProgress? progress = null)
        {
            DirectoryInfo dirInfo;
            try { dirInfo = new DirectoryInfo(dir.Path); }
            catch
            {
                dir.Subdirectories = [];
                dir.Files = [];
                dir.IsScanned = true;
                if (progress != null) Interlocked.Increment(ref progress.Directories);
                return;
            }

            if (dir.LastWriteTimeUtcTicks == 0)
                dir.LastWriteTimeUtcTicks = SafeMtime(dirInfo);

            var subInfos = SafeGetDirectoryInfos(dirInfo);
            var subs = new DirOD[subInfos.Length];
            for (int i = 0; i < subInfos.Length; i++)
            {
                var info = subInfos[i];
                subs[i] = new DirOD(info.Name, info.FullName)
                {
                    LastWriteTimeUtcTicks = SafeMtime(info),
                };
            }
            dir.Subdirectories = subs;

            var fileInfos = SafeGetFileInfos(dirInfo);
            var files = new FileOD[fileInfos.Length];
            for (int i = 0; i < fileInfos.Length; i++)
            {
                long len;
                try { len = fileInfos[i].Length; } catch { len = 0; }
                files[i] = new FileOD(fileInfos[i].Name, len / 1000);
                if (progress != null) Interlocked.Increment(ref progress.Files);
            }
            dir.Files = files;

            if (progress != null) Interlocked.Increment(ref progress.Directories);
            dir.IsScanned = true;
        }

        // Only the top level fans out: nested Parallel.ForEach saturates the ThreadPool
        // faster than it grows (~1 thread/500ms), which presents as low CPU + UI stalls.
        // ~20-30 root subdirs of C:\ already give the top-level fan-out plenty to schedule.
        internal static void RecurseIntoChildren(DirOD dir, CancellationToken ct, ScanProgress? progress, int level)
        {
            if (dir.Subdirectories.Length == 0) return;

            if (level == 0 && dir.Subdirectories.Length > 1)
            {
                ParallelForEach(dir.Subdirectories, ct, sub =>
                {
                    DeepScan(sub, ct, progress, level + 1);
                });
            }
            else
            {
                foreach (var sub in dir.Subdirectories)
                {
                    if (ct.IsCancellationRequested) return;
                    DeepScan(sub, ct, progress, level + 1);
                }
            }
        }

        internal static void ParallelForEach<T>(
            IEnumerable<T> source,
            CancellationToken ct,
            Action<T> body)
        {
            // Cap workers at ProcessorCount-1 so the UI dispatcher always has at
            // least one core for the loading-bar Storyboard and binding updates.
            var po = new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
            };
            Parallel.ForEach(source, po, body);
        }

        // Single GetFileAttributesEx syscall; cheaper than the two FindFirstFileEx
        // enumerations needed to list children. Returns false (and ticks=0) if the
        // path doesn't exist or can't be read.
        internal static bool TryGetDirMtimeTicks(string path, out long ticks)
        {
            ticks = 0;
            try
            {
                var t = Directory.GetLastWriteTimeUtc(path);
                if (t.Year < 1700) return false;
                ticks = t.Ticks;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Reads mtime from an already-constructed FileSystemInfo. For a child
        // returned by an enumeration, this is free (WIN32_FIND_DATA already filled
        // in). For a freshly-constructed DirectoryInfo, this triggers a Refresh.
        internal static long SafeMtime(FileSystemInfo info)
        {
            try
            {
                var t = info.LastWriteTimeUtc;
                if (t.Year < 1700) return 0;
                return t.Ticks;
            }
            catch
            {
                return 0;
            }
        }

        internal static DirectoryInfo[] SafeGetDirectoryInfos(DirectoryInfo di)
        {
            try { return di.GetDirectories(); }
            catch { return []; }
        }

        internal static FileInfo[] SafeGetFileInfos(DirectoryInfo di)
        {
            try { return di.GetFiles(); }
            catch { return []; }
        }
    }
}
