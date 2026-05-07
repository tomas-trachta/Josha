using FluentAssertions;
using Josha.Business.Ftp;
using Josha.IntegrationTests.Fixtures;
using Josha.Models;
using System.Text;
using Xunit;

namespace Josha.IntegrationTests;

// Smoke tests that prove the SftpServerFixture wires up correctly and
// SftpClientComponent can talk to it. Once these pass we can write the
// wider matrix (RemoteFileSystemProvider, RemoteConnectionPool, etc).
[Collection("Sftp")]
public sealed class SftpClientSmokeTests
{
    private readonly SftpServerFixture _fx;

    public SftpClientSmokeTests(SftpServerFixture fx) => _fx = fx;

    // SSH host-key validation defaults to Strict — the test container has a
    // freshly generated key we have no fingerprint for, so AcceptAny is the
    // right default for non-host-key tests.
    private FtpSite NewSite() => new()
    {
        Name           = "test-sftp",
        Host           = _fx.Host,
        Port           = _fx.Port,
        Username       = SftpServerFixture.User,
        Password       = SftpServerFixture.Password,
        Protocol       = FtpProtocol.Sftp,
        StartDirectory = SftpServerFixture.UploadDir,
        TlsValidation  = TlsValidation.AcceptAny,
    };

    [Fact]
    public async Task Connects_against_atmoz_sftp()
    {
        var client = new SftpClientComponent(NewSite());
        await using var _ = client;

        await client.ConnectAsync(CancellationToken.None);

        client.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Upload_then_download_round_trips_bytes()
    {
        var client = new SftpClientComponent(NewSite());
        await using var _ = client;
        await client.ConnectAsync(CancellationToken.None);

        var payload = Encoding.UTF8.GetBytes("hello-from-josha-integration-test");
        var remote  = $"{SftpServerFixture.UploadDir}/round-trip.txt";

        using (var src = new MemoryStream(payload))
            await client.UploadAsync(src, remote, overwrite: true, resume: false, null, CancellationToken.None);

        using var dst = new MemoryStream();
        await client.DownloadAsync(remote, dst, null, CancellationToken.None);

        dst.ToArray().Should().Equal(payload);
    }

    [Fact]
    public async Task ListAsync_includes_uploaded_file()
    {
        var client = new SftpClientComponent(NewSite());
        await using var _ = client;
        await client.ConnectAsync(CancellationToken.None);

        var name   = $"listed-{Guid.NewGuid():N}.txt";
        var remote = $"{SftpServerFixture.UploadDir}/{name}";

        using (var src = new MemoryStream(Encoding.UTF8.GetBytes("x")))
            await client.UploadAsync(src, remote, overwrite: true, resume: false, null, CancellationToken.None);

        var entries = await client.ListAsync(SftpServerFixture.UploadDir, CancellationToken.None);

        entries.Should().Contain(e => e.Name == name);
    }
}
