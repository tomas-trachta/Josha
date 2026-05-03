using Josha.Models;
using Josha.Services;
using Renci.SshNet;
using Renci.SshNet.Common;
using System.IO;
using System.Security.Cryptography;

namespace Josha.Business.Ftp
{
    // SSH.NET-backed implementation of IRemoteClient.
    //
    // SSH host-key validation mirrors FTP TLS validation:
    //   Strict           — host key must match a previously pinned fingerprint.
    //                      First connect with no pinned key fails (no TOFU on Strict).
    //   AcceptOnFirstUse — first connect captures fingerprint; later connects
    //                      must match.
    //   AcceptAny        — accept every host key (warns into log only).
    //
    // Fingerprint = SHA-256 of the server's host-key blob (matches
    // ssh-keygen -l -E sha256).
    internal sealed class SftpClientComponent : IRemoteClient
    {
        private readonly FtpSite _site;
        private readonly Action<string>? _fingerprintPinned;
        private SftpClient? _client;

        public SftpClientComponent(FtpSite site, Action<string>? fingerprintPinned = null)
        {
            _site = site;
            _fingerprintPinned = fingerprintPinned;
        }

        public bool IsConnected => _client?.IsConnected ?? false;

        public Task ConnectAsync(CancellationToken ct)
        {
            return Task.Run(() =>
            {
                if (_client?.IsConnected == true) return;

                var info = new ConnectionInfo(_site.Host, _site.Port, _site.Username,
                    new PasswordAuthenticationMethod(_site.Username, _site.Password))
                {
                    Timeout = TimeSpan.FromSeconds(15),
                };

                _client = new SftpClient(info);
                _client.HostKeyReceived += OnHostKeyReceived;
                _client.OperationTimeout = TimeSpan.FromSeconds(30);

                try
                {
                    _client.Connect();
                }
                catch (Exception ex)
                {
                    Log.Error("Sftp", $"Connect failed for {_site.Username}@{_site.Host}:{_site.Port}", ex);
                    _client.HostKeyReceived -= OnHostKeyReceived;
                    _client.Dispose();
                    _client = null;
                    throw;
                }

                if (!string.IsNullOrEmpty(_site.StartDirectory) && _site.StartDirectory != "/")
                {
                    try { _client.ChangeDirectory(_site.StartDirectory); }
                    catch (Exception ex) { Log.Warn("Sftp", $"Failed to chdir to {_site.StartDirectory}", ex); }
                }
            }, ct);
        }

        public Task DisconnectAsync()
        {
            return Task.Run(() =>
            {
                if (_client == null) return;
                try { if (_client.IsConnected) _client.Disconnect(); }
                catch (Exception ex) { Log.Warn("Sftp", "Disconnect threw", ex); }
                finally
                {
                    _client.HostKeyReceived -= OnHostKeyReceived;
                    _client.Dispose();
                    _client = null;
                }
            });
        }

        public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);

        public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken ct)
        {
            EnsureClient();
            var result = new List<RemoteEntry>();
            await foreach (var f in _client!.ListDirectoryAsync(path, ct).ConfigureAwait(false))
            {
                if (f.Name == "." || f.Name == "..") continue;
                result.Add(new RemoteEntry
                {
                    Name = f.Name,
                    Size = f.IsDirectory ? 0 : f.Length,
                    ModifiedUtc = f.LastWriteTimeUtc,
                    IsDirectory = f.IsDirectory,
                    IsSymlink = f.IsSymbolicLink,
                    Owner = f.UserId.ToString(),
                    Group = f.GroupId.ToString(),
                });
            }
            return result;
        }

        public Task UploadAsync(
            Stream src, string remotePath, bool overwrite, bool resume,
            IProgress<long>? bytesTransferred, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                EnsureClient();
                if (resume && _client!.Exists(remotePath))
                {
                    var existingLen = (long)_client.GetAttributes(remotePath).Size;
                    if (existingLen > 0 && src.CanSeek)
                    {
                        src.Seek(existingLen, SeekOrigin.Begin);
                        using var append = _client.Open(remotePath, FileMode.Append, FileAccess.Write);
                        CopyWithProgress(src, append, bytesTransferred, existingLen, ct);
                        return;
                    }
                }

                Action<ulong>? progressFn = bytesTransferred == null
                    ? null
                    : (b => bytesTransferred.Report((long)b));

                // canOverride must be true when resume is set: if the remote
                // file doesn't exist yet (or has length 0), we fall through
                // here and need permission to create/replace it. Without this,
                // resume + missing target throws PermissionDenied.
                var canOverride = overwrite || resume;
                _client!.UploadFile(src, remotePath, canOverride: canOverride, uploadCallback: progressFn);
            }, ct);
        }

        public Task DownloadAsync(
            string remotePath, Stream dst, IProgress<long>? bytesTransferred, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                EnsureClient();
                Action<ulong>? progressFn = bytesTransferred == null
                    ? null
                    : (b => bytesTransferred.Report((long)b));
                _client!.DownloadFile(remotePath, dst, progressFn);
            }, ct);
        }

        public Task MkdirAsync(string remotePath, CancellationToken ct)
            => Task.Run(() => { EnsureClient(); _client!.CreateDirectory(remotePath); }, ct);

        public Task DeleteFileAsync(string remotePath, CancellationToken ct)
            => Task.Run(() => { EnsureClient(); _client!.DeleteFile(remotePath); }, ct);

        public Task DeleteDirectoryAsync(string remotePath, bool recursive, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                EnsureClient();
                if (!recursive)
                {
                    _client!.DeleteDirectory(remotePath);
                    return;
                }
                DeleteDirectoryRecursive(remotePath, ct);
            }, ct);
        }

        public Task RenameAsync(string srcPath, string dstPath, CancellationToken ct)
            => Task.Run(() => { EnsureClient(); _client!.RenameFile(srcPath, dstPath, true); }, ct);

        public Task<bool> FileExistsAsync(string remotePath, CancellationToken ct)
            => Task.Run(() =>
            {
                EnsureClient();
                if (!_client!.Exists(remotePath)) return false;
                return !_client.GetAttributes(remotePath).IsDirectory;
            }, ct);

        public Task<bool> DirectoryExistsAsync(string remotePath, CancellationToken ct)
            => Task.Run(() =>
            {
                EnsureClient();
                if (!_client!.Exists(remotePath)) return false;
                return _client.GetAttributes(remotePath).IsDirectory;
            }, ct);

        public Task<long> GetFileSizeAsync(string remotePath, CancellationToken ct)
            => Task.Run(() =>
            {
                EnsureClient();
                return (long)_client!.GetAttributes(remotePath).Size;
            }, ct);

        private void DeleteDirectoryRecursive(string path, CancellationToken ct)
        {
            foreach (var entry in _client!.ListDirectory(path))
            {
                if (ct.IsCancellationRequested) return;
                if (entry.Name == "." || entry.Name == "..") continue;
                if (entry.IsDirectory) DeleteDirectoryRecursive(entry.FullName, ct);
                else _client.DeleteFile(entry.FullName);
            }
            _client.DeleteDirectory(path);
        }

        private static void CopyWithProgress(Stream src, Stream dst, IProgress<long>? progress, long startingTotal, CancellationToken ct)
        {
            var buf = new byte[256 * 1024];
            int read;
            long total = startingTotal;
            while ((read = src.Read(buf, 0, buf.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                dst.Write(buf, 0, read);
                total += read;
                progress?.Report(total);
            }
        }

        private void EnsureClient()
        {
            if (_client == null || !_client.IsConnected)
                throw new InvalidOperationException("SFTP client not connected");
        }

        private void OnHostKeyReceived(object? sender, HostKeyEventArgs e)
        {
            var fp = Convert.ToHexString(SHA256.HashData(e.HostKey));

            if (_site.TlsValidation == TlsValidation.AcceptAny)
            {
                e.CanTrust = true;
                return;
            }

            if (string.IsNullOrEmpty(_site.PinnedFingerprint))
            {
                if (_site.TlsValidation == TlsValidation.AcceptOnFirstUse)
                {
                    _site.PinnedFingerprint = fp;
                    _fingerprintPinned?.Invoke(fp);
                    Log.Info("Sftp", $"Pinned new host key for {_site.Host}: {fp}");
                    e.CanTrust = true;
                    return;
                }
                Log.Warn("Sftp", $"Strict mode: no pinned host key for {_site.Host}; rejecting");
                e.CanTrust = false;
                return;
            }

            if (string.Equals(_site.PinnedFingerprint, fp, StringComparison.OrdinalIgnoreCase))
            {
                e.CanTrust = true;
                return;
            }

            Log.Warn("Sftp", $"Host key mismatch for {_site.Host}; rejecting (expected {_site.PinnedFingerprint}, got {fp})");
            e.CanTrust = false;
        }
    }
}
