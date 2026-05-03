using Josha.Models;
using Josha.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Josha.Business
{
    internal static class SnapshotComponent
    {
        private const string LegacySnapshotFileName = "tree.daps";
        private const string LogCat = "Snapshot";

        private static readonly byte[] DpapiEntropy =
            Encoding.UTF8.GetBytes("Josha/snapshot/v1");

        public static event Action? SnapshotChanged;

        public static void NotifySnapshotChanged() => SnapshotChanged?.Invoke();

        private static string GetDataDir() =>
            DirectoryAnalyserComponent.WinRoot + "josha_data";

        private static string GetSnapshotPath(string driveLetter)
        {
            var letter = NormalizeDriveLetter(driveLetter);
            return Path.Combine(GetDataDir(), $"tree.{letter}.daps");
        }

        // Defaults to "C" on empty/invalid input rather than throwing — the caller
        // has already committed to a write or load.
        private static string NormalizeDriveLetter(string? driveLetter)
        {
            if (string.IsNullOrWhiteSpace(driveLetter)) return "C";
            var s = driveLetter.TrimStart().TrimEnd(':', '\\', '/').Trim();
            if (s.Length == 0) return "C";
            var ch = char.ToUpperInvariant(s[0]);
            if (ch < 'A' || ch > 'Z') return "C";
            return ch.ToString();
        }

        public static void MigrateLegacyOnStartup()
        {
            try
            {
                var dir = GetDataDir();
                var legacy = Path.Combine(dir, LegacySnapshotFileName);
                var newC = GetSnapshotPath("C");

                if (!FileAnalyserComponent.FileExists(legacy)) return;
                if (FileAnalyserComponent.FileExists(newC)) return;

                if (!DirectoryAnalyserComponent.DirectoryExists(dir)) return;

                File.Move(legacy, newC);
                Log.Info("Snapshot",
                    $"Migrated legacy {LegacySnapshotFileName} → {Path.GetFileName(newC)}");
            }
            catch (Exception ex)
            {
                Log.Warn("Snapshot", "Legacy tree.daps migration failed", ex);
            }
        }

        public static bool SnapshotExists() => SnapshotExists("C");

        public static bool SnapshotExists(string driveLetter) =>
            FileAnalyserComponent.FileExists(GetSnapshotPath(driveLetter));

        public static void SaveSnapshot(DirOD root) => SaveSnapshot("C", root);

        public static void SaveSnapshot(string driveLetter, DirOD root)
        {
            if (root == null) return;

            var dirPath = GetDataDir();
            var filePath = GetSnapshotPath(driveLetter);

            if (!DirectoryAnalyserComponent.DirectoryExists(dirPath))
            {
                if (!DirectoryAnalyserComponent.CreateDirectory(dirPath))
                {
                    Log.Warn("Snapshot", $"Could not create data directory at {dirPath}");
                    return;
                }
            }

            var sb = new StringBuilder();
            WriteNode(sb, root, 0);

            try
            {
                var encrypted = CryptoComponent.ProtectString(sb.ToString(), DpapiEntropy);
                var wrapped = PersistenceMigrator.WrapV1(encrypted);
                FileAnalyserComponent.WriteFile(filePath, wrapped);
            }
            catch (Exception ex)
            {
                Log.Error(LogCat, $"Failed to save {Path.GetFileName(filePath)}", ex);
            }
        }

        public static DirOD? LoadSnapshot() => LoadSnapshot("C");

        public static DirOD? LoadSnapshot(string driveLetter)
        {
            var filePath = GetSnapshotPath(driveLetter);
            if (!FileAnalyserComponent.FileExists(filePath))
                return null;

            byte[] rawBytes;
            try { rawBytes = FileAnalyserComponent.ReadFile(filePath); }
            catch (Exception ex)
            {
                Log.Error(LogCat, $"Failed to read {Path.GetFileName(filePath)}", ex);
                return null;
            }
            if (rawBytes is null || rawBytes.Length == 0)
                return null;

            var unwrap = PersistenceMigrator.Unwrap(rawBytes);
            byte[] ciphertext;

            switch (unwrap.Status)
            {
                case PersistenceMigrator.UnwrapStatus.Ok:
                    ciphertext = unwrap.Payload;
                    break;

                case PersistenceMigrator.UnwrapStatus.NoEnvelope:
                    // Pre-DPAPI snapshots were AES-HWID encrypted with no envelope.
                    // Can't decrypt; isolate so the next save doesn't blow it away.
                    // Snapshot will be rebuilt on next rescan.
                    Log.Warn(LogCat,
                        $"{Path.GetFileName(filePath)}: pre-DPAPI format; cannot read");
                    BackupAndIsolate(filePath, "legacy-format");
                    return null;

                case PersistenceMigrator.UnwrapStatus.NewerVersion:
                    Log.Warn(LogCat,
                        $"{Path.GetFileName(filePath)}: written by newer format (v{unwrap.Version}); cannot read with v{PersistenceMigrator.CurrentVersion}");
                    BackupAndIsolate(filePath, "newer-version");
                    return null;

                case PersistenceMigrator.UnwrapStatus.Truncated:
                    Log.Warn(LogCat,
                        $"{Path.GetFileName(filePath)}: truncated / corrupted; will not load");
                    BackupAndIsolate(filePath, "truncated");
                    return null;

                default:
                    return null;
            }

            string text;
            try
            {
                text = CryptoComponent.UnprotectString(ciphertext, DpapiEntropy);
            }
            catch (Exception ex)
            {
                Log.Error(LogCat, $"DPAPI unprotect failed for {Path.GetFileName(filePath)} (different user/machine?)", ex);
                BackupAndIsolate(filePath, "dpapi-failed");
                return null;
            }

            if (string.IsNullOrEmpty(text))
                return null;

            try
            {
                return ParseTree(text);
            }
            catch (Exception ex)
            {
                Log.Error(LogCat, $"Parse failed for {Path.GetFileName(filePath)}", ex);
                return null;
            }
        }

        private static void BackupAndIsolate(string path, string reason)
        {
            try
            {
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                var bak = $"{path}.{reason}-{stamp}.bak";
                File.Move(path, bak);
                Log.Warn(LogCat, $"Moved unreadable {Path.GetFileName(path)} to {Path.GetFileName(bak)}");
            }
            catch (Exception ex)
            {
                Log.Error(LogCat, $"Could not preserve {Path.GetFileName(path)} as backup", ex);
            }
        }

        public static void Reconcile(DirOD root, CancellationToken ct, ScanCore.ScanProgress? progress = null)
            => Reconcile("C", root, ct, progress);

        public static void Reconcile(string driveLetter, DirOD root, CancellationToken ct, ScanCore.ScanProgress? progress = null)
        {
            if (root == null) return;
            _ = driveLetter;

            var p = progress ?? new ScanCore.ScanProgress();

            ReconcileDir(root, ct, p, level: 0);
            if (!ct.IsCancellationRequested)
                root.GetDirSizeSequential();

            if (!ct.IsCancellationRequested && p.Changes > 0)
                SnapshotChanged?.Invoke();
        }

        private static void WriteNode(StringBuilder sb, DirOD dir, int depth)
        {
            sb.Append(' ', depth);
            AppendSafe(sb, dir.Name);
            sb.Append('[');
            sb.Append(dir.SizeKiloBytes.ToString(CultureInfo.InvariantCulture));
            sb.Append("][");
            sb.Append(dir.Subdirectories.Length.ToString(CultureInfo.InvariantCulture));
            sb.Append("][");
            sb.Append(dir.Files.Length.ToString(CultureInfo.InvariantCulture));
            sb.Append("][");
            sb.Append(dir.LastWriteTimeUtcTicks.ToString(CultureInfo.InvariantCulture));
            sb.Append("]:");
            sb.Append(Environment.NewLine);

            foreach (var sub in dir.Subdirectories)
                WriteNode(sb, sub, depth + 1);

            foreach (var file in dir.Files)
            {
                sb.Append(' ', depth + 1);
                AppendSafe(sb, file.Name);
                sb.Append('[');
                sb.Append(file.SizeKiloBytes.ToString(CultureInfo.InvariantCulture));
                sb.Append(']');
                sb.Append(Environment.NewLine);
            }
        }

        private static void AppendSafe(StringBuilder sb, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            foreach (var c in name)
            {
                if (c == '\r' || c == '\n') continue;
                sb.Append(c);
            }
        }

        private static DirOD? ParseTree(string text)
        {
            var lines = text.Split('\n');
            var stack = new Stack<PendingDir>();
            DirOD? root = null;

            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;

                int depth = 0;
                while (depth < line.Length && line[depth] == ' ') depth++;
                if (depth == line.Length) continue;

                var content = line.Substring(depth);

                if (content.EndsWith("]:"))
                {
                    if (!TryParseDirLine(content, out var name, out var size, out var mtimeTicks))
                        continue;

                    while (stack.Count > 0 && stack.Peek().Depth >= depth)
                        FinalizePending(stack.Pop());

                    string fullPath = stack.Count == 0
                        ? name
                        : Path.Combine(stack.Peek().Dir.Path, name);

                    var dirOd = new DirOD(name, fullPath)
                    {
                        SizeKiloBytes = size,
                        LastWriteTimeUtcTicks = mtimeTicks,
                        IsScanned = true,
                    };

                    if (stack.Count == 0)
                    {
                        if (root != null) continue;
                        root = dirOd;
                    }
                    else
                    {
                        stack.Peek().Subs.Add(dirOd);
                    }

                    stack.Push(new PendingDir
                    {
                        Dir = dirOd,
                        Depth = depth,
                        Subs = new List<DirOD>(),
                        Files = new List<FileOD>(),
                    });
                }
                else if (content[content.Length - 1] == ']')
                {
                    if (!TryParseFileLine(content, out var name, out var size))
                        continue;

                    while (stack.Count > 0 && stack.Peek().Depth >= depth)
                        FinalizePending(stack.Pop());

                    if (stack.Count == 0) continue;
                    stack.Peek().Files.Add(new FileOD(name, size));
                }
            }

            while (stack.Count > 0)
                FinalizePending(stack.Pop());

            return root;
        }

        // Directory line format: <name>[<size>][<numSubs>][<numFiles>][<mtimeTicks>]:
        // Counts are written for reconciliation but not retained in memory
        // (Subdirectories.Length / Files.Length gives them implicitly).
        private static bool TryParseDirLine(string content, out string name, out decimal size, out long mtimeTicks)
        {
            name = "";
            size = 0;
            mtimeTicks = 0;

            if (!content.EndsWith("]:")) return false;
            var s = content.Substring(0, content.Length - 1);

            if (!TryStripRightBracket(ref s, out var mtimeStr)) return false;
            if (!TryStripRightBracket(ref s, out _)) return false;
            if (!TryStripRightBracket(ref s, out _)) return false;
            if (!TryStripRightBracket(ref s, out var sizeStr)) return false;

            if (!decimal.TryParse(sizeStr, NumberStyles.Number, CultureInfo.InvariantCulture, out size))
                return false;
            long.TryParse(mtimeStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out mtimeTicks);

            name = s;
            return name.Length > 0;
        }

        private static bool TryParseFileLine(string content, out string name, out decimal size)
        {
            name = "";
            size = 0;

            var s = content;
            if (!TryStripRightBracket(ref s, out var sizeStr)) return false;
            if (!decimal.TryParse(sizeStr, NumberStyles.Number, CultureInfo.InvariantCulture, out size))
                return false;

            name = s;
            return name.Length > 0;
        }

        private static bool TryStripRightBracket(ref string s, out string inner)
        {
            inner = "";
            if (s.Length < 2 || s[s.Length - 1] != ']') return false;
            var openIdx = s.LastIndexOf('[', s.Length - 2);
            if (openIdx < 0) return false;
            inner = s.Substring(openIdx + 1, s.Length - 2 - openIdx);
            s = s.Substring(0, openIdx);
            return true;
        }

        private static void FinalizePending(PendingDir p)
        {
            p.Dir.Subdirectories = p.Subs.ToArray();
            p.Dir.Files = p.Files.ToArray();
        }

        private class PendingDir
        {
            public DirOD Dir = null!;
            public int Depth;
            public List<DirOD> Subs = new();
            public List<FileOD> Files = new();
        }

        private static void ReconcileDir(DirOD dir, CancellationToken ct, ScanCore.ScanProgress? progress, int level)
        {
            if (ct.IsCancellationRequested) return;

            // Ultra-fast path: directory mtime unchanged → no add/remove/rename happened
            // at this level. NTFS bumps the parent dir's LastWriteTime on those operations,
            // so an unchanged mtime is a strict superset of the old count+name heuristic.
            // Single GetFileAttributesEx syscall; skips both FindFirstFileEx enumerations.
            // Still recurses into subdirs so deeper changes are detected at their own level.
            long actualTicks = 0;
            bool mtimeProbed = false;
            if (dir.LastWriteTimeUtcTicks != 0)
            {
                if (!ScanCore.TryGetDirMtimeTicks(dir.Path, out actualTicks))
                {
                    if (dir.Subdirectories.Length > 0 || dir.Files.Length > 0)
                        if (progress != null) Interlocked.Increment(ref progress.Changes);
                    dir.Subdirectories = [];
                    dir.Files = [];
                    dir.IsScanned = true;
                    if (progress != null) Interlocked.Increment(ref progress.Directories);
                    return;
                }
                mtimeProbed = true;

                if (actualTicks == dir.LastWriteTimeUtcTicks)
                {
                    if (progress != null) Interlocked.Increment(ref progress.Directories);
                    dir.IsScanned = true;
                    RecurseChildren(dir, ct, progress, level);
                    return;
                }
            }

            DirectoryInfo dirInfo;
            try { dirInfo = new DirectoryInfo(dir.Path); }
            catch
            {
                if (dir.Subdirectories.Length > 0 || dir.Files.Length > 0)
                    if (progress != null) Interlocked.Increment(ref progress.Changes);
                dir.Subdirectories = [];
                dir.Files = [];
                dir.IsScanned = true;
                if (progress != null) Interlocked.Increment(ref progress.Directories);
                return;
            }

            var actualSubInfos = ScanCore.SafeGetDirectoryInfos(dirInfo);
            var actualFileInfos = ScanCore.SafeGetFileInfos(dirInfo);

            // Refresh cached mtime — reuse the probe value if we already paid for it,
            // otherwise read from dirInfo (this triggers a Refresh syscall).
            if (mtimeProbed)
                dir.LastWriteTimeUtcTicks = actualTicks;
            else
                dir.LastWriteTimeUtcTicks = ScanCore.SafeMtime(dirInfo);

            // Fast path: counts and names match → trust cached file sizes,
            // just recurse into subdirs to detect deeper changes.
            if (actualSubInfos.Length == dir.Subdirectories.Length &&
                actualFileInfos.Length == dir.Files.Length &&
                DirectoryNamesMatch(dir.Subdirectories, actualSubInfos) &&
                FileNamesMatch(dir.Files, actualFileInfos))
            {
                if (progress != null) Interlocked.Increment(ref progress.Directories);
                dir.IsScanned = true;
                RecurseChildren(dir, ct, progress, level);
                return;
            }

            // Reaching the slow path means at least one sub or file was added,
            // removed, or renamed at this level — the on-disk snapshot is stale.
            if (progress != null) Interlocked.Increment(ref progress.Changes);

            var existingSubs = new Dictionary<string, DirOD>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in dir.Subdirectories)
            {
                if (!existingSubs.ContainsKey(s.Name))
                    existingSubs[s.Name] = s;
            }

            var newSubs = new DirOD[actualSubInfos.Length];
            for (int i = 0; i < actualSubInfos.Length; i++)
            {
                if (ct.IsCancellationRequested) return;
                var subDi = actualSubInfos[i];
                if (existingSubs.TryGetValue(subDi.Name, out var existing))
                {
                    existing.Path = subDi.FullName;
                    newSubs[i] = existing;
                }
                else
                {
                    // Capture the new sub's mtime from the parent's enumeration — free,
                    // since FindFirstFileEx already returned WIN32_FIND_DATA for it.
                    newSubs[i] = new DirOD(subDi.Name, subDi.FullName)
                    {
                        IsScanned = false,
                        LastWriteTimeUtcTicks = ScanCore.SafeMtime(subDi),
                    };
                }
            }
            dir.Subdirectories = newSubs;

            var newFiles = new FileOD[actualFileInfos.Length];
            for (int i = 0; i < actualFileInfos.Length; i++)
            {
                if (ct.IsCancellationRequested) return;
                var fi = actualFileInfos[i];
                long len;
                try { len = fi.Length; } catch { len = 0; }
                newFiles[i] = new FileOD(fi.Name, len / 1000);
                if (progress != null) Interlocked.Increment(ref progress.Files);
            }
            dir.Files = newFiles;

            if (progress != null) Interlocked.Increment(ref progress.Directories);
            dir.IsScanned = true;
            RecurseChildren(dir, ct, progress, level);
        }

        // Recurses into a directory's children, parallelizing the top 2 levels of the
        // tree. Already-scanned subs go through ReconcileDir (mtime probe + fast/slow
        // path); unscanned subs (newly added since the snapshot) get a fresh DeepScan.
        private static void RecurseChildren(DirOD dir, CancellationToken ct, ScanCore.ScanProgress? progress, int level)
        {
            if (dir.Subdirectories.Length == 0) return;

            if (level < 2 && dir.Subdirectories.Length > 1)
            {
                ScanCore.ParallelForEach(dir.Subdirectories, ct, sub =>
                {
                    if (sub.IsScanned)
                        ReconcileDir(sub, ct, progress, level + 1);
                    else
                        ScanCore.DeepScan(sub, ct, progress, level + 1);
                });
            }
            else
            {
                foreach (var sub in dir.Subdirectories)
                {
                    if (ct.IsCancellationRequested) return;
                    if (sub.IsScanned)
                        ReconcileDir(sub, ct, progress, level + 1);
                    else
                        ScanCore.DeepScan(sub, ct, progress, level + 1);
                }
            }
        }

        private static bool DirectoryNamesMatch(DirOD[] cached, DirectoryInfo[] actual)
        {
            var set = new HashSet<string>(actual.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var di in actual)
                set.Add(di.Name);
            foreach (var s in cached)
                if (!set.Contains(s.Name)) return false;
            return true;
        }

        private static bool FileNamesMatch(FileOD[] cached, FileInfo[] actual)
        {
            var set = new HashSet<string>(actual.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var fi in actual)
                set.Add(fi.Name);
            foreach (var f in cached)
                if (!set.Contains(f.Name)) return false;
            return true;
        }
    }
}
