using Josha.Models;
using Josha.Services;
using FluentFTP;
using FluentFTP.Client.BaseClient;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Josha.Business.Ftp
{
    // FluentFTP-backed implementation of IRemoteClient. Handles plain FTP plus
    // FTPS (explicit AUTH TLS and implicit on port 990).
    //
    // TLS validation modes (mirrors FtpSite.TlsValidation):
    //   Strict           — let the OS chain validation decide; reject anything invalid.
    //   AcceptOnFirstUse — pin the SHA-256 cert fingerprint on first valid-or-not
    //                      connect; subsequent connects must match.
    //   AcceptAny        — accept every certificate (warns into log only).
    //
    // Fingerprint pinning persists back into FtpSite via the optional
    // FingerprintPinned callback so SiteManagerComponent can save it.
    internal sealed class FtpClientComponent : IRemoteClient
    {
        private readonly FtpSite _site;
        private readonly Action<string>? _fingerprintPinned;
        private AsyncFtpClient? _client;

        public FtpClientComponent(FtpSite site, Action<string>? fingerprintPinned = null)
        {
            _site = site;
            _fingerprintPinned = fingerprintPinned;
        }

        public bool IsConnected => _client?.IsConnected ?? false;

        public async Task ConnectAsync(CancellationToken ct)
        {
            if (_client?.IsConnected == true) return;

            var cfg = new FtpConfig
            {
                DataConnectionType = _site.Mode == FtpMode.Active
                    ? FtpDataConnectionType.AutoActive
                    : FtpDataConnectionType.AutoPassive,
                ConnectTimeout = 15000,
                ReadTimeout = 30000,
                DataConnectionConnectTimeout = 15000,
                DataConnectionReadTimeout = 30000,
                RetryAttempts = 0,
                // Keep the control connection from going idle between LISTs so
                // each directory click doesn't pay a TCP-reconnect penalty.
                SocketKeepAlive = true,
            };

            switch (_site.Protocol)
            {
                case FtpProtocol.Ftp:
                    cfg.EncryptionMode = FtpEncryptionMode.None;
                    break;
                case FtpProtocol.FtpsExplicit:
                    cfg.EncryptionMode = FtpEncryptionMode.Explicit;
                    cfg.ValidateAnyCertificate = false;
                    // Reuse the TLS session across data-channel handshakes.
                    // Without this, every LIST/STOR/RETR pays a full TLS round
                    // trip on top of the TCP one — the dominant cost of
                    // browsing FTPS dirs over latent links.
                    cfg.SslSessionLength = 0;
                    break;
                case FtpProtocol.FtpsImplicit:
                    cfg.EncryptionMode = FtpEncryptionMode.Implicit;
                    cfg.ValidateAnyCertificate = false;
                    cfg.SslSessionLength = 0;
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported FTP protocol: {_site.Protocol}");
            }

            _client = new AsyncFtpClient(_site.Host, _site.Username, _site.Password, _site.Port, cfg);
            try { _client.Encoding = Encoding.GetEncoding(_site.Encoding); }
            catch { _client.Encoding = Encoding.UTF8; }
            _client.ValidateCertificate += OnValidateCertificate;

            try
            {
                await _client.Connect(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error("Ftp", $"Connect failed for {_site.Username}@{_site.Host}:{_site.Port}", ex);
                _client.ValidateCertificate -= OnValidateCertificate;
                _client.Dispose();
                _client = null;
                throw;
            }

            if (!string.IsNullOrEmpty(_site.StartDirectory) && _site.StartDirectory != "/")
            {
                try { await _client.SetWorkingDirectory(_site.StartDirectory, ct).ConfigureAwait(false); }
                catch (Exception ex) { Log.Warn("Ftp", $"Failed to chdir to {_site.StartDirectory}", ex); }
            }
        }

        public async Task DisconnectAsync()
        {
            if (_client == null) return;
            try
            {
                if (_client.IsConnected) await _client.Disconnect().ConfigureAwait(false);
            }
            catch (Exception ex) { Log.Warn("Ftp", "Disconnect threw", ex); }
            finally
            {
                _client.ValidateCertificate -= OnValidateCertificate;
                _client.Dispose();
                _client = null;
            }
        }

        public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);

        public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken ct)
        {
            EnsureClient();
            var items = await _client!.GetListing(path, ct).ConfigureAwait(false);
            var result = new List<RemoteEntry>(items.Length);
            foreach (var i in items)
            {
                if (i.Name == "." || i.Name == "..") continue;
                result.Add(new RemoteEntry
                {
                    Name = i.Name,
                    Size = i.Size,
                    ModifiedUtc = i.Modified == DateTime.MinValue
                        ? null
                        : DateTime.SpecifyKind(i.Modified, DateTimeKind.Utc),
                    IsDirectory = i.Type == FtpObjectType.Directory,
                    IsSymlink = i.Type == FtpObjectType.Link,
                    LinkTarget = i.LinkTarget,
                    RawPermissions = i.RawPermissions,
                    Owner = i.RawOwner,
                    Group = i.RawGroup,
                });
            }
            return result;
        }

        public async Task UploadAsync(
            Stream src, string remotePath, bool overwrite, bool resume,
            IProgress<long>? bytesTransferred, CancellationToken ct)
        {
            EnsureClient();
            var existsBehaviour = resume
                ? FtpRemoteExists.Resume
                : (overwrite ? FtpRemoteExists.Overwrite : FtpRemoteExists.Skip);

            IProgress<FtpProgress>? wrap = bytesTransferred == null ? null
                : new Progress<FtpProgress>(p => bytesTransferred.Report(p.TransferredBytes));

            var status = await _client!.UploadStream(
                src, remotePath, existsBehaviour, createRemoteDir: true, progress: wrap, token: ct)
                .ConfigureAwait(false);

            if (status == FtpStatus.Failed)
                throw new IOException($"Upload failed: {Path.GetFileName(remotePath)}");
        }

        public async Task DownloadAsync(
            string remotePath, Stream dst, IProgress<long>? bytesTransferred, CancellationToken ct)
        {
            EnsureClient();
            IProgress<FtpProgress>? wrap = bytesTransferred == null ? null
                : new Progress<FtpProgress>(p => bytesTransferred.Report(p.TransferredBytes));
            var ok = await _client!.DownloadStream(dst, remotePath, progress: wrap, token: ct).ConfigureAwait(false);
            if (!ok) throw new IOException($"Download failed: {Path.GetFileName(remotePath)}");
        }

        public async Task MkdirAsync(string remotePath, CancellationToken ct)
        {
            EnsureClient();
            await _client!.CreateDirectory(remotePath, force: true, ct).ConfigureAwait(false);
        }

        public async Task DeleteFileAsync(string remotePath, CancellationToken ct)
        {
            EnsureClient();
            await _client!.DeleteFile(remotePath, ct).ConfigureAwait(false);
        }

        public async Task DeleteDirectoryAsync(string remotePath, bool recursive, CancellationToken ct)
        {
            EnsureClient();
            // FluentFTP's DeleteDirectory walks the tree server-side regardless of
            // option; the `recursive` flag is preserved on the interface for
            // SFTP parity but doesn't change behaviour here.
            _ = recursive;
            await _client!.DeleteDirectory(remotePath, ct).ConfigureAwait(false);
        }

        public async Task RenameAsync(string srcPath, string dstPath, CancellationToken ct)
        {
            EnsureClient();
            await _client!.Rename(srcPath, dstPath, ct).ConfigureAwait(false);
        }

        public async Task<bool> FileExistsAsync(string remotePath, CancellationToken ct)
        {
            EnsureClient();
            return await _client!.FileExists(remotePath, ct).ConfigureAwait(false);
        }

        public async Task<bool> DirectoryExistsAsync(string remotePath, CancellationToken ct)
        {
            EnsureClient();
            return await _client!.DirectoryExists(remotePath, ct).ConfigureAwait(false);
        }

        public async Task<long> GetFileSizeAsync(string remotePath, CancellationToken ct)
        {
            EnsureClient();
            return await _client!.GetFileSize(remotePath, -1, ct).ConfigureAwait(false);
        }

        private void EnsureClient()
        {
            if (_client == null || !_client.IsConnected)
                throw new InvalidOperationException("FTP client not connected");
        }

        private void OnValidateCertificate(BaseFtpClient control, FtpSslValidationEventArgs e)
        {
            if (_site.TlsValidation == TlsValidation.AcceptAny)
            {
                if (e.PolicyErrors != System.Net.Security.SslPolicyErrors.None)
                    Log.Warn("Ftp", $"AcceptAny: ignoring TLS errors {e.PolicyErrors} for {_site.Host}");
                e.Accept = true;
                return;
            }

            var fp = ComputeFingerprint(e.Certificate);

            if (_site.TlsValidation == TlsValidation.AcceptOnFirstUse)
            {
                if (string.IsNullOrEmpty(_site.PinnedFingerprint))
                {
                    _site.PinnedFingerprint = fp;
                    _fingerprintPinned?.Invoke(fp);
                    Log.Info("Ftp", $"Pinned new fingerprint for {_site.Host}: {fp}");
                    e.Accept = true;
                    return;
                }
                if (string.Equals(_site.PinnedFingerprint, fp, StringComparison.OrdinalIgnoreCase))
                {
                    e.Accept = true;
                    return;
                }
                Log.Warn("Ftp", $"Fingerprint mismatch for {_site.Host}; rejecting (expected {_site.PinnedFingerprint}, got {fp})");
                e.Accept = false;
                return;
            }

            // Strict: chain must be clean OR pinned fingerprint must match.
            if (e.PolicyErrors == System.Net.Security.SslPolicyErrors.None)
            {
                // Refresh the pinned fingerprint to the new clean-chain cert so
                // legitimate cert rotation doesn't get rejected later if the
                // user toggles validation modes. Persist via the same callback
                // the AcceptOnFirstUse path uses.
                if (!string.Equals(_site.PinnedFingerprint, fp, StringComparison.OrdinalIgnoreCase))
                {
                    _site.PinnedFingerprint = fp;
                    _fingerprintPinned?.Invoke(fp);
                }
                e.Accept = true;
                return;
            }
            if (!string.IsNullOrEmpty(_site.PinnedFingerprint) &&
                string.Equals(_site.PinnedFingerprint, fp, StringComparison.OrdinalIgnoreCase))
            {
                e.Accept = true;
                return;
            }
            Log.Warn("Ftp", $"Strict TLS reject for {_site.Host}: {e.PolicyErrors}");
            e.Accept = false;
        }

        private static string ComputeFingerprint(X509Certificate cert)
        {
            var bytes = cert.GetRawCertData();
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }
    }
}
