using System.IO;

namespace Josha.Business
{
    // Adapter: routes IFileSystemProvider calls to the existing static
    // FileOpsComponent so Phase-1 local code paths stay byte-for-byte
    // identical while remote providers slot in alongside.
    internal sealed class LocalFileSystemProvider : IFileSystemProvider
    {
        public static readonly LocalFileSystemProvider Instance = new();

        private LocalFileSystemProvider() { }

        public string ProviderId => "local";
        public bool IsRemote => false;
        public bool SupportsTreeGraph => true;

        public Task<IReadOnlyList<FsEntry>> EnumerateAsync(string path, CancellationToken ct = default)
        {
            return Task.Run<IReadOnlyList<FsEntry>>(() =>
            {
                var list = new List<FsEntry>();
                if (!Directory.Exists(path)) return list;

                var di = new DirectoryInfo(path);
                foreach (var d in di.EnumerateDirectories())
                {
                    if (ct.IsCancellationRequested) break;
                    DateTime? mtime = null;
                    try { mtime = d.LastWriteTimeUtc; } catch { }
                    list.Add(new FsEntry
                    {
                        Name = d.Name,
                        FullPath = d.FullName,
                        IsDirectory = true,
                        ModifiedUtc = mtime,
                    });
                }
                foreach (var f in di.EnumerateFiles())
                {
                    if (ct.IsCancellationRequested) break;
                    long? size = null;
                    DateTime? mtime = null;
                    try { size = f.Length; } catch { }
                    try { mtime = f.LastWriteTimeUtc; } catch { }
                    list.Add(new FsEntry
                    {
                        Name = f.Name,
                        FullPath = f.FullName,
                        IsDirectory = false,
                        Size = size,
                        ModifiedUtc = mtime,
                    });
                }
                return list;
            }, ct);
        }

        public Task<FileOpsComponent.OpResult> CopyAsync(
            string srcPath, string dstPath,
            IProgress<long>? bytesCopied = null,
            bool overwrite = false,
            CancellationToken ct = default)
            => FileOpsComponent.CopyAsync(srcPath, dstPath, bytesCopied, overwrite, ct);

        public Task<FileOpsComponent.OpResult> MoveAsync(
            string srcPath, string dstPath,
            IProgress<long>? bytesCopied = null,
            bool overwrite = false,
            CancellationToken ct = default)
            => FileOpsComponent.MoveAsync(srcPath, dstPath, bytesCopied, overwrite, ct);

        public Task<FileOpsComponent.OpResult> RenameAsync(string srcPath, string newName, CancellationToken ct = default)
            => Task.FromResult(FileOpsComponent.Rename(srcPath, newName));

        public Task<FileOpsComponent.OpResult> CreateDirectoryAsync(string parentPath, string name, CancellationToken ct = default)
            => Task.FromResult(FileOpsComponent.CreateDirectory(parentPath, name));

        public Task<FileOpsComponent.OpResult> DeleteAsync(string path, bool toRecycle, CancellationToken ct = default)
            => Task.FromResult(toRecycle ? FileOpsComponent.DeleteToRecycle(path) : FileOpsComponent.DeletePermanent(path));

        public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
        {
            Stream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, useAsync: true);
            return Task.FromResult(s);
        }

        public Task<Stream> OpenWriteAsync(string path, bool overwrite, CancellationToken ct = default)
        {
            var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
            Stream s = new FileStream(path, mode, FileAccess.Write, FileShare.None,
                bufferSize: 64 * 1024, useAsync: true);
            return Task.FromResult(s);
        }

        // Local→local import = ordinary CopyAsync. Remote providers will override.
        public Task<FileOpsComponent.OpResult> ImportFromAsync(
            IFileSystemProvider src, string srcPath, string dstPath,
            IProgress<long>? bytesCopied,
            bool overwrite,
            CancellationToken ct)
        {
            if (src is LocalFileSystemProvider)
                return CopyAsync(srcPath, dstPath, bytesCopied, overwrite, ct);

            return CrossProviderCopyAsync(src, srcPath, dstPath, bytesCopied, overwrite, ct);
        }

        private async Task<FileOpsComponent.OpResult> CrossProviderCopyAsync(
            IFileSystemProvider src, string srcPath, string dstPath,
            IProgress<long>? bytesCopied,
            bool overwrite,
            CancellationToken ct)
        {
            try
            {
                if (File.Exists(dstPath) && !overwrite)
                    return FileOpsComponent.OpResult.Fail($"Destination exists: {Path.GetFileName(dstPath)}");

                await using var srcStream = await src.OpenReadAsync(srcPath, ct).ConfigureAwait(false);
                await using var dstStream = await OpenWriteAsync(dstPath, overwrite, ct).ConfigureAwait(false);

                var buffer = new byte[1024 * 1024];
                long total = 0;
                int read;
                while ((read = await srcStream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
                {
                    await dstStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    total += read;
                    bytesCopied?.Report(total);
                }
                return FileOpsComponent.OpResult.Ok();
            }
            catch (OperationCanceledException) { return FileOpsComponent.OpResult.Cancelled(); }
            catch (Exception ex) { return FileOpsComponent.OpResult.Fail(ex.Message); }
        }
    }
}
