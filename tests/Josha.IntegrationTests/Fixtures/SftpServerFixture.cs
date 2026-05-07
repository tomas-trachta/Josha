using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Josha.IntegrationTests.Fixtures;

// Spins up atmoz/sftp:alpine for the duration of a test class.
// One container, one user, one writable home dir — enough for round-trip
// tests of IRemoteClient and RemoteFileSystemProvider.
//
// Public surface is purely connection details (host/port/creds/paths) so
// this stays decoupled from Josha's internal types — tests build whatever
// FtpSite/SftpClientComponent they need themselves.
//
// Usage:
//   public class MyTests : IClassFixture<SftpServerFixture> { ... }
public sealed class SftpServerFixture : IAsyncLifetime
{
    public const string User     = "tester";
    public const string Password = "secret";

    // atmoz/sftp chroots the user into /home/<user>/, so SFTP-client paths
    // are relative to that — clients see /upload, not /home/tester/upload.
    // The chroot root itself is owned by root and not writable; only the
    // listed subfolder ("upload" here) is writable.
    public const string UploadDir = "/upload";

    private IContainer? _container;

    public string Host => _container?.Hostname ?? throw NotStarted();
    public ushort Port => _container?.GetMappedPublicPort(22) ?? throw NotStarted();

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder()
            .WithImage("atmoz/sftp:alpine")
            // CMD takes "<user>:<pass>:<uid>:<gid>:<dirs>".
            // ":::upload" → default uid/gid + a writable "upload" subfolder.
            .WithCommand($"{User}:{Password}:::upload")
            .WithPortBinding(22, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(22))
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    private static InvalidOperationException NotStarted() =>
        new("SftpServerFixture has not been initialised yet — InitializeAsync must run first.");
}
