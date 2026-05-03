using Josha.Models;
using Josha.Services;

namespace Josha.Business.Ftp
{
    // Per-site connection pool. Each FtpSite gets up to MaxConnections concurrent
    // IRemoteClient instances. When released, a client sits idle for IdleSeconds
    // before being disconnected and disposed.
    //
    // Acquire-Release pattern:
    //   await using var lease = await pool.AcquireAsync(site, ct);
    //   await lease.Client.UploadAsync(...)
    //
    // The Lease's DisposeAsync returns the client to the pool. If the operation
    // threw with a connection-level fault, callers should set lease.Faulted = true
    // so the pool disposes the client instead of recycling it.
    internal static class RemoteConnectionPool
    {
        public static int MaxConnectionsPerSite = 2;
        public static int IdleSeconds = 30;

        private static readonly object _lock = new();
        private static readonly Dictionary<Guid, SitePool> _pools = new();
        private static Action<FtpSite>? _onSiteUpdated;

        // Wired by AppServices.Initialize so fingerprint-on-first-use updates
        // persist back into sites.dans automatically.
        public static void SetSiteUpdateCallback(Action<FtpSite> cb)
        {
            _onSiteUpdated = cb;
        }

        public static async Task<Lease> AcquireAsync(FtpSite site, CancellationToken ct)
        {
            SitePool pool;
            lock (_lock)
            {
                if (!_pools.TryGetValue(site.Id, out pool!))
                {
                    pool = new SitePool(site);
                    _pools[site.Id] = pool;
                }
            }
            return await pool.AcquireAsync(ct).ConfigureAwait(false);
        }

        public static async Task DisconnectAllAsync(Guid siteId)
        {
            SitePool? pool;
            lock (_lock)
            {
                if (!_pools.TryGetValue(siteId, out pool)) return;
            }
            await pool.DisconnectAllAsync().ConfigureAwait(false);
        }

        public static async Task ShutdownAsync()
        {
            List<SitePool> all;
            lock (_lock)
            {
                all = _pools.Values.ToList();
                _pools.Clear();
            }
            foreach (var p in all) await p.DisconnectAllAsync().ConfigureAwait(false);
        }

        internal static void NotifySiteUpdated(FtpSite s) => _onSiteUpdated?.Invoke(s);

        public sealed class Lease : IAsyncDisposable
        {
            private readonly SitePool _owner;
            private bool _disposed;

            public IRemoteClient Client { get; }
            public bool Faulted { get; set; }

            internal Lease(SitePool owner, IRemoteClient client)
            {
                _owner = owner;
                Client = client;
            }

            public async ValueTask DisposeAsync()
            {
                if (_disposed) return;
                _disposed = true;
                await _owner.ReleaseAsync(Client, Faulted).ConfigureAwait(false);
            }
        }

        internal sealed class SitePool
        {
            private readonly FtpSite _site;
            private readonly SemaphoreSlim _gate;
            private readonly object _stateLock = new();
            private readonly List<IdleEntry> _idle = new();
            private int _outstanding;

            public SitePool(FtpSite site)
            {
                _site = site;
                _gate = new SemaphoreSlim(MaxConnectionsPerSite, MaxConnectionsPerSite);
            }

            public async Task<Lease> AcquireAsync(CancellationToken ct)
            {
                await _gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    IRemoteClient? client = TakeIdle();
                    if (client == null)
                    {
                        client = CreateClient();
                        await client.ConnectAsync(ct).ConfigureAwait(false);
                    }
                    Interlocked.Increment(ref _outstanding);
                    return new Lease(this, client);
                }
                catch
                {
                    _gate.Release();
                    throw;
                }
            }

            public async Task ReleaseAsync(IRemoteClient client, bool faulted)
            {
                Interlocked.Decrement(ref _outstanding);
                try
                {
                    if (faulted || !client.IsConnected)
                    {
                        await client.DisposeAsync().ConfigureAwait(false);
                        return;
                    }

                    // CTS must exist before the entry is visible to TakeIdle —
                    // otherwise a parallel acquire pulls the entry while Cts is
                    // null, can't cancel the pending eviction, and the eviction
                    // later disposes a client the lease holder is still using.
                    var entry = new IdleEntry { Client = client };
                    entry.Cts = new CancellationTokenSource();
                    var token = entry.Cts.Token;

                    lock (_stateLock) _idle.Add(entry);

                    _ = Task.Delay(TimeSpan.FromSeconds(IdleSeconds), token)
                        .ContinueWith(async _ => await EvictAsync(entry).ConfigureAwait(false),
                            TaskContinuationOptions.OnlyOnRanToCompletion);
                }
                finally
                {
                    _gate.Release();
                }
            }

            public async Task DisconnectAllAsync()
            {
                List<IdleEntry> snapshot;
                lock (_stateLock)
                {
                    snapshot = _idle.ToList();
                    _idle.Clear();
                }
                foreach (var e in snapshot)
                {
                    e.Cts?.Cancel();
                    try { await e.Client.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception ex) { Log.Warn("Pool", "Idle disconnect threw", ex); }
                }
            }

            private IRemoteClient? TakeIdle()
            {
                lock (_stateLock)
                {
                    if (_idle.Count == 0) return null;
                    var e = _idle[^1];
                    _idle.RemoveAt(_idle.Count - 1);
                    e.Cts?.Cancel();
                    return e.Client;
                }
            }

            private async Task EvictAsync(IdleEntry e)
            {
                lock (_stateLock)
                {
                    if (!_idle.Remove(e)) return;
                }
                try { await e.Client.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { Log.Warn("Pool", "Evict-disconnect threw", ex); }
            }

            private IRemoteClient CreateClient()
            {
                Action<string> pin = fp =>
                {
                    _site.PinnedFingerprint = fp;
                    NotifySiteUpdated(_site);
                };

                return _site.Protocol == FtpProtocol.Sftp
                    ? new SftpClientComponent(_site, pin)
                    : new FtpClientComponent(_site, pin);
            }

            private sealed class IdleEntry
            {
                public required IRemoteClient Client { get; init; }
                public CancellationTokenSource? Cts { get; set; }
            }
        }
    }
}
