using Josha.Business;
using Josha.Models;
using System.Collections.Concurrent;

namespace Josha.Services
{
    internal sealed class SnapshotService
    {
        private readonly ConcurrentDictionary<string, DirOD> _roots =
            new(StringComparer.OrdinalIgnoreCase);

        private CancellationTokenSource? _saveCts;
        private readonly SemaphoreSlim _saveSem = new(1, 1);

        public SnapshotService()
        {
            SnapshotComponent.SnapshotChanged += OnSnapshotChanged;
        }

        public DirOD? GetRoot(string driveLetter)
        {
            var key = NormalizeDriveLetter(driveLetter);
            return _roots.TryGetValue(key, out var root) ? root : null;
        }

        public bool IsLoaded(string driveLetter) =>
            _roots.ContainsKey(NormalizeDriveLetter(driveLetter));

        public async Task<DirOD?> LoadAsync(string driveLetter)
        {
            var key = NormalizeDriveLetter(driveLetter);
            if (_roots.TryGetValue(key, out var existing))
                return existing;

            var loaded = await Task.Run(() => SnapshotComponent.LoadSnapshot(key))
                                   .ConfigureAwait(false);
            if (loaded != null)
                _roots[key] = loaded;
            return loaded;
        }

        public async Task<DirOD?> ScanDriveAsync(
            string driveLetter,
            ScanCore.ScanProgress? progress = null,
            CancellationToken ct = default)
        {
            var key = NormalizeDriveLetter(driveLetter);
            var rootPath = key + @":\";
            var root = new DirOD(rootPath, rootPath);

            await Task.Run(() => ScanCore.DeepScan(root, ct, progress, level: 0), ct)
                      .ConfigureAwait(false);

            if (ct.IsCancellationRequested)
                return null;

            root.GetDirSizeSequential();
            _roots[key] = root;
            SnapshotComponent.SaveSnapshot(key, root);
            Log.Info("SnapshotService", $"Scanned and saved drive {key}: ({root.SizeKiloBytes:N0} KB)");
            return root;
        }

        public async Task ReconcileAsync(
            string driveLetter,
            ScanCore.ScanProgress? progress = null,
            CancellationToken ct = default)
        {
            var key = NormalizeDriveLetter(driveLetter);
            if (!_roots.TryGetValue(key, out var root)) return;
            await Task.Run(() => SnapshotComponent.Reconcile(key, root, ct, progress), ct)
                      .ConfigureAwait(false);
        }

        // Coalesce a burst of SnapshotChanged events into one write per drive
        // by deferring 2s past the last event.
        private void OnSnapshotChanged()
        {
            var oldCts = Interlocked.Exchange(ref _saveCts, new CancellationTokenSource());
            oldCts?.Cancel();
            var token = _saveCts!.Token;

            _ = Task.Run(async () =>
            {
                try { await Task.Delay(2000, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }

                await _saveSem.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (token.IsCancellationRequested) return;
                    foreach (var kv in _roots)
                    {
                        try { SnapshotComponent.SaveSnapshot(kv.Key, kv.Value); }
                        catch (Exception ex)
                        {
                            Log.Error("SnapshotService", $"Failed to save snapshot for drive {kv.Key}", ex);
                        }
                    }
                }
                finally { _saveSem.Release(); }
            });
        }

        private static string NormalizeDriveLetter(string? driveLetter)
        {
            if (string.IsNullOrWhiteSpace(driveLetter)) return "C";
            var s = driveLetter.TrimStart().TrimEnd(':', '\\', '/').Trim();
            if (s.Length == 0) return "C";
            var ch = char.ToUpperInvariant(s[0]);
            if (ch < 'A' || ch > 'Z') return "C";
            return ch.ToString();
        }
    }
}
