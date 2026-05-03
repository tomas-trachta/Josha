using Josha.Services;
using System.IO;

namespace Josha.Business
{
    internal static class FileOpsComponent
    {
        // 1 MB chunks: large enough that per-chunk overhead is negligible, small
        // enough for sub-second progress granularity on slow disks.
        private const int CopyBufferBytes = 1024 * 1024;

        public sealed record OpResult(bool Success, string? Error = null)
        {
            public static OpResult Ok() => new(true);
            public static OpResult Fail(string message) => new(false, message);
            public static OpResult Cancelled() => new(false, "Cancelled");
        }

        public static async Task<OpResult> CopyAsync(
            string srcPath,
            string dstPath,
            IProgress<long>? bytesCopied = null,
            bool overwrite = false,
            CancellationToken ct = default)
        {
            Log.Info("FileOps", $"Copy start: {srcPath} → {dstPath} (overwrite={overwrite})");
            var ctx = new CopyContext { Progress = bytesCopied, Overwrite = overwrite };
            var result = await CopyAnyAsync(srcPath, dstPath, ctx, ct).ConfigureAwait(false);

            if (result.Success)
            {
                Log.Info("FileOps", $"Copy ok: {srcPath} → {dstPath} ({ctx.TotalBytes:N0} bytes)");
                SnapshotComponent.NotifySnapshotChanged();
            }
            else
            {
                Log.Warn("FileOps", $"Copy failed: {srcPath} → {dstPath} — {result.Error}");
            }
            return result;
        }

        public static async Task<OpResult> MoveAsync(
            string srcPath,
            string dstPath,
            IProgress<long>? bytesCopied = null,
            bool overwrite = false,
            CancellationToken ct = default)
        {
            Log.Info("FileOps", $"Move start: {srcPath} → {dstPath} (overwrite={overwrite})");

            if (IsSameVolume(srcPath, dstPath))
            {
                try
                {
                    if (Directory.Exists(srcPath))
                    {
                        // Directory.Move has no overwrite mode and refuses if dst exists.
                        if (overwrite && Directory.Exists(dstPath))
                            Directory.Delete(dstPath, recursive: true);
                        Directory.Move(srcPath, dstPath);
                    }
                    else if (File.Exists(srcPath))
                    {
                        File.Move(srcPath, dstPath, overwrite: overwrite);
                    }
                    else
                    {
                        Log.Warn("FileOps", $"Move source missing: {srcPath}");
                        return OpResult.Fail("Source does not exist");
                    }

                    Log.Info("FileOps", $"Move ok (same volume): {srcPath} → {dstPath}");
                    SnapshotComponent.NotifySnapshotChanged();
                    return OpResult.Ok();
                }
                catch (Exception ex)
                {
                    Log.Error("FileOps", $"Move failed: {srcPath} → {dstPath}", ex);
                    return OpResult.Fail(ex.Message);
                }
            }

            var copyResult = await CopyAsync(srcPath, dstPath, bytesCopied, overwrite, ct).ConfigureAwait(false);
            if (!copyResult.Success) return copyResult;

            try
            {
                if (Directory.Exists(srcPath))
                    Directory.Delete(srcPath, recursive: true);
                else
                    File.Delete(srcPath);

                Log.Info("FileOps", $"Move ok (cross-volume): {srcPath} → {dstPath}");
                SnapshotComponent.NotifySnapshotChanged();
                return OpResult.Ok();
            }
            catch (Exception ex)
            {
                // Cross-volume move's source-delete failed after a successful
                // copy: leave both items in place — destination has the data,
                // source is still readable, user decides.
                var msg = $"Copied to destination but couldn't remove source: {ex.Message}";
                Log.Warn("FileOps",
                    $"Move partial: copy OK, source delete failed. Both {srcPath} (kept) and {dstPath} (new) now exist.", ex);
                return OpResult.Fail(msg);
            }
        }

        public static OpResult Rename(string srcPath, string newName)
        {
            Log.Info("FileOps", $"Rename: {srcPath} → {newName}");

            if (string.IsNullOrWhiteSpace(newName))
                return OpResult.Fail("Name cannot be empty");
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return OpResult.Fail("Name contains invalid characters");

            try
            {
                var parent = Path.GetDirectoryName(srcPath);
                if (string.IsNullOrEmpty(parent))
                    return OpResult.Fail("Could not determine parent directory");

                var dstPath = Path.Combine(parent, newName);
                if (string.Equals(dstPath, srcPath, StringComparison.OrdinalIgnoreCase))
                    return OpResult.Ok();
                if (File.Exists(dstPath) || Directory.Exists(dstPath))
                    return OpResult.Fail("An item with that name already exists");

                if (Directory.Exists(srcPath))
                    Directory.Move(srcPath, dstPath);
                else if (File.Exists(srcPath))
                    File.Move(srcPath, dstPath);
                else
                    return OpResult.Fail("Source does not exist");

                Log.Info("FileOps", $"Rename ok: {srcPath} → {dstPath}");
                SnapshotComponent.NotifySnapshotChanged();
                return OpResult.Ok();
            }
            catch (Exception ex)
            {
                Log.Error("FileOps", $"Rename failed: {srcPath} → {newName}", ex);
                return OpResult.Fail(ex.Message);
            }
        }

        public static OpResult CreateDirectory(string parentPath, string name)
        {
            Log.Info("FileOps", $"Mkdir: {parentPath}\\{name}");

            if (string.IsNullOrWhiteSpace(name))
                return OpResult.Fail("Folder name cannot be empty");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return OpResult.Fail("Folder name contains invalid characters");

            try
            {
                var path = Path.Combine(parentPath, name);
                if (Directory.Exists(path) || File.Exists(path))
                    return OpResult.Fail("An item with that name already exists");

                Directory.CreateDirectory(path);
                Log.Info("FileOps", $"Mkdir ok: {path}");
                SnapshotComponent.NotifySnapshotChanged();
                return OpResult.Ok();
            }
            catch (Exception ex)
            {
                Log.Error("FileOps", $"Mkdir failed: {parentPath}\\{name}", ex);
                return OpResult.Fail(ex.Message);
            }
        }

        // VB.FileIO is the only standard-library surface for shell-integrated
        // recycle semantics; we accept its modal-on-error behaviour here because
        // recycle is the whole point. Copy/Move/Rename above deliberately use
        // File.* + manual stream loop to avoid VB.FileIO's shell modal.
        public static OpResult DeleteToRecycle(string path)
        {
            Log.Info("FileOps", $"Recycle: {path}");
            try
            {
                if (Directory.Exists(path))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                        Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
                }
                else if (File.Exists(path))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                        Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
                }
                else
                {
                    return OpResult.Fail("Item does not exist");
                }

                Log.Info("FileOps", $"Recycle ok: {path}");
                SnapshotComponent.NotifySnapshotChanged();
                return OpResult.Ok();
            }
            catch (OperationCanceledException)
            {
                Log.Info("FileOps", $"Recycle cancelled by user: {path}");
                return OpResult.Cancelled();
            }
            catch (Exception ex)
            {
                Log.Error("FileOps", $"Recycle failed: {path}", ex);
                return OpResult.Fail(ex.Message);
            }
        }

        public static OpResult DeletePermanent(string path)
        {
            Log.Info("FileOps", $"Permanent delete: {path}");
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                else if (File.Exists(path))
                    File.Delete(path);
                else
                    return OpResult.Fail("Item does not exist");

                Log.Info("FileOps", $"Permanent delete ok: {path}");
                SnapshotComponent.NotifySnapshotChanged();
                return OpResult.Ok();
            }
            catch (Exception ex)
            {
                Log.Error("FileOps", $"Permanent delete failed: {path}", ex);
                return OpResult.Fail(ex.Message);
            }
        }

        private sealed class CopyContext
        {
            public long TotalBytes;
            public IProgress<long>? Progress;
            public bool Overwrite;

            public void Add(long bytes)
            {
                TotalBytes += bytes;
                Progress?.Report(TotalBytes);
            }
        }

        private static async Task<OpResult> CopyAnyAsync(
            string srcPath, string dstPath, CopyContext ctx, CancellationToken ct)
        {
            if (Directory.Exists(srcPath))
                return await CopyDirectoryRecursiveAsync(srcPath, dstPath, ctx, ct).ConfigureAwait(false);
            if (File.Exists(srcPath))
                return await CopyOneFileAsync(srcPath, dstPath, ctx, ct).ConfigureAwait(false);
            return OpResult.Fail("Source does not exist");
        }

        private static async Task<OpResult> CopyOneFileAsync(
            string srcPath, string dstPath, CopyContext ctx, CancellationToken ct)
        {
            try
            {
                if (File.Exists(dstPath))
                {
                    if (!ctx.Overwrite)
                        return OpResult.Fail($"Destination exists: {Path.GetFileName(dstPath)}");
                    try { File.Delete(dstPath); }
                    catch (Exception ex)
                    {
                        return OpResult.Fail($"Couldn't replace {Path.GetFileName(dstPath)}: {ex.Message}");
                    }
                }

                await using var src = new FileStream(
                    srcPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    CopyBufferBytes, useAsync: true);
                await using var dst = new FileStream(
                    dstPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    CopyBufferBytes, useAsync: true);

                var buffer = new byte[CopyBufferBytes];
                int read;
                while ((read = await src.ReadAsync(buffer.AsMemory(0, CopyBufferBytes), ct)
                                       .ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    ctx.Add(read);
                }
                return OpResult.Ok();
            }
            catch (OperationCanceledException)
            {
                TryDeletePartial(dstPath);
                return OpResult.Cancelled();
            }
            catch (Exception ex)
            {
                TryDeletePartial(dstPath);
                return OpResult.Fail(ex.Message);
            }
        }

        private static async Task<OpResult> CopyDirectoryRecursiveAsync(
            string srcDir, string dstDir, CopyContext ctx, CancellationToken ct)
        {
            try
            {
                if (Directory.Exists(dstDir))
                {
                    if (!ctx.Overwrite)
                        return OpResult.Fail($"Destination folder exists: {Path.GetFileName(dstDir)}");
                    try { Directory.Delete(dstDir, recursive: true); }
                    catch (Exception ex)
                    {
                        return OpResult.Fail($"Couldn't replace {Path.GetFileName(dstDir)}: {ex.Message}");
                    }
                }
                Directory.CreateDirectory(dstDir);
            }
            catch (Exception ex)
            {
                return OpResult.Fail(ex.Message);
            }

            string[] files;
            string[] subDirs;
            try
            {
                files = Directory.GetFiles(srcDir);
                subDirs = Directory.GetDirectories(srcDir);
            }
            catch (Exception ex)
            {
                return OpResult.Fail($"Couldn't enumerate {srcDir}: {ex.Message}");
            }

            foreach (var srcFile in files)
            {
                if (ct.IsCancellationRequested) return OpResult.Cancelled();
                var dstFile = Path.Combine(dstDir, Path.GetFileName(srcFile));
                var r = await CopyOneFileAsync(srcFile, dstFile, ctx, ct).ConfigureAwait(false);
                if (!r.Success) return r;
            }

            foreach (var srcSubDir in subDirs)
            {
                if (ct.IsCancellationRequested) return OpResult.Cancelled();
                var dstSubDir = Path.Combine(dstDir, Path.GetFileName(srcSubDir));
                var r = await CopyDirectoryRecursiveAsync(srcSubDir, dstSubDir, ctx, ct).ConfigureAwait(false);
                if (!r.Success) return r;
            }

            return OpResult.Ok();
        }

        private static void TryDeletePartial(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.Warn("FileOps", $"Partial cleanup failed for {path}", ex);
            }
        }

        private static bool IsSameVolume(string a, string b)
        {
            try
            {
                var rootA = Path.GetPathRoot(Path.GetFullPath(a));
                var rootB = Path.GetPathRoot(Path.GetFullPath(b));
                return string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
