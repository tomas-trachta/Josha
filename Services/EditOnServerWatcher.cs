using Josha.Business;
using Josha.Business.Ftp;
using Josha.Models;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace Josha.Services
{
    // F4-on-remote workflow:
    //
    //   1. Download the remote file to %TEMP%\Josha\edits\<guid>\<name>
    //   2. Launch the user's default editor on the temp file
    //   3. Watch the temp directory (NOT the file — VS Code, Vim, Notepad++ all
    //      atomic-save by writing a sibling .tmp then renaming over the target,
    //      which a per-file FileSystemWatcher would miss). Listen for both
    //      `Changed` and `Renamed→target` events.
    //   4. Debounce 400 ms — editors emit multiple events per save.
    //   5. Hash the file before each upload, skip if hash matches the previous
    //      upload (guards against editor-on-load Changed bursts and identical-
    //      content saves).
    //   6. Upload via the same RemoteConnectionPool as the rest of the app.
    internal sealed class EditOnServerWatcher : IDisposable
    {
        private static readonly object _registryLock = new();
        private static readonly List<EditOnServerWatcher> _active = new();

        private readonly FtpSite _site;
        private readonly string _remotePath;
        private readonly string _tempDir;
        private readonly string _tempFile;
        private readonly FileSystemWatcher _watcher;
        private readonly SemaphoreSlim _uploadGate = new(1, 1);
        private string? _lastUploadedHash;
        private CancellationTokenSource? _debounceCts;
        private Process? _editorProcess;
        private volatile bool _disposed;

        private EditOnServerWatcher(FtpSite site, string remotePath, string tempDir, string tempFile)
        {
            _site = site;
            _remotePath = remotePath;
            _tempDir = tempDir;
            _tempFile = tempFile;
            _watcher = new FileSystemWatcher(tempDir)
            {
                NotifyFilter = NotifyFilters.LastWrite
                             | NotifyFilters.FileName
                             | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnTempChanged;
            _watcher.Renamed += OnTempRenamed;
        }

        public static async Task<EditOnServerWatcher?> StartAsync(FtpSite site, string remotePath, CancellationToken ct = default)
        {
            try
            {
                var name = Path.GetFileName(remotePath.TrimEnd('/'));
                if (string.IsNullOrEmpty(name)) name = "remote.bin";

                var tempDir = Path.Combine(Path.GetTempPath(), "Josha", "edits", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                var tempFile = Path.Combine(tempDir, name);

                await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None,
                    256 * 1024, useAsync: true))
                {
                    await using var lease = await RemoteConnectionPool.AcquireAsync(site, ct).ConfigureAwait(false);
                    try { await lease.Client.DownloadAsync(remotePath, fs, null, ct).ConfigureAwait(false); }
                    catch { lease.Faulted = true; throw; }
                }

                var watcher = new EditOnServerWatcher(site, remotePath, tempDir, tempFile);
                watcher._lastUploadedHash = HashFile(tempFile);

                lock (_registryLock) _active.Add(watcher);

                try
                {
                    watcher._editorProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = tempFile,
                        WorkingDirectory = tempDir,
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    Log.Warn("EditOnServer", $"Could not launch editor for {tempFile}", ex);
                }

                Log.Info("EditOnServer", $"Started watch on {remotePath} → {tempFile}");
                return watcher;
            }
            catch (Exception ex)
            {
                Log.Error("EditOnServer", $"Failed to start watch on {remotePath}", ex);
                return null;
            }
        }

        public static async Task DisposeAllAsync()
        {
            List<EditOnServerWatcher> snap;
            lock (_registryLock) { snap = _active.ToList(); _active.Clear(); }
            foreach (var w in snap) { try { w.Dispose(); } catch { } }
            await Task.CompletedTask;
        }

        private void OnTempChanged(object sender, FileSystemEventArgs e)
        {
            if (_disposed) return;
            if (!string.Equals(e.FullPath, _tempFile, StringComparison.OrdinalIgnoreCase)) return;
            ScheduleUpload();
        }

        private void OnTempRenamed(object sender, RenamedEventArgs e)
        {
            if (_disposed) return;
            if (!string.Equals(e.FullPath, _tempFile, StringComparison.OrdinalIgnoreCase)) return;
            ScheduleUpload();
        }

        private void ScheduleUpload()
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;
            _ = Task.Delay(TimeSpan.FromMilliseconds(400), token).ContinueWith(async t =>
            {
                if (t.IsCanceled) return;
                await TryUploadAsync().ConfigureAwait(false);
            }, TaskScheduler.Default);
        }

        // Serialized by _uploadGate so two saves in flight (the user mashing
        // Ctrl+S, or VSCode emitting Changed twice within the debounce window)
        // can't interleave UploadAsync calls or corrupt _lastUploadedHash.
        private async Task TryUploadAsync()
        {
            if (_disposed) return;

            await _uploadGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed) return;

                string newHash;
                try
                {
                    newHash = HashFile(_tempFile);
                }
                catch (IOException) { return; }
                catch (Exception ex)
                {
                    Log.Warn("EditOnServer", $"Hash failed for {_tempFile}", ex);
                    return;
                }

                if (newHash == _lastUploadedHash) return;

                try
                {
                    await using var fs = new FileStream(_tempFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                        256 * 1024, useAsync: true);
                    await using var lease = await RemoteConnectionPool.AcquireAsync(_site, default).ConfigureAwait(false);
                    try
                    {
                        await lease.Client.UploadAsync(fs, _remotePath, overwrite: true, resume: false, null, default).ConfigureAwait(false);
                    }
                    catch { lease.Faulted = true; throw; }

                    _lastUploadedHash = newHash;
                    Log.Info("EditOnServer", $"Uploaded edited {_remotePath}");
                    AppServices.Toast.Success($"Saved '{Path.GetFileName(_remotePath)}' to server");
                }
                catch (Exception ex)
                {
                    Log.Error("EditOnServer", $"Upload-on-save failed for {_remotePath}", ex);
                    AppServices.Toast.Error($"Upload failed: {ex.Message}");
                }
            }
            finally
            {
                _uploadGate.Release();
            }
        }

        private static string HashFile(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Convert.ToHexString(SHA256.HashData(fs));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnTempChanged;
            _watcher.Renamed -= OnTempRenamed;
            _watcher.Dispose();
            _debounceCts?.Cancel();

            // Wait briefly for any in-flight upload to finish before tearing
            // down — otherwise app exit can race a save the user just hit.
            try { _uploadGate.Wait(TimeSpan.FromSeconds(2)); _uploadGate.Release(); }
            catch { }

            // Editor may still have the file open. Deleting it now corrupts
            // VSCode/Notepad++/Vim's view of the file (subsequent saves write
            // to a deleted path → silent data loss). Leak the temp dir if so;
            // OS will eventually reclaim %TEMP%. Otherwise clean up.
            bool editorAlive = false;
            try { editorAlive = _editorProcess != null && !_editorProcess.HasExited; } catch { }

            if (!editorAlive)
            {
                try { File.Delete(_tempFile); } catch { }
                try { Directory.Delete(_tempDir, recursive: true); } catch { }
            }
            else
            {
                Log.Info("EditOnServer",
                    $"Editor still open for {_tempFile}; leaving temp dir in place");
            }

            try { _editorProcess?.Dispose(); } catch { }
            _uploadGate.Dispose();

            lock (_registryLock) _active.Remove(this);
        }
    }
}
