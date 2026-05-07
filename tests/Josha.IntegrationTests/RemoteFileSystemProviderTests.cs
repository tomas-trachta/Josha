using FluentAssertions;
using Josha.Business;
using Josha.Business.Ftp;
using Josha.IntegrationTests.Fixtures;
using Josha.Models;
using System.Text;
using Xunit;

namespace Josha.IntegrationTests;

// Drives RemoteFileSystemProvider against a real SFTP server. Each test uses
// a unique remote subfolder under /upload so cases stay independent even
// though they share the container.
[Collection("Sftp")]
public sealed class RemoteFileSystemProviderTests : IAsyncLifetime
{
    private readonly SftpServerFixture _fx;
    private FtpSite _site = null!;
    private RemoteFileSystemProvider _provider = null!;
    private string _scope = null!;

    public RemoteFileSystemProviderTests(SftpServerFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        _site = new FtpSite
        {
            // Per-test Guid so each test gets its own SitePool entry inside
            // RemoteConnectionPool — keeps lease state from bleeding across
            // tests in the collection.
            Id            = Guid.NewGuid(),
            Name          = "rfsp-test",
            Host          = _fx.Host,
            Port          = _fx.Port,
            Username      = SftpServerFixture.User,
            Password      = SftpServerFixture.Password,
            Protocol      = FtpProtocol.Sftp,
            TlsValidation = TlsValidation.AcceptAny,
        };
        _provider = new RemoteFileSystemProvider(_site);

        // Per-test subfolder so list/copy/move don't collide.
        _scope = $"{SftpServerFixture.UploadDir}/test-{Guid.NewGuid():N}";
        var mk = await _provider.CreateDirectoryAsync(SftpServerFixture.UploadDir, _scope[(SftpServerFixture.UploadDir.Length + 1)..]);
        mk.Success.Should().BeTrue("scoped test dir must be createable: {0}", mk.Error);
    }

    public async Task DisposeAsync()
    {
        // Tear down the per-site pool so the next test starts cold.
        await RemoteConnectionPool.DisconnectAllAsync(_site.Id);
    }

    private async Task<string> UploadAsync(string name, string content)
    {
        var path = $"{_scope}/{name}";
        await using var s = await _provider.OpenWriteAsync(path, overwrite: true);
        var bytes = Encoding.UTF8.GetBytes(content);
        await s.WriteAsync(bytes);
        return path;
    }

    private async Task<string> ReadAsync(string path)
    {
        await using var s = await _provider.OpenReadAsync(path);
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    [Fact]
    public async Task EnumerateAsync_returns_uploaded_files_with_correct_metadata()
    {
        await UploadAsync("a.txt", "alpha");
        await UploadAsync("b.txt", "bravo");

        var entries = await _provider.EnumerateAsync(_scope);

        entries.Should().HaveCount(2);
        entries.Should().OnlyContain(e => !e.IsDirectory);
        entries.Should().Contain(e => e.Name == "a.txt" && e.Size == 5);
        entries.Should().Contain(e => e.Name == "b.txt" && e.Size == 5);
    }

    [Fact]
    public async Task CopyAsync_round_trips_content()
    {
        var src = await UploadAsync("src.txt", "copy-me");
        var dst = $"{_scope}/dst.txt";

        var result = await _provider.CopyAsync(src, dst, overwrite: false);

        result.Success.Should().BeTrue(result.Error);
        (await ReadAsync(dst)).Should().Be("copy-me");
        // Source must remain — Copy is not Move.
        (await ReadAsync(src)).Should().Be("copy-me");
    }

    [Fact]
    public async Task CopyAsync_refuses_to_overwrite_when_overwrite_false()
    {
        var src = await UploadAsync("src.txt", "new");
        var dst = await UploadAsync("dst.txt", "old");

        var result = await _provider.CopyAsync(src, dst, overwrite: false);

        result.Success.Should().BeFalse();
        (await ReadAsync(dst)).Should().Be("old");
    }

    [Fact]
    public async Task MoveAsync_renames_in_place_and_drops_source()
    {
        var src = await UploadAsync("src.txt", "moveme");
        var dst = $"{_scope}/moved.txt";

        var result = await _provider.MoveAsync(src, dst);

        result.Success.Should().BeTrue(result.Error);
        var entries = await _provider.EnumerateAsync(_scope);
        entries.Select(e => e.Name).Should().BeEquivalentTo(new[] { "moved.txt" });
    }

    [Fact]
    public async Task RenameAsync_changes_only_the_leaf()
    {
        var src = await UploadAsync("before.txt", "x");

        var result = await _provider.RenameAsync(src, "after.txt");

        result.Success.Should().BeTrue(result.Error);
        var entries = await _provider.EnumerateAsync(_scope);
        entries.Select(e => e.Name).Should().BeEquivalentTo(new[] { "after.txt" });
    }

    [Fact]
    public async Task CreateDirectoryAsync_then_EnumerateAsync_sees_it_as_directory()
    {
        var result = await _provider.CreateDirectoryAsync(_scope, "subdir");

        result.Success.Should().BeTrue(result.Error);
        var entries = await _provider.EnumerateAsync(_scope);
        entries.Should().Contain(e => e.Name == "subdir" && e.IsDirectory);
    }

    [Fact]
    public async Task DeleteAsync_removes_a_file()
    {
        var path = await UploadAsync("doomed.txt", "x");

        var result = await _provider.DeleteAsync(path, toRecycle: false);

        result.Success.Should().BeTrue(result.Error);
        (await _provider.EnumerateAsync(_scope)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_recursively_removes_a_populated_directory()
    {
        var sub = $"{_scope}/sub";
        (await _provider.CreateDirectoryAsync(_scope, "sub")).Success.Should().BeTrue();
        await UploadAsync("sub/leaf-1.txt", "1");
        await UploadAsync("sub/leaf-2.txt", "2");

        var result = await _provider.DeleteAsync(sub, toRecycle: false);

        result.Success.Should().BeTrue(result.Error);
        (await _provider.EnumerateAsync(_scope)).Should().BeEmpty();
    }

    [Fact]
    public async Task ImportFromAsync_uploads_a_local_file()
    {
        var localTemp = Path.Combine(Path.GetTempPath(), $"josha-import-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(localTemp, "from-disk");
        try
        {
            var local = LocalFileSystemProvider.Instance;
            var dst = $"{_scope}/imported.txt";

            var result = await _provider.ImportFromAsync(local, localTemp, dst, null, overwrite: false, default);

            result.Success.Should().BeTrue(result.Error);
            (await ReadAsync(dst)).Should().Be("from-disk");
        }
        finally
        {
            File.Delete(localTemp);
        }
    }
}
