using Josha.Models;
using Josha.Services;
using System.IO;

namespace Josha.Business.Ftp
{
    // Single provider that fronts both FTP and SFTP — the protocol split lives in
    // the connection pool's client factory. Each call leases a client from the
    // per-site pool and returns it; faulted leases get disposed instead of recycled
    // so transient connection errors don't poison the next operation.
    //
    // ProviderId is the site Guid string, so cross-pane copy/move can detect
    // "same remote" (intra-site move = server RNFR/RNTO) vs. "different remote"
    // (cross-site copy = download-then-upload through ImportFromAsync).
    internal sealed class RemoteFileSystemProvider : IFileSystemProvider
    {
        private readonly FtpSite _site;

        public RemoteFileSystemProvider(FtpSite site)
        {
            _site = site;
        }

        public string ProviderId => "ftp:" + _site.Id.ToString("N");
        public bool IsRemote => true;
        public bool SupportsTreeGraph => false;

        public FtpSite Site => _site;

        public async Task<IReadOnlyList<FsEntry>> EnumerateAsync(string path, CancellationToken ct = default)
        {
            await using var lease = await RemoteConnectionPool.AcquireAsync(_site, ct).ConfigureAwait(false);
            try
            {
                var entries = await lease.Client.ListAsync(path, ct).ConfigureAwait(false);
                var basePath = NormalizeDir(path);
                var list = new List<FsEntry>(entries.Count);
                foreach (var e in entries)
                {
                    list.Add(new FsEntry
                    {
                        Name = e.Name,
                        FullPath = basePath + e.Name,
                        IsDirectory = e.IsDirectory,
                        Size = e.IsDirectory ? null : e.Size,
                        ModifiedUtc = e.ModifiedUtc,
                    });
                }
                return list;
            }
            catch
            {
                lease.Faulted = true;
                throw;
            }
        }

        public async Task<FileOpsComponent.OpResult> CopyAsync(
            string srcPath, string dstPath,
            IProgress<long>? bytesCopied = null,
            bool overwrite = false,
            CancellationToken ct = default)
        {
            await using var lease = await RemoteConnectionPool.AcquireAsync(_site, ct).ConfigureAwait(false);
            string? tempPath = null;
            try
            {
                if (!overwrite && await lease.Client.FileExistsAsync(dstPath, ct).ConfigureAwait(false))
                    return FileOpsComponent.OpResult.Fail($"Destination exists: {Path.GetFileName(dstPath)}");

                // Spool through a local temp file. FTP has no server-side copy,
                // so we have to download then upload — but doing it through a
                // MemoryStream OOMs on multi-GB files. Temp file is bounded by
                // disk, not RAM.
                tempPath = SpoolTempPath();
                await using (var spool = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    256 * 1024, useAsync: true))
                {
                    await lease.Client.DownloadAsync(srcPath, spool, null, ct).ConfigureAwait(false);
                }
                await using (var spool = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    256 * 1024, useAsync: true))
                {
                    await lease.Client.UploadAsync(spool, dstPath, overwrite, resume: false, bytesCopied, ct).ConfigureAwait(false);
                }
                return FileOpsComponent.OpResult.Ok();
            }
            catch (OperationCanceledException) { lease.Faulted = true; return FileOpsComponent.OpResult.Cancelled(); }
            catch (Exception ex) { lease.Faulted = true; return FileOpsComponent.OpResult.Fail(ex.Message); }
            finally
            {
                if (tempPath != null) { try { File.Delete(tempPath); } catch { } }
            }
        }

        public async Task<FileOpsComponent.OpResult> MoveAsync(
            string srcPath, string dstPath,
            IProgress<long>? bytesCopied = null,
            bool overwrite = false,
            CancellationToken ct = default)
        {
            await using var lease = await RemoteConnectionPool.AcquireAsync(_site, ct).ConfigureAwait(false);
            try
            {
                if (await lease.Client.FileExistsAsync(dstPath, ct).ConfigureAwait(false))
                {
                    if (!overwrite)
                        return FileOpsComponent.OpResult.Fail($"Destination exists: {Path.GetFileName(dstPath)}");
                    await lease.Client.DeleteFileAsync(dstPath, ct).ConfigureAwait(false);
                }
                await lease.Client.RenameAsync(srcPath, dstPath, ct).ConfigureAwait(false);
                return FileOpsComponent.OpResult.Ok();
            }
            // Cancellation mid-RNFR (between RNFR and RNTO on FTP) leaves the
            // control connection in an undefined state. Don't recycle this lease
            // back into the pool — next op would inherit the half-rename.
            catch (OperationCanceledException) { lease.Faulted = true; return FileOpsComponent.OpResult.Cancelled(); }
            catch (Exception ex) { lease.Faulted = true; return FileOpsComponent.OpResult.Fail(ex.Message); }
        }

        public async Task<FileOpsComponent.OpResult> RenameAsync(string srcPath, string newName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(newName))
                return FileOpsComponent.OpResult.Fail("Name cannot be empty");

            var parent = GetParent(srcPath);
            var dst = parent + newName;
            await using var lease = await RemoteConnectionPool.AcquireAsync(_site, ct).ConfigureAwait(false);
            try
            {
                await lease.Client.RenameAsync(srcPath, dst, ct).ConfigureAwait(false);
                return FileOpsComponent.OpResult.Ok();
            }
            catch (OperationCanceledException) { lease.Faulted = true; return FileOpsComponent.OpResult.Cancelled(); }
            catch (Exception ex) { lease.Faulted = true; return FileOpsComponent.OpResult.Fail(ex.Message); }
        }

        public async Task<FileOpsComponent.OpResult> CreateDirectoryAsync(string parentPath, string name, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name))
                return FileOpsComponent.OpResult.Fail("Folder name cannot be empty");
            var path = NormalizeDir(parentPath) + name;
            await using var lease = await RemoteConnectionPool.AcquireAsync(_site, ct).ConfigureAwait(false);
            try
            {
                await lease.Client.MkdirAsync(path, ct).ConfigureAwait(false);
                return FileOpsComponent.OpResult.Ok();
            }
            catch (OperationCanceledException) { lease.Faulted = true; return FileOpsComponent.OpResult.Cancelled(); }
            catch (Exception ex) { lease.Faulted = true; return FileOpsComponent.OpResult.Fail(ex.Message); }
        }

        public async Task<FileOpsComponent.OpResult> DeleteAsync(string path, bool toRecycle, CancellationToken ct = default)
        {
            // Remote servers don't have a recycle bin; toRecycle flag is ignored.
            _ = toRecycle;
            await using var lease = await RemoteConnectionPool.AcquireAsync(_site, ct).ConfigureAwait(false);
            try
            {
                if (await lease.Client.DirectoryExistsAsync(path, ct).ConfigureAwait(false))
                    await lease.Client.DeleteDirectoryAsync(path, recursive: true, ct).ConfigureAwait(false);
                else
                    await lease.Client.DeleteFileAsync(path, ct).ConfigureAwait(false);
                return FileOpsComponent.OpResult.Ok();
            }
            catch (OperationCanceledException) { lease.Faulted = true; return FileOpsComponent.OpResult.Cancelled(); }
            catch (Exception ex) { lease.Faulted = true; return FileOpsComponent.OpResult.Fail(ex.Message); }
        }

        // Spools the remote file to a temp path and returns a FileStream that
        // deletes the temp file on Close/Dispose. We can't keep the FTP lease
        // alive across the caller's read loop (it'd starve the per-site pool of
        // 2 connections), and we can't buffer to MemoryStream (OOM on large
        // files), so disk is the right answer.
        public async Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
        {
            var tempPath = SpoolTempPath();
            await using (var lease = await RemoteConnectionPool.AcquireAsync(_site, ct).ConfigureAwait(false))
            {
                try
                {
                    await using var spool = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                        256 * 1024, useAsync: true);
                    await lease.Client.DownloadAsync(path, spool, null, ct).ConfigureAwait(false);
                }
                catch
                {
                    lease.Faulted = true;
                    try { File.Delete(tempPath); } catch { }
                    throw;
                }
            }

            return new SelfDeletingFileStream(tempPath);
        }

        public Task<Stream> OpenWriteAsync(string path, bool overwrite, CancellationToken ct = default)
        {
            // Backed by a temp file (NOT a MemoryStream — would OOM on large
            // writes). The backing temp is uploaded on async dispose. Sync
            // Dispose blocks with a hard timeout so we don't deadlock the
            // dispatcher; for guaranteed completion callers MUST `await using`.
            return Task.FromResult<Stream>(new RemoteUploadStream(this, path, overwrite));
        }

        private static string SpoolTempPath()
        {
            var dir = Path.Combine(Path.GetTempPath(), "Josha", "spool");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tmp");
        }

        public async Task<FileOpsComponent.OpResult> ImportFromAsync(
            IFileSystemProvider src, string srcPath, string dstPath,
            IProgress<long>? bytesCopied,
            bool overwrite,
            CancellationToken ct)
        {
            // Same-site = server-side rename equivalent? No — import semantics is
            // copy, not move. Same-site copy still needs download+upload (FTP has
            // no SITE COPY in the standard).
            if (src is LocalFileSystemProvider)
                return await UploadFromLocalAsync(srcPath, dstPath, bytesCopied, overwrite, ct).ConfigureAwait(false);

            return await CrossProviderCopyAsync(src, srcPath, dstPath, bytesCopied, overwrite, ct).ConfigureAwait(false);
        }

        private async Task<FileOpsComponent.OpResult> UploadFromLocalAsync(
            string srcPath, string dstPath, IProgress<long>? progress, bool overwrite, CancellationToken ct)
        {
            if (Directory.Exists(srcPath))
                return await UploadDirectoryAsync(srcPath, dstPath, progress, overwrite, ct).ConfigureAwait(false);

            await using var lease = await RemoteConnectionPool.AcquireAsync(_site, ct).ConfigureAwait(false);
            try
            {
                if (!overwrite && await lease.Client.FileExistsAsync(dstPath, ct).ConfigureAwait(false))
                    return FileOpsComponent.OpResult.Fail($"Destination exists: {Path.GetFileName(dstPath)}");

                await using var fs = new FileStream(srcPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 256 * 1024, useAsync: true);
                await lease.Client.UploadAsync(fs, dstPath, overwrite, resume: false, progress, ct).ConfigureAwait(false);
                return FileOpsComponent.OpResult.Ok();
            }
            catch (OperationCanceledException) { return FileOpsComponent.OpResult.Cancelled(); }
            catch (Exception ex) { lease.Faulted = true; return FileOpsComponent.OpResult.Fail(ex.Message); }
        }

        private async Task<FileOpsComponent.OpResult> UploadDirectoryAsync(
            string srcDir, string dstDir, IProgress<long>? progress, bool overwrite, CancellationToken ct)
        {
            await using var lease = await RemoteConnectionPool.AcquireAsync(_site, ct).ConfigureAwait(false);
            try
            {
                if (!await lease.Client.DirectoryExistsAsync(dstDir, ct).ConfigureAwait(false))
                    await lease.Client.MkdirAsync(dstDir, ct).ConfigureAwait(false);

                long running = 0;
                var inner = progress == null ? null : new Progress<long>(b => progress.Report(running + b));

                foreach (var f in Directory.EnumerateFiles(srcDir))
                {
                    if (ct.IsCancellationRequested) return FileOpsComponent.OpResult.Cancelled();
                    var name = Path.GetFileName(f);
                    var remote = NormalizeDir(dstDir) + name;

                    await using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read,
                        bufferSize: 256 * 1024, useAsync: true);
                    await lease.Client.UploadAsync(fs, remote, overwrite, resume: false, inner, ct).ConfigureAwait(false);
                    running += fs.Length;
                    progress?.Report(running);
                }

                foreach (var d in Directory.EnumerateDirectories(srcDir))
                {
                    if (ct.IsCancellationRequested) return FileOpsComponent.OpResult.Cancelled();
                    var name = Path.GetFileName(d);
                    var remote = NormalizeDir(dstDir) + name;
                    var r = await UploadDirectoryAsync(d, remote, progress, overwrite, ct).ConfigureAwait(false);
                    if (!r.Success) return r;
                }
                return FileOpsComponent.OpResult.Ok();
            }
            catch (OperationCanceledException) { return FileOpsComponent.OpResult.Cancelled(); }
            catch (Exception ex) { lease.Faulted = true; return FileOpsComponent.OpResult.Fail(ex.Message); }
        }

        private async Task<FileOpsComponent.OpResult> CrossProviderCopyAsync(
            IFileSystemProvider src, string srcPath, string dstPath,
            IProgress<long>? progress, bool overwrite, CancellationToken ct)
        {
            try
            {
                await using var srcStream = await src.OpenReadAsync(srcPath, ct).ConfigureAwait(false);
                await using var lease = await RemoteConnectionPool.AcquireAsync(_site, ct).ConfigureAwait(false);
                try
                {
                    await lease.Client.UploadAsync(srcStream, dstPath, overwrite, resume: false, progress, ct).ConfigureAwait(false);
                    return FileOpsComponent.OpResult.Ok();
                }
                catch { lease.Faulted = true; throw; }
            }
            catch (OperationCanceledException) { return FileOpsComponent.OpResult.Cancelled(); }
            catch (Exception ex) { return FileOpsComponent.OpResult.Fail(ex.Message); }
        }

        private static string NormalizeDir(string path)
        {
            if (string.IsNullOrEmpty(path)) return "/";
            return path.EndsWith("/") ? path : path + "/";
        }

        private static string GetParent(string remotePath)
        {
            var i = remotePath.TrimEnd('/').LastIndexOf('/');
            if (i <= 0) return "/";
            return remotePath.Substring(0, i + 1);
        }

        // Spool-to-disk + upload-on-dispose. Backing file lives in %TEMP% so a
        // write of any size is bounded by disk, not RAM. On async dispose we
        // await the upload; on sync dispose we wait with a 30s timeout so a
        // wedged remote can't deadlock the dispatcher. Callers that need
        // guaranteed completion must `await using` the stream.
        private sealed class RemoteUploadStream : Stream
        {
            private readonly RemoteFileSystemProvider _provider;
            private readonly string _path;
            private readonly bool _overwrite;
            private readonly string _spoolPath;
            private readonly FileStream _spool;
            private bool _disposed;

            public RemoteUploadStream(RemoteFileSystemProvider p, string path, bool overwrite)
            {
                _provider = p; _path = path; _overwrite = overwrite;
                _spoolPath = SpoolTempPath();
                _spool = new FileStream(_spoolPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    256 * 1024, useAsync: true);
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => _spool.Length;
            public override long Position { get => _spool.Position; set => throw new NotSupportedException(); }
            public override void Flush() => _spool.Flush();
            public override Task FlushAsync(CancellationToken ct) => _spool.FlushAsync(ct);
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => _spool.Write(buffer, offset, count);
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
                => _spool.WriteAsync(buffer, offset, count, ct);
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
                => _spool.WriteAsync(buffer, ct);

            protected override void Dispose(bool disposing)
            {
                if (_disposed || !disposing) { base.Dispose(disposing); return; }
                _disposed = true;

                // Sync path: schedule the upload and wait with a hard cap so
                // the dispatcher can never wedge. The Task.Run shifts the await
                // chain off this thread so .Wait can't deadlock against a
                // sync-context-bound continuation.
                try
                {
                    var upload = Task.Run(() => UploadSpoolAsync().AsTask());
                    if (!upload.Wait(TimeSpan.FromSeconds(30)))
                        Log.Warn("Ftp", $"Upload timeout on sync Dispose for {_path}");
                }
                catch (Exception ex)
                {
                    Log.Error("Ftp", $"Sync-dispose upload failed for {_path}", ex);
                }
                base.Dispose(disposing);
            }

            public override async ValueTask DisposeAsync()
            {
                if (_disposed) { await base.DisposeAsync().ConfigureAwait(false); return; }
                _disposed = true;
                await UploadSpoolAsync().ConfigureAwait(false);
                await base.DisposeAsync().ConfigureAwait(false);
            }

            private async ValueTask UploadSpoolAsync()
            {
                try
                {
                    await _spool.DisposeAsync().ConfigureAwait(false);

                    var len = new FileInfo(_spoolPath).Length;
                    if (len == 0) return;

                    await using var src = new FileStream(_spoolPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                        256 * 1024, useAsync: true);
                    await using var lease = await RemoteConnectionPool.AcquireAsync(_provider._site, default).ConfigureAwait(false);
                    try
                    {
                        await lease.Client.UploadAsync(src, _path, _overwrite, resume: false, null, default).ConfigureAwait(false);
                    }
                    catch { lease.Faulted = true; throw; }
                }
                catch (Exception ex)
                {
                    Log.Error("Ftp", $"Spool upload failed for {_path}", ex);
                }
                finally
                {
                    try { File.Delete(_spoolPath); } catch { }
                }
            }
        }

        // Wraps a FileStream and deletes the underlying file on close. Used by
        // OpenReadAsync so callers can read remote content as if it were local
        // and the spool cleans itself up the moment they Dispose.
        private sealed class SelfDeletingFileStream : FileStream
        {
            private readonly string _path;
            private bool _deleted;

            public SelfDeletingFileStream(string path)
                : base(path, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024, useAsync: true)
            {
                _path = path;
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing && !_deleted)
                {
                    _deleted = true;
                    try { File.Delete(_path); } catch { }
                }
            }
        }
    }
}
