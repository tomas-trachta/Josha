using FluentAssertions;
using Josha.Business.Ftp;
using Josha.IntegrationTests.Fixtures;
using Josha.Models;
using Xunit;

namespace Josha.IntegrationTests;

// Exercises RemoteConnectionPool against real connections so we know the
// lease/recycle/faulted machinery survives a real disconnect, not just a
// stubbed IRemoteClient.
//
// Lives in the "Sftp" collection so the static pool state can't race with
// RemoteFileSystemProviderTests (which also touches the pool).
[Collection("Sftp")]
public sealed class RemoteConnectionPoolTests : IAsyncLifetime
{
    private readonly SftpServerFixture _fx;
    private FtpSite _site = null!;
    private int _savedIdleSeconds;
    private int _savedMax;

    public RemoteConnectionPoolTests(SftpServerFixture fx) => _fx = fx;

    public Task InitializeAsync()
    {
        _site = new FtpSite
        {
            Id            = Guid.NewGuid(),
            Name          = "pool-test",
            Host          = _fx.Host,
            Port          = _fx.Port,
            Username      = SftpServerFixture.User,
            Password      = SftpServerFixture.Password,
            Protocol      = FtpProtocol.Sftp,
            TlsValidation = TlsValidation.AcceptAny,
        };
        _savedIdleSeconds = RemoteConnectionPool.IdleSeconds;
        _savedMax         = RemoteConnectionPool.MaxConnectionsPerSite;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // Drop any connections this test parked, then restore pool config
        // so we don't leak the test's settings into siblings.
        await RemoteConnectionPool.DisconnectAllAsync(_site.Id);
        RemoteConnectionPool.IdleSeconds          = _savedIdleSeconds;
        RemoteConnectionPool.MaxConnectionsPerSite = _savedMax;
    }

    [Fact]
    public async Task AcquireAsync_after_clean_release_recycles_the_same_client()
    {
        // Long idle so the entry stays parked between the two acquires.
        RemoteConnectionPool.IdleSeconds = 60;

        IRemoteClient first;
        await using (var lease = await RemoteConnectionPool.AcquireAsync(_site, default))
        {
            first = lease.Client;
            first.IsConnected.Should().BeTrue();
        }

        await using var second = await RemoteConnectionPool.AcquireAsync(_site, default);
        second.Client.Should().BeSameAs(first, "clean release should recycle the same client");
        second.Client.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Faulted_lease_is_disposed_not_recycled()
    {
        RemoteConnectionPool.IdleSeconds = 60;

        IRemoteClient first;
        await using (var lease = await RemoteConnectionPool.AcquireAsync(_site, default))
        {
            first = lease.Client;
            lease.Faulted = true;
        }

        // First client got disposed on release; the next acquire creates a new one.
        first.IsConnected.Should().BeFalse("faulted lease must dispose, not park");

        await using var second = await RemoteConnectionPool.AcquireAsync(_site, default);
        second.Client.Should().NotBeSameAs(first);
        second.Client.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Idle_client_is_evicted_after_IdleSeconds_elapses()
    {
        RemoteConnectionPool.IdleSeconds = 1;

        IRemoteClient parked;
        await using (var lease = await RemoteConnectionPool.AcquireAsync(_site, default))
            parked = lease.Client;

        // Eviction is scheduled via Task.Delay; give it a comfortable margin
        // beyond IdleSeconds before checking.
        await Task.Delay(TimeSpan.FromSeconds(3));

        parked.IsConnected.Should().BeFalse("eviction must dispose the parked client");

        await using var fresh = await RemoteConnectionPool.AcquireAsync(_site, default);
        fresh.Client.Should().NotBeSameAs(parked);
        fresh.Client.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireAsync_blocks_when_MaxConnectionsPerSite_reached()
    {
        RemoteConnectionPool.MaxConnectionsPerSite = 1;
        // The pool's semaphore was sized when the SitePool was first created
        // for this site Id — using a fresh Id picks up the just-set max.

        await using var first = await RemoteConnectionPool.AcquireAsync(_site, default);

        var blocked = RemoteConnectionPool.AcquireAsync(_site, default);
        var raceWinner = await Task.WhenAny(blocked, Task.Delay(500));
        raceWinner.Should().NotBeSameAs(blocked, "second acquire must wait for the first to release");

        await first.DisposeAsync();

        // Now the second acquire unblocks promptly.
        var second = await blocked.WaitAsync(TimeSpan.FromSeconds(5));
        try { second.Client.IsConnected.Should().BeTrue(); }
        finally { await second.DisposeAsync(); }
    }
}
